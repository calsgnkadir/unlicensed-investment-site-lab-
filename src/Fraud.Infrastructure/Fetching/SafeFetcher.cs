using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Http;

namespace Fraud.Infrastructure.Fetching;

/// <summary>
/// Arşivden metin sayfa çeken, SSRF'e karşı korumalı istemci.
/// </summary>
/// <remarks>
/// Koruma katmanları:
/// <list type="number">
/// <item>Adres denetimi (<see cref="SafeUrlPolicy"/>): yalnızca https, izinli ana bilgisayar, varsayılan port, IP yazımı yok.</item>
/// <item>Yönlendirmeler otomatik izlenmez; her <c>Location</c> aynı denetimden yeniden geçer.</item>
/// <item>Bağlantı anında IP denetimi (<see cref="SafeConnector"/>): ad çözüldükten sonra özel adreslere bağlanılmaz.</item>
/// <item>Vekil sunucu kullanılmaz; vekil, bağlantı denetimini atlatırdı.</item>
/// <item>Sıkıştırma burada açılır ve sınır açılmış bayt üzerinden sayılır (sıkıştırma bombası).</item>
/// <item>Toplam süre sınırı ve yalnızca metin içerik türleri.</item>
/// </list>
/// İçerik hiçbir zaman çalıştırılmaz, dosya olarak açılmaz; yalnızca metin olarak döner.
/// </remarks>
public sealed class SafeFetcher : ISafeFetcher, IDisposable
{
    /// <summary>
    /// İstek en fazla HTTP/2 olur, asla HTTP/3'e yükseltilmez. HTTP/3 (QUIC) bağlantıları
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> üzerinden geçmez; yükseltilirse bağlantı anındaki IP denetimi atlanır.
    /// </summary>
    public static readonly Version RequestVersion = HttpVersion.Version20;

    /// <summary><see cref="RequestVersion"/> ya da daha düşüğü (HTTP/1.1); daha yükseği asla.</summary>
    public const HttpVersionPolicy RequestVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

    private const int Utf7CodePage = 65000;

    private readonly HttpClient _client;
    private readonly SafeFetcherOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <param name="transport">
    /// Üretimde <see cref="CreateTransport"/> ile kurulan işleyici. Testlerde sahte işleyici verilir.
    /// </param>
    public SafeFetcher(HttpMessageHandler transport, SafeFetcherOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _client = new HttpClient(transport, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = RequestVersion,
            DefaultVersionPolicy = RequestVersionPolicy,
        };
    }

    /// <summary>Bağlantı anında IP denetimi yapan gerçek ağ işleyicisi.</summary>
    public static SocketsHttpHandler CreateTransport(IHostAddressResolver resolver)
    {
        var connector = new SafeConnector(resolver);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseProxy = false,
            UseCookies = false,
            ConnectCallback = connector.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 2,
            MaxResponseHeadersLength = 64,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
    }

    public async Task<SafeFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        using var timeout = new CancellationTokenSource(_options.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var current = url;
        var redirects = 0;

        try
        {
            while (true)
            {
                var block = SafeUrlPolicy.Check(current, _options);
                if (block != SafeFetchBlockReason.None)
                {
                    return SafeFetchResult.Blocked(block, current, redirects);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current)
                {
                    Version = RequestVersion,
                    VersionPolicy = RequestVersionPolicy,
                };
                request.Headers.UserAgent.ParseAdd(_options.UserAgent);
                request.Headers.Accept.ParseAdd("text/html, text/plain;q=0.9");
                request.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");

                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                    .ConfigureAwait(false);
                var status = (int)response.StatusCode;

                if (status is >= 300 and <= 399 && status != 304)
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return SafeFetchResult.Failed(SafeFetchStatus.InvalidResponse, current, status, redirects);
                    }

                    if (redirects >= _options.MaxRedirects)
                    {
                        return SafeFetchResult.Failed(SafeFetchStatus.TooManyRedirects, current, status, redirects);
                    }

                    // Göreli yönlendirme bulunduğumuz adrese göre çözülür; sonra döngü başında yeniden denetlenir.
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    redirects++;
                    continue;
                }

                if (status is < 200 or > 299)
                {
                    return SafeFetchResult.Failed(SafeFetchStatus.HttpError, current, status, redirects);
                }

                return await ReadBodyAsync(response, current, redirects, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return SafeFetchResult.Failed(SafeFetchStatus.Timeout, current, null, redirects);
        }
        catch (HttpRequestException ex) when (IsBlockedAddress(ex))
        {
            return SafeFetchResult.Blocked(SafeFetchBlockReason.AddressNotAllowed, current, redirects);
        }
        catch (HttpRequestException)
        {
            return SafeFetchResult.Failed(SafeFetchStatus.NetworkError, current, null, redirects);
        }
        catch (IOException)
        {
            return SafeFetchResult.Failed(SafeFetchStatus.NetworkError, current, null, redirects);
        }
    }

