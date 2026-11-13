using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Collectors.Monitoring;
using Fraud.Collectors.PortAudit;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Observability;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Fraud.Integration.Tests.Monitoring;

/// <summary>
/// Kural 4 ve 5: Gerçek dışa aktarıcıyı (OpenTelemetry + Prometheus HttpListener) loopback'te açar, bütün metrik
/// sınıflarını kötü niyetli değerlerle besler, <c>/metrics</c> çıktısını okur ve her etiketin izin listesinde olduğunu,
/// çıktıda alan adına veya IP'ye benzeyen hiçbir metin bulunmadığını doğrular. Dış ağa çıkılmaz.
/// </summary>
public sealed partial class MetricsEndpointTests
{
    [Fact]
    public void Tag_helper_rejects_unknown_keys_and_replaces_free_text_values()
    {
        Assert.Throws<ArgumentException>(() => MetricLabels.Tag("domain", "ornek.example"));
        Assert.Throws<ArgumentException>(() => MetricLabels.Tag("ip", "192.0.2.1"));

        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("log", "ct.ornek.example").Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("resolver", "8.8.8.8").Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("port", "80; DROP").Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("transport", (DnsTransport)99).Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("http.route", "/a/ornek.example").Value);
        Assert.Equal("sycamore2027h1", MetricLabels.Tag("log", "sycamore2027h1").Value);
        Assert.Equal("Doh", MetricLabels.Tag("transport", DnsTransport.Doh).Value);
    }

    [Theory]
    [InlineData("*", 9464, true)]
    [InlineData("+", 9464, true)]
    [InlineData("localhost", 9464, true)]
    [InlineData("10.213.199.1", 9465, true)]
    [InlineData("metrics.ornek.example", 9464, false)] // ad çözümüne bağlı dinleme adresi yok
    [InlineData("::1", 9464, false)]
    [InlineData("*", 80, false)]                       // ayrıcalıklı port yok
    public void Metrics_listen_address_is_wildcard_localhost_or_ipv4(string host, int port, bool valid)
    {
        var options = new MetricsOptions { Enabled = true, Host = host, Port = port };

        if (valid)
        {
            options.Validate();
        }
        else
        {
            Assert.Throws<InvalidOperationException>(options.Validate);
        }
    }

    [Fact]
    public async Task Scraped_metrics_contain_only_allow_listed_labels_and_no_hostnames_or_ips()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = FreeLoopbackPort();
        var options = new MetricsOptions { Enabled = true, Host = "localhost", Port = port };
        string[] meters = [StaticCtMetrics.MeterName, CollectionMetrics.MeterName, DnsMetrics.MeterName, PortAuditMetrics.MeterName,
            DataFreshnessMonitor.MeterName, "Fraud.Tests.Hostile"];

        using var provider = MetricsSetup.Configure(Sdk.CreateMeterProviderBuilder(), options, meters).Build();
        using var capture = new MetricCapture();
        using var ctMetrics = new StaticCtMetrics(capture.Factory);
        using var collection = new CollectionMetrics(capture.Factory);
        using var dns = new DnsMetrics(capture.Factory);
        using var portAudit = new PortAuditMetrics(capture.Factory);
        using var hostile = new System.Diagnostics.Metrics.Meter("Fraud.Tests.Hostile");

        // Normal yol: her sınıfın her ölçümü en az bir kez.
        ctMetrics.Position("sycamore2027h1", 1_000_000, 999_744);
        ctMetrics.Processed("sycamore2027h1", 256, 0, 3);
        ctMetrics.Failure("sycamore2027h1", CollectionErrorKind.Timeout);
        ctMetrics.BatchSucceeded("sycamore2027h1", advanced: true, DateTimeOffset.UtcNow.AddSeconds(-40));
        ctMetrics.TileFetched("sycamore2027h1", TimeSpan.FromMilliseconds(300));
        ctMetrics.Lag("sycamore2027h1", TimeSpan.FromSeconds(40));
        ctMetrics.HttpObserver("sycamore2027h1").Fetched(new HttpFetchResult(new byte[100], 200, 2, CollectionErrorKind.None), TimeSpan.Zero);
        collection.RunStarted(CollectionSource.Dns);
        collection.TargetFailed(CollectionSource.Dns, CollectionErrorKind.Timeout);
        collection.Retried(CollectionSource.RipeStat, 2);
        collection.RunFinished(new CollectionRunSummary(CollectionSource.Dns, 3, 0, 2, 0, 1, 10, false));
        dns.RunStarted();
        dns.Query("cloudflare", DnsTransport.Udp, Result(DnsOutcome.Success, DnsErrorKind.None), blockMarker: true);
        dns.Query("google", DnsTransport.Doh, Result(DnsOutcome.Error, DnsErrorKind.ServerFailure), blockMarker: false);
        dns.RunFinished(cancelled: false);
        dns.Comparison([new ResolverComparison("cloudflare", 10, 1, 10, 0)]);
        portAudit.Completed(
            ExpectedListeners.Compare([new Listener(ListenerProtocol.Tcp, IPAddress.Parse("192.0.2.10"), 8099)], []), 3, DateTimeOffset.UtcNow);

        // Kötü niyetli yol: alan adı ve IP etiket değeri olarak verilmeye çalışılıyor.
        ctMetrics.Processed("ornek-yatirim.example", 1, 0, 1);
        ctMetrics.Position("203.0.113.7", 5, 1);
        dns.Query("dns.ornek.example", DnsTransport.Doh, Result(DnsOutcome.Success, DnsErrorKind.None), blockMarker: false);
        dns.Comparison([new ResolverComparison("198.51.100.9", 1, 1, 0, 0)]);
        // Görünüm (view) katmanı: İzin listesi dışındaki anahtarlar dışa aktarılmadan düşer.
        hostile.CreateCounter<long>("fraud_test_hostile").Add(
            1, new("domain", "ornek-yatirim.example"), new("server.address", "203.0.113.7"), new("url.full", "https://ornek.example/x"));

        var body = await ScrapeAsync(port, ct);

        Assert.Contains("fraud_ct_lag_entries{log=\"sycamore2027h1\"} 256", body, StringComparison.Ordinal);
        Assert.Contains("fraud_dns_last_run_queries{", body, StringComparison.Ordinal);
        // Etiketsiz seriler "ad{} değer" biçiminde yazılır.
        Assert.Matches(@"(?m)^port_audit_unexpected_listeners(\{\})? 1$", body);
        Assert.Matches(@"(?m)^fraud_test_hostile_total(\{\})? 1$", body);
        Assert.Contains("dotnet_gc_collections_total", body, StringComparison.Ordinal); // çalışma zamanı metrikleri

        var labels = LabelPairs(body).ToList();
        Assert.NotEmpty(labels);
        Assert.All(labels, l =>
        {
            if (l.Key == "le")
            {
                Assert.Matches(@"^(\+Inf|[0-9]+(\.[0-9]+)?(e[+-]?[0-9]+)?)$", l.Value);
                return;
            }

            Assert.Contains(l.Key, MetricLabels.AllowedPrometheusKeys);
            Assert.True(MetricLabels.IsAllowedPrometheusLabel(l.Key, l.Value), $"İzinsiz etiket değeri: {l.Key}");
        });

        // HELP satırları koddaki sabit açıklamalardır (çalışma zamanı metriklerinde "e.g." gibi ifadeler var); veri değildir.
        var samples = string.Join('\n', body.Split('\n').Where(l => !l.StartsWith("# HELP", StringComparison.Ordinal)));
        Assert.DoesNotMatch(HostnameLike(), samples);
        Assert.DoesNotMatch(Ipv4Like(), samples);
        Assert.DoesNotContain("ornek", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otel_scope", body, StringComparison.Ordinal);
        Assert.DoesNotContain("target_info", body, StringComparison.Ordinal);
    }

    private static DnsQueryResult Result(DnsOutcome outcome, DnsErrorKind kind) =>
        new(outcome, kind, outcome == DnsOutcome.Success ? 0 : 2, [], null, false, 1, TimeSpan.FromMilliseconds(25));

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> ScrapeAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var uri = new Uri($"http://localhost:{port}/metrics");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await http.GetStringAsync(uri, ct);
            }
            catch (HttpRequestException) when (attempt < 20)
            {
                await Task.Delay(100, ct); // dinleyici arka planda açılıyor
            }
        }
    }

    /// <summary>Örnek satırlarındaki (yorum olmayan) bütün etiket çiftleri.</summary>
    private static IEnumerable<KeyValuePair<string, string>> LabelPairs(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith('#') || line.IndexOf('{', StringComparison.Ordinal) is var open && open < 0)
            {
                continue;
            }

            var close = line.LastIndexOf('}');
            foreach (Match m in LabelPair().Matches(line[(open + 1)..close]))
            {
                yield return new(m.Groups["k"].Value, m.Groups["v"].Value);
            }
        }
    }

    [GeneratedRegex(@"(?<k>[a-zA-Z_][a-zA-Z0-9_]*)=""(?<v>(?:[^""\\]|\\.)*)""")]
    private static partial Regex LabelPair();

    // Noktayla ayrılmış en az iki parça ve harfle biten son parça (ör. ornek.example, a.b.com.tr).
    [GeneratedRegex(@"(?i)\b[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z][a-z0-9-]{1,62}\b")]
    private static partial Regex HostnameLike();

    [GeneratedRegex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex Ipv4Like();
}
