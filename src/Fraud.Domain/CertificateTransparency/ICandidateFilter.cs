namespace Fraud.Domain.CertificateTransparency;

/// <summary>
/// Sertifika logundan okunan bir kaydın saklanıp saklanmayacağına karar verir.
/// </summary>
/// <remarks>
/// Bu yalnızca bir SAKLAMA filtresidir: Günde milyonlarca kaydın hepsini saklamamak için kaba bir eleme yapar.
/// Bir sinyal, puan ya da "şüpheli" kararı değildir; filtreden geçen kayıt hakkında hiçbir yargı içermez.
/// Uygulamalar saf ve iş parçacığı güvenli olmalıdır (aynı örnek paralel çağrılır).
/// </remarks>
public interface ICandidateFilter
{
    /// <summary>
    /// Sertifikadaki temizlenmiş DNS adlarına bakar. Eşleşen filtre terimlerini döner;
    /// boş liste "saklama" demektir.
    /// </summary>
    IReadOnlyList<string> Match(IReadOnlyList<string> dnsNames);
}
