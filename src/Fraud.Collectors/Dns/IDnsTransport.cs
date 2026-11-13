using Fraud.Domain.Network;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Bir sorgu paketini çözücüye götürüp cevap paketini getiren yol (UDP, TCP veya DoH).
/// Paketin içine bakmaz; yalnızca baytları taşır. Ayrıştırma ve doğrulama <see cref="DnsQueryClient"/> içindedir.
/// </summary>
public interface IDnsTransport
{
    DnsTransport Kind { get; }

    /// <summary>
    /// Tek bir gönder-al. Hata istisna olarak değil, sonuç olarak döner (zaman aşımı, ağ hatası, HTTP hatası).
    /// Yalnızca dışarıdan iptal edilirse <see cref="OperationCanceledException"/> fırlatır.
    /// </summary>
    Task<DnsExchangeResult> ExchangeAsync(DnsResolver resolver, DnsQuery query, CancellationToken cancellationToken);
}

/// <summary>Taşıma katmanının sonucu. <see cref="Response"/> yalnızca hata yoksa doludur.</summary>
/// <param name="HttpStatus">Yalnızca DoH'ta: HTTP durum kodu.</param>
public sealed record DnsExchangeResult(byte[]? Response, DnsErrorKind Error, int? HttpStatus)
{
    public static DnsExchangeResult Ok(byte[] response, int? httpStatus = null) => new(response, DnsErrorKind.None, httpStatus);

    public static DnsExchangeResult Failed(DnsErrorKind error, int? httpStatus = null) => new(null, error, httpStatus);
}
