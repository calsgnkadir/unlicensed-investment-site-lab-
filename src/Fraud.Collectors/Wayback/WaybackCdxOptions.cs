using Fraud.Collectors.Http;

namespace Fraud.Collectors.Wayback;

/// <summary>
/// Wayback CDX erişim ayarları. Resmi sınır yok; topluluk kaynakları dakikada ~15 isteği aşınca 429/503
/// geldiğini söylüyor. DATA_SOURCES Bölüm 3 önerisiyle dakikada 12 istek (5 saniyede 1) kullanılır.
/// </summary>
public sealed class WaybackCdxOptions : SourceHttpOptions
{
    public const string SectionName = "WaybackCdx";

    public static readonly Uri BaseUri = new("https://web.archive.org/cdx/search/cdx");

    /// <summary>CDX'in döndürdüğü alanlar. Sırası önemli değil; başlık satırından okunur.</summary>
    public const string Fields = "timestamp,original,statuscode,mimetype,digest";

    public WaybackCdxOptions()
    {
        MinRequestInterval = TimeSpan.FromSeconds(5);
        MaxResponseBytes = 8L * 1024 * 1024;
        AttemptTimeout = TimeSpan.FromMinutes(1);
        RetryBaseDelay = TimeSpan.FromSeconds(10);
    }

    /// <summary>Sayfa başına satır (<c>limit</c>).</summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>Alan adı başına en fazla sayfa. Aşılırsa sonuç "eksik" (truncated) işaretlenir.</summary>
    public int MaxPages { get; set; } = 10;

    /// <summary>
    /// İsteğe bağlı <c>collapse</c> değeri (ör. <c>timestamp:8</c> = adres başına günde bir kayıt).
    /// Boşsa ham liste alınır.
    /// </summary>
    public string? Collapse { get; set; }
}
