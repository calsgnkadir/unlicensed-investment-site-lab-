using System.Net;
using Fraud.Collectors;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Collectors.InternetDb;
using Fraud.Collectors.Network;
using Fraud.Collectors.RipeStat;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Fetching;
using Fraud.Security.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Security.Tests.Ssrf;

/// <summary>
/// Ağ toplayıcıları (DNS, RIPEstat, InternetDB) yalnızca koda gömülü adreslere bağlanabilir mi? Yapılandırma, DNS
/// cevabı veya JSON içeriği başka bir yere (iç ağ, metadata, rastgele sunucu) bağlantı kurduramamalı.
/// Gerçek bağlantı denenen testlerde sahte çözümleyici yalnızca özel adres döndürür; paket makineden çıkmaz.
/// </summary>
public sealed class NetworkAllowlistAttackTests
{
    private static ServiceProvider BuildServices(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddCollectors(configuration);
        return services.BuildServiceProvider();
    }

    public static TheoryData<string> NetworkClientNames => new()
    {
        CollectorServiceCollectionExtensions.DohHttpClientName,
        nameof(IRipeStatClient),
        nameof(IInternetDbClient),
    };

    [Theory]
    [MemberData(nameof(NetworkClientNames))]
    public void Network_clients_use_the_allow_list_handler_without_proxy_redirects_or_url_logging(string name)
    {
        using var sp = BuildServices();
        var factory = sp.GetRequiredService<IHttpMessageHandlerFactory>();

        var chain = new List<HttpMessageHandler>();
        HttpMessageHandler? current = factory.CreateHandler(name);
        while (current is not null)
        {
            chain.Add(current);
            current = (current as DelegatingHandler)?.InnerHandler;
        }

        // Log işleyicisi istek adresini yazar: RIPEstat ve InternetDB adresinde IP var.
        Assert.DoesNotContain(chain, h => h.GetType().Name.Contains("Logging", StringComparison.Ordinal));
        var primary = Assert.Single(chain.OfType<SocketsHttpHandler>());
        Assert.False(primary.UseProxy);            // HTTPS_PROXY gibi ortam değişkenleri yok sayılır
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
        Assert.NotNull(primary.ConnectCallback);
    }

