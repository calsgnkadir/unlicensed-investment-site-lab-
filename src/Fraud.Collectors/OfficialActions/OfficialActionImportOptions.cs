namespace Fraud.Collectors.OfficialActions;

public sealed class OfficialActionImportOptions
{
    public const string SectionName = "OfficialActionImport";

    /// <summary>
    /// CSV dosyasının yolu. Göreli yol, programın çalıştığı klasöre göre çözülür.
    /// Dosya gizli veridir; yalnızca <c>data/private/</c> altında durur.
    /// </summary>
    public string FilePath { get; set; } = "data/private/phase0/engellenenler.csv";

    public string Delimiter { get; set; } = ",";

    /// <summary>
    /// Kaynak bağlantısının izin verilen ana bilgisayarları. Bağlantı bu adlardan biri
    /// ya da onların alt alan adı olmalıdır.
    /// </summary>
    public IList<string> AllowedSourceHosts { get; } = ["spk.gov.tr"];

    /// <summary>
    /// Kayıtlı alan adı bu listede olan satırlar reddedilir. Bültenlerde sosyal medya hesapları ve
    /// kısaltılmış bağlantılar da geçer; bunlar "engellenen alan adı" olarak kaydedilirse
    /// platformun tamamı yanlışlıkla etiketlenmiş olur. Bu satırlar elle ayrılmalıdır.
    /// Yapılandırmadan gelen değerler bu listeye eklenir.
    /// </summary>
    public IList<string> PlatformDomains { get; } =
    [
        "t.me",
        "telegram.me",
        "instagram.com",
        "facebook.com",
        "x.com",
        "twitter.com",
        "youtube.com",
        "youtu.be",
        "tiktok.com",
        "whatsapp.com",
        "wa.me",
        "linkedin.com",
        "bit.ly",
        "tinyurl.com",
        "cutt.ly",
        "goo.gl",
    ];
}
