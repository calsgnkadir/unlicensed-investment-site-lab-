using System.Diagnostics.CodeAnalysis;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.Collection;

public sealed record CollectionRunSummary(
    CollectionSource Source, int Targets, int SkippedFresh, int Succeeded, int Empty, int Failed, int NewItems, bool Cancelled);

/// <summary>
/// Bir kaynak için bir toplama turu: official_actions içindeki alan adlarını sırayla sorgular.
/// </summary>
/// <remarks>
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
    ILogger<CollectionRunner> logger)
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir alan adının beklenmeyen hatası turun geri kalanını durdurmamalı; yalnızca alan adı kimliği ve hata türü loglanır.")]
    public async Task<CollectionRunSummary> RunAsync(CollectionSource source, bool force, CancellationToken cancellationToken)
    {
        var collector = collectors.SingleOrDefault(c => c.Source == source)
            ?? throw new InvalidOperationException($"{source} için toplayıcı kayıtlı değil.");

        var targets = await db.OfficialActions
            .Select(a => new { a.DomainId, a.Domain.Host })
            .Distinct()
            .OrderBy(t => t.DomainId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fresh = force
            ? []
            : await FreshDomainIdsAsync(source, timeProvider.GetUtcNow() - collector.CacheTtl, cancellationToken).ConfigureAwait(false);

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

            db.ChangeTracker.Clear();
            CollectionLogEntry entry;
            try
            {
                entry = await collector
                    .CollectAsync(db, new CollectionTarget(target.DomainId, target.Host), options.MeasurementLocation, cancellationToken)
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
                    break;
            }

            if (entry.ErrorKind == CollectionErrorKind.Cancelled)
            {
                cancelled = true;
                break;
            }
        }

        CollectorLog.RunFinished(logger, source, succeeded, empty, failed, skipped, newItems);
        return new CollectionRunSummary(source, targets.Count, skipped, succeeded, empty, failed, newItems, cancelled);
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
