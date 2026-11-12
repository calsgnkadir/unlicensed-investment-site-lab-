using System.IO.Compression;
using System.Net;
using System.Text;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Fetching;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Fetching;

// SSRF testleri. Hiçbiri gerçek ağa çıkmaz: ya sahte işleyici ya da sahte DNS çözümleyici kullanılır.
public sealed class SafeFetcherTests
{
    private static readonly Uri ArchivePage = new("https://web.archive.org/web/20260101000000/http://ornek.example/");

    private static SafeFetcherOptions Options(long maxBytes = 1024 * 1024, double timeoutSeconds = 10) => new()
    {
        MaxBodyBytes = maxBytes,
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        MaxRedirects = 3,
    };

    private static SafeFetcher Fetcher(FakeHttpHandler handler, SafeFetcherOptions? options = null) =>
        new(handler, options ?? Options(), TimeProvider.System);

    [Theory]
    [InlineData("file:///etc/passwd", SafeFetchBlockReason.SchemeNotAllowed)]
    [InlineData("http://web.archive.org/", SafeFetchBlockReason.SchemeNotAllowed)]
    [InlineData("gopher://web.archive.org/", SafeFetchBlockReason.SchemeNotAllowed)]
    [InlineData("https://127.0.0.1/", SafeFetchBlockReason.IpLiteralNotAllowed)]
    [InlineData("https://169.254.169.254/latest/meta-data/", SafeFetchBlockReason.IpLiteralNotAllowed)]
    [InlineData("https://[::ffff:127.0.0.1]/", SafeFetchBlockReason.IpLiteralNotAllowed)]
    [InlineData("https://[::1]/", SafeFetchBlockReason.IpLiteralNotAllowed)]
    [InlineData("https://web.archive.org@127.0.0.1/", SafeFetchBlockReason.UserInfoNotAllowed)]
    [InlineData("https://web.archive.org:8443/", SafeFetchBlockReason.PortNotAllowed)]
    [InlineData("https://web.archive.org.evil.example/", SafeFetchBlockReason.HostNotAllowed)]
    [InlineData("https://archive.org/", SafeFetchBlockReason.HostNotAllowed)]
    [InlineData("https://localhost/", SafeFetchBlockReason.HostNotAllowed)]
    public async Task Disallowed_urls_are_blocked_before_any_request(string url, SafeFetchBlockReason expected)
    {
        var handler = new FakeHttpHandler();

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(new Uri(url), TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Blocked, result.Status);
        Assert.Equal(expected, result.BlockReason);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://2130706433/")]   // 127.0.0.1'in ondalık yazımı
    [InlineData("https://0x7f000001/")]   // onaltılık yazım
    [InlineData("https://0177.0.0.1/")]   // sekizlik yazım
    [InlineData("https://127.1/")]        // kısaltılmış yazım
    public async Task Alternative_ip_notations_are_blocked(string url)
    {
        var handler = new FakeHttpHandler();

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(new Uri(url), TestContext.Current.CancellationToken);

        // .NET bu yazımları IPv4 olarak tanır (IpLiteralNotAllowed); tanımasa bile izin listesinde değildir.
        Assert.Equal(SafeFetchStatus.Blocked, result.Status);
        Assert.Contains(result.BlockReason, new[] { SafeFetchBlockReason.IpLiteralNotAllowed, SafeFetchBlockReason.HostNotAllowed });
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://127.0.0.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    [InlineData("//127.0.0.1/goreli-sema")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://evil.example/")]
    public async Task Redirect_from_allowed_host_to_internal_address_is_blocked(string location)
    {
        var handler = new FakeHttpHandler().EnqueueRedirect(location);

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Blocked, result.Status);
        Assert.Equal(1, result.Redirects);
        Assert.Single(handler.Requests); // yönlendirilen adrese istek gitmedi
    }

    [Fact]
    public async Task Redirect_within_allowed_host_is_followed()
    {
        var handler = new FakeHttpHandler()
            .EnqueueRedirect("/web/20260101000001/http://ornek.example/")
            .EnqueueText("<html><script>alert(1)</script>Merhaba</html>", "text/html");

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Success, result.Status);
        Assert.Equal(1, result.Redirects);
        Assert.Equal("web.archive.org", result.FinalUri!.Host);
        // İçerik olduğu gibi metin döner; çalıştırılmaz, yorumlanmaz.
        Assert.Equal("<html><script>alert(1)</script>Merhaba</html>", result.Body);
        Assert.Equal("text/html", result.ContentType);
    }

