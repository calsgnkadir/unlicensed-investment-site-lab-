using System.Diagnostics.CodeAnalysis;
using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Monitoring;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Fetching;
using Fraud.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.Network;

/// <summary>Hangi IP'lerin dış kaynağa gönderilebileceği. Üretimde yalnızca herkese açık adresler.</summary>
public sealed class IpTargetFilter(Func<IPAddress, bool> isAllowed)
{
    public static IpTargetFilter PublicOnly { get; } = new(IpAddressPolicy.IsPublic);

    public Func<IPAddress, bool> IsAllowed => isAllowed;
}

/// <summary>
/// IP tabanlı bir kaynak için bir tur: DNS ölçümlerinden IP'leri alır, son 7 günde sorulmuş olanları atlar,
/// kalanları tek tek sorar. Sıralı çalışır (eşzamanlılık 1); hız sınırı kaynağın HTTP istemcisindedir.
/// </summary>
/// <remarks>
/// collection_log'a IP başına değil tur başına bir satır yazılır (<c>domain_id</c> boş, Static CT'deki gibi):
/// collection_log'da IP sütunu yok, IP'nin kendisi de loga girmez. Ayrıntı her IP için kaynak tablosundadır.
/// Satır: <c>item_count</c> cevap alınan IP (Success + Empty), <c>skipped_item_count</c> hata alan IP,
/// <c>new_item_count</c> yazılan satır. Hiç IP sorulmadıysa satır yazılmaz (yalnızca log mesajı).
/// </remarks>
public sealed class IpCollectionRunner(
    FraudDbContext db,
    IEnumerable<IIpSourceCollector> collectors,
    CollectionOptions options,
    IpTargetFilter filter,
    TimeProvider timeProvider,
    ILogger<IpCollectionRunner> logger,
    CollectionMetrics? metrics = null)
{
    public Task<CollectionRunSummary> RunAsync(CollectionSource source, bool force, CancellationToken cancellationToken) =>
        RunAsync(source, force, null, cancellationToken);

    /// <param name="sourceLock">
    /// Ağ kaynağı kilidi. Verilirse her IP'den önce <see cref="INetworkSourceLockHandle.IsHeldAsync"/> ile denetlenir;
    /// kilit gittiyse tur iptal edilmiş gibi biter ve collection_log satırı (Cancelled) yine yazılır (THREAT_MODEL N9).
    /// </param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir IP'nin beklenmeyen hatası turu durdurmamalı; tur yine collection_log satırını yazar.")]
    public async Task<CollectionRunSummary> RunAsync(
        CollectionSource source, bool force, INetworkSourceLockHandle? sourceLock, CancellationToken cancellationToken)
    {
        var collector = collectors.SingleOrDefault(c => c.Source == source)
            ?? throw new InvalidOperationException($"{source} için IP toplayıcısı kayıtlı değil.");

        var now = timeProvider.GetUtcNow();
        var ips = await NetworkTargetQuery
            .ResolvedIpsAsync(db, DomainSet.OfficialActions, now - collector.DnsLookback, filter.IsAllowed, cancellationToken)
            .ConfigureAwait(false);
        var fresh = force
            ? []
            : await collector.RecentlyAnsweredAsync(db, now - collector.CacheTtl, cancellationToken).ConfigureAwait(false);

        var pending = ips.Where(ip => !fresh.Contains(ip)).ToList();
        CollectorLog.RunStarted(logger, source, ips.Count, ips.Count - pending.Count);

        var startedAt = timeProvider.GetUtcNow();
        int succeeded = 0, empty = 0, failed = 0, stored = 0, attempts = 0;
        var errors = new Dictionary<CollectionErrorKind, int>();
        var cancelled = false;

        foreach (var ip in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            if (sourceLock is not null && !await sourceLock.IsHeldAsync(cancellationToken).ConfigureAwait(false))
            {
                CollectorLog.LockLost(logger, source);
                cancelled = true;
                break;
            }

            db.ChangeTracker.Clear();
            IpCollectResult result;
            try
            {
                result = await collector.CollectAsync(db, ip, options.MeasurementLocation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Ör. o IP'nin satırı yazılırken veritabanı hatası. Tur durmaz; IP hata sayılır, sonraki turda
                // yeniden sorulur (satırı yazılamadığı için önbellekte görünmez). Mesaj ve IP loglanmaz.
                result = new IpCollectResult(CollectionOutcome.Error, CollectionErrorKind.Unexpected, 0, false);
            }

            attempts += result.Attempts;
            stored += result.Stored ? 1 : 0;
            switch (result.Outcome)
            {
                case CollectionOutcome.Success:
                    succeeded++;
                    break;
                case CollectionOutcome.Empty:
                    empty++;
                    break;
                default:
                    failed++;
                    errors[result.ErrorKind] = errors.GetValueOrDefault(result.ErrorKind) + 1;
                    metrics?.TargetFailed(source, result.ErrorKind);
                    break;
            }
        }

        var answered = succeeded + empty;
        if (answered + failed > 0 || cancelled)
        {
            var (outcome, kind) = cancelled
                ? (CollectionOutcome.Error, CollectionErrorKind.Cancelled)
                : answered > 0
                    ? (CollectionOutcome.Success, CollectionErrorKind.None)
                    : (CollectionOutcome.Error, errors.OrderByDescending(e => e.Value).ThenBy(e => e.Key).First().Key);

            // Önceki bir yazma hatası bağlamda yarım değişiklik bırakmış olabilir; log satırı temiz bağlamla yazılır.
            db.ChangeTracker.Clear();
            db.CollectionLog.Add(new CollectionLogEntry(
                source, domainId: null, options.MeasurementLocation, startedAt, timeProvider.GetUtcNow(), httpStatus: null,
                attempts, outcome, kind, answered, failed, stored, truncated: false));
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        CollectorLog.RunFinished(logger, source, succeeded, empty, failed, ips.Count - pending.Count, stored);
        return new CollectionRunSummary(source, ips.Count, ips.Count - pending.Count, succeeded, empty, failed, stored, cancelled);
    }
}

/// <summary>
/// Kaynağa göre doğru çalıştırıcıyı seçer (worker ve komutlar için tek giriş). Ağ kaynakları (DNS, RIPEstat,
/// InternetDB) için önce süreçler arası kilit alınır.
/// </summary>
/// <remarks>
/// Hız sınırlayıcı süreç içidir. Servis çalışırken elle bir <c>collect-dns</c> başlatılırsa iki süreç aynı çözücülere
/// saniyede 2 paket gönderirdi (THREAT_MODEL N8). Bu yüzden kaynak başına bir PostgreSQL advisory lock
/// (<see cref="NetworkSourceLock"/>) alınır; alınamazsa hiç sorgu atılmadan çıkılır.
/// </remarks>
/// <param name="metrics">İsteğe bağlı: tur sonucu, zamanı ve süresi.</param>
/// <param name="dnsMetrics">İsteğe bağlı: DNS "son tur" göstergeleri ve UDP-DoH karşılaştırması.</param>
/// <param name="dnsCompare">DNS turundan sonra karşılaştırmayı hesaplar (veritabanından, sorgu atmaz).</param>
/// <param name="dnsOptions">Karşılaştırmanın konumu (<see cref="DnsOptions.Location"/>).</param>
public sealed class CollectionDispatcher(
    CollectionRunner domainRunner,
    IpCollectionRunner ipRunner,
    NetworkSourceLock sourceLock,
    ILogger<CollectionDispatcher> logger,
    CollectionMetrics? metrics = null,
    DnsMetrics? dnsMetrics = null,
    DnsCompareReportBuilder? dnsCompare = null,
    DnsOptions? dnsOptions = null)
{
    public async Task<CollectionRunSummary> RunAsync(CollectionSource source, bool force, CancellationToken cancellationToken)
    {
        metrics?.RunStarted(source);
        var summary = await RunCoreAsync(source, force, cancellationToken).ConfigureAwait(false);
        metrics?.RunFinished(summary);
        return summary;
    }

    private async Task<CollectionRunSummary> RunCoreAsync(CollectionSource source, bool force, CancellationToken cancellationToken)
    {
        if (source is CollectionSource.CrtSh or CollectionSource.WaybackCdx)
        {
            return await domainRunner.RunAsync(source, force, cancellationToken).ConfigureAwait(false);
        }

        if (source is not (CollectionSource.Dns or CollectionSource.RipeStat or CollectionSource.InternetDb))
        {
            throw new InvalidOperationException(source + " worker ile çalıştırılamaz (ct-follow ayrı bir komuttur).");
        }

        var held = await sourceLock.TryAcquireAsync(source, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            CollectorLog.SourceLocked(logger, source);
            return new CollectionRunSummary(source, 0, 0, 0, 0, 0, 0, Cancelled: false) { LockedOut = true };
        }

        await using (held.ConfigureAwait(false))
        {
            // Kilit tur boyunca her hedeften önce yeniden denetlenir (bağlantı koparsa kilit gider).
            if (source != CollectionSource.Dns)
            {
                return await ipRunner.RunAsync(source, force, held, cancellationToken).ConfigureAwait(false);
            }

            dnsMetrics?.RunStarted();
            var summary = await domainRunner.RunAsync(source, force, held, cancellationToken).ConfigureAwait(false);
            dnsMetrics?.RunFinished(summary.Cancelled);
            await UpdateDnsComparisonAsync(summary, cancellationToken).ConfigureAwait(false);
            return summary;
        }
    }

    /// <summary>
    /// Son ölçüm gününün UDP-DoH karşılaştırmasını metriğe yazar (yalnızca sayılar). Veritabanını okur, sorgu atmaz.
    /// Hata turu bozmasın diye yalnızca istisna türü loglanır.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Metrik hesabı toplama turunun sonucunu değiştirmemeli; yalnızca istisna türü loglanır.")]
    private async Task UpdateDnsComparisonAsync(CollectionRunSummary summary, CancellationToken cancellationToken)
    {
        if (dnsMetrics is null || dnsCompare is null || summary.Cancelled || string.IsNullOrWhiteSpace(dnsOptions?.Location))
        {
            return;
        }

        try
        {
            var report = await dnsCompare.BuildAsync(dnsOptions.Location, day: null, cancellationToken).ConfigureAwait(false);
            dnsMetrics.Comparison(report.Resolvers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Kapanış: karşılaştırma bir sonraki turda hesaplanır.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CollectorLog.RunCrashed(logger, CollectionSource.Dns, ex.GetType().Name);
        }
    }
}
