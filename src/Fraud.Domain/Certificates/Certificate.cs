namespace Fraud.Domain.Certificates;

/// <summary>
/// crt.sh üzerinden görülen bir sertifika (tablo: certificates). Anahtar crt.sh kimliğidir;
/// aynı sertifika ikinci kez gelirse yeni satır açılmaz.
/// </summary>
/// <remarks>
/// Kaynağın kendi tarihleri (<see cref="NotBefore"/>, <see cref="LoggedAt"/>) ile bu sistemin
/// sertifikayı ilk gördüğü an (<see cref="ObservedAt"/>) ayrı tutulur.
/// Kişi verisi saklanmaz: Sertifika adları yalnızca DNS adı biçimindeyse, issuer adı yalnızca
/// C, O, OU ve CN alanlarıyla ve e-posta içermiyorsa kaydedilir.
/// </remarks>
public sealed class Certificate
{
    // EF Core için.
    private Certificate()
    {
        SerialNumber = string.Empty;
        IssuerName = string.Empty;
        Names = [];
    }

    public long Id { get; private set; }

    /// <summary>crt.sh'teki kayıt numarası.</summary>
    public long CrtShId { get; private set; }

    public string SerialNumber { get; private set; }

    /// <summary>crt.sh'in CA kimliği. Issuer adından daha kararlı bir eşleştirme anahtarıdır.</summary>
    public int? IssuerCaId { get; private set; }

    /// <summary>Temizlenmiş issuer adı. Örnek: <c>C=US, O=Let's Encrypt, CN=R3</c>.</summary>
    public string IssuerName { get; private set; }

    /// <summary>Sertifikanın CN alanı; yalnızca DNS adı biçimindeyse dolu.</summary>
    public string? CommonName { get; private set; }

    /// <summary>
    /// crt.sh'in <c>name_value</c> alanındaki DNS adları. Sorguyla eşleşen adlardır; sertifikanın
    /// tüm SAN listesi olmayabilir. Başka bir alan adı sorgusunda yeni ad gelirse listeye eklenir.
    /// </summary>
    public string[] Names { get; private set; }

    public DateTimeOffset NotBefore { get; private set; }

    public DateTimeOffset NotAfter { get; private set; }

    /// <summary>crt.sh'in kaydı loga girmiş olarak gördüğü an (kaynağın tarihi).</summary>
    public DateTimeOffset? LoggedAt { get; private set; }

    /// <summary>
    /// Ön sertifika mı. crt.sh JSON çıktısı bunu söylemez; bu yüzden şimdilik boştur (bilinmiyor).
    /// Sertifika logu istemcisi (Hafta 3) doldurabilir.
    /// </summary>
    public bool? IsPrecertificate { get; private set; }

    /// <summary>Bu sistemin sertifikayı ilk gördüğü an.</summary>
    public DateTimeOffset ObservedAt { get; private set; }
}
