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
    /// <param name="neutralSamples">Tarafsız örneğe giren kayıtlar, log indeksine göre artan sırada.</param>
    /// <returns>Tarafsız örneğe ilk kez giren kayıtlı alan adı sayısı.</returns>
    Task<int> CommitAsync(
        CtLogState? state,
        bool isNew,
        IReadOnlyList<CtCandidate> candidates,
        IReadOnlyList<CtNeutralSample> neutralSamples,
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
    public async Task<int> CommitAsync(
        CtLogState? state,
        bool isNew,
        IReadOnlyList<CtCandidate> candidates,
        IReadOnlyList<CtNeutralSample> neutralSamples,
        Func<int, IReadOnlyList<CollectionLogEntry>> finish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(neutralSamples);
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

        var newNeutralDomains = 0;
        foreach (var sample in neutralSamples)
        {
            newNeutralDomains += await UpsertNeutralSampleAsync(sample).ConfigureAwait(false);
        }

        var entries = finish(inserted);
        if (isNew && state is not null)
        {
            db.CtLogStates.Add(state);
        }

        db.CollectionLog.AddRange(entries);
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return newNeutralDomains;
    }

    /// <summary>
    /// Tarafsız örnek: alan adı başına tek satır. Yeni ad eklenir (1 döner); var olan adın sayacı artar (0 döner).
    /// </summary>
    /// <remarks>
    /// <para>
    /// İki ayrı, düz SQL cümlesi kullanılır, çünkü okuması kolay: Önce "yoksa ekle", eklenmediyse "sayacı artır".
    /// <c>ON CONFLICT DO NOTHING</c>, başka bir log izleyicisi aynı adı aynı anda eklese bile çift satır açmaz
    /// (tekil indeks ux_ct_neutral_sample_domain).
    /// </para>
    /// <para>
    /// İdempotentlik: Sayaç, yalnızca gelen kayıt aynı logda son sayılan kayıttan SONRA ise artar. Aynı aralık iki kez
    /// işlenirse (ör. durum elle geri alınırsa) aynı kayıt ikinci kez sayılmaz. Normal akışta bu durum zaten olmaz:
    /// Sayaç artışı, indeksin ilerlemesiyle aynı veritabanı işleminde yazılır.
    /// </para>
    /// </remarks>
    private async Task<int> UpsertNeutralSampleAsync(CtNeutralSample s)
    {
        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ct_neutral_sample
                (registrable_domain, first_seen_at, first_logged_at, first_not_before, first_log_name, first_entry_index,
                 last_seen_at, last_log_name, last_entry_index, seen_count)
            VALUES
                ({s.RegistrableDomain}, {s.FirstSeenAt}, {s.FirstLoggedAt}, {s.FirstNotBefore}, {s.FirstLogName}, {s.FirstEntryIndex},
                 {s.LastSeenAt}, {s.LastLogName}, {s.LastEntryIndex}, 1)
            ON CONFLICT (registrable_domain) DO NOTHING
            """, CancellationToken.None).ConfigureAwait(false);
        if (added == 1)
        {
            return 1;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE ct_neutral_sample
            SET seen_count = seen_count + 1,
                last_seen_at = {s.LastSeenAt},
                last_log_name = {s.LastLogName},
                last_entry_index = {s.LastEntryIndex}
            WHERE registrable_domain = {s.RegistrableDomain}
              AND NOT (last_log_name = {s.LastLogName} AND last_entry_index >= {s.LastEntryIndex})
            """, CancellationToken.None).ConfigureAwait(false);
        return 0;
    }
}
