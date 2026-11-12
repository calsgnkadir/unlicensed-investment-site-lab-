using System.Net;
using System.Net.Sockets;

namespace Fraud.Infrastructure.Fetching;

/// <summary>Ad çözümlemesini soyutlar. Testlerde gerçek DNS yerine sahte çözümleyici kullanılır.</summary>
public interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>Bağlanılacak adres herkese açık değil. Bağlantı açılmadan fırlatılır.</summary>
public sealed class BlockedAddressException : Exception
{
    public BlockedAddressException()
        : base("Hedef adres herkese açık bir adres değil; bağlantı kurulmadı.")
    {
    }

    public BlockedAddressException(string message)
        : base(message)
    {
    }

    public BlockedAddressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> için bağlantı kurucu. Adı kendisi çözer, bütün adresleri
/// <see cref="IpAddressPolicy"/> ile denetler ve yalnızca denetlenen adrese bağlanır.
/// </summary>
/// <remarks>
/// Adı çözüp denetledikten sonra soketi yine ada değil, denetlenen IP'ye açarız. Böylece "denetimde başka,
/// bağlantıda başka adres" (DNS rebinding) açığı kapanır. Yanıttaki adreslerden biri bile özel aralıktaysa
/// hepsi reddedilir: Karışık yanıt, normal bir arşiv sunucusundan beklenmez.
/// </remarks>
public sealed class SafeConnector(IHostAddressResolver resolver)
{
    public async Task<IPAddress[]> ResolveAllowedAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }

        if (addresses.Length == 0 || addresses.Any(a => !IpAddressPolicy.IsPublic(a)))
        {
            throw new BlockedAddressException();
        }

        // Eşlemeli IPv6 adresi düz IPv4'e çevrilir; soket gerçekten denetlenen adrese açılır.
        return addresses.Select(a => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToArray();
    }

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var endpoint = context.DnsEndPoint;
        var addresses = await ResolveAllowedAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }
}
