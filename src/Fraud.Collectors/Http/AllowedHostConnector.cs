using Fraud.Infrastructure.Fetching;

namespace Fraud.Collectors.Http;

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> için: Yalnızca izinli ana bilgisayarlara, 443 numaralı porta ve
/// herkese açık IP'ye bağlanır. DoH, RIPEstat ve InternetDB istemcilerinde kullanılır.
/// </summary>
/// <remarks>
/// Adresleri kod kurar, yönlendirme kapalıdır; bu sınıf ikinci kilittir: Bir hata ya da yanlış ayar başka bir ana
/// bilgisayara istek kurdursa bile bağlantı açılmaz. IP denetimi <see cref="SafeConnector"/> ile yapılır
/// (ad bir kez çözülür, soket denetlenen IP'ye açılır).
/// </remarks>
public sealed class AllowedHostConnector(IReadOnlySet<string> allowedHosts, SafeConnector connector)
{
    public ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var endpoint = context.DnsEndPoint;
        if (endpoint.Port != 443 || !allowedHosts.Contains(endpoint.Host))
        {
            throw new BlockedAddressException("İzin listesinde olmayan ana bilgisayara bağlantı reddedildi.");
        }

        return connector.ConnectAsync(context, cancellationToken);
    }

    /// <summary>İzinli ana bilgisayarlar için işleyici: yönlendirme yok, çerez yok, vekil yok, bağlantı sayısı 1.</summary>
    public static SocketsHttpHandler CreateHandler(IReadOnlySet<string> allowedHosts)
    {
        var connector = new AllowedHostConnector(allowedHosts, new SafeConnector(new SystemHostAddressResolver()));
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            // Vekil kapalı: Vekil olsaydı bağlantı vekile açılır ve ana bilgisayar denetimi anlamını yitirirdi.
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 1,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = connector.ConnectAsync,
        };
    }
}
