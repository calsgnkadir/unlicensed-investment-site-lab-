using System.Buffers.Binary;
using System.Text;
using Fraud.Collectors.Dns;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>
/// Saldırgan DNS paketi kurucu. Hiçbir denetim yapmaz: Sayaçlar, uzunluklar ve işaretçiler ne verilirse o yazılır.
/// Amaç, ayrıştırıcıya RFC 1035'e uymayan paketleri elle vermektir.
/// </summary>
public sealed class DnsPacket
{
    /// <summary>QR + RD + RA, RCODE 0.</summary>
    public const ushort ResponseFlags = 0x8180;

    private readonly List<byte> _bytes = [];

    public int Position => _bytes.Count;

    public DnsPacket U8(int value)
    {
        _bytes.Add((byte)value);
        return this;
    }

    public DnsPacket U16(int value)
    {
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)value);
        return this;
    }

    public DnsPacket U32(uint value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        _bytes.AddRange(span.ToArray());
        return this;
    }

    public DnsPacket Bytes(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    public DnsPacket Header(int id, int flags, int qd, int an, int ns, int ar) =>
        U16(id).U16(flags).U16(qd).U16(an).U16(ns).U16(ar);

    /// <summary>Etiketler ve sondaki kök baytı.</summary>
    public DnsPacket Name(string dotted) => Bytes(EncodeName(dotted));

    /// <summary>Etiketler, kök baytı YOK (ardından işaretçi gelecekse).</summary>
    public DnsPacket Labels(string dotted)
    {
        foreach (var label in dotted.Split('.'))
        {
            U8(label.Length).Bytes(Encoding.ASCII.GetBytes(label));
        }

        return this;
    }

    /// <summary>Sıkıştırma işaretçisi: 11 + 14 bit konum.</summary>
    public DnsPacket Jump(int offset) => U8(0xC0 | ((offset >> 8) & 0x3F)).U8(offset & 0xFF);

    /// <summary>Kaynak kaydının sahip addan sonraki kısmı: TYPE, CLASS, TTL, RDLENGTH ve RDATA.</summary>
    public DnsPacket RecordTail(int type, uint ttl, byte[] rdata, int? declaredLength = null, int @class = 1) =>
        U16(type).U16(@class).U32(ttl).U16(declaredLength ?? rdata.Length).Bytes(rdata);

    public byte[] ToArray() => [.. _bytes];

    public static byte[] EncodeName(string dotted)
    {
        var list = new List<byte>();
        foreach (var label in dotted.Split('.'))
        {
            list.Add((byte)label.Length);
            list.AddRange(Encoding.ASCII.GetBytes(label));
        }

        list.Add(0);
        return [.. list];
    }

    /// <summary>
    /// Sorgunun sorusunu aynen tekrar eden bir cevap kurar. Cevap kayıtları sahip ad olarak tam ad yazar (sıkıştırma yok).
    /// </summary>
    public static byte[] Answer(
        DnsQuery query,
        IReadOnlyList<(string Owner, int Type, byte[] Rdata)> answers,
        int flags = ResponseFlags,
        IReadOnlyList<(string Owner, int Type, byte[] Rdata)>? authority = null,
        IReadOnlyList<(string Owner, int Type, byte[] Rdata)>? additional = null,
        int? id = null)
    {
        authority ??= [];
        additional ??= [];
        var p = new DnsPacket().Header(id ?? query.Id, flags, 1, answers.Count, authority.Count, additional.Count);
        p.Bytes(query.Packet[DnsWire.HeaderLength..]);
        foreach (var section in new[] { answers, authority, additional })
        {
            foreach (var (owner, type, rdata) in section)
            {
                p.Name(owner).RecordTail(type, 300, rdata);
            }
        }

        return p.ToArray();
    }

    public static byte[] Ipv4(string address) => System.Net.IPAddress.Parse(address).GetAddressBytes();

    public static byte[] Ipv6(string address) => System.Net.IPAddress.Parse(address).GetAddressBytes();

    /// <summary>SOA RDATA: MNAME, RNAME (yönetici e-postası), beş sayı.</summary>
    public static byte[] Soa(string mname, string rname, uint minimum)
    {
        var p = new DnsPacket().Name(mname).Name(rname).U32(2026111301).U32(7200).U32(900).U32(1209600).U32(minimum);
        return p.ToArray();
    }
}
