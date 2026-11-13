using System.Diagnostics;
using Fraud.Collectors.Dns;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Dns;

/// <summary>
/// DNS ayrıştırıcısına saldırgan paketler. Tek kabul edilen sonuç: ya başarı ya da <see cref="DnsFormatException"/>.
/// Başka istisna, takılma, sınırsız bellek yok. Ağ yok; bütün paketler elle kurulur.
/// </summary>
public sealed class DnsParserAttackTests
{
    private const int QuestionEnd = 12 + 3 + 4; // "a" sorusu: 01 'a' 00 + QTYPE + QCLASS

    private static DnsPacket WithQuestion(int an, int ns = 0, int ar = 0, int qd = 1)
    {
        var p = new DnsPacket().Header(0x1234, DnsPacket.ResponseFlags, qd, an, ns, ar);
        if (qd > 0)
        {
            p.Name("a").U16(1).U16(1);
        }

        return p;
    }

    private static DnsFormatException Rejects(byte[] packet) => Assert.Throws<DnsFormatException>(() => DnsMessageParser.Parse(packet));

    [Fact]
    public void Pointer_to_itself_is_rejected()
    {
        // Soru adı konum 12'de ve kendini gösteriyor: C0 0C.
        var packet = new DnsPacket().Header(1, DnsPacket.ResponseFlags, 1, 0, 0, 0).Jump(12).U16(1).U16(1).ToArray();

        Rejects(packet);
    }

    [Fact]
    public void Two_pointers_pointing_at_each_other_are_rejected()
    {
        // Kayıt sahibi: konum X'te "ileri" işaretçi X+2'ye; X+2'de geri işaretçi X'e. İlk atlama ileri olduğu için durur.
        var p = WithQuestion(an: 1);
        var x = p.Position;
        p.Jump(x + 2).Jump(x).RecordTail(1, 60, [192, 0, 2, 1]);

        Rejects(p.ToArray());
    }

    [Fact]
    public void Label_run_that_reaches_its_own_pointer_again_is_rejected()
    {
        // Konum X: 01 'b' C0 X  (etiket, sonra kendi parçasının başını gösteren işaretçi).
        var p = WithQuestion(an: 1);
        var x = p.Position;
        p.Labels("b").Jump(x).RecordTail(1, 60, [192, 0, 2, 1]);

        Rejects(p.ToArray());
    }

    [Fact]
    public void Pointer_into_the_header_is_rejected()
    {
        var p = WithQuestion(an: 1).Jump(4).RecordTail(1, 60, [192, 0, 2, 1]);

        Rejects(p.ToArray());
    }

    /// <summary>
    /// Bilinmeyen türdeki bir kaydın RDATA'sına N işaretçilik geri zincir koyar (her biri bir öncekini gösterir; ilki
    /// soru adını). İkinci kaydın sahibi zincirin sonunu gösterir: N + 1 atlama.
    /// </summary>
    private static byte[] PointerChain(int pointers)
    {
        var p = WithQuestion(an: 2);
        p.Jump(12).U16(99).U16(1).U32(60).U16(2 * pointers);
        var previous = 12;
        for (var i = 0; i < pointers; i++)
        {
            var here = p.Position;
            p.Jump(previous);
            previous = here;
        }

        p.Jump(previous).RecordTail(1, 60, [192, 0, 2, 1]);
        return p.ToArray();
    }

    [Fact]
    public void Backward_pointer_chain_is_cut_at_the_jump_limit()
    {
        // 31 işaretçi + sahip işaretçisi = 32 atlama: sınırda, kabul.
        var atLimit = DnsMessageParser.Parse(PointerChain(DnsWire.MaxCompressionJumps - 1));
        Assert.Equal("a", atLimit.Answers[1].Name);

        // 40 işaretçi: 41 atlama, ret.
        Rejects(PointerChain(40));
    }

    [Fact]
    public void Name_expanded_through_pointers_beyond_255_bytes_is_rejected()
    {
        // Her parça: 63 baytlık etiket + bir önceki parçaya işaretçi. 4 parça + "a" = 4*64 + 2 + 1 > 255.
        var p = WithQuestion(an: 2);
        const int segments = 4;
        p.Jump(12).U16(99).U16(1).U32(60).U16(segments * 66);
        var previous = 12;
        for (var i = 0; i < segments; i++)
        {
            var here = p.Position;
            p.Labels(new string((char)('a' + i), 63)).Jump(previous);
            previous = here;
        }

        p.Jump(previous).RecordTail(1, 60, [192, 0, 2, 1]);

        Rejects(p.ToArray());
    }

    [Fact]
    public void Plain_name_of_exactly_255_bytes_parses_and_256_is_rejected()
    {
        static byte[] Packet(int lastLabel) => new DnsPacket()
            .Header(1, DnsPacket.ResponseFlags, 1, 0, 0, 0)
            .Name(string.Join('.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', lastLabel)))
            .U16(1).U16(1).ToArray();

