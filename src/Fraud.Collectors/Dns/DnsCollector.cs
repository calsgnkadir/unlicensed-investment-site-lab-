using System.Diagnostics.CodeAnalysis;
using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Monitoring;
using Fraud.Collectors.Network;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Bir alan adını her çözücüye, her yoldan ve her kayıt türüyle sorar; sonuçları dns_snapshots tablosuna,
/// özeti collection_log tablosuna yazar. <see cref="CollectionRunner"/> tarafından çalıştırılır.
/// </summary>
/// <remarks>
/// <para>
/// Bir alan adı için varsayılan: 3 tür (A, AAAA, NS) x 2 çözücü x 3 yol (UDP, TCP, DoH) = 18 sorgu, saniyede en fazla
/// 1 paket. Sorgular "tür > çözücü > yol" sırasıyla gider; aynı sorunun UDP ve DoH cevabı birkaç saniye arayla alınır,
/// böylece karşılaştırma zamandan çok az etkilenir.
/// </para>
/// <para>
/// collection_log satırı: Ağ hatası, alan adının "kaydı yok" olduğu anlamına gelmez. <c>item_count</c> bilinen bir
/// cevap alan sorgu sayısıdır (Success, Empty, NxDomain); <c>skipped_item_count</c> hata alan sorgu sayısıdır.
/// En az bir bilinen cevap varsa satır Success olur, yoksa en sık görülen hata türüyle Error. DNS'te HTTP kodu yoktur.
/// </para>
/// </remarks>
/// <param name="metrics">İsteğe bağlı DNS metrikleri; <paramref name="collectionMetrics"/> tekrar denemeleri sayar.</param>
public sealed class DnsCollector(
    IDnsQueryClient client,
    DnsOptions options,
    TimeProvider timeProvider,
    DnsMetrics? metrics = null,
    CollectionMetrics? collectionMetrics = null) : ISourceCollector
{
    public CollectionSource Source => CollectionSource.Dns;

    public TimeSpan CacheTtl => options.CacheTtl;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir sorgunun beklenmeyen hatası, alan adının diğer ölçümlerini kaybettirmemeli; hata satırı olarak yazılır.")]
    public async Task<CollectionLogEntry> CollectAsync(
        FraudDbContext db, CollectionTarget target, string measurementLocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(target);

        // DNS kayıtları Collection:MeasurementLocation değil Dns:Location ile yazılır (Program başlarken doğrular).
        var location = options.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new InvalidOperationException("Dns:Location tanımlı değil.");
        }

        var resolvers = DnsResolverCatalog.Select(options.EffectiveResolvers);
        var blockIps = options.EffectiveBlockPageIps;
        var startedAt = timeProvider.GetUtcNow();
        var snapshots = new List<DnsSnapshot>();
        var cancelled = false;

        foreach (var type in options.EffectiveQueryTypes)
        {
            foreach (var resolver in resolvers)
            {
                foreach (var transport in options.EffectiveTransports)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    var queriedAt = timeProvider.GetUtcNow();
                    DnsQueryResult result;
                    try
                    {
                        result = await client.QueryAsync(resolver, transport, target.Host, type, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // İstemcinin sözleşmesi "yalnızca iptalde istisna"; yine de gözden kaçan bir istisna (THREAT_MODEL N2)
                        // bu alan adının o ana kadarki ölçümlerini ve log satırını düşürmemeli. Sorgu hata satırı olarak
                        // yazılır ve tur devam eder. İstisna mesajı yazılmaz (alan adı içerebilir).
                        var latency = timeProvider.GetUtcNow() - queriedAt;
                        result = new DnsQueryResult(DnsOutcome.Error, DnsErrorKind.Unexpected, null, [], null, false, 0,
                            latency < TimeSpan.Zero ? TimeSpan.Zero : latency);
                    }

                    var snapshot = ToSnapshot(target.DomainId, queriedAt, location, resolver, transport, type, result, blockIps);
                    snapshots.Add(snapshot);

                    // Metrikte yalnızca çözücü adı, yol ve sonuç türü var; sorgulanan ad ve cevap yok.
                    metrics?.Query(resolver.Name, transport, result, snapshot.BlockMarker);
                    collectionMetrics?.Retried(CollectionSource.Dns, result.Attempts - 1);
                }

                if (cancelled)
                {
                    break;
                }
            }

            if (cancelled)
            {
                break;
            }
        }

        // İptal edilse bile o ana kadarki ölçümler ve log satırı yazılsın diye buradan sonra iptal belirteci kullanılmaz.
        var finishedAt = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        var stored = 0;
        foreach (var snapshot in snapshots)
        {
            stored += await InsertAsync(db, snapshot).ConfigureAwait(false);
        }

        var entry = BuildLogEntry(target.DomainId, location, startedAt, finishedAt, snapshots, stored, cancelled);
        db.CollectionLog.Add(entry);
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return entry;
    }

    /// <summary>Sorgu sonucunu kayda çevirir; engel işaretini burada koyar.</summary>
    public static DnsSnapshot ToSnapshot(
        long domainId,
        DateTimeOffset queriedAt,
        string location,
        DnsResolver resolver,
        DnsTransport transport,
        DnsRecordType type,
        DnsQueryResult result,
        IReadOnlySet<IPAddress> blockPageIps)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(blockPageIps);

        // Engel işareti yalnızca adres içeren başarılı cevapta aranır. Böyle bir cevap barındırma bilgisi sayılmaz.
        // AAAA cevabındaki ::ffff:a.b.c.d yazımı da düz IPv4'e çevrilip aranır (THREAT_MODEL N4).
        var blockMarker = result.Outcome == DnsOutcome.Success
            && result.Answers.Any(a => a.Type is "A" or "AAAA"
                                       && IPAddress.TryParse(a.Value, out var ip)
                                       && blockPageIps.Contains(NetworkAddress.Canonical(ip)));

        var latencyMs = (int)Math.Clamp(result.Latency.TotalMilliseconds, 0, int.MaxValue);
        return new DnsSnapshot(
            domainId, queriedAt, location, resolver.Name, transport, type.ToString(), result.Rcode, result.Outcome,
            result.ErrorKind, result.Answers, result.NegativeTtl, result.Truncated, latencyMs, result.Attempts, blockMarker);
    }

    /// <summary>
    /// Kaydı ekler. Aynı gün aynı soru zaten varsa: Önceki satır hataysa yenisiyle değiştirilir, değilse dokunulmaz.
    /// Böylece aynı iş iki kez çalışınca veri çoğalmaz, ama sabah alınan hata öğleden sonraki başarılı ölçümü engellemez.
    /// </summary>
    /// <returns>Eklenen veya değiştirilen satır sayısı (0 ya da 1).</returns>
    private static Task<int> InsertAsync(FraudDbContext db, DnsSnapshot s)
    {
        var answers = DnsAnswerJson.Serialize(s.Answers);
        var transport = s.Transport.ToString();
        var outcome = s.Outcome.ToString();
        var errorKind = s.ErrorKind.ToString();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dns_snapshots
                (domain_id, queried_at, measured_on, location, resolver, transport, query_type, rcode, outcome, error_kind,
                 answers, negative_ttl, truncated, latency_ms, attempts, block_marker)
            VALUES
                ({s.DomainId}, {s.QueriedAt}, {s.MeasuredOn}, {s.Location}, {s.Resolver}, {transport}, {s.QueryType}, {s.Rcode},
                 {outcome}, {errorKind}, CAST({answers} AS jsonb), {s.NegativeTtl}, {s.Truncated}, {s.LatencyMs}, {s.Attempts},
                 {s.BlockMarker})
            ON CONFLICT (domain_id, location, resolver, transport, query_type, measured_on) DO UPDATE SET
                queried_at = EXCLUDED.queried_at, rcode = EXCLUDED.rcode, outcome = EXCLUDED.outcome,
                error_kind = EXCLUDED.error_kind, answers = EXCLUDED.answers, negative_ttl = EXCLUDED.negative_ttl,
                truncated = EXCLUDED.truncated, latency_ms = EXCLUDED.latency_ms, attempts = EXCLUDED.attempts,
                block_marker = EXCLUDED.block_marker
            WHERE dns_snapshots.outcome = 'Error'
            """, CancellationToken.None);
    }

    private static CollectionLogEntry BuildLogEntry(
        long domainId, string location, DateTimeOffset startedAt, DateTimeOffset finishedAt,
        List<DnsSnapshot> snapshots, int stored, bool cancelled)
    {
        var answered = snapshots.Count(s => s.Outcome != DnsOutcome.Error);
        var failed = snapshots.Count - answered;
        var attempts = snapshots.Sum(s => s.Attempts);

        CollectionOutcome outcome;
        CollectionErrorKind errorKind;
        if (cancelled)
        {
            (outcome, errorKind) = (CollectionOutcome.Error, CollectionErrorKind.Cancelled);
        }
        else if (answered > 0)
        {
            (outcome, errorKind) = (CollectionOutcome.Success, CollectionErrorKind.None);
        }
        else
        {
            // Hiç bilinen cevap yok (ya da hiç sorgu yapılamadı): en sık hata türü.
            var dominant = snapshots.Count == 0
                ? DnsErrorKind.Timeout
                : snapshots.GroupBy(s => s.ErrorKind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            (outcome, errorKind) = (CollectionOutcome.Error, ToCollectionError(dominant));
        }

        return new CollectionLogEntry(
            CollectionSource.Dns, domainId, location, startedAt, finishedAt, httpStatus: null, attempts,
            outcome, errorKind, answered, failed, stored, truncated: false);
    }

    /// <summary>DNS hata türünü collection_log'daki genel hata türüne çevirir. Ayrıntı dns_snapshots'ta kalır.</summary>
    public static CollectionErrorKind ToCollectionError(DnsErrorKind kind) => kind switch
    {
        DnsErrorKind.Timeout => CollectionErrorKind.Timeout,
        DnsErrorKind.NetworkError => CollectionErrorKind.NetworkError,
        DnsErrorKind.ServerFailure => CollectionErrorKind.ServerError,
        DnsErrorKind.Refused or DnsErrorKind.OtherRcode or DnsErrorKind.HttpError => CollectionErrorKind.ClientError,
        DnsErrorKind.ResponseTooLarge => CollectionErrorKind.ResponseTooLarge,
        DnsErrorKind.Cancelled => CollectionErrorKind.Cancelled,
        DnsErrorKind.Unexpected => CollectionErrorKind.Unexpected,
        _ => CollectionErrorKind.InvalidResponse, // Malformed, IdMismatch, QuestionMismatch, UnrelatedAnswer
    };
}
