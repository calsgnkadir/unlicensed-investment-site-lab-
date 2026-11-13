using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Gelen DNS paketini <see cref="DnsMessage"/> nesnesine çevirir (RFC 1035 Bölüm 4.1).
/// </summary>
/// <remarks>
/// <para>
/// Gelen paket güvenilmez veridir (Kural 10). Ayrıştırıcı katıdır: Biçime uymayan her şeyde durur ve
/// <see cref="DnsFormatException"/> fırlatır. Toplayıcı bunu <c>Error:Malformed</c> diye yazar; bozuk paket
/// asla "kayıt yok" sonucuna dönüşmez.
/// </para>
/// <para>
/// Katılık kuralları: Başlık tam 12 bayt; soru sayısı en fazla 1 (RFC 9619); bölüm başına en fazla 256 kayıt;
/// her RDLENGTH paketin içinde kalmalı ve kaydın verisi tam o uzunlukta bitmeli; son kayıttan sonra artık bayt
/// kalmamalı.
/// </para>
/// </remarks>
public static class DnsMessageParser
{
    /// <summary>
    /// Paketi ayrıştırır. Başarısızlıkta YALNIZCA <see cref="DnsFormatException"/> fırlatır.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Sözleşme: Güvenilmez paketten çıkan her istisna tek türe (DnsFormatException) çevrilir; gözden kaçan bir okuma hatası toplayıcıyı durdurmamalı.")]
    public static DnsMessage Parse(byte[] packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        try
        {
            return ParseCore(packet);
        }
        catch (DnsFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Buraya düşmemesi gerekir: Bütün okumalar sınır denetimli. Yine de bir hata gözden kaçtıysa
            // (ör. IPAddress kurucusu) dışarıya tek tür çıkar. Fuzz testi bunu sınar.
            throw new DnsFormatException("Paket okunurken beklenmeyen hata.", ex);
        }
    }

    private static DnsMessage ParseCore(byte[] packet)
    {
        if (packet.Length < DnsWire.HeaderLength)
        {
            throw new DnsFormatException("Paket başlıktan kısa.");
        }

        if (packet.Length > DnsWire.MaxMessageLength)
        {
            throw new DnsFormatException("Paket 65535 baytı aşıyor.");
        }

        var reader = new DnsMessageReader(packet);

        // --- Başlık (RFC 1035 Bölüm 4.1.1): altı tane 16 bitlik alan. ---
        var header = new DnsHeader(
            Id: reader.ReadUInt16(),
            Flags: reader.ReadUInt16(),
            QuestionCount: reader.ReadUInt16(),
            AnswerCount: reader.ReadUInt16(),
            AuthorityCount: reader.ReadUInt16(),
            AdditionalCount: reader.ReadUInt16());

        // Sayaçlara güvenmeden önce makul mü diye bakılır. 65535 kayıt bildiren 40 baytlık paket, okuyucuyu
        // gereksiz yere döndürmesin. RFC 9619: Bir pakette birden fazla soru geçersizdir.
        if (header.QuestionCount > 1)
        {
            throw new DnsFormatException("Birden fazla soru (RFC 9619).");
        }

        if (header.AnswerCount > DnsWire.MaxRecordsPerSection
            || header.AuthorityCount > DnsWire.MaxRecordsPerSection
            || header.AdditionalCount > DnsWire.MaxRecordsPerSection)
        {
            throw new DnsFormatException("Kayıt sayısı sınırı aşıyor.");
        }

        // --- Soru bölümü (Bölüm 4.1.2): ad + 2 bayt tür + 2 bayt sınıf. ---
        var questions = new List<DnsQuestion>(header.QuestionCount);
        for (var i = 0; i < header.QuestionCount; i++)
        {
            questions.Add(new DnsQuestion(reader.ReadName(), reader.ReadUInt16(), reader.ReadUInt16()));
        }

        // --- Cevap, yetki ve ek bölümleri: hepsi aynı kaynak kaydı biçiminde (Bölüm 4.1.3). ---
        var answers = ReadSection(reader, header.AnswerCount);
        var authority = ReadSection(reader, header.AuthorityCount);
        var additional = ReadSection(reader, header.AdditionalCount);

        // Başlığın bildirdiği her şey okundu; geride bayt kalmışsa paket başlıkla tutarsızdır.
        if (reader.Remaining != 0)
        {
            throw new DnsFormatException("Son kayıttan sonra fazladan bayt var.");
        }

        return new DnsMessage(header, questions, answers, authority, additional);
    }