        // 3 x 64 + (1 + 61) + 1 = 255
        Assert.Single(DnsMessageParser.Parse(Packet(61)).Questions);
        Rejects(Packet(62));
    }

    [Theory]
    [InlineData(0x40)] // 01xxxxxx: eski genişletilmiş etiket
    [InlineData(0x80)] // 10xxxxxx: ayrılmış
    [InlineData(0x7F)]
    [InlineData(0xBF)]
    public void Reserved_label_types_are_rejected(int lengthByte)
    {
        var packet = new DnsPacket().Header(1, DnsPacket.ResponseFlags, 1, 0, 0, 0)
            .U8(lengthByte).Bytes(new byte[70]).U8(0).U16(1).U16(1).ToArray();

        Rejects(packet);
    }

    [Fact]
    public void Label_length_running_past_the_packet_is_rejected()
    {
        var packet = new DnsPacket().Header(1, DnsPacket.ResponseFlags, 1, 0, 0, 0).U8(63).Bytes(new byte[10]).ToArray();

        Rejects(packet);
    }

    [Theory]
    [InlineData(1, 0xFFFF)]  // A, RDLENGTH paketten büyük
    [InlineData(1, 5)]       // A, 4 değil
    [InlineData(1, 3)]
    [InlineData(28, 4)]      // AAAA, 16 değil
    [InlineData(28, 17)]
    [InlineData(99, 0x8000)] // bilinmeyen tür, taşma
    public void Rdlength_lies_are_rejected(int type, int declaredLength)
    {
        var p = WithQuestion(an: 1).Jump(12).RecordTail(type, 60, new byte[Math.Min(declaredLength, 17)], declaredLength);

        Rejects(p.ToArray());
    }

    [Fact]
    public void Cname_target_running_past_its_rdlength_is_rejected()
    {
        // RDLENGTH 2 ama ad 7 bayt. Ad, bir sonraki kaydın baytlarını kendi parçası gibi okumaya çalışır.
        var p = WithQuestion(an: 1).Jump(12).RecordTail(5, 60, DnsPacket.EncodeName("hedef"), declaredLength: 2);

        Rejects(p.ToArray());
    }

    [Fact]
    public void Soa_shorter_than_its_fixed_fields_is_rejected()
    {
        var soa = DnsPacket.Soa("ns.a", "hostmaster.a", 300);
        var p = WithQuestion(an: 0, ns: 1).Jump(12).RecordTail(6, 60, soa[..^8], declaredLength: soa.Length - 8);

        Rejects(p.ToArray());
    }

    [Theory]
    [InlineData(65535, 0, 0)]
    [InlineData(0, 65535, 0)]
    [InlineData(0, 0, 65535)]
    [InlineData(257, 0, 0)]
    public void Huge_record_counts_are_rejected_before_any_record_is_read(int an, int ns, int ar)
    {
        var packet = WithQuestion(an, ns, ar).Jump(12).RecordTail(1, 60, [192, 0, 2, 1]).ToArray();
        var stopwatch = Stopwatch.StartNew();

        Rejects(packet);

        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Sayaç reddi {stopwatch.ElapsedMilliseconds} ms sürdü.");
    }

    [Fact]
    public void Maximum_allowed_record_count_still_parses()
    {
        var p = WithQuestion(an: DnsWire.MaxRecordsPerSection);
        for (var i = 0; i < DnsWire.MaxRecordsPerSection; i++)
        {
            p.Jump(12).RecordTail(1, 60, [192, 0, 2, (byte)i]);
        }

        Assert.Equal(DnsWire.MaxRecordsPerSection, DnsMessageParser.Parse(p.ToArray()).Answers.Count);
    }

    [Fact]
    public void Counts_claiming_more_records_than_present_are_rejected()
    {
        Rejects(WithQuestion(an: 2).Jump(12).RecordTail(1, 60, [192, 0, 2, 1]).ToArray());
    }

    [Fact]
    public void Records_beyond_the_counts_are_rejected_as_trailing_bytes()
    {
        Rejects(WithQuestion(an: 0).Jump(12).RecordTail(1, 60, [192, 0, 2, 1]).ToArray());
    }

    [Fact]
    public void More_than_one_question_is_rejected()
    {
        var packet = new DnsPacket().Header(1, DnsPacket.ResponseFlags, 2, 0, 0, 0)
            .Name("a").U16(1).U16(1).Name("b").U16(1).U16(1).ToArray();

        Rejects(packet);
    }

    [Fact]
    public void Every_truncation_of_a_valid_answer_is_rejected()
    {
        var query = DnsQueryBuilder.Build(0x4242, "ornek.example", DnsRecordType.A);
        var full = DnsPacket.Answer(query,
        [
            ("ornek.example", 5, DnsPacket.EncodeName("cdn.ornek.example")),
            ("cdn.ornek.example", 1, DnsPacket.Ipv4("192.0.2.7")),
        ], authority: [("ornek.example", 6, DnsPacket.Soa("ns.ornek.example", "hostmaster.ornek.example", 300))]);
        Assert.Equal(2, DnsMessageParser.Parse(full).Answers.Count);

        for (var length = 0; length < full.Length; length++)
        {
            Rejects(full[..length]);
        }
    }

    [Fact]
    public void Packet_larger_than_the_tcp_maximum_is_rejected()
    {
        var packet = new byte[DnsWire.MaxMessageLength + 1];
        new DnsPacket().Header(1, DnsPacket.ResponseFlags, 0, 0, 0, 0).ToArray().CopyTo(packet, 0);

        Rejects(packet);
    }

    [Fact]
    public void Format_errors_never_carry_packet_text()
    {
        // Paket içindeki ad (alan adı veya saldırgan metni) istisna mesajına, dolayısıyla loga taşınmamalı.
        const string secret = "gizli-aday-adi";
        var packets = new[]
        {
            WithQuestion(an: 2).Labels(secret).Jump(12).RecordTail(1, 60, [192, 0, 2, 1]).ToArray(),
            WithQuestion(an: 1).Labels(secret).Jump(12).RecordTail(1, 60, [1, 2, 3], 3).ToArray(),
            new DnsPacket().Header(1, DnsPacket.ResponseFlags, 1, 0, 0, 0).Labels(secret).ToArray(),
        };

        foreach (var packet in packets)
        {
            var ex = Rejects(packet);
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                Assert.DoesNotContain(secret, e.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Worst_case_legal_packet_parses_in_bounded_time()
    {
        // 32 atlamalık zincir + bunu gösteren 255 kayıt: Ayrıştırıcının kabul ettiği en pahalı paketlerden biri.
        var p = WithQuestion(an: DnsWire.MaxRecordsPerSection);
        const int chain = DnsWire.MaxCompressionJumps - 1;
        p.Jump(12).U16(99).U16(1).U32(60).U16(2 * chain);
        var previous = 12;
        for (var i = 0; i < chain; i++)
        {
            var here = p.Position;
            p.Jump(previous);
            previous = here;
        }

        for (var i = 1; i < DnsWire.MaxRecordsPerSection; i++)
        {
            p.Jump(previous).RecordTail(1, 60, [192, 0, 2, (byte)i]);
        }

        var packet = p.ToArray();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(DnsWire.MaxRecordsPerSection, DnsMessageParser.Parse(packet).Answers.Count);
        }

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"20 ayrıştırma {stopwatch.ElapsedMilliseconds} ms sürdü.");
    }

    [Fact]
    public void Pointer_and_count_mutation_fuzz_throws_only_format_errors_with_bounded_time_and_memory()
    {
        // Sabit tohum; yalnızca yerelde. Mutasyonlar özellikle işaretçi, uzunluk ve sayaç baytlarına yönelir.
        var random = new Random(4_2026_1113);
        var query = DnsQueryBuilder.Build(0x7777, "ornek.example", DnsRecordType.A);
        var seeds = new[]
        {
            DnsPacket.Answer(query,
            [
                ("ornek.example", 5, DnsPacket.EncodeName("cdn.ornek.example")),
                ("cdn.ornek.example", 1, DnsPacket.Ipv4("192.0.2.7")),
            ], authority: [("ornek.example", 6, DnsPacket.Soa("ns.ornek.example", "hostmaster.ornek.example", 300))]),
            PointerChain(20),
            DnsPacket.Answer(query, [("ornek.example", 28, DnsPacket.Ipv6("2001:db8::1"))]),
        };

        var stopwatch = Stopwatch.StartNew();
        long maxAllocated = 0;
        int parsed = 0, rejected = 0;
        for (var i = 0; i < 60_000; i++)
        {
            var packet = (byte[])seeds[i % seeds.Length].Clone();
            var mutations = random.Next(1, 5);
            for (var m = 0; m < mutations; m++)
            {
                var at = random.Next(0, packet.Length);
                switch (random.Next(5))
                {
                    case 0: // işaretçi ekle (rastgele hedef, ileri olabilir)
                        packet[at] = (byte)(0xC0 | random.Next(0, 0x40));
                        if (at + 1 < packet.Length)
                        {
                            packet[at + 1] = (byte)random.Next(256);
                        }

                        break;
                    case 1: // sayaçlar
                        packet[random.Next(0, Math.Min(12, packet.Length))] = (byte)random.Next(256);
                        break;
                    case 2: // uzunluk baytı
                        packet[at] = (byte)random.Next(0, 64);
                        break;
                    case 3: // kes
                        packet = packet[..at];
                        break;
                    default:
                        packet[at] = (byte)random.Next(256);
                        break;
                }

                if (packet.Length == 0)
                {
                    break;
                }
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                DnsMessageParser.Parse(packet);
                parsed++;
            }
            catch (DnsFormatException)
            {
                rejected++;
            }

            maxAllocated = Math.Max(maxAllocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(rejected > 0 && parsed > 0, $"parsed={parsed}, rejected={rejected}");
        Assert.True(maxAllocated < 1024 * 1024, $"Tek paket {maxAllocated} bayt ayırdı.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Fuzz {stopwatch.Elapsed} sürdü.");
    }
}
