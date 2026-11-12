using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Fetching;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.ResourceLimits;

/// <summary>Güvenli çekicinin gövde sınırları: bombalar, yalan başlıklar, bitmeyen ve yavaş akışlar.</summary>
public sealed class SafeFetcherBodyAttackTests
{
    private static readonly Uri Page = new("https://web.archive.org/web/2026/http://ornek.example/");

    private static SafeFetcher Fetcher(StubHttpHandler handler, long maxBytes = 1024 * 1024, double timeoutSeconds = 10) =>
        new(handler, new SafeFetcherOptions { MaxBodyBytes = maxBytes, Timeout = TimeSpan.FromSeconds(timeoutSeconds) }, TimeProvider.System);

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Compression_bomb_is_cut_after_decompression_for_every_encoding(string encoding)
    {
        // ~32 MB sıfır; sıkıştırılmış hali birkaç KB. Sınır 1 MB.
        var bomb = Payloads.ZeroBomb(encoding, 32 * 1024 * 1024);
        Assert.True(bomb.Length < 1024 * 1024);
        var handler = new StubHttpHandler().EnqueueBytes(bomb, "text/html", encoding);
        using var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync(Page, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooLarge, result.Status);
        Assert.Null(result.Body);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Corrupt_compressed_body_is_reported_not_thrown(string encoding)
    {
        var handler = new StubHttpHandler().EnqueueBytes(Payloads.Garbage(4096), "text/html", encoding);
        using var fetcher = Fetcher(handler);

        var exception = await Record.ExceptionAsync(() => fetcher.FetchAsync(Page, TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Lying_content_length_does_not_bypass_the_limit()
    {
        // Başlık 10 bayt diyor, akış 5 MB veriyor.
        var handler = new StubHttpHandler().EnqueueStream(new MemoryStream(new byte[5 * 1024 * 1024]), "text/plain", declaredLength: 10);
        using var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync(Page, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooLarge, result.Status);
    }

    [Fact]
    public async Task Endless_body_is_cut_at_the_limit()
    {
        var handler = new StubHttpHandler().EnqueueStream(new EndlessStream(), "text/plain");
        using var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync(Page, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooLarge, result.Status);
    }

    [Fact]
    public async Task Slow_drip_body_hits_the_total_timeout()
    {
        // Slowloris benzeri: Her bayt arasında 200 ms. Toplam süre sınırı 1 saniye.
        var handler = new StubHttpHandler().EnqueueStream(new SlowStream(TimeSpan.FromMilliseconds(200)), "text/plain");
        using var fetcher = Fetcher(handler, timeoutSeconds: 1);

        var result = await fetcher.FetchAsync(Page, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Timeout, result.Status);
    }

    [Theory]
    [InlineData("text/html; charset=utf-7")]
    [InlineData("text/html; charset=\"x-bilinmeyen\"")]
    [InlineData("text/html; charset=utf-32")]
    public async Task Hostile_charset_values_do_not_throw(string contentType)
    {
        var handler = new StubHttpHandler().EnqueueBytes("+ADw-script+AD4-"u8.ToArray(), contentType);
        using var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync(Page, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Success, result.Status);
        // UTF-7 ile "<script>" kaçırma denemesi çözülmemeli.
        Assert.DoesNotContain("<script>", result.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hiç bitmeyen sıfır akışı.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Her okumada bir bayt veren ve araya bekleme koyan akış.</summary>
    private sealed class SlowStream(TimeSpan delay) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("yalnızca async");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            buffer.Span[0] = (byte)'a';
            return 1;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
