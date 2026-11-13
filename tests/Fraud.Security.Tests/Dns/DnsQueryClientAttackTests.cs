using System.Net;
using System.Text.Json;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Domain.Network;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Dns;

/// <summary>
/// Sorgu akışına biçimce doğru ama kötü niyetli cevaplar: bailiwick dışı kayıt, kesik cevaba gömülü sahte adres,
/// CNAME döngüsü, engel IP'sinin başka yazımı. Ağ yok; sahte taşıma kullanılır.
/// </summary>
public sealed class DnsQueryClientAttackTests
{
    private const string Name = "ornek.example";

    /// <summary>Her çağrıda sıradaki cevabı veren sahte taşıma.</summary>
    private sealed class ScriptedTransport(DnsTransport kind, params Func<DnsQuery, DnsExchangeResult>[] script) : IDnsTransport
    {
        private int _next;

        public DnsTransport Kind => kind;

        public int Calls => _next;

        public Task<DnsExchangeResult> ExchangeAsync(DnsResolver resolver, DnsQuery query, CancellationToken cancellationToken)
        {
            if (_next >= script.Length)
            {
                throw new InvalidOperationException("Beklenmeyen sorgu: " + kind);
            }

            return Task.FromResult(script[_next++](query));
        }
    }

    private static DnsQueryClient Client(params IDnsTransport[] transports) =>
        new(transports, new RequestPacer(TimeSpan.Zero, TimeProvider.System), TimeProvider.System, maxRetries: 1);

    private static Task<DnsQueryResult> Ask(DnsQueryClient client, DnsTransport transport, DnsRecordType type = DnsRecordType.A) =>
        client.QueryAsync(DnsResolverCatalog.Cloudflare, transport, Name, type, TestContext.Current.CancellationToken);

    private static Func<DnsQuery, DnsExchangeResult> Reply(
        Func<DnsQuery, IReadOnlyList<(string, int, byte[])>> answers, int flags = DnsPacket.ResponseFlags) =>
        q => DnsExchangeResult.Ok(DnsPacket.Answer(q, answers(q), flags));

    [Theory]
    [InlineData(DnsErrorKind.Timeout)]
    [InlineData(DnsErrorKind.NetworkError)]
    [InlineData(DnsErrorKind.HttpError)]
    [InlineData(DnsErrorKind.ResponseTooLarge)]
    public async Task Transport_failures_are_errors_never_empty_or_nxdomain(DnsErrorKind failure)
    {
        var doh = new ScriptedTransport(DnsTransport.Doh,
            _ => DnsExchangeResult.Failed(failure), _ => DnsExchangeResult.Failed(failure));

        var result = await Ask(Client(doh), DnsTransport.Doh);

        Assert.Equal(DnsOutcome.Error, result.Outcome);
        Assert.Equal(failure, result.ErrorKind);
        Assert.Empty(result.Answers);
        Assert.Null(result.Rcode);
        Assert.Null(result.NegativeTtl);
    }

