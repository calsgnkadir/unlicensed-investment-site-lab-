using System.Text;

namespace Fraud.Collectors.Wayback;

/// <summary>
/// Arşivdeki özgün adresi saklamadan önce kişi verisinden arındırır.
/// </summary>
/// <remarks>
/// Adreslerde e-posta, telefon veya oturum anahtarı genelde sorgu metninde (<c>?email=...</c>) taşınır.
/// Bu yüzden sorgu metni, parça (<c>#...</c>) ve kullanıcı bilgisi atılır; <c>@</c> içeren yol parçası
/// (yüzde kodlaması kaç kat olursa olsun: <c>%40</c>, <c>%2540</c>) <c>[redacted]</c> yapılır. Araştırma için gereken bilgi (hangi ana bilgisayar, hangi yol, ne zaman) kalır.
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

    /// <summary>Yüzde kodlaması en fazla bu kadar kat çözülür (<c>%252540</c> üç kat).</summary>
    private const int MaxDecodeRounds = 4;

    /// <summary>
    /// Parça ya da yüzde kodlaması çözülmüş hallerinden biri <c>@</c> içeriyorsa kişisel sayılır.
    /// <c>%40</c> bir kez, <c>%2540</c> iki kez çözülünce <c>@</c> olur; o yüzden değişmeyene kadar çözülür.
    /// </summary>
    private static bool LooksPersonal(string segment)
    {
        var current = segment;
        for (var round = 0; round <= MaxDecodeRounds; round++)
        {
            if (current.Contains('@', StringComparison.Ordinal))
            {
                return true;
            }

            var decoded = Uri.UnescapeDataString(current);
            if (decoded == current)
            {
                return false;
            }

            current = decoded;
        }

        // Bu kadar katmanlı kodlama olağan değil; güvenli tarafta kalınır.
        return true;
    }
}
