using Fraud.Collectors.Http;

namespace Fraud.Collectors.InternetDb;

/// <summary>
/// Shodan InternetDB ayarları (bölüm: <c>InternetDb</c>). Anahtar istemez; ticari olmayan kullanım için ücretsiz,
/// veri haftalık güncellenir. Resmi kitap yüksek patlama sınırı yazsa da biz saniyede en fazla 1 istek göndeririz.
/// </summary>
public sealed class InternetDbOptions : SourceHttpOptions
{
    public const string SectionName = "InternetDb";

    /// <summary>İsteklerin gidebileceği tek ana bilgisayar.</summary>
    public const string Host = "internetdb.shodan.io";

    public static readonly Uri BaseUri = new("https://" + Host + "/");

    public InternetDbOptions()
    {
        MinRequestInterval = TimeSpan.FromSeconds(1);
        MaxResponseBytes = 256 * 1024;
        AttemptTimeout = TimeSpan.FromSeconds(30);
        RetryBaseDelay = TimeSpan.FromSeconds(5);
        // Veri haftalık güncellendiği için bir IP haftada bir sorulur.
        CacheTtl = TimeSpan.FromDays(7);
    }

    /// <summary>IP'ler bu süre içindeki DNS ölçümlerinden alınır.</summary>
    public TimeSpan DnsLookback { get; set; } = TimeSpan.FromDays(3);

    public void Validate()
    {
        if (MinRequestInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException("InternetDb:MinRequestInterval 1 saniyeden kısa olamaz.");
        }
    }
}
