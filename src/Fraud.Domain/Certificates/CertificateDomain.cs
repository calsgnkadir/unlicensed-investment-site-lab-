using Fraud.Domain.DomainNames;

namespace Fraud.Domain.Certificates;

/// <summary>
/// Hangi alan adı sorgusunun hangi sertifikayı getirdiği (tablo: certificate_domains).
/// Bir sertifika birden çok alan adı sorgusunda görünebilir; sertifikanın kendisi bir kez saklanır,
/// bağlantı her alan adı için ayrı satırdır.
/// </summary>
public sealed class CertificateDomain
{
    // EF Core için.
    private CertificateDomain()
    {
    }

    public long CertificateId { get; private set; }

    public Certificate Certificate { get; private set; } = null!;

    public long DomainId { get; private set; }

    public DomainName Domain { get; private set; } = null!;

    /// <summary>Bu bağlantının ilk görüldüğü an.</summary>
    public DateTimeOffset ObservedAt { get; private set; }
}
