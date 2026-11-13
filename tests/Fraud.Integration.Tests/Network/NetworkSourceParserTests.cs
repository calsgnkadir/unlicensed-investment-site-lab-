using System.Net;
using System.Text;
using Fraud.Collectors.Http;
using Fraud.Collectors.InternetDb;
using Fraud.Collectors.RipeStat;
using Fraud.Domain.Collection;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Network;

/// <summary>
/// RIPEstat ve InternetDB cevaplarını okuma. Örnek JSON'lardaki IP'ler belgeleme aralıklarındandır
/// (192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24). İstemci testleri sahte HTTP işleyicisiyle; ağ yok.
/// </summary>
public sealed class NetworkSourceParserTests
{
    private static readonly IPAddress Doc = IPAddress.Parse("192.0.2.10");

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Ripestat_network_info_is_read_strictly()
    {
        var info = RipeStatResponseParser.Parse(Json("""
            {"messages":[],"data_call_name":"network-info","data":{"asns":["64500", 64501],"prefix":"192.0.2.0/24"},
             "status":"ok","status_code":200,"time":"2026-11-13T10:00:00"}
            """), Doc);

        Assert.NotNull(info);
        Assert.Equal([64500L, 64501L], info.Asns);
        Assert.Equal("192.0.2.0/24", info.Prefix);
        Assert.False(info.IsEmpty);
    }

    [Fact]
    public void Ripestat_unannounced_ip_is_empty_not_error()
    {
        var info = RipeStatResponseParser.Parse(Json("""{"status":"ok","data":{"asns":[],"prefix":null}}"""), Doc);

        Assert.NotNull(info);
        Assert.True(info.IsEmpty);
    }

