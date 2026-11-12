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

/// <summary>
/// Bir Static CT logunun kapsaması. Yalnızca sayılar; aday adı içermez (Kural 4).
/// </summary>
/// <param name="CheckpointAge">Son doğrulanıp kaydedilen checkpoint'in imza zamanından bu yana geçen süre.</param>
/// <param name="SinceLastUpdate">ct_log_state satırının son değişiminden (ilerleme, hata, atlama) bu yana geçen süre.</param>
/// <param name="CandidateRows">ct_candidates tablosundaki satır sayısı (bu log için).</param>
public sealed record CtLogQuality(
    string LogName,
    long NextIndex,
    long LatestCheckpointSize,
    long EntriesProcessed,
    long EntriesSkipped,
    long CandidateRows,
    int ConsecutiveFailures,
    TimeSpan CheckpointAge,
    TimeSpan SinceLastUpdate,
    int LogRows,
    int ErrorRows,
    long ProcessedLast24Hours,
    long SkippedLast24Hours,
    IReadOnlyDictionary<CollectionErrorKind, int> ErrorsByKind)
{
    /// <summary>İşlenen / (işlenen + atlanan). Hiç kayıt yoksa <c>null</c>. Henüz okunmamış kayıt bu orana girmez.</summary>
    public double? Coverage => EntriesProcessed + EntriesSkipped == 0
        ? null
        : (double)EntriesProcessed / (EntriesProcessed + EntriesSkipped);

    /// <summary>Son checkpoint ile sıradaki indeks arasındaki, henüz okunmamış kayıt (sondaki kısmi tile dahil).</summary>
    public long Backlog => Math.Max(0, LatestCheckpointSize - NextIndex);
}

/// <param name="StaticCtLogs">Static CT kapsaması; ct_log_state satırı olan her log için bir satır.</param>
public sealed record QualityReport(
    DateTimeOffset GeneratedAt, int TargetDomains, IReadOnlyList<SourceQuality> Sources, IReadOnlyList<CtLogQuality>? StaticCtLogs = null)
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

        AppendStaticCt(sb, c);

        sb.AppendLine();
        sb.AppendLine("Not: \"Yalnızca hata\" ve \"Hiç denenmedi\" grupları bilinmeyen durumdur; \"kayıt yok\" sayılmaz.");
        return sb.ToString();
    }

    private void AppendStaticCt(StringBuilder sb, CultureInfo c)
    {
        sb.AppendLine();
        sb.AppendLine("[StaticCt]");
        if (StaticCtLogs is not { Count: > 0 } logs)
        {
            sb.AppendLine("  Henüz izlenen log yok (ct_log_state boş).");
            return;
        }

        foreach (var l in logs)
        {
            sb.AppendLine(c, $"  Log {l.LogName}:");
            sb.AppendLine(c, $"    Sıradaki indeks:        {l.NextIndex}");
            sb.AppendLine(c, $"    Son checkpoint boyutu:  {l.LatestCheckpointSize}");
            sb.AppendLine(c, $"    Geride kalan:           {l.Backlog} (henüz okunmadı, sondaki kısmi tile dahil)");
            sb.AppendLine(c, $"    İşlenen kayıt:          {l.EntriesProcessed}");
            sb.AppendLine(c, $"    Atlanan kayıt:          {l.EntriesSkipped}");
            sb.AppendLine(c, $"    Kapsama oranı:          {(l.Coverage is { } r ? r.ToString("P4", c) : "-")}");
            sb.AppendLine(c, $"    Aday (ct_candidates):   {l.CandidateRows}");
            sb.AppendLine(c, $"    Son 24 saat:            işlenen {l.ProcessedLast24Hours}, atlanan {l.SkippedLast24Hours}");
            sb.AppendLine(c, $"    Son checkpoint yaşı:    {FormatAge(l.CheckpointAge)}");
            sb.AppendLine(c, $"    Son durum değişimi:     {FormatAge(l.SinceLastUpdate)} önce");
            sb.AppendLine(c, $"    Üst üste hata:          {l.ConsecutiveFailures}");
            sb.AppendLine(c, $"    Log satırı:             {l.LogRows} (hata {l.ErrorRows})");
            foreach (var (kind, count) in l.ErrorsByKind.OrderBy(k => k.Key))
            {
                sb.AppendLine(c, $"      {kind}: {count}");
            }
        }

        sb.AppendLine("  Not: Kapsama = işlenen / (işlenen + atlanan). Geride kalan kayıtlar bu orana girmez, ayrıca yazılır.");
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(long)age.TotalHours} sa {age.Minutes} dk")
            : string.Create(CultureInfo.InvariantCulture, $"{age.Minutes} dk {age.Seconds} sn");
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

        var now = timeProvider.GetUtcNow();
        var staticCt = await BuildStaticCtAsync(now, cancellationToken).ConfigureAwait(false);
        return new QualityReport(now, targets.Count, sources, staticCt);
    }

    /// <summary>ct_log_state, ct_candidates ve collection_log'dan log başına sayılar. Aday adları okunmaz.</summary>
    private async Task<List<CtLogQuality>> BuildStaticCtAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var states = await db.CtLogStates.AsNoTracking().OrderBy(s => s.LogName).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (states.Count == 0)
        {
            return [];
        }

        var candidates = await db.CtCandidates
            .GroupBy(x => x.LogName)
            .Select(g => new { LogName = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.LogName, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var since = now.AddHours(-24);
        var rows = await db.CollectionLog
            .Where(l => l.Source == CollectionSource.StaticCt && l.CtLogName != null)
            .GroupBy(l => new { l.CtLogName, l.Outcome, l.ErrorKind })
            .Select(g => new
            {
                g.Key.CtLogName,
                g.Key.Outcome,
                g.Key.ErrorKind,
                Rows = g.Count(),
                Processed24 = g.Sum(x => x.FinishedAt >= since ? (long)x.ItemCount : 0L),
                Skipped24 = g.Sum(x => x.FinishedAt >= since ? (long)x.SkippedItemCount : 0L),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<CtLogQuality>(states.Count);
        foreach (var s in states)
        {
            var mine = rows.Where(r => r.CtLogName == s.LogName).ToList();
            var errors = mine.Where(r => r.Outcome == CollectionOutcome.Error).ToList();
            result.Add(new CtLogQuality(
                s.LogName,
                s.NextIndex,
                s.LatestCheckpointSize,
                s.EntriesProcessed,
                s.EntriesSkipped,
                candidates.GetValueOrDefault(s.LogName),
                s.ConsecutiveFailures,
                now - s.PendingCheckpointTimestamp,
                now - s.UpdatedAt,
                mine.Sum(r => r.Rows),
                errors.Sum(r => r.Rows),
                mine.Sum(r => r.Processed24),
                mine.Sum(r => r.Skipped24),
                errors.GroupBy(r => r.ErrorKind).ToDictionary(g => g.Key, g => g.Sum(r => r.Rows))));
        }

        return result;
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
