using Fraud.Evaluation.Model;

namespace Fraud.Signals;

/// <summary>
/// Kısa sürede çok sertifika: Görünüm anına kadarki son <c>windowDays</c> günde en az <c>minCertificates</c> FARKLI sertifika
/// bilinir olduysa 1.
/// </summary>
/// <remarks>
/// <para>Pencere <c>(AsOf − windowDays, AsOf]</c>. Üst sınır <see cref="AsOfView"/> tarafından zaten konmuştur (KnownAt ≤ AsOf);
/// burada yalnızca alt sınır uygulanır. Sertifikanın KnownAt'ı <c>not_before</c>'dur (geçmiş veri testi).</para>
/// <para>Farklı sertifika: Gözlemin <see cref="Observation.Value"/> alanı verilişin kimliğidir, yani (yayıncı, seri numarası)
/// çiftinin özeti (yükleyici: EvaluationDataLoader.IssuanceId; DECISIONS 2026-11-21). Ön sertifika ile son sertifika aynı
/// çifti taşır ve bir kez sayılır. Kimlik yoksa (eski yükleyici) her gözlem ayrı sayılır.</para>
/// <para>Varsayılanlar (30 gün, 5 sertifika) sabittir, sonuca bakılarak seçilmedi. Let's Encrypt sertifikası 90 gün geçerli
/// ve genelde 60. günde yenilenir: Normal bir ad 30 günde 1–2 sertifika alır. 5 ve üstü, aynı adın kısa sürede tekrar
/// tekrar kurulduğunu düşündürür.</para>
/// </remarks>
public sealed class CertificateBurstSignal(int windowDays, int minCertificates) : ISignal
{
    public string Name => "cert-burst";

    public double Score(AsOfView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var from = view.AsOf - TimeSpan.FromDays(windowDays);
        var inWindow = view.Observations
            .Where(o => o.Kind == ObservationKinds.Certificate && o.KnownAt > from)
            .ToList();
        var withId = inWindow.Where(o => o.Value is not null).Select(o => o.Value).Distinct(StringComparer.Ordinal).Count();
        var withoutId = inWindow.Count(o => o.Value is null);
        return withId + withoutId >= minCertificates ? 1 : 0;
    }
}
