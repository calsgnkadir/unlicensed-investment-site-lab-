using Fraud.Collectors.Dns;
using Fraud.Domain.Network;

namespace Fraud.Integration.Tests.Dns;

/// <summary>Sorgu kurucu, ayrıştırıcı ve doğrulayıcı. Paketler RFC 1035'e göre elle yazılmıştır; ağ yok.</summary>
public sealed class DnsWireTests
{
    [Fact]
    public void Query_builder_matches_hand_built_rfc_1035_bytes()
    {
        var query = DnsQueryBuilder.Build(0x1234, "example.com", DnsRecordType.A);

        byte[] expected =
        [
            0x12, 0x34,             // ID
            0x01, 0x00,             // bayraklar: yalnızca RD
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // AN, NS, AR = 0
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m',
            0,                      // kök
            0x00, 0x01,             // QTYPE A
            0x00, 0x01,             // QCLASS IN
        ];
        Assert.Equal(expected, query.Packet);
        Assert.Equal("example.com", query.Name);
    }

    [Fact]
    public void Query_builder_lowercases_strips_trailing_dot_and_uses_punycode()
    {
        var query = DnsQueryBuilder.Build(1, "Bücher.EXAMPLE.", DnsRecordType.AAAA);

        Assert.Equal("xn--bcher-kva.example", query.Name);
        Assert.Equal(DnsName.WireLength("xn--bcher-kva.example"), query.Packet.Length - 12 - 4);
        Assert.Equal(28, query.Packet[^3]); // QTYPE AAAA = 28
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("a..example")]
    [InlineData("-ornek.example")]
    [InlineData("ornek-.example")]
    [InlineData("alt_cizgi.example")]
    [InlineData("bosluk var.example")]
    public void Query_builder_rejects_invalid_names(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => DnsQueryBuilder.Build(1, name, DnsRecordType.A));
    }

    [Fact]
    public void Query_builder_enforces_63_byte_labels_and_255_byte_names()
    {
        var label63 = new string('a', 63);
        Assert.Equal(label63 + ".test", DnsQueryBuilder.Build(1, label63 + ".test", DnsRecordType.A).Name);
        Assert.ThrowsAny<ArgumentException>(() => DnsQueryBuilder.Build(1, new string('a', 64) + ".test", DnsRecordType.A));

        // 4 x 63 karakterlik etiket + 3 nokta = 255 karakter => paketteki hali 257 bayt (> 255).
        var tooLong = string.Join('.', Enumerable.Repeat(label63, 4));
        Assert.ThrowsAny<ArgumentException>(() => DnsQueryBuilder.Build(1, tooLong, DnsRecordType.A));

        // 253 karakter => 255 bayt: sınırda, kabul.
        var atLimit = string.Join('.', Enumerable.Repeat(label63, 3)) + "." + new string('b', 61);
        Assert.Equal(253, atLimit.Length);
        Assert.Equal(atLimit, DnsQueryBuilder.Build(1, atLimit, DnsRecordType.A).Name);
    }

    [Fact]
    public void Random_ids_are_not_constant()
    {
        var ids = Enumerable.Range(0, 64).Select(_ => DnsQueryBuilder.NewId()).ToHashSet();
        Assert.True(ids.Count > 32);
    }

    /// <summary>
    /// RFC 1035 Bölüm 4.1.4'teki sıkıştırma örneğine benzer paket: soru "f.isi.example" (12. bayt),
    /// CNAME hedefi "foo" + işaretçi(12), A kaydının sahibi işaretçiyle CNAME hedefine, bir de ilgisiz kayıt.
    /// </summary>
    private static (byte[] Packet, DnsQuery Query) CompressedResponse()
    {
        var query = DnsQueryBuilder.Build(0xBEEF, "f.isi.example", DnsRecordType.A);
        var w = new PacketWriter().Header(0xBEEF, Responses.QrRdRa, 1, 3, 0, 0);
        var questionName = w.Position; // 12
        w.Labels("f.isi.example").U16(1).U16(1);
        var exampleLabel = questionName + 2 + 4; // "f" (2 bayt) + "isi" (4 bayt) => "example" etiketi

        // 1) f.isi.example CNAME foo.f.isi.example
        w.Pointer(questionName).RecordMeta(DnsRecordType.CNAME, 3600);
        var at = w.LengthPlaceholder();
        var fooName = w.Position;
        w.Labels("foo", terminate: false).Pointer(questionName);
        w.PatchLength(at);

        // 2) foo.f.isi.example A 192.0.2.1 (sahip işaretçiyle CNAME hedefine)
        w.Pointer(fooName).RecordMeta(DnsRecordType.A, 300).AData(192, 0, 2, 1);

        // 3) example A 203.0.113.9: sorulmayan ad (zehirleme denemesi gibi), saklanmamalı
        w.Pointer(exampleLabel).RecordMeta(DnsRecordType.A, 300).AData(203, 0, 113, 9);
        return (w.ToArray(), query);
    }

    [Fact]
    public void Parser_follows_compression_pointers_and_keeps_only_the_cname_chain()
    {
        var (packet, query) = CompressedResponse();

        var message = DnsMessageParser.Parse(packet);

        Assert.Equal(DnsErrorKind.None, DnsResponseValidator.Validate(query, message));
        Assert.Equal("f.isi.example", Assert.Single(message.Questions).Name);
        Assert.Equal(["f.isi.example", "foo.f.isi.example", "example"], message.Answers.Select(a => a.Name));
        Assert.Equal("foo.f.isi.example", message.Answers[0].Value);

        var kept = DnsQueryClient.SelectAnswers(query, message.Answers);
        Assert.Equal(
            [new DnsAnswer("CNAME", "foo.f.isi.example", 3600), new DnsAnswer("A", "192.0.2.1", 300)],
            kept);
    }

    [Fact]
    public void Parser_reads_aaaa_and_treats_ttl_with_high_bit_as_zero()
    {
        var query = DnsQueryBuilder.Build(7, "ornek.example", DnsRecordType.AAAA);
        var w = new PacketWriter().Header(7, Responses.QrRdRa, 1, 1, 0, 0).Labels("ornek.example").U16(28).U16(1);
        w.Pointer(12).RecordMeta(DnsRecordType.AAAA, 0x80000001).U16(16)
            .Bytes(0x20, 0x01, 0x0d, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01);

        var answer = Assert.Single(DnsMessageParser.Parse(w.ToArray()).Answers);

        Assert.Equal("2001:db8::1", answer.Value);
        Assert.Equal(0u, answer.Ttl); // RFC 2181 Bölüm 8
        Assert.Equal(DnsErrorKind.None, DnsResponseValidator.Validate(query, DnsMessageParser.Parse(w.ToArray())));
    }

    [Fact]
    public void Nxdomain_soa_gives_negative_ttl_and_the_soa_mailbox_is_not_kept()
    {
        var query = DnsQueryBuilder.Build(9, "yok.example", DnsRecordType.A);

        var message = DnsMessageParser.Parse(Responses.NxDomainWithSoa(query, soaTtl: 3600, minimum: 300));

        Assert.Equal(3, message.Header.Rcode);
        Assert.Equal(300L, DnsQueryClient.NegativeTtl(message)); // min(3600, 300), RFC 2308 Bölüm 5
        var soa = Assert.Single(message.Authority);
        Assert.Null(soa.Value); // RNAME (hostmaster) saklanmaz
        Assert.Equal(60L, DnsQueryClient.NegativeTtl(DnsMessageParser.Parse(Responses.NxDomainWithSoa(query, 60, 300))));
    }

    [Fact]
    public void Unknown_record_types_are_kept_as_type_number_only()
    {
        var w = new PacketWriter().Header(1, Responses.QrRdRa, 1, 1, 0, 0).Labels("ornek.example").U16(1).U16(1);
        w.Pointer(12).U16(65).U16(1).U32(60).U16(3).Bytes(0xAA, 0xBB, 0xCC);

        var record = Assert.Single(DnsMessageParser.Parse(w.ToArray()).Answers);

        Assert.Equal(65, record.Type);
        Assert.Null(record.Value);
        Assert.Equal("TYPE65", DnsQueryClient.TypeName(65));
    }

    [Fact]
    public void Label_bytes_outside_letters_digits_hyphen_are_escaped()
    {
        var w = new PacketWriter().Header(1, Responses.QrRdRa, 1, 0, 0, 0);
        w.U8(5).Bytes((byte)'a', (byte)'.', (byte)'<', 0x00, (byte)'B').Labels("example").U16(1).U16(1);

        var question = Assert.Single(DnsMessageParser.Parse(w.ToArray()).Questions);

        Assert.Equal(@"a\046\060\000b.example", question.Name);
    }

    public static TheoryData<string, byte[]> MalformedPackets()
    {
        var data = new TheoryData<string, byte[]>
        {
            { "başlıktan kısa", new byte[11] },
            { "soru adı yarım", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).U8(5).Bytes((byte)'a').ToArray() },
            { "QTYPE eksik", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).Labels("a.test").U8(0).ToArray() },
            { "iki soru (RFC 9619)", new PacketWriter().Header(1, 0x8180, 2, 0, 0, 0)
                .Labels("a.test").U16(1).U16(1).Labels("b.test").U16(1).U16(1).ToArray() },
            { "kayıt sayısı sınırın üstünde", new PacketWriter().Header(1, 0x8180, 0, 300, 0, 0).ToArray() },
            { "bildirilen cevap yok", new PacketWriter().Header(1, 0x8180, 0, 1, 0, 0).ToArray() },
            { "kendini gösteren işaretçi", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).Pointer(12).U16(1).U16(1).ToArray() },
            { "ileriyi gösteren işaretçi", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).Pointer(20).U16(1).U16(1)
                .Labels("a.test").ToArray() },
            { "başlığı gösteren işaretçi", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).Pointer(2).U16(1).U16(1).ToArray() },
            { "yarım işaretçi", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).U8(0xC0).ToArray() },
            { "ayrılmış etiket türü 01", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).U8(0x41).Bytes(0).U16(1).U16(1).ToArray() },
            { "ayrılmış etiket türü 10", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).U8(0x81).Bytes(0).U16(1).U16(1).ToArray() },
            { "etiket paketin sonunu aşıyor", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).U8(63).Bytes(1, 2, 3).ToArray() },
            { "RDLENGTH paketin sonunu aşıyor", new PacketWriter().Header(1, 0x8180, 1, 1, 0, 0).Labels("a.test").U16(1).U16(1)
                .Pointer(12).RecordMeta(DnsRecordType.A, 60).U16(400).Bytes(192, 0, 2, 1).ToArray() },
            { "A kaydı 5 bayt", new PacketWriter().Header(1, 0x8180, 1, 1, 0, 0).Labels("a.test").U16(1).U16(1)
                .Pointer(12).RecordMeta(DnsRecordType.A, 60).U16(5).Bytes(192, 0, 2, 1, 9).ToArray() },
            { "CNAME adı RDLENGTH'ten taşıyor", new PacketWriter().Header(1, 0x8180, 1, 1, 0, 0).Labels("a.test").U16(1).U16(1)
                .Pointer(12).RecordMeta(DnsRecordType.CNAME, 60).U16(2).Labels("b.test").ToArray() },
            { "sonda fazladan bayt", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0).Labels("a.test").U16(1).U16(1).U8(0).ToArray() },
        };

        // İki işaretçili döngü: 14. bayt "x" etiketi ve 12. bayta işaretçi; 12. bayt 14'ü gösteremez (ileri).
        data.Add("iki işaretçili döngü", new PacketWriter().Header(1, 0x8180, 1, 0, 0, 0)
            .Pointer(14).U8(1).Bytes((byte)'x').Pointer(12).U16(1).U16(1).ToArray());

        // Geriye gösteren işaretçinin hedefinden ileri okuyunca aynı işaretçiye geri gelinen döngü:
        // 12: "a" etiketi, 14: işaretçi(12). Hedef 12 < 14 (geri) ama 12'den okumak yeniden 14'e varır.
        data.Add("geri işaretçiyle kendi üstüne dönen döngü", new PacketWriter().Header(1, 0x8180, 0, 1, 0, 0)
            .U8(1).Bytes((byte)'a').Pointer(12).RecordMeta(DnsRecordType.A, 1).AData(1, 2, 3, 4).ToArray());

        // 255 baytı aşan ad: Soru "a.test" (12. bayt). Her cevabın sahibi 63 baytlık bir etiket + bir önceki ada
        // işaretçidir; ad her kayıtta 64 bayt uzar. 4. kayıtta 4 x 64 + 8 = 264 bayt olur ve reddedilmelidir.
        var longName = new PacketWriter().Header(1, 0x8180, 1, 4, 0, 0).Labels("a.test").U16(1).U16(1);
        var previous = 12;
        for (var i = 0; i < 4; i++)
        {
            var start = longName.Position;
            longName.U8(63).Bytes(Enumerable.Repeat((byte)'a', 63).ToArray()).Pointer(previous)
                .RecordMeta(DnsRecordType.A, 1).AData(192, 0, 2, 1);
            previous = start;
        }

        data.Add("255 baytı aşan sıkıştırılmış ad", longName.ToArray());
        return data;
    }

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public void Malformed_packets_raise_only_dns_format_exception(string reason, byte[] packet)
    {
        var ex = Record.Exception(() => DnsMessageParser.Parse(packet));

        Assert.True(ex is DnsFormatException, reason + ": " + (ex?.GetType().Name ?? "istisna yok"));
    }

    [Fact]
    public void Validator_rejects_wrong_id_non_response_and_wrong_question()
    {
        var query = DnsQueryBuilder.Build(0x1111, "ornek.example", DnsRecordType.A);

        DnsErrorKind Check(byte[] packet) => DnsResponseValidator.Validate(query, DnsMessageParser.Parse(packet));

        Assert.Equal(DnsErrorKind.None, Check(Responses.A(query, addresses: [[192, 0, 2, 1]])));
        Assert.Equal(DnsErrorKind.IdMismatch, Check(Responses.A(query, id: 0x2222, addresses: [[192, 0, 2, 1]])));
        Assert.Equal(DnsErrorKind.Malformed, Check(Responses.A(query, flags: 0x0100))); // QR=0: sorgu, cevap değil

        var otherName = DnsQueryBuilder.Build(0x1111, "baska.example", DnsRecordType.A);
        Assert.Equal(DnsErrorKind.QuestionMismatch, Check(Responses.A(otherName)));
        var otherType = DnsQueryBuilder.Build(0x1111, "ornek.example", DnsRecordType.NS);
        Assert.Equal(DnsErrorKind.QuestionMismatch, Check(Responses.Rcode(otherType, 0)));
        var noQuestion = new PacketWriter().Header(0x1111, Responses.QrRdRa, 0, 0, 0, 0).ToArray();
        Assert.Equal(DnsErrorKind.QuestionMismatch, Check(noQuestion));
    }

    [Fact]
    public void Validator_compares_question_names_case_insensitively()
    {
        var query = DnsQueryBuilder.Build(5, "ornek.example", DnsRecordType.A);
        var upper = new PacketWriter().Header(5, Responses.QrRdRa, 1, 0, 0, 0).Labels("OrNeK.EXAMPLE").U16(1).U16(1).ToArray();

        Assert.Equal(DnsErrorKind.None, DnsResponseValidator.Validate(query, DnsMessageParser.Parse(upper)));
    }
}
