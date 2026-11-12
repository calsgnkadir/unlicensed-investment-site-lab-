namespace Fraud.Infrastructure.Fetching;

public sealed class SafeFetcherOptions
{
    public const string SectionName = "SafeFetcher";

    /// <summary>
    /// İzinli ana bilgisayarlar. Tam eşleşme aranır; alt alan adları otomatik kabul edilmez.
    /// Pasif inceleme kuralı gereği şüpheli sitenin kendisi buraya eklenmez, yalnızca arşiv.
    /// Yapılandırmadan gelen değerler bu listeye eklenir.
    /// </summary>
    public IList<string> AllowedHosts { get; } = ["web.archive.org"];

    /// <summary>İzinli içerik türleri (parametresiz, küçük harf). Yalnızca metin.</summary>
    public IList<string> AllowedContentTypes { get; } = ["text/html", "text/plain", "application/xhtml+xml"];

    /// <summary>Sıkıştırma açıldıktan sonraki en büyük gövde boyutu.</summary>
    public long MaxBodyBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Yönlendirmeler dahil bütün çekme işinin süresi.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxRedirects { get; set; } = 5;

    public string UserAgent { get; set; } = "FraudLab/0.1 (passive research)";
}
