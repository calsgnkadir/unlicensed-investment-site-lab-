using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Fraud.Collectors.Dns;

/// <summary>Gönderilmeye hazır bir sorgu: işlem kimliği, sorulan ad ve tür, paketin baytları.</summary>
/// <param name="Id">İşlem kimliği. Cevap aynı kimliği taşımalı.</param>
/// <param name="Name">Sorulan ad, <see cref="DnsName.ToQueryName"/> biçiminde. Cevaptaki soru bununla karşılaştırılır.</param>
public sealed record DnsQuery(ushort Id, string Name, DnsRecordType Type, byte[] Packet);

/// <summary>
/// RFC 1035 Bölüm 4.1'e göre tek soruluk bir sorgu paketi kurar.
/// </summary>
/// <remarks>
/// Paket düzeni (her kutu 2 bayt, sayılar ağ bayt sırasında, yani büyük uç önce):
/// <code>
/// +----+-------+---------+---------+---------+---------+------------------+-------+--------+
/// | ID | FLAGS | QDCOUNT | ANCOUNT | NSCOUNT | ARCOUNT | QNAME (değişken) | QTYPE | QCLASS |
/// +----+-------+---------+---------+---------+---------+------------------+-------+--------+
///  \_______________ başlık, 12 bayt ________________/  \______ soru bölümü (Bölüm 4.1.2) ____/
/// </code>
/// Bayraklarda yalnızca RD (özyineleme iste) 1'dir. QR = 0 (bu bir sorgu), OPCODE = 0 (standart sorgu).
/// EDNS (OPT kaydı, RFC 6891) bilerek eklenmez: 512 baytı aşan cevap kesilir ve TCP yolu da sınanır.
/// </remarks>
public static class DnsQueryBuilder
{
    /// <summary>
    /// Rastgele 16 bitlik işlem kimliği. <see cref="Random"/> değil <see cref="RandomNumberGenerator"/> kullanılır:
    /// Kimlik tahmin edilebilirse yoldaki biri sahte cevabı doğru kimlikle önceden gönderebilir (RFC 5452 Bölüm 4.3, 9.2).
    /// Kaynak portun da rastgele olması (UDP taşımasında port 0) tahmin edilmesi gereken bit sayısını artırır.
    /// </summary>
    public static ushort NewId() => (ushort)RandomNumberGenerator.GetInt32(0, 65536);

    /// <summary>Sorgu paketini kurar. Ad geçersizse <see cref="ArgumentException"/> fırlatır.</summary>
    /// <param name="id">İşlem kimliği. DoH için 0 verilir (RFC 8484 Bölüm 4.1).</param>
    public static DnsQuery Build(ushort id, string name, DnsRecordType type)
    {
        var queryName = DnsName.ToQueryName(name);

        // Boyut önceden belli: 12 bayt başlık + ad + 2 bayt QTYPE + 2 bayt QCLASS.
        var packet = new byte[DnsWire.HeaderLength + DnsName.WireLength(queryName) + 4];
        var span = packet.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span[0..], id);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], DnsWire.FlagRd);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1); // QDCOUNT: tek soru
        // ANCOUNT, NSCOUNT, ARCOUNT (bayt 6-11) sıfır kalır; dizi zaten sıfırla başlar.

        var position = DnsWire.HeaderLength;
        position += DnsName.Write(queryName, span[position..]);
        BinaryPrimitives.WriteUInt16BigEndian(span[position..], (ushort)type);
        BinaryPrimitives.WriteUInt16BigEndian(span[(position + 2)..], DnsWire.ClassIn);

        return new DnsQuery(id, queryName, type, packet);
    }
}
