using Fraud.Collectors;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Collectors;

public sealed class PacingAndRetryTests
{
    [Fact]
    public async Task Pacer_lets_the_first_request_through_and_holds_the_next_for_the_interval()
    {
        var time = new FakeTimeProvider();
        using var pacer = new RequestPacer(TimeSpan.FromSeconds(5), time);
        var ct = TestContext.Current.CancellationToken;

        await pacer.WaitAsync(ct); // ilk istek beklemez

        var second = pacer.WaitAsync(ct);
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(0.1));
        await second.WaitAsync(TimeSpan.FromSeconds(5), ct);

        // Yeterince zaman geçtiyse bekleme yok.
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(pacer.WaitAsync(ct).IsCompleted);
    }

    [Fact]
    public async Task Pacer_serializes_concurrent_callers()
    {
        var time = new FakeTimeProvider();
        using var pacer = new RequestPacer(TimeSpan.FromSeconds(5), time);
        var ct = TestContext.Current.CancellationToken;

        var waits = Enumerable.Range(0, 3).Select(_ => pacer.WaitAsync(ct)).ToList();

        await Task.Yield();
        Assert.Equal(1, waits.Count(w => w.IsCompleted));
        time.Advance(TimeSpan.FromSeconds(5));
        await WaitUntil(() => waits.Count(w => w.IsCompleted) == 2);
        Assert.Equal(2, waits.Count(w => w.IsCompleted));
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.WhenAll(waits).WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(30, 30)]
    [InlineData(-5, 0)]
    [InlineData(3600, 300)] // üst sınır 5 dakika
    public void Retry_after_is_respected_up_to_the_limit(int? retryAfterSeconds, int? expectedSeconds)
    {
        var delay = PoliteHttpClient.RetryAfterDelay(
            retryAfterSeconds is { } s ? TimeSpan.FromSeconds(s) : null, TimeSpan.FromMinutes(5));

        Assert.Equal(expectedSeconds is { } e ? TimeSpan.FromSeconds(e) : null, delay);
    }

    [Theory]
    [InlineData(CollectionErrorKind.RateLimited, true)]
    [InlineData(CollectionErrorKind.ServerError, true)]
    [InlineData(CollectionErrorKind.Timeout, true)]
    [InlineData(CollectionErrorKind.NetworkError, true)]
    [InlineData(CollectionErrorKind.ClientError, false)]
    [InlineData(CollectionErrorKind.ResponseTooLarge, false)]
    [InlineData(CollectionErrorKind.InvalidResponse, false)]
    public void Only_transient_errors_are_retried(CollectionErrorKind kind, bool expected)
    {
        Assert.Equal(expected, PoliteHttpClient.IsTransient(kind));
    }

    [Fact]
    public async Task Di_registered_client_sends_generic_user_agent_and_uses_the_configured_handler()
    {
        var fake = new FakeHttpHandler().EnqueueText("[]");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // En kısa izinli aralık; tek istek olduğu için test beklemez.
                ["CrtSh:MinRequestInterval"] = "00:00:12",
                ["CrtSh:RetryBaseDelay"] = "00:00:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddCollectors(configuration);
        // Gerçek ağ işleyicisi yerine sahte işleyici. Ağa çıkılmaz.
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = fake));

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ICrtShClient>();

        var result = await client.SearchAsync("ornek.example", TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Empty, result.Outcome);
        var userAgent = Assert.Single(fake.UserAgents);
        Assert.Equal("FraudLab/0.1 (passive research)", userAgent);
        Assert.DoesNotContain('@', userAgent);
        Assert.Contains("application/json", fake.Accepts[0], StringComparison.Ordinal);
        Assert.Equal("crt.sh", fake.Requests[0].Host);
    }

    [Theory]
    [InlineData("00:00:05")]
    [InlineData("00:00:11.900")]
    public void CrtSh_interval_below_twelve_seconds_is_refused(string interval)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CrtSh:MinRequestInterval"] = interval })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCollectors(configuration));
        Assert.Equal(TimeSpan.FromSeconds(15), new CrtShOptions().MinRequestInterval);
    }

    [Fact]
    public void User_agent_with_an_email_address_is_refused()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Collection:UserAgent"] = "Bot (kisi@ornek.example)" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCollectors(configuration));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
