namespace Fraud.Domain.Network;

/// <summary>DNS sorgusunun çözücüye hangi yoldan gittiği.</summary>
public enum DnsTransport
{
    /// <summary>Düz UDP, port 53 (RFC 1035 Bölüm 4.2.1). Yol üzerindeki herkes görebilir ve değiştirebilir.</summary>
    Udp = 1,

    /// <summary>Düz TCP, port 53, 2 baytlık uzunluk önekiyle (RFC 1035 Bölüm 4.2.2, RFC 7766).</summary>
    Tcp = 2,

    /// <summary>HTTPS içinde DNS (RFC 8484). Servis sağlayıcı içeriği göremez ve değiştiremez.</summary>
    Doh = 3,
}

/// <summary>
/// Bir DNS sorgusunun sonucu. Dört durum bilerek ayrıdır: "Hata" hiçbir zaman "kayıt yok" (<see cref="Empty"/>)
/// veya "alan adı yok" (<see cref="NxDomain"/>) gibi yazılmaz.
/// </summary>
public enum DnsOutcome
{
    /// <summary>NOERROR ve en az bir cevap kaydı (CNAME zinciri dahil).</summary>
    Success = 1,

    /// <summary>NOERROR ama cevap bölümü boş (RFC 2308'deki NODATA). Ad var, bu türde kaydı yok.</summary>
    Empty = 2,

    /// <summary>NXDOMAIN (RCODE 3): Çözücü "bu ad yok" dedi.</summary>
    NxDomain = 3,

    /// <summary>Sonuç bilinmiyor. Ayrıntı <see cref="DnsErrorKind"/> içindedir.</summary>
    Error = 4,
}

/// <summary>DNS hatasının türü. Hata olmayan sonuçlarda <see cref="None"/>.</summary>
public enum DnsErrorKind
{
    None = 0,

    /// <summary>Süre içinde (tekrar deneme dahil) geçerli cevap gelmedi.</summary>
    Timeout = 1,

    /// <summary>Bağlantı kurulamadı veya kesildi (TCP, DoH) ya da soket hatası (UDP).</summary>
    NetworkError = 2,

    /// <summary>Paket RFC 1035 biçimine uymuyor (sınır dışı okuma, işaretçi döngüsü, uzunluk taşması ...).</summary>
    Malformed = 3,

    /// <summary>Cevaptaki işlem kimliği sorgununkiyle aynı değil (TCP ve DoH; UDP'de bu paket yok sayılır).</summary>
    IdMismatch = 4,

    /// <summary>Cevaptaki soru bölümü bizim sorumuz değil (ad, tür veya sınıf farklı).</summary>
    QuestionMismatch = 5,

    /// <summary>RCODE 2, SERVFAIL: Çözücü cevabı bulamadı.</summary>
    ServerFailure = 6,

    /// <summary>RCODE 5, REFUSED: Çözücü sorguyu cevaplamayı reddetti.</summary>
    Refused = 7,

    /// <summary>Diğer RCODE değerleri (FORMERR, NOTIMP ...). Sayısal değer ayrıca saklanır.</summary>
    OtherRcode = 8,

    /// <summary>DoH: HTTP durum kodu 200 değil ya da içerik türü <c>application/dns-message</c> değil.</summary>
    HttpError = 9,

    /// <summary>Cevap boyut sınırını aştı.</summary>
    ResponseTooLarge = 10,

    /// <summary>İş durduruldu (uygulama kapanıyor).</summary>
    Cancelled = 11,

    /// <summary>
    /// NOERROR, cevap bölümü dolu ama içindeki kayıtların hiçbiri sorumuza ait değil (başka ad veya başka tür).
    /// Tutarsız bir cevaptır; "kayıt yok" (<see cref="DnsOutcome.Empty"/>) diye yazılmaz.
    /// </summary>
    UnrelatedAnswer = 12,

    /// <summary>Kodda beklenmeyen bir istisna. Sorgunun ölçümü kaybolmasın diye hata satırı olarak yazılır.</summary>
    Unexpected = 13,
}

/// <summary>
/// Ağ ölçümünün hangi alan adı grubuna yapılacağı. Bugün yalnızca resmi liste var; aday ve kontrol grubu
/// eklendiğinde buraya yeni değer gelir ve hedef sorgusu (NetworkTargetQuery) genişletilir.
/// </summary>
public enum DomainSet
{
    /// <summary>official_actions tablosundaki alan adları (SPK ve USOM listeleri).</summary>
    OfficialActions = 1,

    /// <summary>control_members tablosundaki kontrol grubu üyeleri. Adları hiçbir çıktıya yazılmaz (Kural 4).</summary>
    ControlMembers = 2,
}
