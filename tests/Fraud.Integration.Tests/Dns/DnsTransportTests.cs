using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Fetching;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Dns;

/// <summary>
/// Gerçek UDP/TCP taşımaları yalnızca 127.0.0.1 üzerindeki test sunucusuna karşı; DoH sahte HTTP işleyicisiyle.
/// İnternete hiçbir paket gitmez.
/// </summary>
public sealed class DnsTransportTests
{
    private static readonly Uri UnusedDoh = new("https://doh.invalid/dns-query");

    [Fact]
    public async Task Udp_ignores_spoofed_and_wrong_id_datagrams_and_takes_the_matching_one()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var spoofer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        spoofer.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var resolver = new DnsResolver("test", (IPEndPoint)server.LocalEndPoint!, UnusedDoh);
        var query = DnsQueryBuilder.Build(0x4242, "ornek.example", DnsRecordType.A);

        var serverTask = Task.Run(async () =>
        {
            var buffer = new byte[512];
            var received = await server.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct);
            var client = received.RemoteEndPoint;

            // 1) Başka bir adresten, doğru kimlikle sahte cevap (yok sayılmalı).
            await spoofer.SendToAsync(Responses.A(query, addresses: [[203, 0, 113, 66]]), SocketFlags.None, client, ct);
            // 2) Doğru adresten ama yanlış kimlikle (yok sayılmalı).
            await server.SendToAsync(Responses.A(query, id: 0x1111, addresses: [[203, 0, 113, 67]]), SocketFlags.None, client, ct);
            // 3) Doğru adres, doğru kimlik.
            await server.SendToAsync(Responses.A(query, addresses: [[192, 0, 2, 1]]), SocketFlags.None, client, ct);
        }, ct);

        var result = await new UdpDnsTransport(TimeSpan.FromSeconds(5)).ExchangeAsync(resolver, query, ct);
        await serverTask;

        Assert.Equal(DnsErrorKind.None, result.Error);
        var answer = Assert.Single(DnsMessageParser.Parse(result.Response!).Answers);
        Assert.Equal("192.0.2.1", answer.Value);
    }

    [Fact]
    public async Task Udp_without_answer_times_out()
    {
        using var silent = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        silent.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var resolver = new DnsResolver("test", (IPEndPoint)silent.LocalEndPoint!, UnusedDoh);

        var result = await new UdpDnsTransport(TimeSpan.FromMilliseconds(300))
            .ExchangeAsync(resolver, DnsQueryBuilder.Build(1, "ornek.example", DnsRecordType.A), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.Timeout, result.Error);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Tcp_uses_two_byte_length_prefix_in_both_directions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var resolver = new DnsResolver("test", (IPEndPoint)listener.LocalEndpoint, UnusedDoh);
        var query = DnsQueryBuilder.Build(0x0707, "ornek.example", DnsRecordType.A);

        var serverTask = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(ct);
            var stream = connection.GetStream();
            var length = new byte[2];
            await stream.ReadExactlyAsync(length, ct);
            var received = new byte[BinaryPrimitives.ReadUInt16BigEndian(length)];
            await stream.ReadExactlyAsync(received, ct);
            Assert.Equal(query.Packet, received);

            var answer = Responses.A(query, addresses: [[198, 51, 100, 9]]);
            var framed = new byte[2 + answer.Length];
            BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)answer.Length);
            answer.CopyTo(framed, 2);
            await stream.WriteAsync(framed, ct);
        }, ct);

        var result = await new TcpDnsTransport(TimeSpan.FromSeconds(5)).ExchangeAsync(resolver, query, ct);
        await serverTask;

        Assert.Equal(DnsErrorKind.None, result.Error);
        Assert.Equal("198.51.100.9", Assert.Single(DnsMessageParser.Parse(result.Response!).Answers).Value);
    }

    [Fact]
    public async Task Tcp_connection_closed_before_the_promised_length_is_a_network_error()
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var resolver = new DnsResolver("test", (IPEndPoint)listener.LocalEndpoint, UnusedDoh);

        var serverTask = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync(ct);
            var stream = connection.GetStream();
            await stream.ReadExactlyAsync(new byte[2], ct);
            await stream.WriteAsync(new byte[] { 0x01, 0x00, 0xAA }, ct); // 256 bayt söz, 1 bayt gönder, kapat
        }, ct);

        var result = await new TcpDnsTransport(TimeSpan.FromSeconds(5))
            .ExchangeAsync(resolver, DnsQueryBuilder.Build(1, "ornek.example", DnsRecordType.A), ct);
        await serverTask;

        Assert.Equal(DnsErrorKind.NetworkError, result.Error);
    }

    [Fact]
    public async Task Doh_posts_dns_message_over_http2_and_reads_the_answer()
    {
        var query = DnsQueryBuilder.Build(0, "ornek.example", DnsRecordType.A);
        var handler = new FakeHttpHandler().Enqueue(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("application/dns-message", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal(query.Packet, await request.Content.ReadAsByteArrayAsync(ct));
            var content = new ByteArrayContent(Responses.A(query, addresses: [[192, 0, 2, 5]]));
            content.Headers.ContentType = new("application/dns-message");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(5))
            .ExchangeAsync(DnsResolverCatalog.Google, query, TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.None, result.Error);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal(new Uri("https://dns.google/dns-query"), Assert.Single(handler.Requests));
        Assert.Equal(HttpVersion.Version20, handler.Versions[0].Version);
    }

    [Fact]
    public async Task Doh_non_200_or_wrong_content_type_is_an_http_error()
    {
        var query = DnsQueryBuilder.Build(0, "ornek.example", DnsRecordType.A);
        var handler = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.ServiceUnavailable)
            .EnqueueText("<html>engel</html>", "text/html");
        using var http = new HttpClient(handler);
        var doh = new DohDnsTransport(http, TimeSpan.FromSeconds(5));
        var ct = TestContext.Current.CancellationToken;

        var first = await doh.ExchangeAsync(DnsResolverCatalog.Cloudflare, query, ct);
        var second = await doh.ExchangeAsync(DnsResolverCatalog.Cloudflare, query, ct);

        Assert.Equal((DnsErrorKind.HttpError, 503), (first.Error, first.HttpStatus));
        Assert.Equal((DnsErrorKind.HttpError, 200), (second.Error, second.HttpStatus));
    }

    [Fact]
    public async Task Doh_network_failure_is_a_network_error()
    {
        using var http = new HttpClient(new FakeHttpHandler().EnqueueNetworkError());

        var result = await new DohDnsTransport(http, TimeSpan.FromSeconds(5)).ExchangeAsync(
            DnsResolverCatalog.Cloudflare, DnsQueryBuilder.Build(0, "ornek.example", DnsRecordType.A), TestContext.Current.CancellationToken);

        Assert.Equal(DnsErrorKind.NetworkError, result.Error);
    }

    [Fact]
    public void Resolver_catalog_only_accepts_hard_coded_public_resolvers()
    {
        var selected = DnsResolverCatalog.Select(["cloudflare", "GOOGLE", "cloudflare"]);

        Assert.Equal(["cloudflare", "google"], selected.Select(r => r.Name));
        Assert.All(DnsResolverCatalog.All, r => Assert.True(IpAddressPolicy.IsPublic(r.PlainEndpoint.Address)));
        Assert.Equal(["cloudflare-dns.com", "dns.google"], DnsResolverCatalog.DohHosts.Order());
        Assert.Throws<InvalidOperationException>(() => DnsResolverCatalog.Select(["127.0.0.1"]));
        Assert.Throws<InvalidOperationException>(() => DnsResolverCatalog.Select(["quad9"]));
    }

    [Fact]
    public void Dns_options_require_a_location_and_keep_the_one_query_per_second_floor()
    {
        Assert.Throws<InvalidOperationException>(() => new DnsOptions().Validate());
        Assert.Throws<InvalidOperationException>(() => new DnsOptions { Location = "Kadir Ev" }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new DnsOptions { Location = "tr-ev-ubuntu", MinQueryInterval = TimeSpan.FromMilliseconds(500) }.Validate());

        var soa = new DnsOptions { Location = "tr-ev-ubuntu" };
        soa.QueryTypes.Add(DnsRecordType.SOA);
        Assert.Throws<InvalidOperationException>(soa.Validate);

        var ok = new DnsOptions { Location = "tr-ev-ubuntu" };
        ok.Validate();
        Assert.Equal([DnsTransport.Udp, DnsTransport.Tcp, DnsTransport.Doh], ok.EffectiveTransports);
        Assert.Contains(IPAddress.Parse("195.175.254.2"), ok.EffectiveBlockPageIps);
    }

    [Fact]
    public async Task Allowed_host_handler_refuses_other_hosts_before_any_dns_lookup()
    {
        using var handler = AllowedHostConnector.CreateHandler(new HashSet<string> { "stat.ripe.net" });
        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            http.GetAsync(new Uri("https://baska.example/"), TestContext.Current.CancellationToken));

        Assert.IsType<BlockedAddressException>(ex.InnerException);
    }
}
