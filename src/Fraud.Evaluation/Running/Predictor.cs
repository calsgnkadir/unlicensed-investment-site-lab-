using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Running;

/// <summary>
/// Bir sinyali bir birimin geçmişi boyunca, GÜN GÜN çalıştırır ve <see cref="Prediction"/> üretir.
/// </summary>
/// <remarks>
/// <para>
/// Değerlendirme anları: Birimin ilk gözleminin bilinir olduğu günden referanstan önceki son güne kadar HER GÜNÜN son
/// anı (<see cref="DayGrid"/>). Gözlemi yoksa yalnızca son gün. Eskiden yalnızca gözlem anlarında değerlendiriyorduk;
/// bu "puan yalnızca yeni gözlemle değişir" varsayımına dayanıyordu ve sertifika yaşı gibi zamanla büyüyen bir
/// sinyalde ilk alarmı geç buluyordu (mentör maddesi 4). Gün ızgarası bu varsayımı gereksiz kılar.
/// </para>
/// <para>Her gün aynı saatte değerlendirildiği için görünüm anı etiket taşımaz (bulgu E4).</para>
/// </remarks>
public static class Predictor
{
    /// <param name="threshold">Eşik. <c>null</c> ise yalnızca puan hesaplanır (ayar için).</param>
    public static Prediction Predict(ISignal signal, Subject subject, IReadOnlyList<Observation> observations, double? threshold)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(observations);
        var reference = subject.ReferenceTime ?? throw new ArgumentException("Referans zamanı yok.", nameof(subject));

        // Son değerlendirme: referanstan önceki son günün son anı. Karar günü görünmez.
        var last = DayGrid.EndOfDayAtOrBefore(reference.AddTicks(-1));
        var own = observations.Where(o => o.SubjectId == subject.Id && o.KnownAt <= last).ToList();
        // Ad gözlemi zaman taşımaz (KnownAt = MinValue); değerlendirme başlangıcını belirlemez.
        var timed = own.Where(o => o.Kind != ObservationKinds.Name).ToList();
        var day = timed.Count == 0 ? last : DayGrid.EndOfDayAtOrAfter(timed.Min(o => o.KnownAt));

        var best = double.NegativeInfinity;
        DateTimeOffset? firstAlarm = null;
        for (; day <= last; day = day.AddDays(1))
        {
            var score = signal.Score(AsOfView.Create(subject, day, own));
            best = Math.Max(best, score);
            if (firstAlarm is null && threshold is { } t && score >= t)
            {
                firstAlarm = day;
            }
        }

        return new Prediction(subject.Id, best, firstAlarm);
    }
}

/// <summary>
/// Eşiği ayar verisinde, hedef yanlış alarm oranına göre seçer. Test verisi buraya hiç girmez.
/// </summary>
public static class ThresholdSelector
{
    /// <summary>
    /// Kural "puan ≥ eşik ise alarm". En küçük eşiği seçer öyle ki ayar kontrollerinde alarm payı ≤ α olsun.
    /// </summary>
    /// <remarks>
    /// <para>Elle örnek: Puanlar 0,9 0,8 0,7 0,6 0,5 (n = 5), α = 0,2 → izin verilen alarm k = floor(0,2·5) = 1.
    /// Büyükten küçüğe sıralı listede k. sıradaki (0 tabanlı) puan 0,8. Eşik 0,8'in hemen üstündeki double: Yalnızca
    /// 0,9 alarm verir, oran 1/5 = 0,2 ≤ α.</para>
    /// <para>Eşitlik: 0,8 iki kez geçseydi ikisi de alarm vermezdi; oran hedefin altında kalır, üstüne çıkmaz.</para>
    /// <para>floor içindeki 1e-9, 0,29·100 = 28,999999999999996 gibi kayan nokta artığının 29'u 28'e indirmesini önler.</para>
    /// </remarks>
    public static double Select(IReadOnlyList<double> controlScores, double alpha)
    {
        ArgumentNullException.ThrowIfNull(controlScores);
        if (controlScores.Count == 0)
        {
            throw new InvalidOperationException("Eşik seçmek için ayar kontrolü yok.");
        }

        var sorted = controlScores.OrderDescending().ToList();
        var allowed = (int)Math.Floor((alpha * sorted.Count) + 1e-9);
        return allowed >= sorted.Count ? sorted[^1] : Math.BitIncrement(sorted[allowed]);
    }
}
