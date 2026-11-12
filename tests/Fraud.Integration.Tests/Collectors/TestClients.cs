using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Collectors.Wayback;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Collectors;

/// <summary>Sahte işleyiciyle çalışan, beklemesiz istemciler kurar.</summary>
internal static class TestClients
{
    public static CrtShOptions FastCrtShOptions(int maxRetries = 3) => new()
    {
        MinRequestInterval = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryAfter = TimeSpan.Zero,
        AttemptTimeout = TimeSpan.FromSeconds(10),
        MaxRetryAttempts = maxRetries,
    };

    public static WaybackCdxOptions FastWaybackOptions(int pageSize = 3, int maxPages = 10) => new()
    {
        MinRequestInterval = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryAfter = TimeSpan.Zero,
        AttemptTimeout = TimeSpan.FromSeconds(10),
        MaxRetryAttempts = 2,
        PageSize = pageSize,
        MaxPages = maxPages,
    };

    public static PoliteHttpClient Polite(FakeHttpHandler handler, SourceHttpOptions options) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new RequestPacer(options.MinRequestInterval, TimeProvider.System),
            options,
            TimeProvider.System);

    public static CrtShClient CrtSh(FakeHttpHandler handler, CrtShOptions? options = null) =>
        new(Polite(handler, options ?? FastCrtShOptions()));

    public static WaybackCdxClient Wayback(FakeHttpHandler handler, WaybackCdxOptions? options = null)
    {
        options ??= FastWaybackOptions();
        return new WaybackCdxClient(Polite(handler, options), options);
    }
}
