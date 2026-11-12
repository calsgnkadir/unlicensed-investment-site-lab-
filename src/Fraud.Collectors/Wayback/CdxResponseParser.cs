using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.Wayback;

/// <summary>Bir CDX sayfasının ayrıştırma sonucu.</summary>
public sealed record CdxPage(bool IsValid, IReadOnlyList<CdxCapture> Captures, int Skipped, string? ResumeKey)
{
    public static readonly CdxPage Invalid = new(false, [], 0, null);
}

/// <summary>
/// <c>output=json</c> CDX yanıtını okur.
/// </summary>
/// <remarks>
/// Biçim (CDX Server belgesi): Kök bir dizi; ilk satır alan adlarını taşıyan başlık, sonraki satırlar veri.
/// <c>showResumeKey=true</c> verilmişse veri satırlarından sonra boş bir dizi ve ardından tek elemanlı
/// <c>[resumeKey]</c> satırı gelir. Hiç kayıt yoksa yanıt <c>[]</c> olur.
/// Sütunlar başlıktan eşlenir; sıraya güvenilmez.
/// </remarks>
public static partial class CdxResponseParser
{
    public static CdxPage Parse(ReadOnlyMemory<byte> json)
    {
        // Geçersiz UTF-8 baytı JsonDocument.Parse'tan geçer ama GetString'de istisna fırlatır; baştan reddedilir.
        if (!Utf8.IsValid(json.Span))
        {
            return CdxPage.Invalid;
        }

        try
        {
            return ParseDocument(json);
        }
        catch (InvalidOperationException)
        {
            // Kaçış dizisiyle yazılmış yalnız vekil karakter (ör. \ud800) GetString'de istisna fırlatır.
            return CdxPage.Invalid;
        }
    }

    private static CdxPage ParseDocument(ReadOnlyMemory<byte> json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (JsonException)
        {
            return CdxPage.Invalid;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return CdxPage.Invalid;
            }

            var rows = root.EnumerateArray().ToList();
            if (rows.Count == 0)
            {
                return new CdxPage(true, [], 0, null);
            }

            var header = ReadRow(rows[0]);
            if (header is null)
            {
                return CdxPage.Invalid;
            }

            // Aynı sütun iki kez gelirse hangisinin doğru olduğu bilinemez; yanıt geçersiz sayılır.
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var c = 0; c < header.Count; c++)
            {
                if (!columns.TryAdd(header[c], c))
                {
                    return CdxPage.Invalid;
                }
            }

            if (!columns.TryGetValue("timestamp", out var tsIndex) || !columns.TryGetValue("original", out var urlIndex))
            {
                return CdxPage.Invalid;
            }

            var statusIndex = columns.GetValueOrDefault("statuscode", -1);
            var mimeIndex = columns.GetValueOrDefault("mimetype", -1);
            var digestIndex = columns.GetValueOrDefault("digest", -1);

            var captures = new List<CdxCapture>();
            var skipped = 0;
            string? resumeKey = null;

            for (var i = 1; i < rows.Count; i++)
            {
                var row = ReadRow(rows[i]);
                if (row is null)
                {
                    skipped++;
                    continue;
                }

                // Boş satır: veri bitti, varsa bir sonraki satır sayfalama anahtarıdır.
                if (row.Count == 0)
                {
                    if (i + 1 < rows.Count && ReadRow(rows[i + 1]) is [var key] && key.Length > 0)
                    {
                        resumeKey = key;
                    }

                    break;
                }

                if (row.Count != header.Count)
                {
                    skipped++;
                    continue;
                }

                var capture = ToCapture(row, tsIndex, urlIndex, statusIndex, mimeIndex, digestIndex);
                if (capture is null)
                {
                    skipped++;
                }
                else
                {
                    captures.Add(capture);
                }
            }

            return new CdxPage(true, captures, skipped, resumeKey);
        }
    }

    private static CdxCapture? ToCapture(List<string> row, int ts, int url, int status, int mime, int digest)
    {
        var timestamp = row[ts];
        if (!TimestampPattern().IsMatch(timestamp)
            || !DateTime.TryParseExact(timestamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var capturedAt))
        {
            return null;
        }

        if (ArchiveUrlSanitizer.Sanitize(row[url]) is not var (originalUrl, queryRemoved))
        {
            return null;
        }

        int? statusCode = status >= 0 && int.TryParse(row[status], NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                          && code is >= 100 and <= 599
            ? code
            : null;

        var mimeType = mime >= 0 ? Clean(row[mime], 255) : null;
        var digestValue = digest >= 0 && Clean(row[digest], 64) is { } d && DigestPattern().IsMatch(d) ? d : null;

        return new CdxCapture(timestamp, new DateTimeOffset(capturedAt, TimeSpan.Zero), originalUrl, queryRemoved, statusCode, mimeType, digestValue);
    }

    private static List<string>? ReadRow(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    private static string? Clean(string value, int maxLength)
    {
        value = value.Trim();
        if (value.Length == 0 || value == "-" || value.Length > maxLength || value.Any(char.IsControl))
        {
            return null;
        }

        return value;
    }

    // "$" sondaki satır sonundan önce de eşleşir; "\z" yalnızca metnin gerçek sonunda.
    [GeneratedRegex(@"^[0-9]{14}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^[A-Za-z0-9:+/=_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
