using System.Security.Cryptography;
using System.Text;
using Fraud.Collectors.Collection;

namespace Fraud.Collectors.Wayback;

/// <summary>Bir CDX satırı (temizlenmiş). İçerik değil, yalnızca arşiv dizini bilgisi.</summary>
public sealed record CdxCapture(
    string Timestamp,
    DateTimeOffset CapturedAt,
    string OriginalUrl,
    bool QueryRemoved,
    int? StatusCode,
    string? MimeType,
    string? Digest)
{
    /// <summary>Tekrarı önleyen anahtar: zaman damgası, temizlenmiş adres ve özetin SHA-256 değeri (küçük harf onaltılık).</summary>
    public string CaptureKey
    {
        get
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Timestamp + "\n" + OriginalUrl + "\n" + Digest));
            return Convert.ToHexStringLower(bytes);
        }
    }
}

public interface IWaybackCdxClient
{
    /// <summary>
    /// <c>matchType=domain</c> ile alan adı ve alt alan adlarının arşiv kayıtları. Sayfalar
    /// <c>resumeKey</c> ile izlenir. Hata olursa sonuç <c>Error</c> olur; okunmuş sayfalar korunur.
    /// </summary>
    Task<SourceResult<CdxCapture>> ListCapturesAsync(string host, CancellationToken cancellationToken);
}
