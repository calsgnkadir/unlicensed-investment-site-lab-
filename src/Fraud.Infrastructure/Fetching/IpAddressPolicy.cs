using System.Net;
using System.Net.Sockets;

namespace Fraud.Infrastructure.Fetching;

/// <summary>
/// Bir IP adresinin internette herkese açık olup olmadığına karar verir. Açık değilse çekici o adrese bağlanmaz.
/// </summary>
/// <remarks>
/// Neden gerekli: İzinli bir ad (ör. <c>web.archive.org</c>) bile kötü niyetli bir DNS yanıtıyla
/// <c>127.0.0.1</c> veya bulut metadata adresine (<c>169.254.169.254</c>) çözülebilir. Bu yüzden kontrol,
/// adres doğrulamasında değil, bağlantı kurulmadan hemen önce, gerçekten bağlanılacak adres üzerinde yapılır.
/// Liste "engellenenler" değil "izin verilenler" mantığıyla yazılmıştır: IPv6'da yalnızca genel unicast
/// bloğu (2000::/3) kabul edilir, içindeki özel amaçlı parçalar ayrıca reddedilir.
/// </remarks>
public static class IpAddressPolicy
{
    // IANA özel amaçlı IPv4 blokları (RFC 6890 ve devamı).
    private static readonly (uint Network, int Prefix)[] BlockedV4 =
    [
        (Parse("0.0.0.0"), 8),         // "bu ağ"
        (Parse("10.0.0.0"), 8),        // özel ağ
        (Parse("100.64.0.0"), 10),     // CGNAT (bazı bulutlarda metadata da burada)
        (Parse("127.0.0.0"), 8),       // loopback
        (Parse("169.254.0.0"), 16),    // link-local, bulut metadata (169.254.169.254)
        (Parse("172.16.0.0"), 12),     // özel ağ
        (Parse("192.0.0.0"), 24),      // IETF protokol atamaları
        (Parse("192.0.2.0"), 24),      // belgeleme
        (Parse("192.88.99.0"), 24),    // eski 6to4 aktarımı
        (Parse("192.168.0.0"), 16),    // özel ağ
        (Parse("198.18.0.0"), 15),     // performans testi
        (Parse("198.51.100.0"), 24),   // belgeleme
        (Parse("203.0.113.0"), 24),    // belgeleme
        (Parse("224.0.0.0"), 4),       // multicast
        (Parse("240.0.0.0"), 4),       // ayrılmış ve 255.255.255.255
    ];

    /// <summary>Adres herkese açık bir unicast adres ise <c>true</c>.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // ::ffff:127.0.0.1 gibi IPv4 eşlemeli adresler, içindeki IPv4 adresine göre değerlendirilir.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false,
        };
    }

    private static bool IsPublicV4(IPAddress address)
    {
        var value = ToUInt32(address.GetAddressBytes());
        foreach (var (network, prefix) in BlockedV4)
        {
            var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            if ((value & mask) == network)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (address.ScopeId != 0)
        {
            return false; // bölgeye bağlı (link-local) adres
        }

        var b = address.GetAddressBytes();

        // Yalnızca genel unicast (2000::/3). ::1, ::, fc00::/7, fe80::/10, ff00::/8, 64:ff9b::/96 ve
        // IPv4 uyumlu adresler bu bloğun dışında kaldığı için burada reddedilir.
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        // 2001::/23: IETF protokol atamaları (Teredo 2001::/32 dahil). Teredo içine IPv4 adresi gömer.
        if (b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xFE) == 0x00)
        {
            return false;
        }

        // 2001:db8::/32: belgeleme.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
        {
            return false;
        }

        // 2002::/16: 6to4, içine IPv4 adresi gömer (2002:7f00:1:: = 127.0.0.1).
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return false;
        }

        // 3fff::/20: belgeleme (RFC 9637).
        if (b[0] == 0x3F && b[1] == 0xFF && (b[2] & 0xF0) == 0x00)
        {
            return false;
        }

        return true;
    }

    private static uint Parse(string ipv4) => ToUInt32(IPAddress.Parse(ipv4).GetAddressBytes());

    private static uint ToUInt32(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
}
