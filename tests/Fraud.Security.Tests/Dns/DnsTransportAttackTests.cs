using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Fraud.Collectors.Dns;
using Fraud.Domain.Network;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Dns;

/// <summary>
/// Taşıma katmanına saldırgan sunucu davranışı: sahte datagram seli, yalan uzunluk öneki, damla damla gövde,
/// sıkıştırma bombası, bozuk sıkıştırma, yönlendirme. Yalnızca 127.0.0.1; hiçbir paket makineden çıkmaz.
/// </summary>
public sealed class DnsTransportAttackTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(1);

    private static DnsResolver Local(int port, Uri? doh = null) =>
        new("yerel", new IPEndPoint(IPAddress.Loopback, port), doh ?? new Uri("https://dns.invalid/dns-query"));

    private static DnsQuery Query(ushort id = 0x5151) => DnsQueryBuilder.Build(id, "ornek.example", DnsRecordType.A);

    [Fact]
    public async Task Udp_flood_of_spoofed_and_wrong_id_datagrams_cannot_extend_the_deadline()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var spoofer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        var query = Query();

        var attack = Task.Run(async () =>
        {
            var received = await server.ReceiveAsync(ct);
            var good = DnsPacket.Answer(query, [("ornek.example", 1, DnsPacket.Ipv4("192.0.2.1"))]);
            var wrongId = (byte[])good.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(wrongId, (ushort)(query.Id ^ 0xFFFF));
            for (var i = 0; i < 400; i++)
            {
                await spoofer.SendAsync(good, received.RemoteEndPoint, ct);   // doğru kimlik, yanlış kaynak port
                await server.SendAsync(wrongId, received.RemoteEndPoint, ct); // doğru kaynak, yanlış kimlik
                await server.SendAsync(new byte[] { 0x51 }, received.RemoteEndPoint, ct); // kimlik bile yok
            }
        }, ct);

        var stopwatch = Stopwatch.StartNew();
        var result = await new UdpDnsTransport(Short).ExchangeAsync(Local(port), query, ct);
        stopwatch.Stop();
        try
        {
            await attack;
        }
        catch (SocketException)
        {
            // İstemci soketi kapandıktan sonra gelen ICMP "port kapalı" (Windows'ta ConnectionReset).
        }

        Assert.Equal(DnsErrorKind.Timeout, result.Error);
        Assert.Null(result.Response);
        Assert.True(stopwatch.Elapsed < Short + TimeSpan.FromSeconds(2), $"Süre {stopwatch.Elapsed}");
    }

    private static async Task<DnsExchangeResult> TcpAgainst(Func<NetworkStream, CancellationToken, Task> serve, TimeSpan timeout)
    {
        var ct = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                var length = new byte[2];
                await stream.ReadExactlyAsync(length, ct);
                await stream.ReadExactlyAsync(new byte[BinaryPrimitives.ReadUInt16BigEndian(length)], ct);
                try
                {
                    await serve(stream, ct);
                }
                catch (IOException)
                {
                    // İstemci süresi dolunca bağlantıyı kapatır.
                }
            }, ct);

            var result = await new TcpDnsTransport(timeout).ExchangeAsync(Local(port), Query(), ct);
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            catch (SocketException)
            {
            }

            return result;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Tcp_length_prefix_65535_then_close_is_a_network_error()
    {
        var result = await TcpAgainst(async (s, ct) =>
        {
            await s.WriteAsync(new byte[] { 0xFF, 0xFF, 1, 2, 3 }, ct);
            s.Close();
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(DnsErrorKind.NetworkError, result.Error);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Tcp_slow_drip_body_hits_the_deadline()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await TcpAgainst(async (s, ct) =>
        {
            await s.WriteAsync(new byte[] { 0x00, 0x40 }, ct);
            for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
            {
                await s.WriteAsync(new byte[] { 0 }, ct);
                await Task.Delay(100, ct);
            }
        }, Short);

        Assert.Equal(DnsErrorKind.Timeout, result.Error);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), $"Süre {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Tcp_zero_length_message_never_becomes_an_answer()
    {
        var exchange = await TcpAgainst(async (s, ct) => await s.WriteAsync(new byte[] { 0, 0 }, ct), TimeSpan.FromSeconds(5));

        // Taşıma boş diziyi taşır; ayrıştırıcı onu reddeder (Malformed), "kayıt yok" olmaz.
        Assert.Equal(DnsErrorKind.None, exchange.Error);
        Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(exchange.Response!));
    }

    private static HttpResponseMessage DnsMessage(HttpContent content, HttpStatusCode status = HttpStatusCode.OK, string media = DohDnsTransport.MediaType)
    {
        content.Headers.ContentType = new MediaTypeHeaderValue(media);
        return new HttpResponseMessage(status) { Content = content };
    }

    [Fact]
    public async Task Doh_body_larger_than_a_dns_message_is_cut()
    {
        var handler = new StubHttpHandler().Enqueue(_ => DnsMessage(new ByteArrayContent(new byte[DnsWire.MaxMessageLength + 1])));
        using var http = new HttpClient(handler);

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(5))
            .ExchangeAsync(DnsResolverCatalog.Cloudflare, Query(0), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.ResponseTooLarge, result.Error);
    }

    [Fact]
    public async Task Doh_endless_body_with_a_lying_content_length_is_cut()
    {
        var handler = new StubHttpHandler().Enqueue(_ =>
        {
            var content = new StreamContent(new EndlessStream());
            content.Headers.ContentLength = 100;
            return DnsMessage(content);
        });
        using var http = new HttpClient(handler);

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(5))
            .ExchangeAsync(DnsResolverCatalog.Cloudflare, Query(0), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.ResponseTooLarge, result.Error);
    }

    [Fact]
    public async Task Doh_slow_body_hits_the_deadline()
    {
        var handler = new StubHttpHandler().Enqueue(_ => DnsMessage(new StreamContent(new SlowStream(TimeSpan.FromMilliseconds(200)))));
        using var http = new HttpClient(handler);
        var stopwatch = Stopwatch.StartNew();

        var result = await new DohDnsTransport(http, Short)
            .ExchangeAsync(DnsResolverCatalog.Cloudflare, Query(0), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.Timeout, result.Error);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Süre {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("application/dns-json")]
    [InlineData("text/html")]
    [InlineData("application/json")]
    public async Task Doh_wrong_content_type_is_an_http_error(string media)
    {
        var handler = new StubHttpHandler().Enqueue(_ => DnsMessage(new ByteArrayContent(new byte[20]), media: media));
        using var http = new HttpClient(handler);

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(5))
            .ExchangeAsync(DnsResolverCatalog.Cloudflare, Query(0), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.HttpError, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Doh_redirect_is_not_followed(HttpStatusCode status)
    {
        var handler = new StubHttpHandler().EnqueueRedirect("http://169.254.169.254/latest/meta-data/", status);
        using var http = new HttpClient(handler);

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(5))
            .ExchangeAsync(DnsResolverCatalog.Cloudflare, Query(0), TestContext.Current.CancellationToken);

        Assert.Equal((DnsErrorKind.HttpError, (int)status), (result.Error, result.HttpStatus));
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Yerel HTTP/1.1 test sunucusu HTTP/2 konuşmaz. Bu işleyici yalnızca sürümü düşürür; asıl iş (otomatik sıkıştırma
    /// açma) üretimdeki gibi gerçek <see cref="SocketsHttpHandler"/> ile yapılır.
    /// </summary>
    private sealed class Http11Downgrade(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static HttpClient RealDecompressingClient() =>
        new(new Http11Downgrade(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            UseProxy = false,
        }));

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Doh_compression_bomb_is_cut_after_decompression(string encoding)
    {
        await using var server = new LoopbackHttpServer(
            [("Content-Type", DohDnsTransport.MediaType), ("Content-Encoding", encoding)],
            Payloads.ZeroBomb(encoding, 16 * 1024 * 1024));
        using var http = RealDecompressingClient();

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(10))
            .ExchangeAsync(Local(53, server.BaseUri), Query(0), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.ResponseTooLarge, result.Error);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Doh_corrupt_compressed_body_is_an_error_result_not_an_exception(string encoding)
    {
        // Bulgu N2: DohDnsTransport bozuk gzip/deflate (InvalidDataException) ve brotli (InvalidOperationException)
        // istisnasını yakalamıyor. İstisna DnsQueryClient ve DnsCollector'dan çıkar; o alan adının o ana kadarki
        // bütün DNS ölçümleri ve collection_log satırı yazılmadan kaybolur.
        await using var server = new LoopbackHttpServer(
            [("Content-Type", DohDnsTransport.MediaType), ("Content-Encoding", encoding)], Payloads.Garbage(4096));
        using var http = RealDecompressingClient();
        DnsExchangeResult? result = null;

        var exception = await Record.ExceptionAsync(async () => result = await new DohDnsTransport(http, TimeSpan.FromSeconds(10))
            .ExchangeAsync(Local(53, server.BaseUri), Query(0), TestContext.Current.CancellationToken));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.NotEqual(DnsErrorKind.None, result.Error);
    }

    [Fact]
    public async Task Doh_request_cannot_upgrade_to_http3_outside_the_connect_callback()
    {
        // Bulgu N1 (olası): RequestVersionOrHigher + https ile SocketsHttpHandler, sunucunun Alt-Svc: h3 başlığından
        // sonra QUIC'e geçebilir. QUIC bağlantısı ConnectCallback'i (AllowedHostConnector + SafeConnector) kullanmaz.
        // SafeFetcher aynı sorun için RequestVersionOrLower kullanıyor (THREAT_MODEL 3.2).
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);

        await new DohDnsTransport(http, TimeSpan.FromSeconds(5))
            .ExchangeAsync(DnsResolverCatalog.Cloudflare, Query(0), TestContext.Current.CancellationToken);

        Assert.NotNull(handler.Last);
        Assert.True(handler.Last.Version.Major < 3);
        Assert.NotEqual(HttpVersionPolicy.RequestVersionOrHigher, handler.Last.VersionPolicy);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(DnsMessage(new ByteArrayContent([])));
        }
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)0, offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SlowStream(TimeSpan delay) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            buffer.Span[0] = 0;
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
