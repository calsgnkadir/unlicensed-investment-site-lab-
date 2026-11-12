using Fraud.Collectors.Http;

namespace Fraud.Collectors.CrtSh;

/// <summary>
/// crt.sh erişim ayarları. Resmi bir sınır yazılı değil; 2020'deki tek resmi açıklama dakikada 60 istek diyor.
/// Biz bunun çok altında, 5 saniyede 1 istekle çalışırız (docs/DATA_SOURCES.md, Bölüm 2).
/// </summary>
public sealed class CrtShOptions : SourceHttpOptions
{
    public const string SectionName = "CrtSh";

    public static readonly Uri BaseUri = new("https://crt.sh/");

    public CrtShOptions()
    {
        MinRequestInterval = TimeSpan.FromSeconds(5);
        MaxResponseBytes = 32L * 1024 * 1024;
        AttemptTimeout = TimeSpan.FromMinutes(2);
        RetryBaseDelay = TimeSpan.FromSeconds(10);
    }
}
