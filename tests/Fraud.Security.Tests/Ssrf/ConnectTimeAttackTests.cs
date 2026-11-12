using System.Net;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Fetching;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Ssrf;

/// <summary>
/// Bağlantı anındaki IP denetimi (SafeConnector, IpAddressPolicy) ve DNS rebinding.
/// </summary>
/// <remarks>
/// Gerçek <see cref="SocketsHttpHandler"/> kullanılan testlerde sahte çözümleyici yalnızca özel adres döndürür;
/// bağlantı soket açılmadan reddedilir. Herkese açık adres yalnızca soket açmayan birim testlerinde geçer.
/// </remarks>
public sealed class ConnectTimeAttackTests
{
    [Theory]
    [InlineData("0.1.2.3")]
    [InlineData("100.127.255.254")]
    [InlineData("192.0.0.8")]
    [InlineData("192.88.99.1")]
    [InlineData("198.19.255.255")]
    [InlineData("::ffff:0:7f00:1")]          // SIIT (IPv4 çevrilmiş) biçim
    [InlineData("::ffff:100.64.0.1")]
    [InlineData("::a9fe:a9fe")]              // IPv4 uyumlu metadata
    [InlineData("64:ff9b::a9fe:a9fe")]       // NAT64 metadata
    [InlineData("64:ff9b:1::a00:1")]         // yerel NAT64
    [InlineData("2002:a9fe:a9fe::1")]        // 6to4 metadata
    [InlineData("2001:0:4136:e378:8000:63bf:56ff:fefe")] // Teredo
    [InlineData("2001:2::1")]                // kıyaslama bloğu
    [InlineData("2001:20::1")]               // ORCHIDv2
    [InlineData("3fff::1")]                  // belgeleme
    [InlineData("fec0::1")]                  // eski site-local
    [InlineData("ff05::1")]                  // multicast
    [InlineData("100::1")]                   // discard-only
    public void Embedded_or_special_purpose_addresses_are_not_public(string address)
    {
        Assert.False(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Fact]
    public void Link_local_ipv6_with_scope_id_is_not_public()
    {
        var address = IPAddress.Parse("fe80::1");
        address.ScopeId = 2;

        Assert.False(IpAddressPolicy.IsPublic(address));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("2a00:1450:4001::1")]
    [InlineData("::ffff:1.1.1.1")]
    public void Ordinary_public_addresses_remain_allowed(string address)
    {
        Assert.True(IpAddressPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task Rebinding_second_resolution_to_internal_address_is_blocked()
    {
        // 1. çözümleme herkese açık, 2. çözümleme metadata adresi: klasik DNS rebinding.
        var resolver = FakeResolver.Sequence("207.241.224.2", "169.254.169.254");
        var connector = new SafeConnector(resolver);
        var ct = TestContext.Current.CancellationToken;

        var first = await connector.ResolveAllowedAsync("web.archive.org", ct);
        await Assert.ThrowsAsync<BlockedAddressException>(() => connector.ResolveAllowedAsync("web.archive.org", ct));

        // Bağlanılacak adres, denetlenen adresin kendisidir; ad ikinci kez çözülmez.
        Assert.Equal([IPAddress.Parse("207.241.224.2")], first);
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public async Task Connector_resolves_exactly_once_per_connection_attempt()
    {
        var resolver = FakeResolver.Always("207.241.224.2");
        var connector = new SafeConnector(resolver);

        await connector.ResolveAllowedAsync("web.archive.org", TestContext.Current.CancellationToken);

        // Tek çözümleme: "denetimde başka, bağlantıda başka adres" (TOCTOU) için ikinci bir sorgu yok.
        Assert.Equal(1, resolver.Calls);
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:a9fe:a9fe::1")]
    [InlineData("fd00:ec2::254")]
    [InlineData("100.100.100.200")]
    [InlineData("0.0.0.0")]
    public async Task Real_transport_refuses_internal_address_from_dns(string resolved)
    {
        var resolver = FakeResolver.Always(resolved);
        using var fetcher = new SafeFetcher(SafeFetcher.CreateTransport(resolver), new SafeFetcherOptions(), TimeProvider.System);

        var result = await fetcher.FetchAsync(new Uri("https://web.archive.org/"), TestContext.Current.CancellationToken);

        Assert.Equal(SafeFetchStatus.Blocked, result.Status);
        Assert.Equal(SafeFetchBlockReason.AddressNotAllowed, result.BlockReason);

        // Bağlantı vekil üzerinden değil, denetimli bağlantı kurucu üzerinden kuruldu (vekil, denetimi atlatırdı).
        Assert.Equal(["web.archive.org"], resolver.Hosts);
    }

    [Fact]
    public async Task Rebinding_across_connections_on_real_transport_is_blocked_each_time()
    {
        // Her yeni bağlantıda ad yeniden çözülür ve yeniden denetlenir.
        var resolver = FakeResolver.Sequence("127.0.0.1", "10.0.0.1", "169.254.169.254");
        using var fetcher = new SafeFetcher(SafeFetcher.CreateTransport(resolver), new SafeFetcherOptions(), TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            var result = await fetcher.FetchAsync(new Uri("https://web.archive.org/web/" + i), ct);
            Assert.Equal(SafeFetchBlockReason.AddressNotAllowed, result.BlockReason);
        }

        Assert.Equal(3, resolver.Calls);
    }
}
