using System.Text.RegularExpressions;
using Fraud.Collectors.Http;

namespace Fraud.Collectors.RipeStat;

/// <summary>
/// RIPEstat Data API ayarları (bölüm: <c>RipeStat</c>). Resmi bir hız sınırı yok; günde 1000'den fazla istek için
/// kayıt isteniyor, aynı IP'den en fazla 8 eşzamanlı istek. Biz tek bağlantı ve saniyede en fazla 1 istekle çalışırız
/// (docs/research/HAFTA4_AG_KAYNAKLARI.md Bölüm 1.1).
/// </summary>
public sealed partial class RipeStatOptions : SourceHttpOptions
{
    public const string SectionName = "RipeStat";

    /// <summary>İsteklerin gidebileceği tek ana bilgisayar.</summary>
    public const string Host = "stat.ripe.net";

    public static readonly Uri BaseUri = new("https://" + Host + "/data/network-info/data.json");

    public RipeStatOptions()
    {
        MinRequestInterval = TimeSpan.FromSeconds(1);
        MaxResponseBytes = 64 * 1024;
        AttemptTimeout = TimeSpan.FromSeconds(30);
        RetryBaseDelay = TimeSpan.FromSeconds(5);
        CacheTtl = TimeSpan.FromDays(7);
    }

    /// <summary>
    /// RIPEstat'ın düzenli kullanımda istediği uygulama adı. Harf, rakam, tire ve alt çizgi. Kişi adı veya e-posta değil.
    /// </summary>
    public string SourceApp { get; set; } = "fraudlab-research";

    /// <summary>IP'ler bu süre içindeki DNS ölçümlerinden alınır.</summary>
    public TimeSpan DnsLookback { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// ASN -> CDN veya bulut sağlayıcısının kısa adı. Yapılandırmadaki değerler varsayılanlara eklenir veya onları ezer.
    /// </summary>
    /// <remarks>
    /// AS16509 Amazon'un genel ASN'idir (EC2 dahil), yalnızca CloudFront değil; bu yüzden "aws" yazılır, "cdn" değil.
    /// Google (15169) ve Microsoft (8075) da hem CDN hem bulut barındırma taşır.
    /// </remarks>
    public IDictionary<long, string> CdnAsns { get; } = new Dictionary<long, string>
    {
        [13335] = "cloudflare",
        [20940] = "akamai",
        [16625] = "akamai",
        [54113] = "fastly",
        [16509] = "aws",
        [15169] = "google",
        [8075] = "microsoft",
    };

    public void Validate()
    {
        if (!SourceAppPattern().IsMatch(SourceApp ?? string.Empty))
        {
            throw new InvalidOperationException("RipeStat:SourceApp yalnızca harf, rakam, tire ve alt çizgi içerebilir (en fazla 64).");
        }

        if (MinRequestInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException("RipeStat:MinRequestInterval 1 saniyeden kısa olamaz.");
        }
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex SourceAppPattern();
}