    [Fact]
    public async Task Too_many_redirects_stop()
    {
        var handler = new FakeHttpHandler();
        for (var i = 0; i < 4; i++)
        {
            handler.EnqueueRedirect($"/web/{i}/");
        }

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooManyRedirects, result.Status);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("application/javascript")]
    [InlineData("image/svg+xml")]
    [InlineData("application/pdf")]
    public async Task Non_text_content_is_refused(string mediaType)
    {
        var handler = new FakeHttpHandler().EnqueueText("x", mediaType);

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.UnsupportedContent, result.Status);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task Body_over_the_limit_is_refused()
    {
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(new byte[4096])) { Headers = { { "Content-Type", "text/html" } } },
        });

        using var fetcher = Fetcher(handler, Options(maxBytes: 1024));
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooLarge, result.Status);
    }

    [Fact]
    public async Task Compression_bomb_is_measured_after_decompression()
    {
        // 16 MB sıfır, gzip ile birkaç on KB'a iner. Sınır 1 MB.
        var compressed = Gzip(new byte[16 * 1024 * 1024]);
        Assert.True(compressed.Length < 1024 * 1024);
        var handler = new FakeHttpHandler().Enqueue(() => Compressed(compressed, "gzip"));

        using var fetcher = Fetcher(handler, Options(maxBytes: 1024 * 1024));
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooLarge, result.Status);
    }

    [Fact]
    public async Task Small_gzip_body_is_decoded()
    {
        var handler = new FakeHttpHandler().Enqueue(() => Compressed(Gzip(Encoding.UTF8.GetBytes("Yatırım ilanı")), "gzip"));

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Success, result.Status);
        Assert.Equal("Yatırım ilanı", result.Body);
    }

    [Theory]
    [InlineData("compress")]
    [InlineData("gzip, gzip")]
    public async Task Unknown_or_stacked_encodings_are_refused(string encoding)
    {
        var handler = new FakeHttpHandler().Enqueue(() => Compressed(Gzip([1, 2, 3]), encoding));

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.UnsupportedContent, result.Status);
    }

    [Fact]
    public async Task Slow_server_hits_the_total_timeout()
    {
        var handler = new FakeHttpHandler().EnqueueHang();

        using var fetcher = Fetcher(handler, Options(timeoutSeconds: 0.2));
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task Non_success_status_is_reported()
    {
        var handler = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NotFound);

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.HttpError, result.Status);
        Assert.Equal(404, result.HttpStatus);
    }

    // Bağlantı anı denetimi: Ad izinli, ama DNS iç adres döndürüyor (DNS rebinding).
    // Gerçek SocketsHttpHandler kullanılır; çözümleyici sahtedir, soket açılmadan reddedilir.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("100.64.1.1")]
    [InlineData("fe80::1")]
    public async Task Allowed_host_resolving_to_internal_address_is_blocked_at_connect_time(string resolved)
    {
        var resolver = new FakeResolver(IPAddress.Parse(resolved));

        using var fetcher = new SafeFetcher(SafeFetcher.CreateTransport(resolver), Options(), TimeProvider.System);
        var result = await fetcher.FetchAsync(ArchivePage, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Blocked, result.Status);
        Assert.Equal(SafeFetchBlockReason.AddressNotAllowed, result.BlockReason);
        Assert.Equal(["web.archive.org"], resolver.Queries);
    }

    [Fact]
    public async Task Mixed_dns_answer_with_one_internal_address_is_blocked()
    {
        var resolver = new FakeResolver(IPAddress.Parse("207.241.224.2"), IPAddress.Parse("127.0.0.1"));
        var connector = new SafeConnector(resolver);

        await Assert.ThrowsAsync<BlockedAddressException>(
            () => connector.ResolveAllowedAsync("web.archive.org", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Empty_dns_answer_is_blocked()
    {
        var connector = new SafeConnector(new FakeResolver());

        await Assert.ThrowsAsync<BlockedAddressException>(
            () => connector.ResolveAllowedAsync("web.archive.org", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::ffff:127.0.0.1]")]
    [InlineData("::1")]
    public async Task Ip_literal_reaching_the_connector_is_checked_without_dns(string host)
    {
        var resolver = new FakeResolver(IPAddress.Parse("8.8.8.8"));
        var connector = new SafeConnector(resolver);

        await Assert.ThrowsAsync<BlockedAddressException>(
            () => connector.ResolveAllowedAsync(host, TestContext.Current.CancellationToken));
        Assert.Empty(resolver.Queries);
    }

    [Fact]
    public async Task Mapped_public_address_is_connected_as_plain_ipv4()
    {
        var connector = new SafeConnector(new FakeResolver(IPAddress.Parse("::ffff:207.241.224.2")));

        var addresses = await connector.ResolveAllowedAsync("web.archive.org", TestContext.Current.CancellationToken);

        Assert.Equal([IPAddress.Parse("207.241.224.2")], addresses);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static HttpResponseMessage Compressed(byte[] body, string encoding)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=utf-8");
        content.Headers.TryAddWithoutValidation("Content-Encoding", encoding);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class FakeResolver(params IPAddress[] addresses) : IHostAddressResolver
    {
        public List<string> Queries { get; } = [];

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Queries.Add(host);
            return Task.FromResult(addresses);
        }
    }
}
