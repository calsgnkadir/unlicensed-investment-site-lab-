using Fraud.Collectors;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Security.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Security.Tests.ResourceLimits;

/// <summary>
/// Toplayıcıların üretimdeki HTTP işleyicisi (DI'dan alınır) ile yalnızca 127.0.0.1 üzerindeki yerel test sunucusuna
/// istek atılır. Amaç, otomatik sıkıştırma açma ve hata eşleme davranışını gerçek soketle sınamaktır.
/// </summary>
public sealed class CollectorTransportAttackTests
{
    private static ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddCollectors(configuration);
        return services.BuildServiceProvider();
    }

    private static PoliteHttpClient Polite(ServiceProvider sp, long maxBytes)
    {
        // Typed client adı arayüz adıdır; böylece üretimdeki birincil işleyici (CreatePrimaryHandler) kullanılır.
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ICrtShClient));
        var options = new CrtShOptions
        {
            MinRequestInterval = TimeSpan.Zero,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryAfter = TimeSpan.Zero,
            MaxRetryAttempts = 0,
            AttemptTimeout = TimeSpan.FromSeconds(20),
            MaxResponseBytes = maxBytes,
        };
        return new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Compression_bomb_through_automatic_decompression_is_cut(string encoding)
    {
        await using var server = new LoopbackHttpServer(
            [("Content-Type", "application/json"), ("Content-Encoding", encoding)],
            Payloads.ZeroBomb(encoding, 32 * 1024 * 1024));
        await using var sp = BuildServices();

        var result = await Polite(sp, 1024 * 1024).GetAsync(server.BaseUri, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.ResponseTooLarge, result.ErrorKind);
        Assert.Null(result.Body);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task Corrupt_compressed_response_becomes_an_error_kind_not_an_exception(string encoding)
    {
        await using var server = new LoopbackHttpServer(
            [("Content-Type", "application/json"), ("Content-Encoding", encoding)],
            Payloads.Garbage(4096));
        await using var sp = BuildServices();

        var exception = await Record.ExceptionAsync(
            () => Polite(sp, 1024 * 1024).GetAsync(server.BaseUri, TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    [Fact]
    public void Http_client_logging_handlers_are_removed_so_query_urls_are_not_logged()
    {
        // HttpClientFactory'nin varsayılan log işleyicileri istek adresini (yani alan adını) yazar.
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IHttpMessageHandlerFactory>();

        foreach (var name in new[] { nameof(ICrtShClient), "IWaybackCdxClient" })
        {
            var chain = new List<string>();
            HttpMessageHandler? current = factory.CreateHandler(name);
            while (current is not null)
            {
                chain.Add(current.GetType().Name);
                current = (current as DelegatingHandler)?.InnerHandler;
            }

            Assert.DoesNotContain(chain, n => n.Contains("Logging", StringComparison.Ordinal));
            Assert.Contains(nameof(SocketsHttpHandler), chain);
        }
    }
}
