using System.Net;
using System.Net.Http.Headers;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Http;

namespace Fraud.Collectors.Dns;

/// <summary>
/// HTTPS içinde DNS (DoH, RFC 8484). Aynı DNS paketi, HTTP POST gövdesi olarak gönderilir.
/// </summary>
/// <remarks>
/// <para>
/// Neden: Servis sağlayıcı düz UDP/TCP 53 trafiğini görebilir ve değiştirebilir. DoH şifreli olduğu için cevap
/// yolda değiştirilemez. UDP ile DoH cevabını karşılaştırmak "araya girme var mı" sorusunun ölçümüdür
/// (DECISIONS.md, 2026-11-13).
/// </para>
/// <para>
/// İstek (RFC 8484 Bölüm 4.1): <c>POST</c>, gövde ham DNS paketi, <c>Content-Type: application/dns-message</c>.
/// İşlem kimliği 0 gönderilir: Kimliğin sahte cevaba karşı koruma işi burada TLS'tedir, ve 0 kimliği
/// HTTP önbelleklerinin aynı soruyu tanımasını sağlar. HTTP/2 istenir (Bölüm 5.2: en az HTTP/2 önerilir), ama
/// HTTP/3'e çıkılmaz: HTTP/3 (QUIC) bağlantısı <c>ConnectCallback</c>'i kullanmadığı için izin listesi ve IP denetimi
/// atlanırdı. Bu yüzden politika <c>RequestVersionOrLower</c>'dır (SafeFetcher ile aynı, THREAT_MODEL N1).
/// </para>
/// <para>
/// Bağlantının yalnızca izinli DoH adreslerine açılması HTTP işleyicisinde, bağlantı anında denetlenir
/// (bkz. <c>CollectorServiceCollectionExtensions.AddNetworkCollectors</c>).
/// </para>
/// </remarks>
public sealed class DohDnsTransport(HttpClient http, TimeSpan timeout) : IDnsTransport
{
    public const string MediaType = "application/dns-message";

    public DnsTransport Kind => DnsTransport.Doh;

    public async Task<DnsExchangeResult> ExchangeAsync(DnsResolver resolver, DnsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(query);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using var content = new ByteArrayContent(query.Packet);
            content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
            using var request = new HttpRequestMessage(HttpMethod.Post, resolver.DohUri)
            {
                Content = content,
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            // RFC 8484 Bölüm 4.2.1: Başarılı cevap 2xx'tir. Biz yalnızca 200'ü kabul ederiz; çözücülerin gerçekte döndürdüğü budur.
            if (status != 200)
            {
                return DnsExchangeResult.Failed(DnsErrorKind.HttpError, status);
            }

            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, MediaType, StringComparison.OrdinalIgnoreCase))
            {
                return DnsExchangeResult.Failed(DnsErrorKind.HttpError, status);
            }

            try
            {
                var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    // Bir DNS mesajı 65535 baytı geçemez; daha büyük gövde (açılmış haliyle) okunmadan kesilir.
                    var body = await BoundedStreamReader.ReadAsync(stream, DnsWire.MaxMessageLength, deadline.Token).ConfigureAwait(false);
                    return body is null
                        ? DnsExchangeResult.Failed(DnsErrorKind.ResponseTooLarge, status)
                        : DnsExchangeResult.Ok(body, status);
                }
            }
            catch (InvalidDataException)
            {
                // Bozuk gzip veya deflate gövdesi (THREAT_MODEL N2). Yalnızca gövde okunurken yakalanır.
                return DnsExchangeResult.Failed(DnsErrorKind.Malformed, status);
            }
            catch (InvalidOperationException)
            {
                // Bozuk brotli gövdesi BrotliStream'de InvalidOperationException olarak çıkar.
                return DnsExchangeResult.Failed(DnsErrorKind.Malformed, status);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DnsExchangeResult.Failed(DnsErrorKind.Timeout);
        }
        catch (HttpRequestException)
        {
            // Bağlantı kurulamadı, TLS doğrulanamadı ya da izinli olmayan bir adrese bağlanılmak istendi.
            return DnsExchangeResult.Failed(DnsErrorKind.NetworkError);
        }
        catch (IOException)
        {
            return DnsExchangeResult.Failed(DnsErrorKind.NetworkError);
        }
    }
}
