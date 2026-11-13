using System.Buffers.Binary;
using System.Net.Sockets;
using Fraud.Domain.Network;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Düz TCP ile DNS (RFC 1035 Bölüm 4.2.2, RFC 7766). Kesik (TC) UDP cevabından sonra ve ayrı bir ölçüm yolu olarak kullanılır.
/// </summary>
/// <remarks>
/// TCP bir bayt akışıdır; mesajların nerede bittiği belli değildir. Bu yüzden her mesajın önüne 2 baytlık
/// (büyük uç önce) uzunluk yazılır:
/// <code>
/// [uzunluk: 2 bayt][DNS mesajı: uzunluk kadar bayt]
/// </code>
/// Uzunluk 2 bayt olduğu için bir mesaj en fazla 65535 bayttır; bu yüzden ayrı bir boyut sınırına gerek yok.
/// Her sorgu için yeni bağlantı açılır ve kapatılır (günde birkaç yüz sorgu için bağlantı havuzu gereksiz).
/// </remarks>
public sealed class TcpDnsTransport(TimeSpan timeout) : IDnsTransport
{
    public DnsTransport Kind => DnsTransport.Tcp;

    public async Task<DnsExchangeResult> ExchangeAsync(DnsResolver resolver, DnsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(query);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        using var socket = new Socket(resolver.PlainEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(resolver.PlainEndpoint, deadline.Token).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: false);
            await using (stream.ConfigureAwait(false))
            {
                // Uzunluk ve mesaj tek yazmada gider (iki küçük TCP parçası yerine bir).
                var framed = new byte[2 + query.Packet.Length];
                BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Packet.Length);
                query.Packet.CopyTo(framed, 2);
                await stream.WriteAsync(framed, deadline.Token).ConfigureAwait(false);

                // Önce 2 baytlık uzunluk, sonra tam o kadar bayt. ReadExactlyAsync, eksik gelirse EndOfStreamException atar.
                var lengthBytes = new byte[2];
                await stream.ReadExactlyAsync(lengthBytes, deadline.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);

                var response = new byte[length];
                await stream.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
                return DnsExchangeResult.Ok(response);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DnsExchangeResult.Failed(DnsErrorKind.Timeout);
        }
        catch (EndOfStreamException)
        {
            // Sunucu, söylediği uzunluk kadar veri göndermeden bağlantıyı kapattı.
            return DnsExchangeResult.Failed(DnsErrorKind.NetworkError);
        }
        catch (IOException)
        {
            return DnsExchangeResult.Failed(DnsErrorKind.NetworkError);
        }
        catch (SocketException)
        {
            return DnsExchangeResult.Failed(DnsErrorKind.NetworkError);
        }
    }
}
