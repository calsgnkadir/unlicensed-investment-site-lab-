using System.Globalization;
using System.Text;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Quality;

/// <summary>
/// Bir kaynağın veri kalitesi. Her hedef alan adı tam olarak bir gruba düşer:
/// veri var, kesin boş, yalnızca hata (durum bilinmiyor), hiç denenmedi.
/// </summary>
public sealed record SourceQuality(
    CollectionSource Source,
    int DomainsWithData,
    int DomainsConfirmedEmpty,
    int DomainsOnlyErrors,
    int DomainsNeverAttempted,
    int LogRows,
    int ErrorRows,
    int TruncatedRows,
    long StoredItems,
    IReadOnlyDictionary<CollectionErrorKind, int> ErrorsByKind)
{
    /// <summary>Log satırlarında hata oranı. Hiç satır yoksa <c>null</c>.</summary>
    public double? ErrorRate => LogRows == 0 ? null : (double)ErrorRows / LogRows;

    /// <summary>Durumu bilinmeyen alan adları: yalnızca hata alınmış ya da hiç sorulmamış. "Kayıt yok" sayılmaz.</summary>
    public int DomainsUnknown => DomainsOnlyErrors + DomainsNeverAttempted;
}

public sealed record QualityReport(DateTimeOffset GeneratedAt, int TargetDomains, IReadOnlyList<SourceQuality> Sources)
{
    /// <summary>Konsol metni. Yalnızca sayılar; alan adı yazılmaz.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(c, $"Veri kalitesi raporu ({GeneratedAt:yyyy-MM-dd HH:mm} UTC)");
        sb.AppendLine(c, $"Hedef alan adı (official_actions): {TargetDomains}");
        foreach (var s in Sources)
        {
            sb.AppendLine();
            sb.AppendLine(c, $"[{s.Source}]");
            sb.AppendLine(c, $"  Veri var:               {s.DomainsWithData}");
            sb.AppendLine(c, $"  Kesin boş (2xx, 0 satır): {s.DomainsConfirmedEmpty}");
            sb.AppendLine(c, $"  Yalnızca hata:          {s.DomainsOnlyErrors}");
            sb.AppendLine(c, $"  Hiç denenmedi:          {s.DomainsNeverAttempted}");
            sb.AppendLine(c, $"  Saklanan kayıt:         {s.StoredItems}");
            sb.AppendLine(c, $"  Log satırı:             {s.LogRows} (hata {s.ErrorRows}, oran {(s.ErrorRate is { } r ? r.ToString("P1", c) : "-")})");
            sb.AppendLine(c, $"  Sayfa sınırında kesilen: {s.TruncatedRows}");
            foreach (var (kind, count) in s.ErrorsByKind.OrderBy(k => k.Key))
            {
                sb.AppendLine(c, $"    {kind}: {count}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Not: \"Yalnızca hata\" ve \"Hiç denenmedi\" grupları bilinmeyen durumdur; \"kayıt yok\" sayılmaz.");
        return sb.ToString();
    }
}

/// <summary>Veritabanındaki toplama sonuçlarından <see cref="QualityReport"/> üretir.</summary>
public sealed class QualityReportBuilder(FraudDbContext db, TimeProvider timeProvider)
{
    public async Task<QualityReport> BuildAsync(CancellationToken cancellationToken)
    {
        var targets = (await db.OfficialActions.Select(a => a.DomainId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet();

        var withCertificates = await db.CertificateDomains.Select(x => x.DomainId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var certificateCount = await db.CertificateDomains.Select(x => x.CertificateId).Distinct()
            .LongCountAsync(cancellationToken).ConfigureAwait(false);

        var withSnapshots = await db.ArchiveSnapshots.Select(x => x.DomainId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var snapshotCount = await db.ArchiveSnapshots.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var sources = new List<SourceQuality>
        {
            await BuildSourceAsync(CollectionSource.CrtSh, targets, withCertificates, certificateCount, cancellationToken).ConfigureAwait(false),
            await BuildSourceAsync(CollectionSource.WaybackCdx, targets, withSnapshots, snapshotCount, cancellationToken).ConfigureAwait(false),
        };

        return new QualityReport(timeProvider.GetUtcNow(), targets.Count, sources);
    }

    private async Task<SourceQuality> BuildSourceAsync(
        CollectionSource source, HashSet<long> targets, List<long> domainsWithItems, long storedItems, CancellationToken cancellationToken)
    {
        var logs = await db.CollectionLog
            .Where(l => l.Source == source)
            .Select(l => new { l.DomainId, l.Outcome, l.ErrorKind, l.Truncated })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var withData = domainsWithItems.Where(targets.Contains).ToHashSet();
        var answered = logs.Where(l => l.Outcome != CollectionOutcome.Error && l.DomainId is not null)
            .Select(l => l.DomainId!.Value).ToHashSet();
        var attempted = logs.Where(l => l.DomainId is not null).Select(l => l.DomainId!.Value).ToHashSet();

        int confirmedEmpty = 0, onlyErrors = 0, neverAttempted = 0;
        foreach (var id in targets.Where(t => !withData.Contains(t)))
        {
            if (answered.Contains(id))
            {
                confirmedEmpty++;
            }
            else if (attempted.Contains(id))
            {
                onlyErrors++;
            }
            else
            {
                neverAttempted++;
            }
        }

        var errors = logs.Where(l => l.Outcome == CollectionOutcome.Error).ToList();
        return new SourceQuality(
            source,
            withData.Count,
            confirmedEmpty,
            onlyErrors,
            neverAttempted,
            logs.Count,
            errors.Count,
            logs.Count(l => l.Truncated),
            storedItems,
            errors.GroupBy(l => l.ErrorKind).ToDictionary(g => g.Key, g => g.Count()));
    }
}
