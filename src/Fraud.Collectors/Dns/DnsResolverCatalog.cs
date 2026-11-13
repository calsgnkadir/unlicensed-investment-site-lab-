using System.Net;
using Fraud.Infrastructure.Fetching;

namespace Fraud.Collectors.Dns;

/// <summary>Bir herkese açık çözücü: kısa adı, düz DNS adresi (UDP ve TCP, port 53) ve DoH adresi.</summary>
/// <remarks>
/// Üretim kodu bu kaydı yalnızca <see cref="DnsResolverCatalog"/> içinden alır. Testler yerel (loopback) adresle
/// kurabilir; yapılandırma kuramaz.
/// </remarks>
public sealed record DnsResolver(string Name, IPEndPoint PlainEndpoint, Uri DohUri);

/// <summary>
/// Kullanılabilecek çözücülerin koda gömülü izin listesi.
/// </summary>
/// <remarks>
/// Yapılandırma (<c>Dns:Resolvers</c>) yalnızca bu listeden ad seçebilir; adres veremez. Böylece yanlış veya kötü
/// niyetli bir ayar, DNS istemcisini rastgele bir sunucuya ya da iç ağdaki bir adrese (ör. 127.0.0.1, 10.0.0.0/8)
/// yönlendiremez. Adresler ayrıca <see cref="IpAddressPolicy"/> ile denetlenir.
/// Liste: docs/research/HAFTA4_AG_KAYNAKLARI.md Bölüm 4 (gizlilik ve hız şartları okunmuş çözücüler).
/// </remarks>
public static class DnsResolverCatalog
{
    public static readonly DnsResolver Cloudflare = new(
        "cloudflare", new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53), new Uri("https://cloudflare-dns.com/dns-query"));

    public static readonly DnsResolver Google = new(
        "google", new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), new Uri("https://dns.google/dns-query"));

    public static IReadOnlyList<DnsResolver> All { get; } = [Cloudflare, Google];

    /// <summary>DoH bağlantısının açılabileceği ana bilgisayarlar. Bağlantı anında denetlenir.</summary>
    public static IReadOnlySet<string> DohHosts { get; } =
        All.Select(r => r.DohUri.IdnHost).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Yapılandırmadaki adları izin listesindeki çözücülere çevirir. Bilinmeyen ad başlangıçta hata verir.</summary>
    public static IReadOnlyList<DnsResolver> Select(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var selected = new List<DnsResolver>();
        foreach (var name in names)
        {
            var resolver = All.SingleOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "Dns:Resolvers yalnızca şu adları içerebilir: " + string.Join(", ", All.Select(r => r.Name)));
            if (!selected.Contains(resolver))
            {
                selected.Add(resolver);
            }
        }

        foreach (var resolver in selected)
        {
            // Liste koda gömülü olduğu için bu denetim yalnızca bir kod değişikliğini yakalar; ucuz bir emniyet kemeri.
            if (!IpAddressPolicy.IsPublic(resolver.PlainEndpoint.Address) || resolver.PlainEndpoint.Port != 53
                || resolver.DohUri.Scheme != Uri.UriSchemeHttps || !resolver.DohUri.IsDefaultPort)
            {
                throw new InvalidOperationException("Çözücü listesi geçersiz.");
            }
        }

        return selected;
    }
}
