using System.Net;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Domain.Network;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Dns;

/// <summary>Sorgu akışı: TC sonrası TCP, tekrar deneme, kimlik ve soru denetimi, RCODE, DoH kimliği 0. Sahte taşıma; ağ yok.</summary>
public sealed class DnsQueryClientTests
{
    private static readonly DnsResolver Resolver = DnsResolverCatalog.Cloudflare;

    /// <summary>Her çağrıda sıradaki cevabı üreten sahte taşıma. Gönderilen sorguları kaydeder.</summary>
    internal sealed class FakeTransport(DnsTransport kind) : IDnsTransport
    {
        private readonly Queue<Func<DnsQuery, DnsExchangeResult>> _responses = new();

        public DnsTransport Kind => kind;

        public List<DnsQuery> Queries { get; } = [];

        public FakeTransport Then(Func<DnsQuery, DnsExchangeResult> respond)
        {
            _responses.Enqueue(respond);
            return this;
        }

        public FakeTransport ThenPacket(Func<DnsQuery, byte[]> packet) => Then(q => DnsExchangeResult.Ok(packet(q)));

        public Task<DnsExchangeResult> ExchangeAsync(DnsResolver resolver, DnsQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Beklenmeyen sorgu (" + kind + ")");
            }

            return Task.FromResult(_responses.Dequeue()(query));
        }
    }

    private static DnsQueryClient Client(params IDnsTransport[] transports) =>
        new(transports, new RequestPacer(TimeSpan.Zero, TimeProvider.System), TimeProvider.System, maxRetries: 1);

    private static Task<DnsQueryResult> Query(DnsQueryClient client, DnsTransport transport, DnsRecordType type = DnsRecordType.A) =>
        client.QueryAsync(Resolver, transport, "ornek.example", type, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Truncated_udp_answer_is_retried_over_tcp_and_marked()
    {
        var udp = new FakeTransport(DnsTransport.Udp)
            .ThenPacket(q => Responses.Rcode(q, 0, extraFlags: 0x0200)); // TC=1, cevap yok
        var tcp = new FakeTransport(DnsTransport.Tcp)
            .ThenPacket(q => Responses.A(q, addresses: [[192, 0, 2, 1], [192, 0, 2, 2]]));

        var result = await Query(Client(udp, tcp), DnsTransport.Udp);

        Assert.Equal(DnsOutcome.Success, result.Outcome);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(["192.0.2.1", "192.0.2.2"], result.Answers.Select(a => a.Value));
        Assert.Single(tcp.Queries);
    }

    [Fact]
    public async Task Truncated_flag_on_tcp_answer_is_malformed()
    {
        var tcp = new FakeTransport(DnsTransport.Tcp).ThenPacket(q => Responses.Rcode(q, 0, extraFlags: 0x0200));

        var result = await Query(Client(tcp), DnsTransport.Tcp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.Malformed), (result.Outcome, result.ErrorKind));
    }

    [Fact]
    public async Task Udp_timeout_is_retried_once_with_a_new_id()
    {
        var udp = new FakeTransport(DnsTransport.Udp)
            .Then(_ => DnsExchangeResult.Failed(DnsErrorKind.Timeout))
            .ThenPacket(q => Responses.A(q, addresses: [[192, 0, 2, 7]]));

        var result = await Query(Client(udp), DnsTransport.Udp);

        Assert.Equal(DnsOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, udp.Queries.Count);
        // Kimlik her denemede yeniden üretilir; 1/65536 ihtimalle aynı çıkabilir, bu yüzden yalnızca paket kurulumu denetlenir.
        Assert.All(udp.Queries, q => Assert.Equal(q.Id, (ushort)((q.Packet[0] << 8) | q.Packet[1])));
    }

    [Fact]
    public async Task Two_timeouts_are_an_error_never_an_empty_answer()
    {
        var udp = new FakeTransport(DnsTransport.Udp)
            .Then(_ => DnsExchangeResult.Failed(DnsErrorKind.Timeout))
            .Then(_ => DnsExchangeResult.Failed(DnsErrorKind.Timeout));

        var result = await Query(Client(udp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.Timeout, 2), (result.Outcome, result.ErrorKind, result.Attempts));
        Assert.Empty(result.Answers);
        Assert.Null(result.Rcode);
    }

    [Fact]
    public async Task Tcp_answer_with_wrong_id_is_rejected_without_retry()
    {
        var tcp = new FakeTransport(DnsTransport.Tcp)
            .ThenPacket(q => Responses.A(q, id: (ushort)(q.Id ^ 0xFFFF), addresses: [[192, 0, 2, 1]]));

        var result = await Query(Client(tcp), DnsTransport.Tcp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.IdMismatch, 1), (result.Outcome, result.ErrorKind, result.Attempts));
    }

    [Fact]
    public async Task Answer_to_a_different_question_is_rejected()
    {
        var tcp = new FakeTransport(DnsTransport.Tcp).ThenPacket(q =>
            Responses.A(DnsQueryBuilder.Build(q.Id, "baska.example", DnsRecordType.A), addresses: [[192, 0, 2, 1]]));

        var result = await Query(Client(tcp), DnsTransport.Tcp);

        Assert.Equal(DnsErrorKind.QuestionMismatch, result.ErrorKind);
    }

    [Fact]
    public async Task Garbage_answer_is_malformed()
    {
        var tcp = new FakeTransport(DnsTransport.Tcp).Then(_ => DnsExchangeResult.Ok([1, 2, 3]));

        var result = await Query(Client(tcp), DnsTransport.Tcp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.Malformed), (result.Outcome, result.ErrorKind));
    }

    [Fact]
    public async Task Nxdomain_with_soa_is_nxdomain_with_negative_ttl()
    {
        var tcp = new FakeTransport(DnsTransport.Tcp).ThenPacket(q => Responses.NxDomainWithSoa(q, 900, 300));

        var result = await Query(Client(tcp), DnsTransport.Tcp);

        Assert.Equal((DnsOutcome.NxDomain, DnsErrorKind.None, 3), (result.Outcome, result.ErrorKind, result.Rcode));
        Assert.Equal(300L, result.NegativeTtl);
        Assert.Empty(result.Answers);
    }

    [Fact]
    public async Task Noerror_without_answers_is_empty_not_error()
    {
        var tcp = new FakeTransport(DnsTransport.Tcp).ThenPacket(q => Responses.Rcode(q, 0));

        var result = await Query(Client(tcp), DnsTransport.Tcp, DnsRecordType.AAAA);

        Assert.Equal((DnsOutcome.Empty, 0), (result.Outcome, result.Rcode));
    }

    [Theory]
    [InlineData(2, DnsErrorKind.ServerFailure)]
    [InlineData(5, DnsErrorKind.Refused)]
    [InlineData(4, DnsErrorKind.OtherRcode)]
    [InlineData(1, DnsErrorKind.OtherRcode)]
    public async Task Error_rcodes_are_errors_with_the_rcode_kept(int rcode, DnsErrorKind expected)
    {
        var tcp = new FakeTransport(DnsTransport.Tcp).ThenPacket(q => Responses.Rcode(q, rcode));

        var result = await Query(Client(tcp), DnsTransport.Tcp);

        Assert.Equal((DnsOutcome.Error, expected, rcode, 1), (result.Outcome, result.ErrorKind, result.Rcode, result.Attempts));
    }

    [Fact]
    public async Task Doh_query_uses_id_zero_and_accepts_answer_with_id_zero()
    {
        var doh = new FakeTransport(DnsTransport.Doh).ThenPacket(q =>
        {
            Assert.Equal(0, q.Id);
            Assert.Equal(0, q.Packet[0] | q.Packet[1]);
            return Responses.A(q, addresses: [[198, 51, 100, 4]]);
        });

        var result = await Query(Client(doh), DnsTransport.Doh);

        Assert.Equal(DnsOutcome.Success, result.Outcome);
        Assert.Equal("198.51.100.4", Assert.Single(result.Answers).Value);
    }

    [Fact]
    public async Task Doh_http_error_is_an_error()
    {
        var doh = new FakeTransport(DnsTransport.Doh).Then(_ => DnsExchangeResult.Failed(DnsErrorKind.HttpError, 503));

        var result = await Query(Client(doh), DnsTransport.Doh);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.HttpError), (result.Outcome, result.ErrorKind));
    }

    [Fact]
    public async Task Every_packet_including_tcp_fallback_waits_for_the_shared_pacer()
    {
        var time = new FakeTimeProvider();
        using var pacer = new RequestPacer(TimeSpan.FromSeconds(1), time);
        var udp = new FakeTransport(DnsTransport.Udp).ThenPacket(q => Responses.Rcode(q, 0, extraFlags: 0x0200));
        var tcp = new FakeTransport(DnsTransport.Tcp).ThenPacket(q => Responses.A(q, addresses: [[192, 0, 2, 1]]));
        var client = new DnsQueryClient([udp, tcp], pacer, time, maxRetries: 1);

        var task = client.QueryAsync(Resolver, DnsTransport.Udp, "ornek.example", DnsRecordType.A, TestContext.Current.CancellationToken);

        await Task.Yield();
        Assert.Single(udp.Queries);
        Assert.Empty(tcp.Queries); // TCP paketi 1 saniye beklemeli
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(result.Truncated);
        Assert.Single(tcp.Queries);
    }

    [Fact]
    public void Block_page_ip_in_answer_sets_the_block_marker()
    {
        var blockIps = new HashSet<IPAddress> { IPAddress.Parse("195.175.254.2") };
        var blocked = new DnsQueryResult(DnsOutcome.Success, DnsErrorKind.None, 0,
            [new DnsAnswer("A", "195.175.254.2", 60)], null, false, 1, TimeSpan.FromMilliseconds(12));
        var normal = blocked with { Answers = [new DnsAnswer("A", "192.0.2.1", 60)] };
        var at = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

        var a = DnsCollector.ToSnapshot(1, at, "test-lab", Resolver, DnsTransport.Udp, DnsRecordType.A, blocked, blockIps);
        var b = DnsCollector.ToSnapshot(1, at, "test-lab", Resolver, DnsTransport.Doh, DnsRecordType.A, normal, blockIps);

        Assert.True(a.BlockMarker);
        Assert.False(b.BlockMarker);
        Assert.Equal(12, a.LatencyMs);
        Assert.Equal(new DateOnly(2026, 11, 13), a.MeasuredOn);
    }

    [Fact]
    public void Snapshot_refuses_an_error_disguised_as_empty_or_nxdomain()
    {
        var at = DateTimeOffset.UnixEpoch;
        Assert.Throws<ArgumentException>(() => new DnsSnapshot(1, at, "l", "r", DnsTransport.Udp, "A", null,
            DnsOutcome.Empty, DnsErrorKind.None, [], null, false, 0, 1, false));
        Assert.Throws<ArgumentException>(() => new DnsSnapshot(1, at, "l", "r", DnsTransport.Udp, "A", 2,
            DnsOutcome.NxDomain, DnsErrorKind.None, [], null, false, 0, 1, false));
        Assert.Throws<ArgumentException>(() => new DnsSnapshot(1, at, "l", "r", DnsTransport.Udp, "A", null,
            DnsOutcome.Error, DnsErrorKind.None, [], null, false, 0, 1, false));
    }
}
