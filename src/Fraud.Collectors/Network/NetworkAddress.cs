using System.Net;

namespace Fraud.Collectors.Network;

/// <summary>IP adresini karşılaştırmadan önce tek bir yazıma indirir.</summary>
public static class NetworkAddress
{
    /// <summary>
    /// IPv4'e eşlenmiş IPv6 adresini (<c>::ffff:a.b.c.d</c>, RFC 4291 Bölüm 2.5.5.2) düz IPv4'e çevirir; diğerlerini
    /// olduğu gibi bırakır. Aynı adresin iki yazımı olursa engel işareti karşılaştırması kaçırır ve aynı IP iki kez
    /// sorulur (THREAT_MODEL N4). Engel işareti ve RIPEstat/InternetDB hedefleri bu yöntemi birlikte kullanır.
    /// </summary>
    public static IPAddress Canonical(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
