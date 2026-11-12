using Fraud.Domain.Fetching;

namespace Fraud.Infrastructure.Fetching;

/// <summary>
/// İstek gönderilmeden önce adresin kendisini denetler. İlk adreste ve her yönlendirmede yeniden çağrılır.
/// </summary>
/// <remarks>
/// Bu katman adres metnine bakar; adın hangi IP'ye çözüldüğünü bilemez. O kontrol bağlantı anında
/// <see cref="SafeConnector"/> içinde yapılır. İki katman birbirinin yerine geçmez.
/// </remarks>
public static class SafeUrlPolicy
{
    public static SafeFetchBlockReason Check(Uri url, SafeFetcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);

        if (!url.IsAbsoluteUri)
        {
            return SafeFetchBlockReason.NotAbsolute;
        }

        // Yalnızca https. file:, ftp:, gopher:, data: ve düz http reddedilir.
        if (!string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return SafeFetchBlockReason.SchemeNotAllowed;
        }

        // https://web.archive.org@127.0.0.1/ gibi kullanıcı bilgisi içeren adresler okuyanı yanıltır.
        if (!string.IsNullOrEmpty(url.UserInfo))
        {
            return SafeFetchBlockReason.UserInfoNotAllowed;
        }

        if (!url.IsDefaultPort)
        {
            return SafeFetchBlockReason.PortNotAllowed;
        }

        // IP yazımları (127.0.0.1, [::ffff:127.0.0.1], ondalık 2130706433, onaltılık 0x7f000001)
        // ayrıştırıcıda IPv4/IPv6 olarak tanınır; ad değildir, reddedilir.
        if (url.HostNameType != UriHostNameType.Dns)
        {
            return SafeFetchBlockReason.IpLiteralNotAllowed;
        }

        var host = url.IdnHost.TrimEnd('.').ToLowerInvariant();
        var allowed = options.AllowedHosts.Any(a => string.Equals(a.Trim().ToLowerInvariant(), host, StringComparison.Ordinal));
        return allowed ? SafeFetchBlockReason.None : SafeFetchBlockReason.HostNotAllowed;
    }
}
