using System.Globalization;
using System.Text;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Wayback;

public sealed class ArchiveSampleOptions
{
    public const string DefaultSeed = "asama0-arsiv-2026-11-13";
    public const string DefaultOutputDirectory = "data/private/phase0/arsiv_icerik";
    public const int DefaultCount = 20;
    public const int MaxCount = 99;

    public string Seed { get; set; } = DefaultSeed;

    public int Count { get; set; } = DefaultCount;

    /// <summary>Çalıştırılan klasöre göre çözülür (CSV içe aktarma yolu gibi).</summary>
    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    public static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(5);
}

/// <summary>
/// <c>archive-sample</c>: Aşama 0, ROADMAP 2.2 "Arşiv içeriği" kontrolü. Tek seferlik.
/// </summary>
/// <remarks>
/// İlk sertifikası karardan 1-365 gün önce olan ve karardan önce 200 + text/html arşiv kaydı bulunan alan adları
/// tohumlu md5 sırasıyla dizilir, ilk N tanesi alınır. Her biri için tek kayıt web.archive.org'dan (id_ kipi) çekilir.
/// Şüpheli siteye hiç istek gitmez. Konsola yalnızca sayılar yazılır; alan adı ve adres yalnızca
/// <c>data/private</c> altındaki <c>secim.csv</c> dosyasında durur.
/// Gün hesabı UTC tarihle yapılır (oturum saat diliminden bağımsız olsun diye).
/// </remarks>
public static class ArchiveSampleCommand
{
    public static async Task<int> RunAsync(FraudDbContext db, ISafeFetcher fetcher, ArchiveSampleOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Count is < 1 or > ArchiveSampleOptions.MaxCount || string.IsNullOrWhiteSpace(options.Seed))
        {
            await Console.Error.WriteLineAsync($"--count 1-{ArchiveSampleOptions.MaxCount} arası olmalı, --seed boş olamaz.").ConfigureAwait(false);
            return 64;
        }

        var output = Path.GetFullPath(options.OutputDirectory);
        if (!IsUnderPrivateDirectory(output))
        {
            // secim.csv alan adı içerir; yalnızca "private" adlı bir klasörün altına yazılır (Kural 8).
            await Console.Error.WriteLineAsync("--out yolu 'private' adlı bir klasörün altında olmalı.").ConfigureAwait(false);
            return 64;
        }

        var (eligibleCount, items) = await SelectAsync(db, options.Seed, options.Count, ct).ConfigureAwait(false);
        var results = await new ArchiveSampleRunner(fetcher).RunAsync(items, output, ct).ConfigureAwait(false);
        await Console.Out.WriteAsync(Summarize(options.Seed, eligibleCount, results)).ConfigureAwait(false);
        return results.All(r => r.Success) ? 0 : 2;
    }

    /// <summary>
    /// Uygun alan adlarını bulur, tohumlu sırayla ilk <paramref name="count"/> tanesini alır ve her biri için
    /// açılacak kaydı seçer. Ağa çıkmaz.
    /// </summary>
    public static async Task<(int EligibleCount, IReadOnlyList<ArchiveSampleItem> Items)> SelectAsync(
        FraudDbContext db, string seed, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);

        var eligible = await db.Database.SqlQuery<EligibleDomainRow>($"""
            with decision as (
              select domain_id, min(decision_date) as decided, min(bulletin_number) as bulletin, min(category) as category
              from official_actions group by domain_id
            ),
            cert as (
              select cd.domain_id, min((c.not_before at time zone 'UTC')::date) as first_cert
              from certificate_domains cd join certificates c on c.id = cd.certificate_id
              group by cd.domain_id
            )
            select d.domain_id as "DomainId", dm.registrable_domain as "RegistrableDomain", d.bulletin as "Bulletin",
                   d.category as "Category", d.decided as "Decided", cert.first_cert as "FirstCert",
                   md5(d.domain_id::text || ':' || {seed}) as "OrderKey"
            from decision d
            join cert on cert.domain_id = d.domain_id
            join domains dm on dm.id = d.domain_id
            where d.decided - cert.first_cert > 0
              and d.decided - cert.first_cert <= 365
              and exists (
                select 1 from archive_snapshots a
                where a.domain_id = d.domain_id
                  and (a.captured_at at time zone 'UTC')::date < d.decided
                  and a.status_code = 200
                  and a.mime_type = 'text/html')
            """).ToListAsync(ct).ConfigureAwait(false);

        // Sıra C# tarafında da sabitlenir: md5 onaltılık metni sıralı karşılaştırılır, eşitlikte kimlik.
        var selected = eligible
            .OrderBy(r => r.OrderKey, StringComparer.Ordinal)
            .ThenBy(r => r.DomainId)
            .Take(count)
            .ToList();

        var ids = selected.Select(r => r.DomainId).ToList();
        var captures = (await db.ArchiveSnapshots.AsNoTracking()
                .Where(a => ids.Contains(a.DomainId) && a.StatusCode == 200 && a.MimeType == "text/html")
                .Select(a => new { a.DomainId, a.Timestamp, a.CapturedAt, a.OriginalUrl })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(a => a.DomainId, a => new CaptureCandidate(a.Timestamp, a.CapturedAt, a.OriginalUrl));

        var items = selected
            .Select((r, i) => new ArchiveSampleItem(
                i + 1, r.DomainId, r.RegistrableDomain, r.Bulletin, r.Category, r.Decided, r.FirstCert,
                ArchiveSampleSelector.Pick(captures[r.DomainId], r.Decided)))
            .ToList();

        return (eligible.Count, items);
    }

    /// <summary>Konsol özeti. Yalnızca sayılar; alan adı ve adres yok.</summary>
    public static string Summarize(string seed, int eligible, IReadOnlyList<ArchiveSampleResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(c, $"Arşiv içeriği örneği (tohum: {seed})");
        sb.AppendLine(c, $"  Uygun alan adı:   {eligible}");
        sb.AppendLine(c, $"  Seçilen:          {results.Count}");
        sb.AppendLine(c, $"  Ana sayfa kaydı:  {results.Count(r => r.Item.Capture is not null && ArchivePageUrl.IsHomepage(r.Item.Capture.OriginalUrl))}");
        sb.AppendLine(c, $"  Başarılı:         {results.Count(r => r.Success)}");
        sb.AppendLine(c, $"  Hata:             {results.Count(r => !r.Success)}");
        foreach (var group in results.Where(r => !r.Success).GroupBy(r => r.Outcome).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(c, $"    {group.Key}: {group.Count()}");
        }

        sb.AppendLine("  Hata \"boş sayfa\" demek değildir; o örneğin içeriği bilinmiyor.");
        return sb.ToString();
    }

    private static bool IsUnderPrivateDirectory(string fullPath)
    {
        var directory = new DirectoryInfo(fullPath);
        for (var d = directory; d is not null; d = d.Parent)
        {
            if (string.Equals(d.Name, "private", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
