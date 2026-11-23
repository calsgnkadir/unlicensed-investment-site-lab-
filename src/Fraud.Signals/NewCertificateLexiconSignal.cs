using Fraud.Domain.Signals;
using Fraud.Evaluation.Model;

namespace Fraud.Signals;

/// <summary>
/// Yeni sertifika ve finans kelimesi: Görünümdeki İLK sertifika, görünüm anından en çok <c>maxAgeDays</c> gün önceyse VE
/// soldaki etiket geniş finans sözlüğüyle eşleşiyorsa 1.
/// </summary>
/// <remarks>
/// <para>N (<c>maxAgeDays</c>) sabit bir varsayılandır (28 gün). Ayar verisinde seçilmedi; seçilseydi yalnızca ayar verisinde
/// seçilirdi (ölçüm kuralı). 28 gün = kilitli test penceresinin uzunluğu; "aynı ay içinde" demek.</para>
/// <para>DİKKAT, okunması gereken sonuç: Predictor her birimi ilk gözleminin gününden itibaren gün gün puanlar ve EN YÜKSEK
/// puanı tutar. İlk sertifika görüldüğü gün her zaman "yeni"dir; bu yüzden geçmiş veri testinde bu sinyal pratikte
/// "sözlük eşleşmesi + en az bir sertifika" olur. "Yeni" şartı alarmın NE ZAMAN verildiğini belirler (ilk 28 gün),
/// verilip verilmediğini değil. Canlı sistem de aynı şekilde çalışırdı: Alarm o 28 günde verilir ya da hiç verilmez.</para>
/// <para>G3 NOTU: G3 (zor negatifler) "aynı dönemde ilk sertifikasını almış ve sözlükle eşleşen" adlardan kurulur. Bu
/// sinyal G3'te tanım gereği alarm verir; G3'teki yanlış alarm oranı bu sinyal için ölçüm değil, kurgudur.</para>
/// </remarks>
public sealed class NewCertificateLexiconSignal(FinanceLexicon lexicon, int maxAgeDays) : ISignal
{
    public string Name => "new-cert-lexicon";

    public double Score(AsOfView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var name = SubjectName.Registrable(view);
        // Görünüm zaten KnownAt ≤ AsOf ile süzülü ve sıralı: İlk sertifika görünümde bilinen en eski sertifikadır.
        var first = view.Observations.FirstOrDefault(o => o.Kind == ObservationKinds.Certificate);
        if (name is null || first is null)
        {
            return 0;
        }

        var isNew = view.AsOf - first.KnownAt <= TimeSpan.FromDays(maxAgeDays);
        return isNew && lexicon.Match(name) is not null ? 1 : 0;
    }
}
