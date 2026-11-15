using System.Diagnostics.CodeAnalysis;
using Fraud.Collectors.Collection;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.StaticCt;

public enum CtBatchStatus
{
    /// <summary>Yeni tam tile yok; bekle.</summary>
    CaughtUp = 1,

    /// <summary>Bir aralık doğrulandı ve yazıldı.</summary>
    Advanced = 2,

    /// <summary>Tur bir hatayla bitti (collection_log'a yazıldı).</summary>
    Failed = 3,
}

/// <summary>Bir turun özeti. Yalnızca sayılar.</summary>
public sealed record CtBatchResult(CtBatchStatus Status, long Examined, long Skipped, int NewCandidates, long Backlog)
{
    /// <summary>Geride en az bir tam tile kaldıysa beklemeden devam edilir.</summary>
    public bool HasMoreWork => Status == CtBatchStatus.Advanced && Backlog >= TilePath.Width;
}

/// <summary>
/// Bir Static CT logunu bir tur ilerletir: checkpoint, veri tile'ları, Merkle doğrulaması, aday saklama.
/// </summary>
/// <remarks>
/// <para>Bir turun adımları:</para>
/// <list type="number">
/// <item>Checkpoint okunur, imzası logun anahtarıyla doğrulanır. Boyut geriye gittiyse tur hata ile biter.</item>
/// <item>Kalınan yerin Merkle sağ kenarı veritabanından alınır (yoksa hash tile'larından kurulur).</item>
/// <item>Sıradaki tam veri tile'ları sınırlı eşzamanlılıkla indirilir, her kaydın yaprak özeti hesaplanır ve
/// saklama filtresi uygulanır. Ham tile bellekte kalır, diske yazılmaz.</item>
/// <item>Yaprak özetleri sağ kenara eklenir. Akış, daha önce görülen bir checkpoint boyutundan geçerse kök o noktada
/// yeniden hesaplanıp o checkpoint'in köküyle karşılaştırılır (ardışık checkpoint tutarlılığı).</item>
/// <item>Okunan aralığın sonundan yeni checkpoint boyutuna kadar olan kısım hash tile'larından eklenir ve kök,
/// imzalı checkpoint köküyle karşılaştırılır. Tutmazsa hiçbir şey yazılmaz.</item>
/// <item>Adaylar, yeni durum ve collection_log satırı tek veritabanı işleminde yazılır.</item>
/// </list>
/// <para>
/// Hata: Aralık işlenemezse (indirme, bozuk tile, doğrulama) indeks ilerlemez ve sayaç artar. Aynı aralık
/// <see cref="StaticCtOptions.MaxRangeAttempts"/> turda işlenemezse atlanır; atlama collection_log'a "atlanan kayıt"
/// sayısıyla yazılır ve kapsama oranına girer. Hiçbir hata "kayıt yok" olarak yazılmaz.
/// </para>
/// </remarks>
public sealed class StaticCtFollower(
    CtLogOptions log,
    int logNumber,
    IStaticCtClient client,
    ICtStateStore store,
    TileProcessor processor,
    StaticCtOptions options,
    CollectionOptions collection,
    StaticCtMetrics metrics,
    TimeProvider timeProvider,
    ILogger logger)
{
    public async Task<CtBatchResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var checkpointFetch = await client.GetCheckpointAsync(cancellationToken).ConfigureAwait(false);
        var state = await store.LoadAsync(log.Name, cancellationToken).ConfigureAwait(false);
        var requests = checkpointFetch.Attempts;

        if (!checkpointFetch.IsSuccess)
        {
            StaticCtLog.CheckpointRejected(logger, logNumber, checkpointFetch.ErrorKind, checkpointFetch.HttpStatus);
            metrics.Failure(log.Name, checkpointFetch.ErrorKind);
            await WriteLogOnlyAsync(Error(startedAt, checkpointFetch.ErrorKind, checkpointFetch.HttpStatus, requests, 0, null, null)).ConfigureAwait(false);
            return new CtBatchResult(CtBatchStatus.Failed, 0, 0, 0, 0);
        }

        var checkpoint = checkpointFetch.Value!;
        var isNew = state is null;
        if (state is null)
        {
            state = new CtLogState(log.Name, StartIndex(checkpoint.Size), checkpoint.Size, checkpoint.RootHash, checkpoint.Timestamp, startedAt);
            StaticCtLog.FollowStarted(logger, logNumber, state.NextIndex, checkpoint.Size);
        }
        else if (checkpoint.Size < state.LatestCheckpointSize
                 || (checkpoint.Size == state.PendingCheckpointSize && !checkpoint.RootHash.AsSpan().SequenceEqual(state.PendingCheckpointRoot)))
        {
            // Log geriye gitti ya da aynı boyutta başka bir kök imzaladı: ikisi de logun tutarsız olduğunu gösterir.
            StaticCtLog.CheckpointInconsistent(logger, logNumber, state.LatestCheckpointSize, checkpoint.Size);
            metrics.Failure(log.Name, CollectionErrorKind.VerificationFailed);
            await WriteLogOnlyAsync(Error(startedAt, CollectionErrorKind.VerificationFailed, checkpointFetch.HttpStatus, requests, 0, null, null))
                .ConfigureAwait(false);
            return new CtBatchResult(CtBatchStatus.Failed, 0, 0, 0, checkpoint.Size - state.NextIndex);
        }

        var fullEnd = checkpoint.Size / TilePath.Width * TilePath.Width;
        var rangeStart = state.NextIndex;
        metrics.Position(log.Name, checkpoint.Size, rangeStart);
        if (rangeStart >= fullEnd)
        {
            if (isNew)
            {
                await store.CommitAsync(state, isNew: true, [], [], _ => [], CancellationToken.None).ConfigureAwait(false);
            }

            // Yetişmiş: checkpoint doğrulandı, yeni tam tile yok. Bu da hatasız bir turdur (durma uyarısı için).
            metrics.BatchSucceeded(log.Name, advanced: false, newestEntry: null);
            return new CtBatchResult(CtBatchStatus.CaughtUp, 0, 0, 0, checkpoint.Size - rangeStart);
        }

        var tileCount = (int)Math.Min(options.MaxTilesPerBatch, (fullEnd - rangeStart) / TilePath.Width);
        var attemptedEnd = rangeStart + (tileCount * (long)TilePath.Width);
        var reader = new TreeHashReader(client, checkpoint.Size);
        var run = new Run(state, isNew, checkpoint, startedAt, rangeStart, requests) { LastStatus = checkpointFetch.HttpStatus };

        // 1) Kalınan yerin Merkle sağ kenarı.
        CompactRange range;
        try
        {
            range = TryRestoreFrontier(state) ?? await reader.BuildFrontierAsync(rangeStart, cancellationToken).ConfigureAwait(false);
        }
        catch (StaticCtFetchException ex)
        {
            run.Requests += reader.Requests;
            return await FailRangeAsync(run, ex.ErrorKind, ex.HttpStatus, attemptedEnd, rangeStart + TilePath.Width).ConfigureAwait(false);
        }

        // 2) Veri tile'ları.
        var outcomes = await FetchTilesAsync(rangeStart / TilePath.Width, tileCount, cancellationToken).ConfigureAwait(false);

        // 3) Yaprakları sırayla ekle; önceki checkpoint'ten geçerken kökü karşılaştır.
        var pendingChecked = state.PendingCheckpointSize < range.Size;
        if (state.PendingCheckpointSize == range.Size)
        {
            if (!range.Root().AsSpan().SequenceEqual(state.PendingCheckpointRoot))
            {
                run.Requests += reader.Requests + outcomes.Sum(o => o.Attempts);
                return await InconsistentAsync(run, attemptedEnd).ConfigureAwait(false);
            }

            pendingChecked = true;
        }

        var drafts = new List<CandidateDraft>();
        var neutralDrafts = new List<NeutralSampleDraft>();
        int examined = 0, unreadable = 0;
        var newest = DateTimeOffset.MinValue;
        TileOutcome? failure = null;
        foreach (var outcome in outcomes)
        {
            if (outcome.Tile is not { } tile)
            {
                failure = outcome;
                break;
            }

            run.Requests += outcome.Attempts;
            run.LastStatus = outcome.HttpStatus ?? run.LastStatus;
            foreach (var leafHash in tile.LeafHashes)
            {
                range.AppendLeaf(leafHash);
                if (!pendingChecked && range.Size == state.PendingCheckpointSize)
                {
                    if (!range.Root().AsSpan().SequenceEqual(state.PendingCheckpointRoot))
                    {
                        run.Requests += reader.Requests;
                        return await InconsistentAsync(run, attemptedEnd).ConfigureAwait(false);
                    }

                    pendingChecked = true;
                }
            }

            drafts.AddRange(tile.Candidates);
            neutralDrafts.AddRange(tile.NeutralSamples);
            examined += tile.Examined;
            unreadable += tile.UnreadableCertificates;
            newest = tile.NewestEntry > newest ? tile.NewestEntry : newest;
        }

        var processedEnd = range.Size;
        if (processedEnd == rangeStart)
        {
            // İlk tile bile okunamadı.
            run.Requests += reader.Requests + failure!.Attempts;
            return await FailRangeAsync(run, failure.ErrorKind, failure.HttpStatus, rangeStart + TilePath.Width, rangeStart + TilePath.Width)
                .ConfigureAwait(false);
        }

        // 4) Okunan kısmın yeni checkpoint köküne bağlandığını doğrula.
        try
        {
            var check = range.Clone();
            await reader.AppendAsync(check, checkpoint.Size, cancellationToken).ConfigureAwait(false);
            run.Requests += reader.Requests;
            run.LastStatus = reader.LastHttpStatus ?? run.LastStatus;
            if (!check.Root().AsSpan().SequenceEqual(checkpoint.RootHash))
            {
                return await FailRangeAsync(run, CollectionErrorKind.VerificationFailed, run.LastStatus, processedEnd, processedEnd).ConfigureAwait(false);
            }
        }
        catch (StaticCtFetchException ex)
        {
            run.Requests += reader.Requests;
            return await FailRangeAsync(run, ex.ErrorKind, ex.HttpStatus, processedEnd, rangeStart + TilePath.Width).ConfigureAwait(false);
        }

        // 5) Doğrulandı: yaz.
        var observedAt = timeProvider.GetUtcNow();
        var candidates = drafts.Select(d => d.ToEntity(log.Name, observedAt)).ToList();
        // Tile'lar sırayla eklendiği için örnek taslakları log indeksine göre artan sıradadır (depo bunu bekler).
        var neutralSamples = neutralDrafts.Select(d => d.ToEntity(log.Name, observedAt)).ToList();
        var newCandidates = 0;
        var trailingSkip = 0L;
        var newNeutralDomains = await store.CommitAsync(state, isNew, candidates, neutralSamples, inserted =>
        {
            newCandidates = inserted;
            state.ObserveCheckpoint(checkpoint.Size, checkpoint.RootHash, checkpoint.Timestamp, replacePending: pendingChecked);
            state.Advance(processedEnd, range.ToFrontier(), examined, unreadable, inserted, observedAt);

            var entries = new List<CollectionLogEntry>
            {
                new CollectionLogEntry(
                    CollectionSource.StaticCt, null, collection.MeasurementLocation, startedAt, observedAt, run.LastStatus, run.Requests,
                    examined > 0 ? CollectionOutcome.Success : CollectionOutcome.Error,
                    examined > 0 ? CollectionErrorKind.None : CollectionErrorKind.InvalidResponse,
                    examined, unreadable, inserted, truncated: false)
                    .ForCtRange(log.Name, rangeStart, processedEnd),
            };

            if (failure is not null)
            {
                // Aralığın devamındaki tile okunamadı: Sonraki turda bu tile'dan başlanır.
                trailingSkip = RecordRangeFailure(state, observedAt, processedEnd + TilePath.Width);
                entries.Add(Error(observedAt, failure.ErrorKind, failure.HttpStatus, failure.Attempts, trailingSkip,
                    processedEnd, processedEnd + TilePath.Width));
            }

            return entries;
        }, CancellationToken.None).ConfigureAwait(false);

        metrics.Processed(log.Name, examined, unreadable + trailingSkip, newCandidates);
        metrics.NeutralSampled(log.Name, newNeutralDomains);
        if (newest > DateTimeOffset.MinValue)
        {
            metrics.Lag(log.Name, observedAt - newest);
        }

        var backlog = checkpoint.Size - state.NextIndex;
        metrics.Position(log.Name, checkpoint.Size, state.NextIndex);
        metrics.BatchSucceeded(log.Name, advanced: true, newest > DateTimeOffset.MinValue ? newest : null);
        StaticCtLog.RangeCommitted(logger, logNumber, rangeStart, processedEnd, examined, unreadable, newCandidates, backlog);
        if (failure is not null)
        {
            LogFailure(failure.ErrorKind, failure.HttpStatus, processedEnd, processedEnd + TilePath.Width, state.ConsecutiveFailures, trailingSkip);
        }

        return new CtBatchResult(CtBatchStatus.Advanced, examined, unreadable + trailingSkip, newCandidates, backlog);
    }

    /// <summary>İlk başlangıç indeksi: yapılandırmadaki indeks ya da şimdiki uç; her durumda tile sınırına yuvarlanır.</summary>
    private long StartIndex(long treeSize)
    {
        var head = treeSize / TilePath.Width * TilePath.Width;
        return log.StartIndex is { } start ? Math.Min(start / TilePath.Width * TilePath.Width, head) : head;
    }

    private static CompactRange? TryRestoreFrontier(CtLogState state)
    {
        if (state.Frontier is not { } frontier)
        {
            return null;
        }

        try
        {
            return CompactRange.FromFrontier(state.NextIndex, frontier);
        }
        catch (ArgumentException)
        {
            // Kayıtlı sağ kenar boyutla uyuşmuyor: güvenme, yeniden kur.
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Güvenilmez tile'dan çıkan her istisna o tile'ın hatası olarak işlenmeli; görev hiç hata ile bitmemeli.")]
    private async Task<List<TileOutcome>> FetchTilesAsync(long firstTile, int count, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(options.MaxConcurrency);
        var tasks = new List<Task<TileOutcome>>(count);
        var failed = 0;
        try
        {
            // İstekler sırayla başlatılır: Bir tile başlatılmadıysa ondan sonrakiler de başlatılmamıştır.
            for (var i = 0; i < count; i++)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (Volatile.Read(ref failed) != 0)
                {
                    gate.Release();
                    break;
                }

                var tileIndex = firstTile + i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var fetchStarted = timeProvider.GetTimestamp();
                        var fetch = await client.GetDataTileAsync(tileIndex, cancellationToken).ConfigureAwait(false);
                        metrics.TileFetched(log.Name, timeProvider.GetElapsedTime(fetchStarted));
                        if (!fetch.IsSuccess)
                        {
                            Interlocked.Exchange(ref failed, 1);
                            return TileOutcome.Failed(tileIndex, fetch.ErrorKind, fetch.HttpStatus, fetch.Attempts);
                        }

                        try
                        {
                            return new TileOutcome(tileIndex, processor.Process(fetch.Value!, tileIndex), CollectionErrorKind.None, fetch.HttpStatus, fetch.Attempts);
                        }
                        catch (InvalidDataException)
                        {
                            Interlocked.Exchange(ref failed, 1);
                            return TileOutcome.Failed(tileIndex, CollectionErrorKind.InvalidResponse, fetch.HttpStatus, fetch.Attempts);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return TileOutcome.Failed(tileIndex, CollectionErrorKind.Cancelled, null, 0);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Interlocked.Exchange(ref failed, 1);
                        return TileOutcome.Failed(tileIndex, CollectionErrorKind.Unexpected, null, 0);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, CancellationToken.None));
            }
        }
        finally
        {
            // Başlamış istekler bitmeden semafor bırakılmaz. Görevler hata ile bitmez (yukarıda sonuca çevrilir).
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return [.. tasks.Select(t => t.Result)];
    }

    private Task<CtBatchResult> InconsistentAsync(Run run, long attemptedEnd)
    {
        StaticCtLog.CheckpointInconsistent(logger, logNumber, run.State.PendingCheckpointSize, run.Checkpoint.Size);
        return FailRangeAsync(run, CollectionErrorKind.VerificationFailed, run.LastStatus, attemptedEnd, attemptedEnd);
    }

    /// <summary>
    /// Aralık işlenemedi: Hiçbir aday yazılmaz. Sayaç artar; sınır aşıldıysa [başlangıç, <paramref name="skipEnd"/>) atlanır.
    /// </summary>
    private async Task<CtBatchResult> FailRangeAsync(Run run, CollectionErrorKind kind, int? httpStatus, long attemptedEnd, long skipEnd)
    {
        var now = timeProvider.GetUtcNow();
        var skipped = 0L;
        await store.CommitAsync(run.State, run.IsNew, [], [], _ =>
        {
            skipped = RecordRangeFailure(run.State, now, skipEnd);
            return [Error(run.StartedAt, kind, httpStatus, run.Requests, skipped, run.RangeStart, attemptedEnd)];
        }, CancellationToken.None).ConfigureAwait(false);

        metrics.Failure(log.Name, kind);
        if (skipped > 0)
        {
            metrics.Skipped(log.Name, skipped);
        }

        metrics.Position(log.Name, run.Checkpoint.Size, run.State.NextIndex);

        LogFailure(kind, httpStatus, run.RangeStart, attemptedEnd, run.State.ConsecutiveFailures, skipped);
        return new CtBatchResult(CtBatchStatus.Failed, 0, skipped, 0, run.Checkpoint.Size - run.State.NextIndex);
    }

    /// <summary>Hata sayacını artırır; sınır aşıldıysa aralığı atlar ve atlanan kayıt sayısını döner.</summary>
    private long RecordRangeFailure(CtLogState state, DateTimeOffset now, long skipEnd)
    {
        var start = state.NextIndex;
        state.RecordFailure(now);
        if (state.ConsecutiveFailures < options.MaxRangeAttempts)
        {
            return 0;
        }

        state.Skip(skipEnd, now);
        return skipEnd - start;
    }

    private void LogFailure(CollectionErrorKind kind, int? httpStatus, long start, long end, int failures, long skipped)
    {
        if (skipped > 0)
        {
            StaticCtLog.RangeSkipped(logger, logNumber, start, start + skipped, kind);
        }
        else
        {
            StaticCtLog.RangeFailed(logger, logNumber, start, end, kind, httpStatus, failures);
        }
    }

    private Task<int> WriteLogOnlyAsync(CollectionLogEntry entry) =>
        store.CommitAsync(null, isNew: false, [], [], _ => [entry], CancellationToken.None);

    private CollectionLogEntry Error(
        DateTimeOffset startedAt, CollectionErrorKind kind, int? httpStatus, int attempts, long skipped, long? rangeStart, long? rangeEnd)
    {
        var finishedAt = timeProvider.GetUtcNow();
        return new CollectionLogEntry(
                CollectionSource.StaticCt, null, collection.MeasurementLocation, startedAt, finishedAt < startedAt ? startedAt : finishedAt,
                httpStatus, attempts, CollectionOutcome.Error, kind == CollectionErrorKind.None ? CollectionErrorKind.Unexpected : kind,
                0, (int)Math.Min(skipped, int.MaxValue), 0, truncated: false)
            .ForCtRange(log.Name, rangeStart, rangeEnd);
    }

    private sealed record TileOutcome(long TileIndex, ProcessedTile? Tile, CollectionErrorKind ErrorKind, int? HttpStatus, int Attempts)
    {
        public static TileOutcome Failed(long tileIndex, CollectionErrorKind kind, int? status, int attempts) =>
            new(tileIndex, null, kind, status, attempts);
    }

    private sealed class Run(CtLogState state, bool isNew, Checkpoint checkpoint, DateTimeOffset startedAt, long rangeStart, int requests)
    {
        public CtLogState State { get; } = state;

        public bool IsNew { get; } = isNew;

        public Checkpoint Checkpoint { get; } = checkpoint;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public long RangeStart { get; } = rangeStart;

        public int Requests { get; set; } = requests;

        public int? LastStatus { get; set; }
    }
}
