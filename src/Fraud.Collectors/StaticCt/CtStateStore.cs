using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.StaticCt;

/// <summary>İzleme durumunun, adayların ve collection_log satırlarının saklandığı yer.</summary>
public interface ICtStateStore
{
    Task<CtLogState?> LoadAsync(string logName, CancellationToken cancellationToken);

    /// <summary>
    /// Adayları çoğaltmadan yazar, sonra <paramref name="finish"/> ile (yeni aday sayısını vererek) durum ve log satırlarını
    /// hazırlatır ve hepsini tek veritabanı işleminde kaydeder. "Aday yazıldı ama indeks ilerlemedi" ya da tersi oluşmaz.
    /// </summary>
    /// <param name="state"><see cref="LoadAsync"/> ile okunan nesne; yeni ise <paramref name="isNew"/> doğru.</param>
    Task CommitAsync(
        CtLogState? state,
        bool isNew,
        IReadOnlyList<CtCandidate> candidates,
        Func<int, IReadOnlyList<CollectionLogEntry>> finish,
        CancellationToken cancellationToken);
}

/// <summary>PostgreSQL üzerinde <see cref="ICtStateStore"/>. Bir tur için bir örnek (DI kapsamı) kullanılır.</summary>
public sealed class EfCtStateStore(FraudDbContext db) : ICtStateStore
{
    public Task<CtLogState?> LoadAsync(string logName, CancellationToken cancellationToken) =>
        db.CtLogStates.SingleOrDefaultAsync(s => s.LogName == logName, cancellationToken);

    /// <remarks>
    /// Aday anahtarı (log_name, entry_index). Aynı kayıt ikinci kez gelirse <c>ON CONFLICT DO NOTHING</c> ile atlanır;
    /// etkilenen satır sayısı yalnızca yeni adayları sayar. İşlem iptal belirteci kullanmaz: Başlamış bir yazma
    /// yarıda kesilmez (iptal, işlemin tamamen yazılması ya da hiç yazılmaması arasında seçim yapmaz).
    /// </remarks>
    public async Task CommitAsync(
        CtLogState? state,
        bool isNew,
        IReadOnlyList<CtCandidate> candidates,
        Func<int, IReadOnlyList<CollectionLogEntry>> finish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(finish);

        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        var inserted = 0;
        foreach (var c in candidates)
        {
            inserted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ct_candidates
                    (log_name, entry_index, entry_type, logged_at, not_before, not_after, issuer, san_names, matched_terms, observed_at)
                VALUES
                    ({c.LogName}, {c.EntryIndex}, {c.EntryType.ToString()}, {c.LoggedAt}, {c.NotBefore}, {c.NotAfter}, {c.Issuer},
                     {c.SanNames}, {c.MatchedTerms}, {c.ObservedAt})
                ON CONFLICT (log_name, entry_index) DO NOTHING
                """, CancellationToken.None).ConfigureAwait(false);
        }

        var entries = finish(inserted);
        if (isNew && state is not null)
        {
            db.CtLogStates.Add(state);
        }

        db.CollectionLog.AddRange(entries);
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
