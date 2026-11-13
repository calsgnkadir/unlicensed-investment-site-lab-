using System.Buffers.Binary;
using System.Text;
using Fraud.Collectors.Dns;

namespace Fraud.Integration.Tests.Dns;

/// <summary>
/// Testlerde DNS paketini bayt bayt elle kurmak için. Kodun kendi kurucusunu kullanmaz; böylece testler
/// ayrıştırıcıyı kendi yazdığı paketle değil, RFC'ye göre elle yazılmış paketle sınar.
/// </summary>
internal sealed class PacketWriter
{
    private readonly List<byte> _bytes = [];

    public int Position => _bytes.Count;

    public PacketWriter U8(int value)
    {
        _bytes.Add((byte)value);
        return this;
    }

    public PacketWriter U16(int value)
    {
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
        return this;
    }

    public PacketWriter U32(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        _bytes.AddRange(b.ToArray());
        return this;
    }

    public PacketWriter Bytes(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>Başlık: ID, bayraklar ve dört sayaç.</summary>
    public PacketWriter Header(int id, int flags, int qd, int an, int ns, int ar) =>
        U16(id).U16(flags).U16(qd).U16(an).U16(ns).U16(ar);

    /// <summary>Etiketleri sıkıştırmasız yazar; <paramref name="terminate"/> ise sona 00 koyar.</summary>
    public PacketWriter Labels(string dotted, bool terminate = true)
    {
        foreach (var label in dotted.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            U8(label.Length);
            _bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }

        return terminate ? U8(0) : this;
    }

    /// <summary>Sıkıştırma işaretçisi: 11 + 14 bitlik konum.</summary>
    public PacketWriter Pointer(int offset) => U8(0xC0 | (offset >> 8)).U8(offset & 0xFF);

    /// <summary>Kaynak kaydının ad sonrası kısmı: tür, sınıf, TTL. RDLENGTH ve RDATA ayrıca yazılır.</summary>
    public PacketWriter RecordMeta(DnsRecordType type, uint ttl, int @class = 1) => U16((int)type).U16(@class).U32(ttl);

    /// <summary>A kaydının RDLENGTH + RDATA kısmı.</summary>
    public PacketWriter AData(byte a, byte b, byte c, byte d) => U16(4).Bytes(a, b, c, d);

    /// <summary>RDLENGTH yerine geçici 0 yazar; konumu döner. Sonra <see cref="PatchLength"/> ile doldurulur.</summary>
    public int LengthPlaceholder()
    {
        var at = Position;
        U16(0);
        return at;
    }

    /// <summary>RDLENGTH'i, yer tutucudan bu yana yazılan bayt sayısıyla doldurur.</summary>
    public PacketWriter PatchLength(int at)
    {
        var length = Position - at - 2;
        _bytes[at] = (byte)(length >> 8);
        _bytes[at + 1] = (byte)length;
        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}

/// <summary>Sık kullanılan cevaplar.</summary>
internal static class Responses
{
    public const int QrRdRa = 0x8180; // QR=1, RD=1, RA=1, RCODE=0

    /// <summary>Soruyu tekrar eden ve verilen A adreslerini dönen cevap (ad işaretçiyle 12. bayta).</summary>
    public static byte[] A(DnsQuery query, int flags = QrRdRa, ushort? id = null, params byte[][] addresses)
    {
        var w = new PacketWriter().Header(id ?? query.Id, flags, 1, addresses.Length, 0, 0)
            .Labels(query.Name).U16((int)query.Type).U16(1);
        foreach (var address in addresses)
        {
            w.Pointer(12).RecordMeta(DnsRecordType.A, 300).AData(address[0], address[1], address[2], address[3]);
        }

        return w.ToArray();
    }

    /// <summary>Cevapsız (yalnızca başlık ve soru) cevap; RCODE <paramref name="rcode"/>.</summary>
    public static byte[] Rcode(DnsQuery query, int rcode, int extraFlags = 0) =>
        new PacketWriter().Header(query.Id, QrRdRa | rcode | extraFlags, 1, 0, 0, 0)
            .Labels(query.Name).U16((int)query.Type).U16(1).ToArray();

    /// <summary>NXDOMAIN ve yetki bölümünde SOA (TTL <paramref name="soaTtl"/>, MINIMUM <paramref name="minimum"/>).</summary>
    public static byte[] NxDomainWithSoa(DnsQuery query, uint soaTtl, uint minimum)
    {
        var w = new PacketWriter().Header(query.Id, QrRdRa | 3, 1, 0, 1, 0)
            .Labels(query.Name).U16((int)query.Type).U16(1);
        // SOA sahibi: sorulan adın son iki etiketi yerine basitçe "example" kökü.
        w.Labels("example").RecordMeta(DnsRecordType.SOA, soaTtl);
        var at = w.LengthPlaceholder();
        w.Labels("ns1.example").Labels("hostmaster.example")
            .U32(2026111301).U32(7200).U32(3600).U32(1209600).U32(minimum);
        return w.PatchLength(at).ToArray();
    }
}