    private async Task<SafeFetchResult> ReadBodyAsync(HttpResponseMessage response, Uri at, int redirects, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var headers = response.Content.Headers;
        var mediaType = headers.ContentType?.MediaType?.Trim().ToLowerInvariant();
        if (mediaType is null || !_options.AllowedContentTypes.Contains(mediaType, StringComparer.Ordinal))
        {
            return SafeFetchResult.Failed(SafeFetchStatus.UnsupportedContent, at, status, redirects);
        }

        if (headers.ContentLength > _options.MaxBodyBytes)
        {
            return SafeFetchResult.Failed(SafeFetchStatus.TooLarge, at, status, redirects);
        }

        var raw = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (raw.ConfigureAwait(false))
        {
            var decoded = OpenDecoder(raw, headers.ContentEncoding);
            if (decoded is null)
            {
                return SafeFetchResult.Failed(SafeFetchStatus.UnsupportedContent, at, status, redirects);
            }

            await using (decoded.ConfigureAwait(false))
            {
                byte[]? bytes;
                try
                {
                    bytes = await BoundedStreamReader.ReadAsync(decoded, _options.MaxBodyBytes, ct).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    // Bozuk gzip veya deflate gövdesi.
                    return SafeFetchResult.Failed(SafeFetchStatus.InvalidResponse, at, status, redirects);
                }
                catch (InvalidOperationException)
                {
                    // BrotliStream bozuk veride InvalidOperationException fırlatır.
                    return SafeFetchResult.Failed(SafeFetchStatus.InvalidResponse, at, status, redirects);
                }

                if (bytes is null)
                {
                    return SafeFetchResult.Failed(SafeFetchStatus.TooLarge, at, status, redirects);
                }

                var text = GetEncoding(headers.ContentType).GetString(bytes);
                return new SafeFetchResult(
                    SafeFetchStatus.Success, SafeFetchBlockReason.None, at, status, mediaType, text, bytes.LongLength, redirects);
            }
        }
    }

    /// <summary>Tek katmanlı sıkıştırmayı açar. Bilinmeyen ya da üst üste sıkıştırma reddedilir (<c>null</c>).</summary>
    private static Stream? OpenDecoder(Stream raw, ICollection<string> contentEncoding)
    {
        var encodings = contentEncoding.Select(e => e.Trim().ToLowerInvariant()).Where(e => e is not ("" or "identity")).ToList();
        if (encodings.Count == 0)
        {
            return raw;
        }

        if (encodings.Count > 1)
        {
            return null;
        }

        return encodings[0] switch
        {
            "gzip" or "x-gzip" => new GZipStream(raw, CompressionMode.Decompress, leaveOpen: true),
            "deflate" => new ZLibStream(raw, CompressionMode.Decompress, leaveOpen: true),
            "br" => new BrotliStream(raw, CompressionMode.Decompress, leaveOpen: true),
            _ => null,
        };
    }

    /// <summary>
    /// Karakter kümesi başlıktan alınır; bilinmiyor, desteklenmiyor ya da UTF-7 ise UTF-8 (geçersiz bayt yerine U+FFFD).
    /// </summary>
    /// <remarks>
    /// UTF-7, "+ADw-script+AD4-" gibi yazımlarla süzgeçlerden kaçan işaretleme üretebilir; .NET'te varsayılan olarak
    /// kapalıdır (<see cref="NotSupportedException"/>), ama biri açarsa da kullanılmasın diye ayrıca reddedilir.
    /// </remarks>
    private static Encoding GetEncoding(MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet?.Trim().Trim('"');
        if (string.IsNullOrEmpty(charset))
        {
            return Encoding.UTF8;
        }

        Encoding encoding;
        try
        {
            encoding = CodePagesEncodingProvider.Instance.GetEncoding(charset) ?? Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
        catch (NotSupportedException)
        {
            return Encoding.UTF8;
        }

        return encoding.CodePage == Utf7CodePage ? Encoding.UTF8 : encoding;
    }

    private static bool IsBlockedAddress(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose() => _client.Dispose();
}
