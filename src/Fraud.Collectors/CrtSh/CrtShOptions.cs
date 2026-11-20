using Fraud.Collectors.Http;

namespace Fraud.Collectors.CrtSh;

/// <summary>
/// crt.sh erişim ayarları. İşletmecinin Haziran 2023 açıklaması IP başına dakikada 5 istek diyor (en az 12 sn aralık).
/// Varsayılan 15 sn (pay bırakır); yapılandırma 12 sn'nin altına izin vermez (Değişiklik kaydı 2026-11-20, madde 2;
/// KONTROL_LISTESI_KURULUM.md 5).
/// </summary>
public sealed class CrtShOptions : SourceHttpOptions
{
    public const string SectionName = "CrtSh";

    public static readonly Uri BaseUri = new("https://crt.sh/");

    public CrtShOptions()
    {
        MinRequestInterval = TimeSpan.FromSeconds(15);
        MaxResponseBytes = 32L * 1024 * 1024;
        AttemptTimeout = TimeSpan.FromMinutes(2);
        RetryBaseDelay = TimeSpan.FromSeconds(10);
    }

    /// <summary>İşletmecinin sınırı: dakikada 5 istek.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(12);

    /// <summary>Aralık 12 sn'den kısaysa başlatmaz.</summary>
    public void Validate()
    {
        if (MinRequestInterval < MinimumInterval)
        {
            throw new InvalidOperationException("CrtSh:MinRequestInterval en az 12 saniye olmalı (crt.sh: IP başına dakikada 5 istek).");
        }
    }
}
