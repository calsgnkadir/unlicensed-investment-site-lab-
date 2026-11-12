using Fraud.Collectors.Collection;

namespace Fraud.Collectors.CrtSh;

/// <summary>crt.sh'ten bir alan adı ve alt alan adları için görülen sertifikalar (temizlenmiş).</summary>
public sealed record CrtShCertificate(
    long CrtShId,
    string SerialNumber,
    int? IssuerCaId,
    string IssuerName,
    string? CommonName,
    IReadOnlyList<string> Names,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    DateTimeOffset? LoggedAt);

public interface ICrtShClient
{
    /// <summary>
    /// <c>%.ALANADI</c> sorgusu. Hata durumunda sonuç <c>Error</c> olur; asla boş liste gibi dönmez.
    /// </summary>
    /// <param name="host">Normalleştirilmiş ASCII alan adı (domains.host).</param>
    Task<SourceResult<CrtShCertificate>> SearchAsync(string host, CancellationToken cancellationToken);
}
