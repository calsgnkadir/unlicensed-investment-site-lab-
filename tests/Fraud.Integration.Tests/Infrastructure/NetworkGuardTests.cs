namespace Fraud.Integration.Tests.Infrastructure;

/// <summary>Ağ korumasının gerçekten kurulu olduğunu doğrular. Bu testler de internete çıkmaz.</summary>
public sealed class NetworkGuardTests
{
    [Theory]
    [InlineData("https://crt.sh/?q=%25.ornek.example&output=json")]
    [InlineData("https://web.archive.org/cdx/search/cdx?url=ornek.example")]
    public void External_hosts_are_routed_to_the_blackhole_proxy(string url)
    {
        var proxy = HttpClient.DefaultProxy.GetProxy(new Uri(url));

        Assert.Equal(NetworkGuard.BlackholeProxy, proxy);
    }

    [Fact]
    public async Task A_default_HttpClient_cannot_reach_crt_sh()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // İstek 127.0.0.1:9'a (vekil) gider ve reddedilir; crt.sh'e ulaşmaz.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("https://crt.sh/?q=%25.ornek.example&output=json"), TestContext.Current.CancellationToken));
    }
}
