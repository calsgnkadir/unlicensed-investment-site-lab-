namespace Fraud.Collectors.Dns;

/// <summary>
/// DNS kayıt türleri (RFC 1035 Bölüm 3.2.2; AAAA için RFC 3596 Bölüm 2.1). Yalnızca kullandıklarımız.
/// </summary>
public enum DnsRecordType
{
    A = 1,
    NS = 2,
    CNAME = 5,
    SOA = 6,
    AAAA = 28,
}

/// <summary>
/// Cevap kodları (RFC 1035 Bölüm 4.1.1, başlıktaki RCODE alanı). EDNS kullanmadığımız için 4 bit yeterli.
/// </summary>
public enum DnsRcode
{
    NoError = 0,
    FormErr = 1,
    ServFail = 2,
    NxDomain = 3,
    NotImp = 4,
    Refused = 5,
}

/// <summary>
/// Paket biçimiyle ilgili sabitler ve sınırlar. Sınırların hepsi RFC'den ya da "saldırgan paket bizi yormasın"
/// kaygısından gelir; hangisinin hangisi olduğu yanında yazılı.
/// </summary>
public static class DnsWire
{
    /// <summary>Başlık her zaman 12 bayttır: ID, bayraklar ve dört sayaç, her biri 2 bayt (RFC 1035 Bölüm 4.1.1).</summary>
    public const int HeaderLength = 12;

    /// <summary>Sınıf IN (internet). Başka sınıf sormuyoruz (RFC 1035 Bölüm 3.2.4).</summary>
    public const ushort ClassIn = 1;

    /// <summary>Bir etiket en fazla 63 bayt olabilir; uzunluk baytının üst iki biti başka iş için ayrılmıştır (RFC 1035 Bölüm 2.3.4).</summary>
    public const int MaxLabelLength = 63;

    /// <summary>
    /// Bir ad, paketteki haliyle (uzunluk baytları ve sondaki 0 dahil) en fazla 255 bayt olabilir (RFC 1035 Bölüm 2.3.4).
    /// </summary>
    public const int MaxNameWireLength = 255;

    /// <summary>
    /// EDNS olmadan UDP cevabı en fazla 512 bayttır (RFC 1035 Bölüm 4.2.1). Sığmazsa sunucu TC bitini koyar ve biz TCP'ye geçeriz.
    /// Sorgularımıza EDNS (OPT kaydı) bilerek eklemiyoruz; böylece kesik cevap yolu gerçekten çalışır.
    /// </summary>
    public const int ClassicUdpPayload = 512;

    /// <summary>TCP'de uzunluk öneki 2 bayttır; bir DNS mesajı en fazla 65535 bayt olabilir (RFC 1035 Bölüm 4.2.2).</summary>
    public const int MaxMessageLength = 65535;

    /// <summary>
    /// Bir ad okunurken izlenebilecek en fazla sıkıştırma işaretçisi. RFC bir sınır koymaz; 255 baytlık bir ad
    /// en fazla 127 etiket taşıyabildiği için meşru bir paket bu sayıya hiç yaklaşmaz. Döngüye karşı ikinci kilit.
    /// </summary>
    public const int MaxCompressionJumps = 32;

    /// <summary>
    /// Bölüm başına en fazla kayıt. RFC sınır koymaz; 2 baytlık sayaç 65535'e kadar gidebilir. Bizim sorularımızın
    /// cevabı birkaç kayıttır; çok büyük sayaç ya bozuk ya da kötü niyetli pakettir.
    /// </summary>
    public const int MaxRecordsPerSection = 256;

    /// <summary>
    /// TTL 32 bitlik alan ama RFC 2181 Bölüm 8, en üst biti 1 olan değerin 0 sayılmasını ister.
    /// </summary>
    public const uint MaxTtl = int.MaxValue;

    // Başlıktaki 16 bitlik bayrak alanı (RFC 1035 Bölüm 4.1.1):
    //
    //   bit: 15  14-11   10  9   8   7   6-4  3-0
    //        QR  OPCODE  AA  TC  RD  RA  Z    RCODE
    //
    // Aşağıdaki maskeler bu alanın içindeki bitleri seçer.

    /// <summary>QR: 0 = sorgu, 1 = cevap.</summary>
    public const ushort FlagQr = 0x8000;

    /// <summary>OPCODE: 4 bit. 0 = standart sorgu (QUERY). Biz yalnızca 0 göndeririz.</summary>
    public const ushort OpcodeMask = 0x7800;

    /// <summary>AA: Cevap yetkili sunucudan mı. Özyinelemeli çözücüden gelen cevapta genelde 0.</summary>
    public const ushort FlagAa = 0x0400;

    /// <summary>TC: Cevap kesildi, tamamı UDP'ye sığmadı. TCP ile yeniden sorulmalı.</summary>
    public const ushort FlagTc = 0x0200;

    /// <summary>RD: "Özyineleme istiyorum." Çözücüden cevabı bizim yerimize bulmasını isteriz.</summary>
    public const ushort FlagRd = 0x0100;

    /// <summary>RA: Sunucu özyineleme yapabiliyor.</summary>
    public const ushort FlagRa = 0x0080;

    /// <summary>RCODE: Alt 4 bit.</summary>
    public const ushort RcodeMask = 0x000F;
}

/// <summary>
/// Paket biçimi hatası. Ayrıştırıcının dışarı fırlattığı TEK istisna türü budur; toplayıcı bunu
/// <c>Error:Malformed</c> olarak yazar. Mesaj sabit metindir, paketten veri taşımaz.
/// </summary>
public sealed class DnsFormatException : Exception
{
    public DnsFormatException()
        : base("DNS paketi biçime uymuyor.")
    {
    }

    public DnsFormatException(string message)
        : base(message)
    {
    }

    public DnsFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
