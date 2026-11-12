using System.Text;

namespace Fraud.Collectors.Wayback;

/// <summary>
/// Arşivdeki özgün adresi saklamadan önce kişi verisinden arındırır.
/// </summary>
/// <remarks>
/// Adreslerde e-posta, telefon veya oturum anahtarı genelde sorgu metninde (<c>?email=...</c>) taşınır.
/// Bu yüzden sorgu metni, parça (<c>#...</c>) ve kullanıcı bilgisi atılır; <c>@</c> içeren yol parçası
/// <c>[redacted]</c> yapılır. Araştırma için gereken bilgi (hangi ana bilgisayar, hangi yol, ne zaman) kalır.
/// </remarks>
public static class ArchiveUrlSanitizer
{
    public const int MaxLength = 2048;
    public const string Redacted = "[redacted]";

    /// <returns>Temizlenmiş adres ve sorgu metninin silinip silinmediği; adres okunamazsa <c>null</c>.</returns>
    public static (string Url, bool QueryRemoved)? Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) && !Uri.TryCreate("http://" + raw, UriKind.Absolute, out uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme).Append("://").Append(uri.IdnHost.ToLowerInvariant());
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        // AbsolutePath "/" ile başlar: "/a/b".Split('/') => ["", "a", "b"], yeniden birleşince "/a/b" olur.
        var segments = uri.AbsolutePath.Split('/').Select(s => LooksPersonal(s) ? Redacted : s);
        builder.Append(string.Join('/', segments));

        var url = builder.ToString();
        var queryRemoved = uri.Query.Length > 0;
        return url.Length <= MaxLength ? (url, queryRemoved) : null;
    }

    private static bool LooksPersonal(string segment) =>
        segment.Contains('@', StringComparison.Ordinal) || segment.Contains("%40", StringComparison.OrdinalIgnoreCase);
}
