using System.Net;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Fetching;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Ssrf;

/// <summary>
/// İzinli arşivden başlayıp iç adrese yönlendiren zincirler. Temel özellik: Yönlendirme hedefi denetimi geçemezse
/// ikinci istek hiç gönderilmez.
/// </summary>
public sealed class RedirectAttackTests
{
    private static readonly Uri Start = new("https://web.archive.org/web/2026/http://ornek.example/");

    private static SafeFetcher Fetcher(StubHttpHandler handler) =>
        new(handler, new SafeFetcherOptions { MaxRedirects = 5, Timeout = TimeSpan.FromSeconds(10) }, TimeProvider.System);

    [Theory]
    [InlineData("https://0x7f000001/")]
    [InlineData("https://2852039166/latest/meta-data/")]
    [InlineData("https://web.archive.org@169.254.169.254/")]
    [InlineData("https://[::ffff:a9fe:a9fe]/")]
    [InlineData("\\\\127.0.0.1\\paylasim")]
    [InlineData("//169.254.169.254/latest/meta-data/")]
    [InlineData("gopher://127.0.0.1:6379/_FLUSHALL")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("http://web.archive.org/")]
    [InlineData("https://web.archive.org:8080/")]
    public async Task Redirect_to_disallowed_target_sends_no_second_request(string location)
    {
        foreach (var status in new[] { HttpStatusCode.Found, HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect })
        {
            var handler = new StubHttpHandler().EnqueueRedirect(location, status);
            using var fetcher = Fetcher(handler);

            var result = await fetcher.FetchAsync(Start, TestContext.Current.CancellationToken);

            Assert.NotEqual(SafeFetchStatus.Success, result.Status);
            Assert.Single(handler.Requests);
        }
    }

    [Theory]
    [InlineData("https:127.0.0.1")]
    [InlineData("/\\127.0.0.1/")]
    [InlineData("https:/127.0.0.1/")]
    public async Task Ambiguous_locations_resolve_under_the_archive_host(string location)
    {
        // Bu yazımlar .NET'te arşiv altında göreli yol olarak çözülür; iç adrese gidilmez.
        var handler = new StubHttpHandler()
            .EnqueueRedirect(location)
            .EnqueueBytes("tamam"u8.ToArray(), "text/plain");
        using var fetcher = Fetcher(handler);

        await fetcher.FetchAsync(Start, TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, u => Assert.Equal("web.archive.org", u.IdnHost));
    }

    [Fact]
    public async Task Internal_target_deep_in_a_redirect_chain_is_still_blocked()
    {
        var handler = new StubHttpHandler()
            .EnqueueRedirect("https://web.archive.org/web/1/")
            .EnqueueRedirect("/web/2/")
            .EnqueueRedirect("https://web.archive.org./web/3/")
            .EnqueueRedirect("https://0177.0.0.1/");
        using var fetcher = Fetcher(handler);

        var result = await fetcher.FetchAsync(Start, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Blocked, result.Status);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, u => Assert.Equal("web.archive.org", u.IdnHost.TrimEnd('.')));
    }

    [Fact]
    public async Task Redirect_loop_stops_at_the_limit()
    {
        var handler = new StubHttpHandler();
        for (var i = 0; i < 20; i++)
        {
            handler.EnqueueRedirect("/web/loop/");
        }

        using var fetcher = Fetcher(handler);
        var result = await fetcher.FetchAsync(Start, TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.TooManyRedirects, result.Status);
        Assert.Equal(6, handler.Requests.Count); // ilk istek + 5 yönlendirme
    }
}