    [Theory]
    [InlineData("""{"status":"error","data":{"asns":["64500"],"prefix":"192.0.2.0/24"}}""")]
    [InlineData("""{"status":"ok"}""")]
    [InlineData("""{"status":"ok","data":{"asns":["AS64500"],"prefix":"192.0.2.0/24"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["0"],"prefix":"192.0.2.0/24"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["4294967296"],"prefix":"192.0.2.0/24"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"198.51.100.0/24"}}""")] // IP'yi içermiyor
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"192.0.2.1/24"}}""")]   // ağ bitleri dışında 1
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"2001:db8::/32"}}""")]  // aile farklı
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"<script>"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":"64500"}}""")]
    [InlineData("not json")]
    [InlineData("""[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]""")]
    public void Ripestat_invalid_answers_are_rejected_as_a_whole(string body)
    {
        Assert.Null(RipeStatResponseParser.Parse(Json(body), Doc));
    }

    [Fact]
    public void Ripestat_uri_carries_ip_and_sourceapp_and_refuses_non_public_ips()
    {
        var uri = RipeStatClient.BuildQueryUri(IPAddress.Parse("1.1.1.1"), "fraudlab-research");

        Assert.Equal("https://stat.ripe.net/data/network-info/data.json?resource=1.1.1.1&sourceapp=fraudlab-research", uri.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => RipeStatClient.BuildQueryUri(Doc, "x"));
        Assert.Throws<ArgumentException>(() => RipeStatClient.BuildQueryUri(IPAddress.Loopback, "x"));
        Assert.Throws<ArgumentException>(() => InternetDbClient.BuildQueryUri(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void Cdn_and_cloud_asns_are_named()
    {
        var map = new RipeStatOptions().CdnAsns;

        Assert.Equal("cloudflare", RipeStatCollector.CdnOrCloud([13335], map));
        Assert.Equal("aws", RipeStatCollector.CdnOrCloud([16509], map));
        Assert.Equal("akamai", RipeStatCollector.CdnOrCloud([64500, 20940], map));
        Assert.Null(RipeStatCollector.CdnOrCloud([64500], map));
    }

    [Fact]
    public void Ripestat_options_reject_unsafe_sourceapp_and_fast_pacing()
    {
        Assert.Throws<InvalidOperationException>(() => new RipeStatOptions { SourceApp = "ad soyad@posta" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new RipeStatOptions { MinRequestInterval = TimeSpan.FromMilliseconds(100) }.Validate());
        new RipeStatOptions().Validate();
    }

    [Fact]
    public void Internetdb_keeps_ports_tags_and_cpes_and_never_reads_vulns_or_hostnames()
    {
        var host = InternetDbResponseParser.ParseHost(Json("""
            {"cpes":["cpe:/a:nginx:nginx","cpe:/a:<bad>"],
             "hostnames":["mail.baska-bir-site.example"],
             "ip":"192.0.2.10",
             "ports":[443,80,443],
             "tags":["cdn","<script>"],
             "vulns":["CVE-2099-0001"]}
            """), Doc);

        Assert.NotNull(host);
        Assert.Equal([80, 443], host.Ports);
        Assert.Equal(["cdn"], host.Tags);
        Assert.Equal(["cpe:/a:nginx:nginx"], host.Cpes);
        Assert.Equal(2, host.SkippedEntries);
        // Kayıtta hostnames ve vulns alanı hiç yok.
        Assert.DoesNotContain(typeof(InternetDbHost).GetProperties(), p => p.Name is "Hostnames" or "Vulns");
    }

    [Theory]
    [InlineData("""{"ip":"198.51.100.1","ports":[80]}""")]   // başka IP
    [InlineData("""{"ip":"192.0.2.10","ports":[70000]}""")]  // port aralık dışı
    [InlineData("""{"ip":"192.0.2.10","ports":"80"}""")]
    [InlineData("""{"ip":"192.0.2.10"}""")]
    [InlineData("""{"ip":"192.0.2.10","ports":[80],"tags":"cdn"}""")]
    [InlineData("<html></html>")]
    public void Internetdb_invalid_answers_are_rejected(string body)
    {
        Assert.Null(InternetDbResponseParser.ParseHost(Json(body), Doc));
    }

    [Fact]
    public void Internetdb_no_information_body_is_recognised_exactly()
    {
        Assert.True(InternetDbResponseParser.IsNoInformation(Json("""{"detail":"No information available"}""")));
        Assert.False(InternetDbResponseParser.IsNoInformation(Json("""{"detail":"Not Found"}""")));
        Assert.False(InternetDbResponseParser.IsNoInformation(Json("<html>404</html>")));
    }

    private static PoliteHttpClient Polite(FakeHttpHandler handler, SourceHttpOptions options) =>
        new(new HttpClient(handler), new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System);

    private static InternetDbOptions FastInternetDb() => new()
    {
        MinRequestInterval = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryAfter = TimeSpan.Zero,
        MaxRetryAttempts = 1,
    };

    [Fact]
    public async Task Internetdb_client_maps_404_no_information_to_empty_and_other_404_to_error()
    {
        var ip = IPAddress.Parse("1.1.1.1");
        var handler = new FakeHttpHandler()
            .EnqueueText("""{"detail":"No information available"}""", status: HttpStatusCode.NotFound)
            .EnqueueText("<html>yok</html>", "text/html", HttpStatusCode.NotFound)
            .EnqueueText("""{"ip":"1.1.1.1","ports":[53,443],"tags":[],"cpes":[],"hostnames":[],"vulns":[]}""")
            .EnqueueText("""{"ip":"1.0.0.1","ports":[53],"tags":[],"cpes":[]}""");
        var client = new InternetDbClient(Polite(handler, FastInternetDb()));
        var ct = TestContext.Current.CancellationToken;

        var empty = await client.LookupAsync(ip, ct);
        var otherNotFound = await client.LookupAsync(ip, ct);
        var ok = await client.LookupAsync(ip, ct);
        var wrongIp = await client.LookupAsync(ip, ct);

        Assert.Equal((CollectionOutcome.Empty, 404, 1), (empty.Outcome, empty.HttpStatus, empty.Attempts));
        Assert.Equal((CollectionOutcome.Error, CollectionErrorKind.ClientError), (otherNotFound.Outcome, otherNotFound.ErrorKind));
        Assert.Equal(CollectionOutcome.Success, ok.Outcome);
        Assert.Equal([53, 443], ok.Host!.Ports);
        Assert.Equal((CollectionOutcome.Error, CollectionErrorKind.InvalidResponse), (wrongIp.Outcome, wrongIp.ErrorKind));
        Assert.All(handler.Requests, u => Assert.Equal("https://internetdb.shodan.io/1.1.1.1", u.AbsoluteUri));
    }

    [Fact]
    public async Task Ripestat_client_retries_server_errors_and_reports_invalid_json_as_error()
    {
        var ip = IPAddress.Parse("1.1.1.1");
        var handler = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.ServiceUnavailable)
            .EnqueueText("""{"status":"ok","data":{"asns":["13335"],"prefix":"1.1.1.0/24"}}""")
            .EnqueueText("""{"status":"ok","data":{"asns":["x"]}}""");
        var options = new RipeStatOptions
        {
            MinRequestInterval = TimeSpan.Zero, RetryBaseDelay = TimeSpan.Zero, MaxRetryAfter = TimeSpan.Zero, MaxRetryAttempts = 1,
        };
        var client = new RipeStatClient(Polite(handler, options), options);
        var ct = TestContext.Current.CancellationToken;

        var ok = await client.LookupAsync(ip, ct);
        var bad = await client.LookupAsync(ip, ct);

        Assert.Equal((CollectionOutcome.Success, 2), (ok.Outcome, ok.Attempts));
        Assert.Equal([13335L], ok.Info!.Asns);
        Assert.Equal((CollectionOutcome.Error, CollectionErrorKind.InvalidResponse), (bad.Outcome, bad.ErrorKind));
    }
}
