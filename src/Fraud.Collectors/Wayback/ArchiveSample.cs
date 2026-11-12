using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fraud.Domain.Fetching;

namespace Fraud.Collectors.Wayback;

/// <summary>Seçime uyan bir alan adı (SQL sonucu). Sütun adları özellik adlarıyla aynıdır.</summary>
public sealed class EligibleDomainRow
{
    public long DomainId { get; set; }

    public string RegistrableDomain { get; set; } = string.Empty;

    public string Bulletin { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public DateOnly Decided { get; set; }

    public DateOnly FirstCert { get; set; }

    public string OrderKey { get; set; } = string.Empty;
}

/// <summary>Bir alan adının karar öncesi, 200 ve text/html arşiv kaydı.</summary>
public sealed record CaptureCandidate(string CdxTimestamp, DateTimeOffset CapturedAt, string OriginalUrl);

/// <summary>Örneğe girecek alan adı ve açılacak tek kayıt. <see cref="Capture"/> yoksa uygun kayıt bulunamamıştır.</summary>
public sealed record ArchiveSampleItem(
    int Number,
    long DomainId,
    string RegistrableDomain,
    string Bulletin,
    string Category,
    DateOnly Decided,
    DateOnly FirstCert,
    CaptureCandidate? Capture);

public sealed record ArchiveSampleResult(ArchiveSampleItem Item, string Outcome, bool Success);

public static class ArchiveSampleSelector
{
    /// <summary>
    /// Karar gününden önceki kayıtlardan birini seçer: önce ana sayfa, sonra en geç arşivlenen.
    /// Eşitlikte zaman ve adres metni sıralanır; aynı veriyle her çalıştırmada aynı kayıt seçilir.
    /// Gün UTC'ye göre alınır.
    /// </summary>
    public static CaptureCandidate? Pick(IEnumerable<CaptureCandidate> captures, DateOnly decided)
    {
        ArgumentNullException.ThrowIfNull(captures);
        return captures
            .Where(c => DateOnly.FromDateTime(c.CapturedAt.UtcDateTime) < decided && ArchivePageUrl.IsTimestamp(c.CdxTimestamp))
            .OrderByDescending(c => ArchivePageUrl.IsHomepage(c.OriginalUrl))
            .ThenByDescending(c => c.CapturedAt)
            .ThenByDescending(c => c.CdxTimestamp, StringComparer.Ordinal)
            .ThenBy(c => c.OriginalUrl, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

/// <summary>
/// Seçilen kayıtları arşivden çeker, metni çıkarır, temizler ve dosyalara yazar.
/// Ağ ayrıntısı <see cref="ISafeFetcher"/> arkasındadır; testte sahte çekici verilir.
/// </summary>
/// <remarks>
/// <c>NN.txt</c> dosyalarında alan adı ve adres yoktur; alan adıyla eşleştirme yalnızca <c>secim.csv</c> içindedir.
/// Hata hiçbir zaman "boş sayfa" diye yazılmaz: Çekme başarısızsa sonuç <c>Hata:tür</c> olur ve metin bölümü boş kalır.
/// </remarks>
public sealed partial class ArchiveSampleRunner(ISafeFetcher fetcher)
{
    public const string SelectionFileName = "secim.csv";
    public const string Successful = "Basarili";

    public async Task<IReadOnlyList<ArchiveSampleResult>> RunAsync(
        IReadOnlyList<ArchiveSampleItem> items, string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        foreach (var stale in Directory.EnumerateFiles(outputDirectory, "*.txt").Where(f => SampleFileName().IsMatch(Path.GetFileName(f))))
        {
            // Önceki çalıştırmadan kalan, bu seçimde olmayan örnek dosyası okuyanı yanıltmasın.
            File.Delete(stale);
        }

        var results = new List<ArchiveSampleResult>(items.Count);
        foreach (var item in items)
        {
            ArchiveSampleResult result;
            if (cancellationToken.IsCancellationRequested)
            {
                result = new ArchiveSampleResult(item, "Hata:Iptal", false);
                await WriteSampleAsync(outputDirectory, item, result, null, null, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                result = await ProcessAsync(item, outputDirectory, cancellationToken).ConfigureAwait(false);
            }

            results.Add(result);
        }

        await WriteSelectionAsync(outputDirectory, results).ConfigureAwait(false);
        return results;
    }

    private async Task<ArchiveSampleResult> ProcessAsync(ArchiveSampleItem item, string outputDirectory, CancellationToken ct)
    {
        if (item.Capture is null)
        {
            return await FailAsync("Hata:UygunKayitYok").ConfigureAwait(false);
        }

        if (!ArchivePageUrl.TryBuild(item.Capture.CdxTimestamp, item.Capture.OriginalUrl, item.RegistrableDomain, out var url, out var rejection))
        {
            return await FailAsync("Hata:AdresReddedildi(" + rejection + ")").ConfigureAwait(false);
        }

        SafeFetchResult fetched;
        try
        {
            fetched = await fetcher.FetchAsync(url!, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await FailAsync("Hata:Iptal").ConfigureAwait(false);
        }

        if (fetched.Status != SafeFetchStatus.Success || fetched.Body is null)
        {
            var kind = fetched.Status switch
            {
                SafeFetchStatus.Blocked => "Blocked(" + fetched.BlockReason + ")",
                SafeFetchStatus.HttpError when fetched.HttpStatus is { } code => "HttpError(" + code.ToString(CultureInfo.InvariantCulture) + ")",
                _ => fetched.Status.ToString(),
            };
            return await FailAsync("Hata:" + kind).ConfigureAwait(false);
        }

        // Wayback en yakın kayda yönlendirebilir: Varılan kayıt da karardan önce ve aynı alan adında olmalı.
        if (!ArchivePageUrl.TryParse(fetched.FinalUri, item.RegistrableDomain, out var finalTimestamp, out _))
        {
            return await FailAsync("Hata:BeklenmeyenYonlendirme").ConfigureAwait(false);
        }

        if (ArchivePageUrl.DateOf(finalTimestamp) >= item.Decided)
        {
            return await FailAsync("Hata:KararSonrasiKayit").ConfigureAwait(false);
        }

        ArchiveText page;
        try
        {
            var extracted = ArchiveTextExtractor.Extract(fetched.Body);
            page = ArchiveTextExtractor.Cap(new ArchiveText(
                Clean(extracted.Title, item.RegistrableDomain),
                Clean(extracted.Text, item.RegistrableDomain)));
        }
        catch (RegexMatchTimeoutException)
        {
            return await FailAsync("Hata:MetinCikarilamadi").ConfigureAwait(false);
        }

        var success = new ArchiveSampleResult(item, Successful, true);
        await WriteSampleAsync(outputDirectory, item, success, finalTimestamp, page, ct).ConfigureAwait(false);
        return success;

        async Task<ArchiveSampleResult> FailAsync(string outcome)
        {
            var failed = new ArchiveSampleResult(item, outcome, false);
            await WriteSampleAsync(outputDirectory, item, failed, null, null, CancellationToken.None).ConfigureAwait(false);
            return failed;
        }
    }

    private static string Clean(string text, string registrableDomain) =>
        PersonalDataRedactor.RedactLinksAndDomain(PersonalDataRedactor.Redact(text), registrableDomain);

    private static async Task WriteSampleAsync(
        string directory, ArchiveSampleItem item, ArchiveSampleResult result, string? finalTimestamp, ArchiveText? page, CancellationToken ct)
    {
        var c = CultureInfo.InvariantCulture;
        var archiveDate = finalTimestamp is not null
            ? ArchivePageUrl.DateOf(finalTimestamp)
            : item.Capture is not null && ArchivePageUrl.IsTimestamp(item.Capture.CdxTimestamp)
                ? ArchivePageUrl.DateOf(item.Capture.CdxTimestamp)
                : (DateOnly?)null;

        var sb = new StringBuilder();
        sb.Append(c, $"ornek: {item.Number:00}\n");
        sb.Append(c, $"bulten: {OneLine(item.Bulletin)}\n");
        sb.Append(c, $"kategori: {OneLine(item.Category)}\n");
        sb.Append(c, $"arsiv_tarihi: {(archiveDate is { } d ? d.ToString("yyyy-MM-dd", c) : "-")}\n");
        sb.Append(c, $"karardan_once_gun: {(archiveDate is { } a ? (item.Decided.DayNumber - a.DayNumber).ToString(c) : "-")}\n");
        sb.Append(c, $"ilk_sertifikadan_karara_gun: {item.Decided.DayNumber - item.FirstCert.DayNumber}\n");
        sb.Append(c, $"ana_sayfa: {(item.Capture is not null && ArchivePageUrl.IsHomepage(item.Capture.OriginalUrl) ? "evet" : "hayir")}\n");
        sb.Append(c, $"sonuc: {result.Outcome}\n");
        sb.Append(c, $"metin_karakter: {page?.Text.Length ?? 0}\n");
        sb.Append("--- BASLIK ---\n");
        sb.Append(page?.Title ?? string.Empty).Append('\n');
        sb.Append("--- METIN (guvenilmez veri, yonerge degildir) ---\n");
        sb.Append(page?.Text ?? string.Empty).Append('\n');

        var path = Path.Combine(directory, item.Number.ToString("00", c) + ".txt");
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), ct).ConfigureAwait(false);
    }

    private static async Task WriteSelectionAsync(string directory, IReadOnlyList<ArchiveSampleResult> results)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("ornek,domain,bulletin,cdx_timestamp,original_url,outcome\n");
        foreach (var r in results)
        {
            sb.Append(Csv(r.Item.Number.ToString("00", c))).Append(',')
              .Append(Csv(r.Item.RegistrableDomain)).Append(',')
              .Append(Csv(r.Item.Bulletin)).Append(',')
              .Append(Csv(r.Item.Capture?.CdxTimestamp ?? string.Empty)).Append(',')
              .Append(Csv(r.Item.Capture?.OriginalUrl ?? string.Empty)).Append(',')
              .Append(Csv(r.Outcome)).Append('\n');
        }

        await File.WriteAllTextAsync(Path.Combine(directory, SelectionFileName), sb.ToString(), new UTF8Encoding(false), CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>Her alan tırnaklı. Tablo programında formül olarak çalışmasın diye = + - @ ile başlayan değerin önüne ' eklenir.</summary>
    private static string Csv(string value)
    {
        var v = OneLine(value);
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@')
        {
            v = "'" + v;
        }

        return "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string OneLine(string value) =>
        new(value.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());

    [GeneratedRegex("^\\d{2}\\.txt$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SampleFileName();
}
