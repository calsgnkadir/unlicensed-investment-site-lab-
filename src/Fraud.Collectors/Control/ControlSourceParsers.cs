using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.Control;

/// <summary>BDDK banka listesi sayfasından çıkarılan bağlantılar (yalnızca sayılar ve adres metinleri, kişi verisi yok).</summary>
/// <param name="Urls">Web adresi bağlantıları (<c>href</c> değerleri), sayfadaki sırayla.</param>
/// <param name="Anchors">Sayfadaki bütün bağlantı sayısı.</param>
/// <param name="SkippedInternal">bddk.org.tr'ye giden bağlantılar (gezinme, "Detay").</param>
/// <param name="SkippedNotAddressText">Yazısı web adresine benzemeyen dış bağlantılar (sosyal medya, altlık).</param>
public sealed record BddkParseResult(IReadOnlyList<string> Urls, int Anchors, int SkippedInternal, int SkippedNotAddressText);

/// <summary>
/// BDDK "Bankalar" sayfası (Kurulus/Liste/77) ayrıştırıcısı. Ağa çıkmaz; kaydedilmiş HTML dosyasını okur.
/// </summary>
/// <remarks>
/// <para>Sayfada liste kayıtları sıra numarası, kurum adı, web adresi bağlantısı ve "Detay" yazısı taşır
/// (KONTROL_LISTESI_KURULUM.md 1.1). Bağlantı yazısı bazen kısaltılmıştır (üç nokta), bu yüzden adres YAZIDAN değil
/// <c>href</c> özniteliğinden okunur.</para>
/// <para>Bir bağlantı web adresi sayılır, eğer (1) <c>href</c> mutlak <c>http(s)</c> adresi, (2) ana bilgisayar
/// bddk.org.tr değil ve (3) bağlantı yazısı bir web adresine benziyorsa (<c>www.</c> ya da <c>http</c> ile başlıyor).
/// Üçüncü şart altlıktaki sosyal medya ve kamu bağlantılarını ayırır. Komut sayıları basar; yaklaşık 66 beklenir.</para>
/// <para>Düzenli ifade yalnızca bağlantı öğesini bulmak için; HTML güvenilmez veridir, hiçbir şey çalıştırılmaz.</para>
/// </remarks>
public static partial class BddkBankListParser
{
    /// <summary>En büyük sayfa boyutu: Gerçek sayfa birkaç yüz KB.</summary>
    public const int MaxHtmlBytes = 4 * 1024 * 1024;

    public static BddkParseResult Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Length > MaxHtmlBytes)
        {
            throw new InvalidDataException("BDDK sayfası beklenenden büyük.");
        }

        var urls = new List<string>();
        int anchors = 0, internalLinks = 0, notAddress = 0;
        foreach (Match m in Anchor().Matches(html))
        {
            anchors++;
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value).Trim();
            var text = WebUtility.HtmlDecode(Tags().Replace(m.Groups["text"].Value, string.Empty)).Trim();
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                internalLinks++; // göreli bağlantılar sitenin kendi sayfalarıdır
                continue;
            }

            if (uri.Host.Equals("bddk.org.tr", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".bddk.org.tr", StringComparison.OrdinalIgnoreCase))
            {
                internalLinks++;
                continue;
            }

            if (!text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                notAddress++;
                continue;
            }

            urls.Add(href);
        }

        return new BddkParseResult(urls, anchors, internalLinks, notAddress);
    }

    [GeneratedRegex("""<a\b[^>]*?\bhref\s*=\s*["'](?<href>[^"']*)["'][^>]*>(?<text>.*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Anchor();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Tags();
}

/// <summary>Tranco liste dosyası: her satır <c>sıra,alan_adı</c>.</summary>
public static class TrancoListParser
{
    /// <summary>İlk <paramref name="maxRank"/> sıradaki adlar, sıraya göre. Bozuk satır sayılır, atlanır.</summary>
    public static (IReadOnlyList<string> Domains, int Malformed) Parse(TextReader reader, int maxRank)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var domains = new List<string>();
        var malformed = 0;
        while (reader.ReadLine() is { } line)
        {
            var comma = line.IndexOf(',', StringComparison.Ordinal);
            if (comma <= 0 || !int.TryParse(line.AsSpan(0, comma), NumberStyles.None, CultureInfo.InvariantCulture, out var rank))
            {
                malformed++;
                continue;
            }

            if (rank > maxRank)
            {
                break;
            }

            domains.Add(line[(comma + 1)..].Trim());
        }

        return (domains, malformed);
    }
}

/// <summary>Tranco API'si: <c>/api/lists/date/{gün}</c> ve <c>/api/lists/id/{kimlik}</c> cevabı.</summary>
public sealed record TrancoListInfo(string ListId, DateTimeOffset? CreatedOn, bool Available);

public static partial class TrancoApiParser
{
    public static TrancoListInfo? ParseInfo(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("list_id", out var id) || id.ValueKind != JsonValueKind.String
                || !ListId().IsMatch(id.GetString()!))
            {
                return null;
            }

            DateTimeOffset? created = root.TryGetProperty("created_on", out var c) && c.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
            var available = root.TryGetProperty("available", out var a) && a.ValueKind == JsonValueKind.True;
            return new TrancoListInfo(id.GetString()!, created, available);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Liste kimliği: kısa harf-rakam kodu (ör. 5 karakter). Adrese girdiği için sıkı denetlenir.</summary>
    public static bool IsListId(string? value) => value is not null && ListId().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex ListId();
}

/// <summary>Cloudflare Radar <c>ranking/domain/{alan_adı}</c> cevabından kategori adları.</summary>
public static class RadarCategoryParser
{
    /// <summary>Kategori adları; cevap geçersizse <c>null</c> (hata "kategori yok" sayılmaz).</summary>
    public static IReadOnlyList<string>? Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("details_0", out var details) || details.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!details.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. categories.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(c => c.GetProperty("name").GetString()!)];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
