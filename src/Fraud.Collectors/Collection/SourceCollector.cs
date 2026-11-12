using System.Diagnostics.CodeAnalysis;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Collection;

/// <summary>Toplanacak alan adı: kimlik numarası ve ASCII adı. Ad yalnızca sorgu adresinde kullanılır, loglanmaz.</summary>
public sealed record CollectionTarget(long DomainId, string Host);

public interface ISourceCollector
{
    CollectionSource Source { get; }

    TimeSpan CacheTtl { get; }

    /// <summary>Kaynağı sorgular, sonucu ve collection_log satırını tek işlemde yazar.</summary>
    Task<CollectionLogEntry> CollectAsync(
        FraudDbContext db, CollectionTarget target, string measurementLocation, CancellationToken cancellationToken);
}

/// <summary>
/// Bütün kaynaklar için aynı akış: sorgula, sonra veriyi ve log satırını aynı veritabanı işleminde yaz.
/// Alt sınıflar yalnızca "nasıl sorgulanır" ve "nasıl saklanır" kısmını yazar.
/// </summary>
/// <remarks>
/// Ağ isteği veritabanı işlemi dışında yapılır (işlem uzun süre açık kalmasın diye).
/// Veri ve log aynı işlemde yazıldığı için "veri var ama log yok" ya da tersi oluşmaz.
/// </remarks>
public abstract class SourceCollector<T>(TimeProvider timeProvider) : ISourceCollector
{
    public abstract CollectionSource Source { get; }

    public abstract TimeSpan CacheTtl { get; }

    protected abstract Task<SourceResult<T>> FetchAsync(string host, CancellationToken cancellationToken);

    /// <summary>Kayıtları çoğaltmadan yazar ve ilk kez eklenen kayıt sayısını döner.</summary>
    protected abstract Task<int> StoreAsync(
        FraudDbContext db, long domainId, IReadOnlyList<T> items, DateTimeOffset observedAt, CancellationToken cancellationToken);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Güvenilmez yanıttan çıkan her istisna collection_log'a hata olarak yazılmalı; tur durmamalı.")]
    public async Task<CollectionLogEntry> CollectAsync(
        FraudDbContext db, CollectionTarget target, string measurementLocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(target);

        var startedAt = timeProvider.GetUtcNow();
        SourceResult<T> result;
        try
        {
            result = await FetchAsync(target.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = SourceResult.Failed<T>(CollectionErrorKind.Cancelled, null, 0);
        }
        catch (OperationCanceledException)
        {
            // Dışarıdan iptal yok ama bir iç süre sınırı doldu.
            result = SourceResult.Failed<T>(CollectionErrorKind.Timeout, null, 0);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Kaynak yanıtı güvenilmezdir; ayrıştırıcıda gözden kaçan bir istisna (bozuk sıkıştırma,
            // geçersiz UTF-8, yinelenen sütun ...) turu durdurmamalı. Sonuç "kayıt yok" değil, hata olarak yazılır.
            // İstisna mesajı yazılmaz: sorgu adresi, yani alan adı içerebilir.
            result = SourceResult.Failed<T>(CollectionErrorKind.InvalidResponse, null, 0);
        }

        // İptal edilse bile log satırı yazılsın diye buradan sonrası iptal belirtecini kullanmaz.
        var finishedAt = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);

        var newItems = result.Items.Count > 0
            ? await StoreAsync(db, target.DomainId, result.Items, finishedAt, CancellationToken.None).ConfigureAwait(false)
            : 0;

        var entry = new CollectionLogEntry(
            Source,
            target.DomainId,
            measurementLocation,
            startedAt,
            finishedAt,
            result.HttpStatus,
            result.Attempts,
            result.Outcome,
            result.ErrorKind,
            result.Items.Count,
            result.SkippedItems,
            newItems,
            result.Truncated);
        db.CollectionLog.Add(entry);
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return entry;
    }
}
