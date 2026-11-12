using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.OfficialActions;

/// <summary>Aynı anahtarla zaten var olan ama içeriği farklı olan satır. Mevcut kayıt değiştirilmez.</summary>
public sealed record RowConflict(int RowNumber, string Detail);

public sealed record OfficialActionImportReport(
    int ValidRows,
    int Inserted,
    int AlreadyPresent,
    int NewDomains,
    IReadOnlyList<RowConflict> Conflicts,
    IReadOnlyList<RowRejection> Rejections)
{
    public bool IsClean => Conflicts.Count == 0 && Rejections.Count == 0;
}

/// <summary>
/// Doğrulanmış satırları veritabanına yazar. İdempotenttir: aynı dosya iki kez yüklenince
/// ikinci çalıştırma hiçbir şey eklemez. Anahtar (kaynak, bülten no, alan adı) üçlüsüdür.
/// Aynı anahtar farklı tarih, kategori veya bağlantıyla gelirse kayıt sessizce ezilmez;
/// çakışma olarak raporlanır ve elle incelenir.
/// </summary>
public sealed class OfficialActionImporter(
    FraudDbContext db,
    OfficialActionCsvParser parser,
    TimeProvider timeProvider)
{
    private const OfficialActionSource Source = OfficialActionSource.SpkBulletin;

    public async Task<OfficialActionImportReport> ImportAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        var parsed = parser.Parse(reader);
        var observedAt = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var hosts = parsed.Rows.Select(r => r.Domain.Host).Distinct(StringComparer.Ordinal).ToList();
        var domains = await db.Domains
            .Where(d => hosts.Contains(d.Host))
            .ToDictionaryAsync(d => d.Host, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var newDomains = 0;
        foreach (var row in parsed.Rows)
        {
            if (!domains.ContainsKey(row.Domain.Host))
            {
                var entity = new DomainName(row.Domain, observedAt);
                domains.Add(row.Domain.Host, entity);
                db.Domains.Add(entity);
                newDomains++;
            }
        }

        // Yeni alan adlarının kimlik numaraları oluşsun diye önce bunlar kaydedilir (aynı işlem içinde).
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var domainIds = domains.Values.Select(d => d.Id).ToList();
        var existing = await db.OfficialActions
            .Where(a => a.Source == Source && domainIds.Contains(a.DomainId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byKey = existing.ToDictionary(a => (a.BulletinNumber, a.DomainId));

        var inserted = 0;
        var alreadyPresent = 0;
        var conflicts = new List<RowConflict>();

        foreach (var row in parsed.Rows)
        {
            var domain = domains[row.Domain.Host];
            var key = (row.BulletinNumber, domain.Id);

            if (byKey.TryGetValue(key, out var current))
            {
                var differences = Compare(current, row);
                if (differences.Count == 0)
                {
                    alreadyPresent++;
                }
                else
                {
                    conflicts.Add(new RowConflict(row.RowNumber, "farklı alan: " + string.Join(", ", differences)));
                }

                continue;
            }

            var action = new OfficialAction(
                domain, Source, row.BulletinNumber, row.DecisionDate, row.Category, row.SourceUrl, row.ListedAs, observedAt);
            db.OfficialActions.Add(action);
            byKey.Add(key, action);
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new OfficialActionImportReport(
            parsed.Rows.Count, inserted, alreadyPresent, newDomains, conflicts, parsed.Rejections);
    }

    private static List<string> Compare(OfficialAction current, OfficialActionRow row)
    {
        var differences = new List<string>();
        if (current.DecisionDate != row.DecisionDate)
        {
            differences.Add("karar_tarihi");
        }

        if (current.Category != row.Category)
        {
            differences.Add("kategori");
        }

        if (!string.Equals(current.SourceUrl, row.SourceUrl.AbsoluteUri, StringComparison.Ordinal))
        {
            differences.Add("kaynak_url");
        }

        return differences;
    }
}
