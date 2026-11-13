using System.Diagnostics;
using Fraud.Collectors.Dns;

namespace Fraud.Integration.Tests.Dns;

/// <summary>
/// Fuzz tarzı test: Binlerce rastgele ve bozulmuş paket ayrıştırıcıya verilir. Tek beklenti: Ya başarı ya da
/// <see cref="DnsFormatException"/>. Başka istisna, takılma veya sonsuz döngü olmamalı. Tohum sabit; sonuç tekrarlanabilir.
/// </summary>
public sealed class DnsFuzzTests
{
    private const int Seed = 20261113;

    private static IEnumerable<byte[]> ValidSeeds()
    {
        var a = DnsQueryBuilder.Build(0x1234, "ornek.example", DnsRecordType.A);
        yield return a.Packet;
        yield return Responses.A(a, addresses: [[192, 0, 2, 1], [192, 0, 2, 2]]);
        yield return Responses.NxDomainWithSoa(a, 3600, 300);
        yield return Responses.Rcode(a, 2);

        // Sıkıştırmalı CNAME zinciri.
        var w = new PacketWriter().Header(0xBEEF, Responses.QrRdRa, 1, 2, 0, 0).Labels("f.isi.example").U16(1).U16(1);
        w.Pointer(12).RecordMeta(DnsRecordType.CNAME, 3600);
        var at = w.LengthPlaceholder();
        var foo = w.Position;
        w.Labels("foo", terminate: false).Pointer(12).PatchLength(at);
        w.Pointer(foo).RecordMeta(DnsRecordType.A, 300).AData(192, 0, 2, 1);
        yield return w.ToArray();
    }

    [Fact]
    public void Parser_survives_random_and_mutated_packets()
    {
        var random = new Random(Seed);
        var seeds = ValidSeeds().ToList();
        var stopwatch = Stopwatch.StartNew();
        int parsed = 0, rejected = 0;

        void Feed(byte[] packet)
        {
            try
            {
                DnsMessageParser.Parse(packet);
                parsed++;
            }
            catch (DnsFormatException)
            {
                rejected++;
            }

            // DnsFormatException dışındaki istisna testi doğrudan kırar (yakalanmaz).
        }

        // 1) Tamamen rastgele paketler (çoğu başlıkta veya ilk adda reddedilir).
        for (var i = 0; i < 4000; i++)
        {
            var packet = new byte[random.Next(0, 600)];
            random.NextBytes(packet);
            Feed(packet);
        }

        // 2) Rastgele gövdeli ama makul başlıklı paketler: sayaçlar küçük, böylece kayıt okuma yolu da çalışır.
        for (var i = 0; i < 4000; i++)
        {
            var packet = new byte[random.Next(12, 300)];
            random.NextBytes(packet);
            packet[4] = 0;
            packet[5] = (byte)random.Next(0, 2);
            packet[6] = 0;
            packet[7] = (byte)random.Next(0, 4);
            packet[8] = 0;
            packet[9] = (byte)random.Next(0, 3);
            packet[10] = 0;
            packet[11] = (byte)random.Next(0, 3);
            Feed(packet);
        }

        // 3) Geçerli paketlerin bozulmuş halleri: bit çevirme, kesme, bayt ekleme, işaretçi baytı ekleme.
        for (var i = 0; i < 12000; i++)
        {
            var packet = seeds[random.Next(seeds.Count)].ToList();
            var mutations = random.Next(1, 4);
            for (var m = 0; m < mutations && packet.Count > 0; m++)
            {
                var position = random.Next(packet.Count);
                switch (random.Next(5))
                {
                    case 0:
                        packet[position] ^= (byte)(1 << random.Next(8));
                        break;
                    case 1:
                        packet.RemoveRange(position, packet.Count - position);
                        break;
                    case 2:
                        packet.Insert(position, (byte)random.Next(256));
                        break;
                    case 3:
                        packet[position] = (byte)(0xC0 | random.Next(4));
                        if (position + 1 < packet.Count)
                        {
                            packet[position + 1] = (byte)random.Next(256);
                        }

                        break;
                    default:
                        packet[position] = (byte)random.Next(256);
                        break;
                }
            }

            Feed([.. packet]);
        }

        stopwatch.Stop();
        Assert.Equal(20000, parsed + rejected);
        Assert.True(parsed > 0, "Hiçbir bozulmuş paket geçmedi; mutasyon yolu sınanmamış olabilir.");
        Assert.True(rejected > 0);
        // Sınırlı süre: Döngü koruması çalışmasaydı test burada takılırdı.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "Ayrıştırma çok yavaş: " + stopwatch.Elapsed);
    }

    [Fact]
    public void Every_single_byte_mutation_of_a_compressed_answer_is_handled()
    {
        var packet = ValidSeeds().Last();
        for (var position = 0; position < packet.Length; position++)
        {
            for (var value = 0; value < 256; value++)
            {
                var copy = (byte[])packet.Clone();
                copy[position] = (byte)value;
                var ex = Record.Exception(() => DnsMessageParser.Parse(copy));
                Assert.True(ex is null or DnsFormatException, $"konum {position}, değer {value}: {ex?.GetType().Name}");
            }
        }
    }
}