    private static List<DnsResourceRecord> ReadSection(DnsMessageReader reader, int count)
    {
        var records = new List<DnsResourceRecord>(count);
        for (var i = 0; i < count; i++)
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    /// <summary>
    /// Bir kaynak kaydı okur. Düzen: NAME, TYPE (2), CLASS (2), TTL (4), RDLENGTH (2), RDATA (RDLENGTH bayt).
    /// </summary>
    private static DnsResourceRecord ReadRecord(DnsMessageReader reader)
    {
        var name = reader.ReadName();
        var type = reader.ReadUInt16();
        var @class = reader.ReadUInt16();
        var ttl = NormalizeTtl(reader.ReadUInt32());
        var dataLength = reader.ReadUInt16();

        // RDATA paketin içinde mi? Değilse burada durulur; aşağıdaki okumalar paketin dışına taşamaz.
        if (dataLength > reader.Remaining)
        {
            throw new DnsFormatException("RDLENGTH paketin sonunu aşıyor.");
        }

        var dataStart = reader.Position;
        var dataEnd = dataStart + dataLength;
        string? value = null;
        uint? soaMinimum = null;

        switch ((DnsRecordType)type)
        {
            case DnsRecordType.A:
                // RFC 1035 Bölüm 3.4.1: 4 baytlık IPv4 adresi.
                RequireLength(dataLength, 4);
                value = new IPAddress(reader.ReadBytes(4)).ToString();
                break;

            case DnsRecordType.AAAA:
                // RFC 3596 Bölüm 2.2: 16 baytlık IPv6 adresi.
                RequireLength(dataLength, 16);
                value = new IPAddress(reader.ReadBytes(16)).ToString();
                break;

            case DnsRecordType.CNAME:
            case DnsRecordType.NS:
                // RFC 1035 Bölüm 3.3.1 ve 3.3.11: RDATA tek bir addır (sıkıştırılmış olabilir).
                value = reader.ReadName();
                break;

            case DnsRecordType.SOA:
                // RFC 1035 Bölüm 3.3.13: MNAME, RNAME, sonra beş tane 32 bitlik sayı.
                // MNAME (birincil sunucu) ve RNAME (yöneticinin e-posta adresi!) okunup atılır: RNAME kişi verisidir
                // (Kural 5), ikisine de ihtiyacımız yok. Okumak yine gerekli, çünkü biçimin doğru olduğunu ve
                // sayıların nerede başladığını ancak böyle biliriz.
                reader.ReadName();
                reader.ReadName();
                reader.Skip(16); // SERIAL, REFRESH, RETRY, EXPIRE
                soaMinimum = NormalizeTtl(reader.ReadUInt32());
                break;

            default:
                // Bilinmeyen tür: İçeriği yorumlanmaz, yalnızca atlanır. Kayıt tür numarasıyla tutulur.
                reader.Skip(dataLength);
                break;
        }

        // Kaydın verisi tam RDLENGTH kadar olmalı. Ad okuyan türlerde ad, RDATA'nın içinde bitmiş olmalı.
        if (reader.Position != dataEnd)
        {
            throw new DnsFormatException("Kayıt verisi RDLENGTH ile uyuşmuyor.");
        }

        return new DnsResourceRecord(name, type, @class, ttl, value, soaMinimum);
    }

    private static void RequireLength(int actual, int expected)
    {
        if (actual != expected)
        {
            throw new DnsFormatException("Adres kaydının uzunluğu yanlış.");
        }
    }

    /// <summary>RFC 2181 Bölüm 8: En üst biti 1 olan TTL (2^31 ve üstü) 0 sayılır.</summary>
    private static uint NormalizeTtl(uint ttl) => ttl > DnsWire.MaxTtl ? 0 : ttl;
}