    [Fact]
    public async Task Malformed_answer_is_an_error_and_is_not_retried()
    {
        var udp = new ScriptedTransport(DnsTransport.Udp, q => DnsExchangeResult.Ok([.. q.Packet[..2], 0x81, 0x80, 0xFF, 0xFF]));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.Malformed, 1), (result.Outcome, result.ErrorKind, result.Attempts));
    }

    [Fact]
    public async Task Records_inside_a_truncated_udp_answer_are_never_kept()
    {
        // Yoldaki biri TC bitli cevaba sahte adres koyar; TCP cevabı NXDOMAIN. Sahte adres hiçbir yere geçmemeli.
        var udp = new ScriptedTransport(DnsTransport.Udp,
            Reply(_ => [(Name, 1, DnsPacket.Ipv4("203.0.113.66"))], DnsPacket.ResponseFlags | DnsWire.FlagTc));
        var tcp = new ScriptedTransport(DnsTransport.Tcp, Reply(_ => [], DnsPacket.ResponseFlags | 3));

        var result = await Ask(Client(udp, tcp), DnsTransport.Udp);

        Assert.Equal(DnsOutcome.NxDomain, result.Outcome);
        Assert.True(result.Truncated);
        Assert.DoesNotContain(result.Answers, a => a.Value == "203.0.113.66");
    }

    [Fact]
    public async Task Tcp_fallback_answer_to_another_question_is_rejected()
    {
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ => [], DnsPacket.ResponseFlags | DnsWire.FlagTc));
        var tcp = new ScriptedTransport(DnsTransport.Tcp,
            q => DnsExchangeResult.Ok(DnsPacket.Answer(DnsQueryBuilder.Build(q.Id, "baska.example", DnsRecordType.A),
                [("baska.example", 1, DnsPacket.Ipv4("203.0.113.66"))])));

        var result = await Ask(Client(udp, tcp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.QuestionMismatch), (result.Outcome, result.ErrorKind));
        Assert.Empty(result.Answers);
    }

    [Theory]
    [InlineData(0x0180)]            // QR = 0: bizim sorgumuz geri yansıtılmış
    [InlineData(0x8180 | 0x2800)]  // OPCODE 5 (UPDATE)
    public async Task Non_response_or_other_opcode_is_malformed(int flags)
    {
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ => [(Name, 1, DnsPacket.Ipv4("192.0.2.1"))], flags));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.Malformed), (result.Outcome, result.ErrorKind));
    }

    [Fact]
    public async Task Question_with_a_dot_inside_one_label_does_not_match_our_name()
    {
        // "ornek.example" tek etiket olarak (içinde nokta baytı) yazılırsa okuyucu \046 üretir; eşleşmemeli.
        var udp = new ScriptedTransport(DnsTransport.Udp, q =>
        {
            var p = new DnsPacket().Header(q.Id, DnsPacket.ResponseFlags, 1, 1, 0, 0);
            p.U8(Name.Length).Bytes(System.Text.Encoding.ASCII.GetBytes(Name)).U8(0).U16(1).U16(1);
            p.Jump(12).RecordTail(1, 60, DnsPacket.Ipv4("203.0.113.66"));
            return DnsExchangeResult.Ok(p.ToArray());
        });

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.QuestionMismatch), (result.Outcome, result.ErrorKind));
    }

    [Fact]
    public async Task Additional_and_authority_section_addresses_are_never_kept()
    {
        // Eski önbellek zehirleme: Ek bölüme sorulan ad veya başka ad için adres koymak.
        var udp = new ScriptedTransport(DnsTransport.Udp, q => DnsExchangeResult.Ok(DnsPacket.Answer(q,
            [(Name, 1, DnsPacket.Ipv4("192.0.2.1"))],
            authority: [(Name, 1, DnsPacket.Ipv4("203.0.113.10"))],
            additional: [(Name, 1, DnsPacket.Ipv4("203.0.113.11")), ("ns.baska.example", 1, DnsPacket.Ipv4("203.0.113.12"))])));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.Equal(DnsOutcome.Success, result.Outcome);
        Assert.Equal(["192.0.2.1"], result.Answers.Select(a => a.Value));
    }

    [Fact]
    public async Task Soa_mailbox_from_a_negative_answer_is_not_kept()
    {
        var udp = new ScriptedTransport(DnsTransport.Udp, q => DnsExchangeResult.Ok(DnsPacket.Answer(q, [],
            DnsPacket.ResponseFlags | 3,
            authority: [("example", 6, DnsPacket.Soa("ns1.example", "ali-veli.kisisel-posta.example", 300))])));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.NxDomain, 300L), (result.Outcome, result.NegativeTtl));
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("ali-veli", json, StringComparison.Ordinal);
        Assert.DoesNotContain("kisisel-posta", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("baska.example", 1)]  // bailiwick dışı: başka ad için A kaydı
    [InlineData(Name, 28)]           // sorulan ad ama başka tür (A sorusuna AAAA)
    [InlineData(Name, 16)]           // TXT
    public async Task Noerror_answer_that_does_not_answer_the_question_is_not_written_as_empty(string owner, int type)
    {
        // Bulgu N3: Cevap bölümü dolu ama içindeki kayıtların hiçbiri sorumuza ait değil. Bugün bu "Empty"
        // (ad var, kaydı yok) yazılıyor; oysa cevap tutarsızdır ve "kayıt yok" anlamına gelmez.
        var rdata = type switch { 1 => DnsPacket.Ipv4("203.0.113.66"), 28 => DnsPacket.Ipv6("2001:db8::66"), _ => [4, 0x61, 0x62, 0x63, 0x64] };
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ => [(owner, type, rdata)]));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.NotEqual(DnsOutcome.Empty, result.Outcome);
        Assert.Equal(DnsOutcome.Error, result.Outcome);
    }

    [Fact]
    public async Task Cname_loop_without_an_address_is_not_a_success()
    {
        // Bulgu N3: ornek.example -> b.example -> ornek.example. Çözücü normalde SERVFAIL döner; sahte veya bozuk
        // cevap "Success" diye yazılıyor (yalnızca iki CNAME, adres yok).
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ =>
        [
            (Name, 5, DnsPacket.EncodeName("b.example")),
            ("b.example", 5, DnsPacket.EncodeName(Name)),
        ]));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.NotEqual(DnsOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task Cname_chain_longer_than_the_documented_limit_is_not_followed()
    {
        // Bulgu N3 (bilgi): MaxCnameChain = 16 "tur" sayar, zincir uzunluğunu değil. Sıralı gelen 40 halkalı zincir
        // tek turda sonuna kadar izlenir ve sondaki adres kabul edilir.
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ =>
        {
            var records = new List<(string, int, byte[])>();
            var current = Name;
            for (var i = 0; i < 40; i++)
            {
                var next = $"h{i}.example";
                records.Add((current, 5, DnsPacket.EncodeName(next)));
                current = next;
            }

            records.Add((current, 1, DnsPacket.Ipv4("203.0.113.66")));
            return records;
        }));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.DoesNotContain(result.Answers, a => a.Value == "203.0.113.66");
    }

    [Fact]
    public void Block_page_ip_written_as_ipv4_mapped_ipv6_still_sets_the_block_marker()
    {
        // Bulgu N4: Engel IP'si AAAA cevabında ::ffff:195.175.254.2 olarak gelirse işaret konmuyor. NetworkTargetQuery
        // bu adresi IPv4'e çevirip RIPEstat ve InternetDB'ye gönderiyor: engel sunucusu "barındırma" diye kaydediliyor.
        var options = new DnsOptions { Location = "test-lab" };
        var result = new DnsQueryResult(DnsOutcome.Success, DnsErrorKind.None, 0,
            [new DnsAnswer("AAAA", "::ffff:195.175.254.2", 300)], null, false, 1, TimeSpan.Zero);

        var snapshot = DnsCollector.ToSnapshot(1, DateTimeOffset.UnixEpoch, "test-lab", DnsResolverCatalog.Cloudflare,
            DnsTransport.Doh, DnsRecordType.AAAA, result, options.EffectiveBlockPageIps);

        Assert.True(snapshot.BlockMarker);
    }

    [Fact]
    public void Block_marker_is_never_set_on_negative_or_error_results()
    {
        var blockIps = new DnsOptions { Location = "test-lab" }.EffectiveBlockPageIps;
        var nx = new DnsQueryResult(DnsOutcome.NxDomain, DnsErrorKind.None, 3,
            [new DnsAnswer("A", "195.175.254.2", 300)], 300, false, 1, TimeSpan.Zero);

        var snapshot = DnsCollector.ToSnapshot(1, DateTimeOffset.UnixEpoch, "test-lab", DnsResolverCatalog.Cloudflare,
            DnsTransport.Udp, DnsRecordType.A, nx, blockIps);

        Assert.False(snapshot.BlockMarker);
        Assert.Throws<ArgumentException>(() => new DnsSnapshot(1, DateTimeOffset.UnixEpoch, "test-lab", "cloudflare",
            DnsTransport.Udp, "A", 3, DnsOutcome.NxDomain, DnsErrorKind.None, [], 300, false, 0, 1, blockMarker: true));
        Assert.Throws<ArgumentException>(() => new DnsSnapshot(1, DateTimeOffset.UnixEpoch, "test-lab", "cloudflare",
            DnsTransport.Udp, "A", null, DnsOutcome.Empty, DnsErrorKind.None, [], null, false, 0, 1, blockMarker: false));
        Assert.Throws<ArgumentException>(() => new DnsSnapshot(1, DateTimeOffset.UnixEpoch, "test-lab", "cloudflare",
            DnsTransport.Udp, "A", null, DnsOutcome.Error, DnsErrorKind.Timeout,
            [new DnsAnswer("A", "203.0.113.66", 1)], null, false, 0, 1, blockMarker: false));
    }

    [Fact]
    public void Dns_snapshot_answers_json_cannot_smuggle_structure()
    {
        // Cevap değeri ayrıştırıcıdan gelir (\DDD kaçışlı ASCII), ama veritabanı da güvenilmez sayılır.
        var answers = new[] { new DnsAnswer("CNAME", "a\\034,\\\"type\\\":\\\"A\\\"", 1), new DnsAnswer("A", "</script><x>", 1) };

        var json = Fraud.Infrastructure.Persistence.DnsAnswerJson.Serialize(answers);
        var back = Fraud.Infrastructure.Persistence.DnsAnswerJson.Deserialize(json);

        Assert.Equal(answers, back);
        Assert.DoesNotContain("<", json, StringComparison.Ordinal);
        Assert.ThrowsAny<JsonException>(() => Fraud.Infrastructure.Persistence.DnsAnswerJson.Deserialize("[[[[[[[[[[1]]]]]]]]]]"));
    }

    [Fact]
    public async Task Rcode_with_answers_is_never_kept_as_success_and_spoofed_ip_is_dropped_on_servfail()
    {
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ => [(Name, 1, DnsPacket.Ipv4("203.0.113.66"))], DnsPacket.ResponseFlags | 2));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.ServerFailure, 2), (result.Outcome, result.ErrorKind, result.Rcode));
        Assert.Empty(result.Answers);
    }

    [Fact]
    public async Task Ip_parsing_of_stored_answers_is_strict_round_trip()
    {
        // Ayrıştırıcının ürettiği adres metni IPAddress.ToString(); başka yazım (ondalık, sekizlik) üretmez.
        var udp = new ScriptedTransport(DnsTransport.Udp, Reply(_ => [(Name, 1, [127, 0, 0, 1])]));

        var result = await Ask(Client(udp), DnsTransport.Udp);

        var value = Assert.Single(result.Answers).Value;
        Assert.Equal("127.0.0.1", value);
        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(value!));
    }
}
