using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Fraud.Collectors.PortAudit;

public enum ListenerProtocol
{
    Tcp = 1,
    Udp = 2,
}

/// <summary>Soketin hangi adrese bağlı olduğu. LAN'a açık olup olmadığını bu belirler.</summary>
public enum BindScope
{
    /// <summary>127.0.0.0/8 veya ::1: Yalnızca bu makineden erişilir.</summary>
    Loopback = 1,

    /// <summary>Belirli bir arayüz adresi (ör. Docker köprüsünün ağ geçidi, WSL DNS tüneli 10.255.255.254).</summary>
    Specific = 2,

    /// <summary>0.0.0.0 veya ::: Bütün arayüzler. LAN'a açık olabilir.</summary>
    Wildcard = 3,
}

/// <summary>Bu makinede dinleyen bir soket. Süreç adı ve kullanıcı okunmaz.</summary>
public sealed record Listener(ListenerProtocol Protocol, IPAddress Address, int Port)
{
    public BindScope Scope => ProcNetParser.ScopeOf(Address);
}

/// <summary>
/// Linux çekirdeğinin soket tablolarını (<c>/proc/net/tcp</c>, <c>tcp6</c>, <c>udp</c>, <c>udp6</c>) okur.
/// Hiçbir yere bağlanmaz, hiçbir paket göndermez: Çekirdeğin zaten tuttuğu tabloyu metin olarak ayrıştırır.
/// </summary>
/// <remarks>
/// <para>Satır biçimi (başlık satırından sonra, boşlukla ayrılmış):</para>
/// <code>
///   sl  local_address rem_address   st tx_queue:rx_queue ... uid timeout inode
///    0: 0100007F:0035 00000000:0000 0A 00000000:00000000 ...
/// </code>
/// <list type="bullet">
/// <item>Adres onaltılık ve makinenin bayt sırasıyla yazılır (x86 ve ARM'de little-endian). IPv4 tek 32 bitlik sözcük:
/// <c>0100007F</c> = 127.0.0.1. IPv6 dört sözcük, her biri kendi içinde ters: <c>00000000000000000000000001000000</c> = ::1.</item>
/// <item>Port onaltılık ama ağ sırasıyla yazılır: <c>0035</c> = 53.</item>
/// <item>Durum (<c>st</c>): TCP'de <c>0A</c> = LISTEN; yalnızca bunlar dinleyicidir. UDP'de bağlantı yoktur;
/// <c>07</c> (CLOSE) ve uzak adres sıfırsa soket bağlanmamış, yani her yerden paket kabul eden bir dinleyicidir.
/// <c>01</c> (ESTABLISHED) UDP soketi <c>connect()</c> ile tek bir karşı uca bağlanmış istemcidir, dinleyici değildir.</item>
/// </list>
/// Bozuk satır atlanır ve sayılır; bütün dosya reddedilmez.
/// </remarks>
public static class ProcNetParser
{
    /// <summary>TCP_LISTEN.</summary>
    public const int TcpListen = 0x0A;

    /// <summary>TCP_CLOSE: UDP'de bağlanmamış soket.</summary>
    public const int UdpUnconnected = 0x07;

    public static IReadOnlyList<Listener> Parse(string content, ListenerProtocol protocol, AddressFamily family, out int malformed)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (family is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            throw new ArgumentOutOfRangeException(nameof(family));
        }

        malformed = 0;
        var listeners = new List<Listener>();
        var first = true;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (first)
            {
                // Başlık satırı ("sl local_address ...").
                first = false;
                if (line.StartsWith("sl", StringComparison.Ordinal))
                {
                    continue;
                }
            }

            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4
                || !TryParseEndpoint(fields[1], family, out var localAddress, out var localPort)
                || !TryParseEndpoint(fields[2], family, out var remoteAddress, out var remotePort)
                || !int.TryParse(fields[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var state))
            {
                malformed++;
                continue;
            }

            var isListener = protocol == ListenerProtocol.Tcp
                ? state == TcpListen
                : state == UdpUnconnected && remotePort == 0 && IsZero(remoteAddress);

            if (isListener)
            {
                listeners.Add(new Listener(protocol, localAddress, localPort));
            }
        }

        return listeners;
    }

    /// <summary><c>ADRES:PORT</c> alanını çözer (adres makine bayt sırasında, port ağ sırasında).</summary>
    public static bool TryParseEndpoint(string field, AddressFamily family, out IPAddress address, out int port)
    {
        ArgumentNullException.ThrowIfNull(field);
        address = IPAddress.None;
        port = 0;

        var colon = field.IndexOf(':', StringComparison.Ordinal);
        var hexLength = family == AddressFamily.InterNetwork ? 8 : 32;
        if (colon != hexLength || field.Length != colon + 5
            || !int.TryParse(field.AsSpan(colon + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out port))
        {
            return false;
        }

        var bytes = new byte[hexLength / 2];
        for (var word = 0; word < hexLength / 8; word++)
        {
            if (!uint.TryParse(field.AsSpan(word * 8, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            // Sözcük makine sırasıyla (little-endian) yazılmış: en düşük bayt adresin ilk baytıdır.
            bytes[(word * 4) + 0] = (byte)value;
            bytes[(word * 4) + 1] = (byte)(value >> 8);
            bytes[(word * 4) + 2] = (byte)(value >> 16);
            bytes[(word * 4) + 3] = (byte)(value >> 24);
        }

        address = new IPAddress(bytes);
        return true;
    }

    /// <summary>Adresin kapsamı. IPv4'e eşlenmiş IPv6 (<c>::ffff:127.0.0.1</c>) IPv4 gibi değerlendirilir.</summary>
    public static BindScope ScopeOf(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IsZero(address))
        {
            return BindScope.Wildcard;
        }

        return IPAddress.IsLoopback(address) ? BindScope.Loopback : BindScope.Specific;
    }

    private static bool IsZero(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
}
