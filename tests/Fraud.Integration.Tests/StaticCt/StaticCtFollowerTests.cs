using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Http;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fraud.Integration.Tests.StaticCt;

// Gerçek PostgreSQL (Testcontainers) üzerinde canlı akış turları. Log sahte; ağ yok.
[Trait("Category", "Integration")]
public sealed class StaticCtFollowerTests(PostgresFixture postgres) : IDisposable
{
    private readonly TestCtLog _log = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly StaticCtMetrics _metrics = new();

    public void Dispose()
    {
        _log.Dispose();
        _logs.Dispose();
        _metrics.Dispose();
    }

    private static StaticCtOptions Fast(int maxTiles = 32, int maxAttempts = 2) => new()
    {
        MinRequestInterval = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryAfter = TimeSpan.Zero,
        MaxRetryAttempts = 0,
        AttemptTimeout = TimeSpan.FromSeconds(30),
        MaxConcurrency = 3,
        MaxTilesPerBatch = maxTiles,
        MaxRangeAttempts = maxAttempts,
    };

    private StaticCtFollower Follower(FraudDbContext db, FakeCtServer server, StaticCtOptions options, string startAt = "0")
    {
        var log = _log.Options(startAt);
        var key = CtLogKey.Create(log.Origin, log.PublicKey);
        var http = new HttpClient(server, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new StaticCtClient(
            new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System),
            log, key, options.MaxResponseBytes);
        return new StaticCtFollower(
            log, 1, client, new EfCtStateStore(db), new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions())),
            options, new CollectionOptions { MeasurementLocation = "test-lab" }, _metrics, TimeProvider.System,
            _logs.CreateLogger("StaticCt"));
    }

    private static async Task<FraudDbContext> FreshContextAsync(FraudDbContext previous)
    {
        // Yeniden başlatma: aynı veritabanına yeni bir bağlam, bellekte hiçbir şey taşınmaz.
        var connection = previous.Database.GetConnectionString()!;
        await previous.DisposeAsync();
        return PostgresFixture.CreateContext(connection);
    }

    [Fact]
    public async Task One_batch_verifies_the_root_and_stores_only_keyword_candidates()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries((3 * 256) + 40);
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };

        var result = await Follower(db, server, Fast()).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Advanced, result.Status);
        Assert.Equal(768, result.Examined);
        Assert.Equal(_log.KeywordCount(0, 768), result.NewCandidates);

        db.ChangeTracker.Clear();
        var state = await db.CtLogStates.SingleAsync(ct);
        Assert.Equal(768, state.NextIndex);
        Assert.NotNull(state.Frontier);
        Assert.Equal(768, state.EntriesProcessed);
        Assert.Equal(1.0, state.Coverage);

        var candidates = await db.CtCandidates.OrderBy(c => c.EntryIndex).ToListAsync(ct);
        Assert.Equal(_log.KeywordCount(0, 768), candidates.Count);
        Assert.All(candidates, c =>
        {
            Assert.True(_log.IsKeyword(c.EntryIndex));
            Assert.Equal(_log.TimestampOf(c.EntryIndex), c.LoggedAt);
            Assert.DoesNotContain('@', c.Issuer);
            Assert.Equal(["yatirim"], c.MatchedTerms);
            Assert.True(c.ObservedAt > c.LoggedAt);
        });
        Assert.Contains(candidates, c => c.EntryType == Fraud.Domain.CertificateTransparency.CtEntryType.Precert);
        Assert.Contains(candidates, c => c.SanNames.SequenceEqual([TestCtLog.IdnKeywordName]));

        var row = await db.CollectionLog.SingleAsync(ct);
        Assert.Equal(CollectionSource.StaticCt, row.Source);
        Assert.Equal(CollectionOutcome.Success, row.Outcome);
        Assert.Null(row.DomainId);
        Assert.Equal("test-lab", row.MeasurementLocation);
        Assert.Equal((0L, 768L), (row.RangeStart!.Value, row.RangeEnd!.Value));
        Assert.Equal(candidates.Count, row.NewItemCount);

        // Yeni tam tile yok: ikinci tur yalnızca checkpoint okur.
        var before = server.Paths.Count;
        var again = await Follower(db, server, Fast()).RunOnceAsync(ct);
        Assert.Equal(CtBatchStatus.CaughtUp, again.Status);
        Assert.Equal(["checkpoint"], server.Paths.Skip(before));
    }

    [Fact]
    public async Task Restart_continues_from_the_stored_index_without_refetching_or_duplicating()
    {
        var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries((5 * 256) + 7);
        using var server = new FakeCtServer(_log) { PublishedSize = (3 * 256) + 10 };

        var first = await Follower(db, server, Fast(maxTiles: 2)).RunOnceAsync(ct);
        Assert.Equal(CtBatchStatus.Advanced, first.Status);
        Assert.True(first.HasMoreWork);

        // Program kapandı, log büyüdü, program yeniden açıldı.
        db = await FreshContextAsync(db);
        server.PublishedSize = _log.Count;
        CtBatchResult result;
        var rounds = 0;
        do
        {
            result = await Follower(db, server, Fast(maxTiles: 2)).RunOnceAsync(ct);
            rounds++;
        }
        while (result.HasMoreWork && rounds < 10);

        Assert.Equal(CtBatchStatus.Advanced, result.Status);
        for (var t = 0; t < 5; t++)
        {
            Assert.Equal(1, server.Count(TilePath.DataTile(t)));
        }

        db.ChangeTracker.Clear();
        var state = await db.CtLogStates.SingleAsync(ct);
        Assert.Equal(5 * 256, state.NextIndex);
        Assert.Equal(5 * 256, state.EntriesProcessed);
        Assert.Equal(_log.KeywordCount(0, 5 * 256), await db.CtCandidates.CountAsync(ct));
        Assert.All(await db.CollectionLog.ToListAsync(ct), r => Assert.Equal(CollectionOutcome.Success, r.Outcome));

        // Aynı aralık bir daha işlenirse (durum silindi) aday çoğalmaz.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ct_log_state", ct);
        db.ChangeTracker.Clear();
        var replay = await Follower(db, server, Fast(maxTiles: 8)).RunOnceAsync(ct);
        Assert.Equal(0, replay.NewCandidates);
        Assert.Equal(_log.KeywordCount(0, 5 * 256), await db.CtCandidates.CountAsync(ct));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Head_start_begins_at_the_current_tile_boundary_and_builds_the_frontier_from_hash_tiles()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries((5 * 256) + 1);
        using var server = new FakeCtServer(_log) { PublishedSize = (3 * 256) + 5 };

        var first = await Follower(db, server, Fast(), startAt: "head").RunOnceAsync(ct);
        Assert.Equal(CtBatchStatus.CaughtUp, first.Status);
        Assert.DoesNotContain(server.Paths, p => p.StartsWith("tile/data/", StringComparison.Ordinal));

        server.PublishedSize = _log.Count;
        var second = await Follower(db, server, Fast(), startAt: "head").RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Advanced, second.Status);
        Assert.Equal(512, second.Examined);
        Assert.Equal(0, server.Count(TilePath.DataTile(0)));
        Assert.Contains(server.Paths, p => p.StartsWith("tile/1/", StringComparison.Ordinal));
        db.ChangeTracker.Clear();
        Assert.Equal(_log.KeywordCount(768, 1280), await db.CtCandidates.CountAsync(ct));
    }

    [Fact]
    public async Task Truncated_tile_is_retried_then_skipped_and_the_skip_is_counted()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries((3 * 256) + 3);
        var broken = _log.DataTile(1)[..^50];
        using var server = new FakeCtServer(_log)
        {
            PublishedSize = _log.Count,
            Intercept = p => p == TilePath.DataTile(1)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(broken) }
                : null,
        };

        var r1 = await Follower(db, server, Fast()).RunOnceAsync(ct); // tile 0 yazılır, tile 1 hata (1. kez)
        var r2 = await Follower(db, server, Fast()).RunOnceAsync(ct); // tile 1 hata (2. kez) => atlanır
        var r3 = await Follower(db, server, Fast()).RunOnceAsync(ct); // sağ kenar hash tile'larından kurulur, tile 2 işlenir

        Assert.Equal(CtBatchStatus.Advanced, r1.Status);
        Assert.Equal(CtBatchStatus.Failed, r2.Status);
        Assert.Equal(CtBatchStatus.Advanced, r3.Status);

        db.ChangeTracker.Clear();
        var state = await db.CtLogStates.SingleAsync(ct);
        Assert.Equal(768, state.NextIndex);
        Assert.Equal(512, state.EntriesProcessed);
        Assert.Equal(256, state.EntriesSkipped);
        Assert.Equal(512.0 / 768, state.Coverage);
        Assert.False(await db.CtCandidates.AnyAsync(c => c.EntryIndex >= 256 && c.EntryIndex < 512, ct));

        var rows = await db.CollectionLog.OrderBy(r => r.Id).ToListAsync(ct);
        Assert.Collection(rows,
            r => Assert.Equal((CollectionOutcome.Success, 0L, 256L, 0), (r.Outcome, r.RangeStart!.Value, r.RangeEnd!.Value, r.SkippedItemCount)),
            r => Assert.Equal((CollectionErrorKind.InvalidResponse, 256L, 512L, 0), (r.ErrorKind, r.RangeStart!.Value, r.RangeEnd!.Value, r.SkippedItemCount)),
            r => Assert.Equal((CollectionErrorKind.InvalidResponse, 256L, 512L, 256), (r.ErrorKind, r.RangeStart!.Value, r.RangeEnd!.Value, r.SkippedItemCount)),
            r => Assert.Equal((CollectionOutcome.Success, 512L, 768L), (r.Outcome, r.RangeStart!.Value, r.RangeEnd!.Value)));
    }

    [Fact]
    public async Task Tampered_tile_fails_root_verification_and_nothing_is_stored()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries(3 * 256);
        // Yapı bozulmadan bir sertifikanın son baytı (imza) değiştirilir: Ayrıştırma geçer, yaprak özeti değişir.
        var tampered = _log.DataTile(0);
        tampered[_log.CertificateLastByteOffset(0, 5)] ^= 0xFF;
        using var server = new FakeCtServer(_log)
        {
            PublishedSize = _log.Count,
            Intercept = p => p == TilePath.DataTile(0)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tampered) }
                : null,
        };

        var result = await Follower(db, server, Fast(maxAttempts: 5)).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Failed, result.Status);
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.CtCandidates.CountAsync(ct));
        var state = await db.CtLogStates.SingleAsync(ct);
        Assert.Equal(0, state.NextIndex);
        Assert.Equal(1, state.ConsecutiveFailures);
        var row = await db.CollectionLog.SingleAsync(ct);
        Assert.Equal(CollectionErrorKind.VerificationFailed, row.ErrorKind);
        Assert.Equal((0L, 768L), (row.RangeStart!.Value, row.RangeEnd!.Value));
    }

    [Fact]
    public async Task Checkpoint_with_a_bad_signature_is_logged_and_nothing_advances()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries(300);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };
        server.CheckpointOverride = size => _log.Checkpoint(size, signer: other);

        var result = await Follower(db, server, Fast()).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Failed, result.Status);
        Assert.Equal(["checkpoint"], server.Paths);
        db.ChangeTracker.Clear();
        Assert.False(await db.CtLogStates.AnyAsync(ct));
        var row = await db.CollectionLog.SingleAsync(ct);
        Assert.Equal((CollectionOutcome.Error, CollectionErrorKind.VerificationFailed), (row.Outcome, row.ErrorKind));
        Assert.Equal("testlog", row.CtLogName);
        Assert.Null(row.RangeStart);
    }

    [Fact]
    public async Task Checkpoint_that_goes_backwards_is_detected()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries(3 * 256);
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };
        await Follower(db, server, Fast()).RunOnceAsync(ct);

        server.PublishedSize = 600;
        var result = await Follower(db, server, Fast()).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Failed, result.Status);
        db.ChangeTracker.Clear();
        var last = await db.CollectionLog.OrderBy(r => r.Id).LastAsync(ct);
        Assert.Equal(CollectionErrorKind.VerificationFailed, last.ErrorKind);
    }

    [Fact]
    public async Task Log_that_rewrites_already_signed_history_is_detected()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries(3 * 256);
        using var server = new FakeCtServer(_log) { PublishedSize = 612 };

        // Tur 1: yalnızca tile 0 okunur; 612 boyutlu checkpoint'in kökü "bekleyen" olarak saklanır.
        var first = await Follower(db, server, Fast(maxTiles: 1)).RunOnceAsync(ct);
        Assert.Equal(CtBatchStatus.Advanced, first.Status);

        // Log, imzaladığı 612 içindeki bir kaydı değiştirip yeni (kendi içinde tutarlı) bir checkpoint yayımlıyor.
        _log.Rewrite(300);
        server.PublishedSize = 768;
        var second = await Follower(db, server, Fast(maxTiles: 4)).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Failed, second.Status);
        db.ChangeTracker.Clear();
        Assert.Equal(256, (await db.CtLogStates.SingleAsync(ct)).NextIndex);
        Assert.False(await db.CtCandidates.AnyAsync(c => c.EntryIndex >= 256, ct));
        var last = await db.CollectionLog.OrderBy(r => r.Id).LastAsync(ct);
        Assert.Equal(CollectionErrorKind.VerificationFailed, last.ErrorKind);
    }

    [Fact]
    public async Task Candidate_names_never_reach_logs_or_metric_tags()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var tags = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == StaticCtMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, t, _) => Collect(t, tags));
        listener.SetMeasurementEventCallback<double>((_, _, t, _) => Collect(t, tags));
        listener.Start();

        _log.AddEntries((4 * 256) + 9);
        var broken = _log.DataTile(2)[..^10];
        using var server = new FakeCtServer(_log)
        {
            PublishedSize = _log.Count,
            Intercept = p => p == TilePath.DataTile(2)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(broken) }
                : null,
        };

        // Başarılı tur, hatalı tur, atlama ve yeniden kurulum: bütün log yolları çalışır.
        for (var i = 0; i < 4; i++)
        {
            await Follower(db, server, Fast(maxAttempts: 2)).RunOnceAsync(ct);
        }

        listener.RecordObservableInstruments();
        Assert.True(await db.CtCandidates.AnyAsync(ct), "Test anlamlı olsun diye aday saklanmış olmalı.");
        Assert.NotEmpty(_logs.Messages);
        Assert.All(_logs.Messages, m =>
        {
            Assert.DoesNotContain("ornek", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("xn--", m, StringComparison.Ordinal);
            Assert.DoesNotContain("test-ct.example", m, StringComparison.Ordinal);
        });
        Assert.NotEmpty(tags);
        Assert.All(tags, t => Assert.DoesNotContain("ornek", t, StringComparison.OrdinalIgnoreCase));
    }

    private static void Collect(ReadOnlySpan<KeyValuePair<string, object?>> tags, ConcurrentBag<string> sink)
    {
        foreach (var tag in tags)
        {
            sink.Add(tag.Key + "=" + tag.Value);
        }
    }
}

/// <summary>Biçimlenmiş bütün log mesajlarını toplar.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            owner.Messages.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
        }
    }
}
