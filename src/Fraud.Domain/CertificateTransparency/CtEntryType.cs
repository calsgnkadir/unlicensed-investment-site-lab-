namespace Fraud.Domain.CertificateTransparency;

/// <summary>RFC 6962 <c>LogEntryType</c>. Sayı değerleri logdaki değerle aynıdır.</summary>
public enum CtEntryType
{
    /// <summary>Son sertifika (<c>x509_entry</c>).</summary>
    X509 = 0,

    /// <summary>Ön sertifika (<c>precert_entry</c>). Sertifika henüz verilmeden loga yazılır.</summary>
    Precert = 1,
}
