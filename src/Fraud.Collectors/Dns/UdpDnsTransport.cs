using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Fraud.Domain.Network;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Düz UDP ile DNS (RFC 1035 Bölüm 4.2.1). Bir datagram gider, bir datagram gelir; bağlantı yoktur.
/// </summary>
/// <remarks>
/// <para>
/// UDP'de "cevap" diye gelen her datagram bizim cevabımız olmayabilir: Yoldaki biri sahte paket atabilir ya da
/// eski bir sorgunun geç kalmış cevabı gelebilir. Bu yüzden (RFC 5452 Bölüm 3):
/// <list type="bullet">
/// <item>Her sorgu yeni bir sokette, işletim sisteminin seçtiği rastgele kaynak portuyla gider (port 0'a bağlanma).</item>
/// <item>Soket <c>Connect</c> ile çözücünün IP:53 adresine bağlanır. UDP'de bu el sıkışma değildir; yalnızca
/// çekirdeğe "bu sokete yalnızca o adresten gelen datagramları ver" der.</item>
/// <item>Gelen datagramın adresi yine de denetlenir; adres veya işlem kimliği tutmazsa paket <b>yok sayılır</b> ve
/// süre dolana kadar beklemeye devam edilir. Sahte bir paket, gerçek cevabı engelleyemesin diye hata sayılmaz.</item>
/// </list>
/// </para>
/// <para>
/// Tekrar deneme burada yapılmaz; <see cref="DnsQueryClient"/> yeni kimlik ve yeni soketle tekrar dener.
/// </para>
/// </remarks>
public sealed class UdpDnsTransport(TimeSpan timeout) : IDnsTransport
{
    public DnsTransport Kind => DnsTransport.Udp;

    public async Task<DnsExchangeResult> ExchangeAsync(DnsResolver resolver, DnsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(query);

        var server = resolver.PlainEndpoint;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        using var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Port 0: "Bana boş bir port ver." İşletim sistemi geçici port aralığından rastgele birini seçer.
            var any = server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
            socket.Bind(new IPEndPoint(any, 0));
            await socket.ConnectAsync(server, deadline.Token).ConfigureAwait(false);
            await socket.SendAsync(query.Packet, SocketFlags.None, deadline.Token).ConfigureAwait(false);

            // 512 baytlık klasik sınırdan büyük tampon: Kurala uymayan büyük bir datagram da tamamen okunur ve
            // ayrıştırıcı karar verir. Tampon küçük olsaydı işletim sistemi paketi sessizce keserdi.
            var buffer = new byte[DnsWire.MaxMessageLength];
            EndPoint anySender = new IPEndPoint(any, 0);
            while (true)
            {
                var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, anySender, deadline.Token).ConfigureAwait(false);

                if (!server.Equals(received.RemoteEndPoint))
                {
                    continue; // başka bir adresten geldi: yok say
                }

                if (received.ReceivedBytes < 2 || BinaryPrimitives.ReadUInt16BigEndian(buffer) != query.Id)
                {
                    continue; // işlem kimliği bizim değil (ya da kimlik bile yok): yok say
                }

                return DnsExchangeResult.Ok(buffer.AsSpan(0, received.ReceivedBytes).ToArray());
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Dışarıdan iptal yok; kendi süremiz doldu.
            return DnsExchangeResult.Failed(DnsErrorKind.Timeout);
        }
        catch (SocketException)
        {
            // Ör. çözücüden "port kapalı" ICMP mesajı geldi (Windows'ta ConnectionReset olarak görünür).
            return DnsExchangeResult.Failed(DnsErrorKind.NetworkError);
        }
    }
}
