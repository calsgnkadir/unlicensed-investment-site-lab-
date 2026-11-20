using System.Diagnostics.CodeAnalysis;
using Fraud.Collectors.Monitoring;
using Fraud.Collectors.Network;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.Collection;

public sealed record CollectionRunSummary(
    CollectionSource Source, int Targets, int SkippedFresh, int Succeeded, int Empty, int Failed, int NewItems, bool Cancelled)
{
    /// <summary>Aynı kaynağı başka bir süreç topluyordu; bu tur hiç sorgu atmadan çıktı.</summary>
    public bool LockedOut { get; init; }

    /// <summary>Hedeflerin kaçı pozitif (official_actions). Yalnızca sayı.</summary>
    public int PositiveTargets { get; init; }

    /// <summary>Hedeflerin kaçı kontrol üyesi (bu tur için seçilenler). Yalnızca sayı; ad yok (Kural 4).</summary>
    public int ControlTargets { get; init; }
}

/// <summary>
/// Bir kaynak için bir toplama turu: pozitifleri (official_actions) ve kontrol üyelerini (control_members) sırayla sorgular.
/// </summary>
/// <remarks>
/// <para>Hedef kuralları (Değişiklik kaydı 2026-11-20):</para>
/// <list type="bullet">
/// <item>Pozitifler: Her kaynakta önceki gibi (<see cref="ISourceCollector.CacheTtl"/>; DNS günlük).</item>
/// <item>Kontrol üyeleri, crt.sh ve Wayback: alan adı başına BİR KEZ (başarılı ya da kesin boş sonuç bir kez yeter).</item>
/// <item>Kontrol üyeleri, DNS: haftalık; üye yalnızca kendi gününde sorulur (<see cref="Fraud.Collectors.Control.ControlDnsSchedule"/>).</item>
/// </list>
/// Önbellek: Bu kaynak bir alan adını <see cref="ISourceCollector.CacheTtl"/> içinde başarıyla ya da kesin boş
/// sonuçla sorguladıysa o alan adı atlanır. Hata sonuçları önbelleğe girmez; sonraki turda yeniden denenir.
/// İdempotentlik iki katmanlıdır: önbellek gereksiz isteği önler, tablolardaki tekil indeksler ise
/// istek tekrarlansa bile (<c>force</c>) veri çoğalmasını önler.
/// </remarks>
public sealed class CollectionRunner(
    FraudDbContext db,
    IEnumerable<ISourceCollector> collectors,
    CollectionOptions options,
    TimeProvider timeProvider,
    ILogger<CollectionRunner> logger,
    CollectionMetrics? metrics = null)
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir alan adının beklenmeyen hatası turun geri kalanını durdurmamalı; yalnızca alan adı kimliği ve hata türü loglanır.")]
    public Task<CollectionRunSummary> RunAsync(CollectionSource source, bool force, CancellationToken cancellationToken) =>
        RunAsync(source, force, null, cancellationToken);

    /// <param name="sourceLock">
    /// Ağ kaynağı kilidi (DNS). Verilirse her alan adından önce <see cref="INetworkSourceLockHandle.IsHeldAsync"/> ile
    /// denetlenir; kilit gittiyse (bağlantı koptu, başka süreç alabilir) tur o noktada iptal edilmiş gibi biter
    /// (THREAT_MODEL N9). Tamamlanan alan adlarının satırları zaten yazılmıştır.
    /// </param>
    public async Task<CollectionRunSummary> RunAsync(
        CollectionSource source, bool force, INetworkSourceLockHandle? sourceLock, CancellationToken cancellationToken)
    {
        var collector = collectors.SingleOrDefault(c => c.Source == source)
            ?? throw new InvalidOperationException($"{source} için toplayıcı kayıtlı değil.");

        var now = timeProvider.GetUtcNow();
        var positives = await NetworkTargetQuery.DomainsAsync(db, DomainSet.OfficialActions, cancellationToken).ConfigureAwait(false);
        var positiveIds = positives.Select(p => p.DomainId).ToHashSet();
        var controls = (await NetworkTargetQuery.DomainsAsync(db, DomainSet.ControlMembers, cancellationToken).ConfigureAwait(false))
            .Where(c => !positiveIds.Contains(c.DomainId))
            .ToList();
        if (source == CollectionSource.Dns)
        {
            // Kontrol DNS'i haftalık: Bugün Türkiye saatiyle bu üyenin günü değilse sorulmaz.
            var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(3)).DateTime);
            controls = [.. controls.Where(c => Fraud.Collectors.Control.ControlDnsSchedule.IsDue(c.Host, today, options.EffectiveControlDnsDays))];
        }

        var targets = positives.Concat(controls).ToList();
        var controlIds = controls.Select(c => c.DomainId).ToHashSet();

        var fresh = new HashSet<long>();
        if (!force)
        {
            fresh = await FreshDomainIdsAsync(source, now - collector.CacheTtl, cancellationToken).ConfigureAwait(false);
            if (source != CollectionSource.Dns && controlIds.Count > 0)
            {
                // crt.sh ve Wayback: kontrol üyesi bir kez toplandıysa yeterli (geçmiş veri değişmez, istek boşa gitmesin).
                var ever = await FreshDomainIdsAsync(source, DateTimeOffset.MinValue, cancellationToken).ConfigureAwait(false);
                fresh.UnionWith(ever.Where(controlIds.Contains));
            }
        }

        CollectorLog.RunStarted(logger, source, targets.Count, fresh.Count);

        int succeeded = 0, empty = 0, failed = 0, skipped = 0, newItems = 0;
        var cancelled = false;

        foreach (var target in targets)
        {
            if (fresh.Contains(target.DomainId))
            {
                skipped++;
                continue;
            }

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
            CollectionLogEntry entry;
            try
            {
                entry = await collector
                    .CollectAsync(db, target, options.MeasurementLocation, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Toplayıcı ağ ve ayrıştırma hatalarını kendisi log satırına çevirir. Buraya yalnızca yazma
                // sırasındaki hatalar (ör. veritabanı) gelir. Bir alan adının hatası turu durdurmaz;
                // log satırı yazılamadığı için bu hedef sonraki turda yeniden denenir.
                // İstisna nesnesi ve mesajı loglanmaz (alan adı veya çekilen veri içerebilir).
                failed++;
                CollectorLog.TargetFailed(logger, source, target.DomainId, CollectionErrorKind.Unexpected, null, 0);
                metrics?.TargetFailed(source, CollectionErrorKind.Unexpected);
                continue;
            }

            newItems += entry.NewItemCount;
            switch (entry.Outcome)
            {
                case CollectionOutcome.Success:
                    succeeded++;
                    break;
                case CollectionOutcome.Empty:
                    empty++;
                    break;
                default:
                    failed++;
                    CollectorLog.TargetFailed(logger, source, target.DomainId, entry.ErrorKind, entry.HttpStatus, entry.Attempts);
                    metrics?.TargetFailed(source, entry.ErrorKind);
                    break;
            }

            if (entry.ErrorKind == CollectionErrorKind.Cancelled)
            {
                cancelled = true;
                break;
            }
        }

        CollectorLog.RunFinished(logger, source, succeeded, empty, failed, skipped, newItems);
        return new CollectionRunSummary(source, targets.Count, skipped, succeeded, empty, failed, newItems, cancelled)
        {
            PositiveTargets = positives.Count,
            ControlTargets = controls.Count,
        };
    }

    private async Task<HashSet<long>> FreshDomainIdsAsync(CollectionSource source, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var ids = await db.CollectionLog
            .Where(l => l.Source == source
                        && l.DomainId != null
                        && l.FinishedAt >= since
                        && (l.Outcome == CollectionOutcome.Success || l.Outcome == CollectionOutcome.Empty))
            .Select(l => l.DomainId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. ids];
    }
}
