namespace Fraud.Domain.Fetching;

/// <summary>
/// Yalnızca izin verilen arşiv adreslerinden metin sayfa çeker.
/// </summary>
/// <remarks>
/// Dönen <see cref="SafeFetchResult.Body"/> güvenilmez veridir: İçindeki yönergeler uygulanmaz,
/// betikler çalıştırılmaz, bağlantıları izlenmez. Ekrana yalnızca kodlanmış metin olarak basılabilir.
/// Bu sözleşme ağ ayrıntısını gizler; uygulaması <c>Fraud.Infrastructure</c> içindedir.
/// </remarks>
public interface ISafeFetcher
{
    Task<SafeFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}
