using System.Globalization;
using System.Text;

namespace Fraud.Collectors.Wayback;

public enum ArchiveUrlRejection
{
    None = 0,
    InvalidTimestamp,
    InvalidUrl,
    SchemeNotAllowed,
    UserInfoNotAllowed,
    HostNotUnderDomain,
    InvalidDomain,
}

/// <summary>
/// Arşivdeki ham sayfanın adresini kurar: <c>https://web.archive.org/web/{zaman}id_/{özgün adres}</c>.
/// </summary>
/// <remarks>
/// <c>id_</c> kipi sayfayı Wayback'in araç çubuğu ve adres yeniden yazımı olmadan, arşivlendiği haliyle döndürür.
/// İstek her zaman web.archive.org'a gider; özgün siteye asla gitmez. Özgün adres yine de denetlenir: Yalnızca
/// http/https, kullanıcı bilgisi yok ve ana bilgisayar seçilen alan adının kendisi ya da alt alan adı olmalı.
/// Sorgu metni ve parça (<c>#</c>) eklenmez; veritabanındaki adreste zaten yoktur, olsa da atılır.
/// </remarks>
public static class ArchivePageUrl
{
    public const string ArchiveHost = "web.archive.org";

    public static bool TryBuild(string? cdxTimestamp, string? originalUrl, string? registrableDomain, out Uri? url, out ArchiveUrlRejection rejection)
    {
        url = null;
        if (!IsTimestamp(cdxTimestamp))
        {
            rejection = ArchiveUrlRejection.InvalidTimestamp;
            return false;
        }

        if (!TryNormalizeOriginal(originalUrl, registrableDomain, out var original, out rejection))
        {
            return false;
        }

        url = new Uri($"https://{ArchiveHost}/web/{cdxTimestamp}id_/{original}");
        rejection = ArchiveUrlRejection.None;
        return true;
    }

    /// <summary>
    /// Yönlendirmelerden sonra varılan adresi çözer. Wayback en yakın kayda yönlendirebilir; bu yüzden son adresteki
    /// zaman ve özgün adres yeniden okunur ve denetlenir.
    /// </summary>
    public static bool TryParse(Uri? archiveUrl, string registrableDomain, out string timestamp, out ArchiveUrlRejection rejection)
    {
        timestamp = string.Empty;
        if (archiveUrl is null || !archiveUrl.IsAbsoluteUri
            || !string.Equals(archiveUrl.IdnHost, ArchiveHost, StringComparison.OrdinalIgnoreCase))
        {
            rejection = ArchiveUrlRejection.InvalidUrl;
            return false;
        }

        // /web/20260101000000id_/http://ornek.example/
        const string prefix = "/web/";
        var path = archiveUrl.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length < prefix.Length + 14 + 4
            || !string.Equals(path.Substring(prefix.Length + 14, 4), "id_/", StringComparison.Ordinal))
        {
            rejection = ArchiveUrlRejection.InvalidUrl;
            return false;
        }

        var candidate = path.Substring(prefix.Length, 14);
        if (!IsTimestamp(candidate))
        {
            rejection = ArchiveUrlRejection.InvalidTimestamp;
            return false;
        }

        if (!TryNormalizeOriginal(path[(prefix.Length + 18)..], registrableDomain, out _, out rejection))
        {
            return false;
        }

        timestamp = candidate;
        return true;
    }

    /// <summary>CDX zamanı (UTC, yyyyMMddHHmmss) geçerli bir tarih mi.</summary>
    public static bool IsTimestamp(string? value) =>
        value is { Length: 14 }
        && DateTime.TryParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static DateOnly DateOf(string cdxTimestamp) =>
        DateOnly.ParseExact(cdxTimestamp[..8], "yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Adresin yolu boş ya da yalnızca "/" ise ana sayfadır.</summary>
    public static bool IsHomepage(string? originalUrl) =>
        Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/";

    private static bool TryNormalizeOriginal(string? originalUrl, string? registrableDomain, out string normalized, out ArchiveUrlRejection rejection)
    {
        normalized = string.Empty;
        if (!TryAsciiDomain(registrableDomain, out var domain))
        {
            rejection = ArchiveUrlRejection.InvalidDomain;
            return false;
        }

        if (string.IsNullOrWhiteSpace(originalUrl) || !Uri.TryCreate(originalUrl.Trim(), UriKind.Absolute, out var uri))
        {
            rejection = ArchiveUrlRejection.InvalidUrl;
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            rejection = ArchiveUrlRejection.SchemeNotAllowed;
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            rejection = ArchiveUrlRejection.UserInfoNotAllowed;
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (uri.HostNameType != UriHostNameType.Dns
            || !(host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)))
        {
            rejection = ArchiveUrlRejection.HostNotUnderDomain;
            return false;
        }

        var sb = new StringBuilder();
        sb.Append(uri.Scheme).Append("://").Append(host);
        if (!uri.IsDefaultPort)
        {
            sb.Append(':').Append(uri.Port.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(uri.AbsolutePath);
        normalized = sb.ToString();
        rejection = ArchiveUrlRejection.None;
        return true;
    }

    private static bool TryAsciiDomain(string? registrableDomain, out string domain)
    {
        domain = string.Empty;
        if (string.IsNullOrWhiteSpace(registrableDomain))
        {
            return false;
        }

        try
        {
            domain = new IdnMapping().GetAscii(registrableDomain.Trim().TrimEnd('.')).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        // En az iki etiket: "com" gibi tek etiket her şeyi "alt alan adı" yapardı.
        return domain.Contains('.', StringComparison.Ordinal);
    }
}