    private static HttpClient AllowListClient(IReadOnlySet<string> hosts, FakeResolver resolver)
    {
        var connector = new AllowedHostConnector(hosts, new SafeConnector(resolver));
        return new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = connector.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        });
    }

    [Theory]
    [InlineData("https://stat.ripe.net:8443/data/network-info/data.json")] // izinli ad, başka port
    [InlineData("https://stat.ripe.net:80/")]
    [InlineData("https://stat.ripe.net.evil.example/")]                    // son ek hilesi
    [InlineData("https://evil.example/stat.ripe.net")]
    [InlineData("https://stat.ripe.net@evil.example/")]                    // kullanıcı bilgisi hilesi
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("https://2130706433/")]                                    // ondalık IP yazımı
    [InlineData("https://0x7f.0.0.1/")]
    [InlineData("https://internetdb.shodan.io/")]                          // başka kaynağın izinli adı
    public async Task Allow_list_connector_refuses_other_hosts_and_ports_before_resolving(string url)
    {
        var resolver = FakeResolver.Always("10.0.0.1");
        using var http = AllowListClient(new HashSet<string> { RipeStatOptions.Host }, resolver);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri(url), TestContext.Current.CancellationToken));

        Assert.IsType<BlockedAddressException>(ex.InnerException);
        Assert.Equal(0, resolver.Calls);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]   // NAT64 içine gömülü metadata
    [InlineData("2002:a9fe:a9fe::1")]    // 6to4 içine gömülü metadata
    [InlineData("fd00:ec2::254")]        // AWS IPv6 metadata
    [InlineData("fe80::1")]
    public async Task Allowed_host_resolving_to_internal_address_is_refused_at_connect_time(string address)
    {
        // Yerel çözücü (veya DNS rebinding) izinli adı iç adrese çevirse bile soket açılmaz.
        var resolver = FakeResolver.Always(address);
        using var http = AllowListClient(new HashSet<string> { RipeStatOptions.Host }, resolver);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            http.GetAsync(RipeStatClient.BuildQueryUri(IPAddress.Parse("8.8.8.8"), "test"), TestContext.Current.CancellationToken));

        Assert.IsType<BlockedAddressException>(ex.InnerException);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task Mixed_public_and_internal_answer_is_refused_entirely()
    {
        var resolver = new FakeResolver(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("127.0.0.1") });
        using var http = AllowListClient(new HashSet<string> { InternetDbOptions.Host }, resolver);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            http.GetAsync(InternetDbClient.BuildQueryUri(IPAddress.Parse("8.8.8.8")), TestContext.Current.CancellationToken));

        Assert.IsType<BlockedAddressException>(ex.InnerException);
    }

    [Fact]
    public void Configuration_cannot_move_source_hosts_or_add_resolvers()
    {
        // Var olmayan ayar anahtarları sessizce yok sayılır; adresler sabittir.
        using var sp = BuildServices(new Dictionary<string, string?>
        {
            ["RipeStat:Host"] = "127.0.0.1",
            ["RipeStat:BaseUri"] = "http://169.254.169.254/",
            ["InternetDb:Host"] = "10.0.0.1",
            ["InternetDb:BaseUri"] = "http://10.0.0.1/",
            ["Dns:DohUri"] = "http://127.0.0.1/",
        });
        var ip = IPAddress.Parse("8.8.8.8");

        Assert.Equal("stat.ripe.net", RipeStatClient.BuildQueryUri(ip, "x").IdnHost);
        Assert.Equal("internetdb.shodan.io", InternetDbClient.BuildQueryUri(ip).IdnHost);
        Assert.All(DnsResolverCatalog.All, r =>
        {
            Assert.True(IpAddressPolicy.IsPublic(r.PlainEndpoint.Address));
            Assert.Equal(53, r.PlainEndpoint.Port);
            Assert.Equal(Uri.UriSchemeHttps, r.DohUri.Scheme);
            Assert.True(r.DohUri.IsDefaultPort);
        });
        Assert.Equal(["cloudflare-dns.com", "dns.google"], DnsResolverCatalog.DohHosts.Order(StringComparer.Ordinal));

        foreach (var bad in new[] { "127.0.0.1", "1.1.1.1", "cloudflare-dns.com", "quad9", "" })
        {
            var options = new DnsOptions { Location = "test-lab" };
            options.Resolvers.Add(bad);
            Assert.Throws<InvalidOperationException>(options.Validate);
        }
    }

    [Theory]
    [InlineData("RipeStat:MinRequestInterval")]
    [InlineData("InternetDb:MinRequestInterval")]
    public void Ip_sources_refuse_pacing_faster_than_one_per_second_at_startup(string key)
    {
        Assert.Throws<InvalidOperationException>(() => BuildServices(new Dictionary<string, string?> { [key] = "00:00:00.100" }));
    }

    [Fact]
    public void Dns_pacer_keeps_the_one_packet_per_second_floor_even_without_validate()
    {
        // Bulgu N6 (bilgi): Taban (1 sn) yalnızca DnsOptions.Validate içinde. Program bunu yalnızca bazı komutlarda
        // çağırıyor; DI'daki ortak hız sınırlayıcı ayardaki değeri denetlemeden kullanıyor.
        using var sp = BuildServices(new Dictionary<string, string?> { ["Dns:MinQueryInterval"] = "00:00:00" });

        var pacer = sp.GetRequiredKeyedService<RequestPacer>(CollectionSource.Dns);

        Assert.True(pacer.MinimumInterval >= TimeSpan.FromSeconds(1), $"DNS hız sınırı {pacer.MinimumInterval}");
    }

    public static TheoryData<string> NonPublicAddresses => new()
    {
        "0.0.0.0", "127.0.0.1", "10.0.0.1", "100.64.0.1", "169.254.169.254", "172.16.0.1", "192.168.1.1",
        "192.0.0.170", "192.0.2.1", "198.18.0.1", "224.0.0.1", "255.255.255.255",
        "::", "::1", "::ffff:127.0.0.1", "::ffff:169.254.169.254", "64:ff9b::7f00:1", "2001::1", "2001:db8::1",
        "2002:7f00:1::", "fc00::1", "fd00:ec2::254", "fe80::1", "ff02::1",
    };

    [Theory]
    [MemberData(nameof(NonPublicAddresses))]
    public void Non_public_ips_from_dns_answers_are_never_sent_to_ripestat_or_internetdb(string address)
    {
        var ip = IPAddress.Parse(address);

        Assert.False(IpTargetFilter.PublicOnly.IsAllowed(ip));
        Assert.Throws<ArgumentException>(() => RipeStatClient.BuildQueryUri(ip, "fraudlab-research"));
        Assert.Throws<ArgumentException>(() => InternetDbClient.BuildQueryUri(ip));
    }

    [Theory]
    [InlineData("2130706433")]       // ondalık 127.0.0.1
    [InlineData("0x7f000001")]
    [InlineData("0177.0.0.1")]       // sekizlik
    [InlineData("127.1")]
    [InlineData("0x7f.1")]
    [InlineData("::ffff:7f00:1")]
    [InlineData("fe80::1%1")]        // bölge kimliği
    [InlineData("[::1]")]
    public void Odd_ip_spellings_stored_in_dns_answers_still_hit_the_filter(string text)
    {
        // answers sütunu jsonb; veritabanı da güvenilmez sayılır. NetworkTargetQuery IPAddress.TryParse kullanır.
        if (!IPAddress.TryParse(text, out var ip))
        {
            return; // ayrıştırılamayan yazım zaten atlanır
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        Assert.False(IpTargetFilter.PublicOnly.IsAllowed(ip));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void Query_uris_stay_on_the_source_host_and_carry_only_the_ip(string address)
    {
        var ip = IPAddress.Parse(address);

        var ripe = RipeStatClient.BuildQueryUri(ip, "fraudlab-research");
        var shodan = InternetDbClient.BuildQueryUri(ip);

        Assert.Equal(("https", "stat.ripe.net", 443), (ripe.Scheme, ripe.IdnHost, ripe.Port));
        Assert.Equal(("https", "internetdb.shodan.io", 443), (shodan.Scheme, shodan.IdnHost, shodan.Port));
        Assert.Equal(ip, IPAddress.Parse(Uri.UnescapeDataString(shodan.AbsolutePath.TrimStart('/'))));
        Assert.Equal("", ripe.UserInfo);
        Assert.Equal("", shodan.Query);
    }
}
