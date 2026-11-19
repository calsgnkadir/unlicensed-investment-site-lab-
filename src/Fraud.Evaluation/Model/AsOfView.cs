namespace Fraud.Evaluation.Model;

/// <summary>
/// Bir birimin <see cref="AsOf"/> anındaki görünümü: Sinyalin görebildiği TEK şey.
/// </summary>
/// <remarks>
/// <para>Gelecek sızıntısını iki yoldan engeller:</para>
/// <list type="number">
/// <item>Gözlemler kurulurken süzülür ve yalnızca süzülmüş kopya saklanır. <c>KnownAt &gt; AsOf</c> olan kayıt bu
/// nesnenin içinde hiç yoktur; sinyal onu bir hata yüzünden bile okuyamaz.</item>
/// <item><see cref="AsOf"/> referans zamanından önce olmak zorundadır. Karar günü ve sonrası görünmez.</item>
/// <item><see cref="AsOf"/> gün ızgarasına (<see cref="DayGrid"/>) aşağı yuvarlanır: Her zaman bir TR gününün son anıdır.
/// Böylece görünüm anının saati etiket taşıyamaz (bulgu E4); sinyal yalnızca "hangi gün" bilgisini alır. Aşağı
/// yuvarlamak tutucudur: Aynı gün içinde sonradan bilinen bir kayıt o günün görünümüne girmez.</item>
/// </list>
/// <para>
/// Sinyale grup, bülten, küme ve referans zamanı VERİLMEZ: Bunlar etikettir. Sinyal "bu pozitif mi" bilgisine
/// görünümden ulaşamaz. Yalnızca opak kimlik, an ve gözlemler var.
/// </para>
/// </remarks>
public sealed class AsOfView
{
    private AsOfView(string subjectId, DateTimeOffset asOf, IReadOnlyList<Observation> observations)
    {
        SubjectId = subjectId;
        AsOf = asOf;
        Observations = observations;
    }

    public string SubjectId { get; }

    /// <summary>Görünümün anı: her zaman bir TR gününün son anı. Bu andan sonra bilinen hiçbir şey <see cref="Observations"/> içinde yoktur.</summary>
    public DateTimeOffset AsOf { get; }

    /// <summary><c>KnownAt &lt;= AsOf</c> olan gözlemler, zamana göre sıralı.</summary>
    public IReadOnlyList<Observation> Observations { get; }

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="asOf"/> referans zamanından önce değil.</exception>
    public static AsOfView Create(Subject subject, DateTimeOffset asOf, IEnumerable<Observation> observations)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(observations);
        if (subject.ReferenceTime is not { } reference)
        {
            throw new ArgumentException("Referans zamanı olmayan birim puanlanamaz.", nameof(subject));
        }

        if (asOf >= reference)
        {
            throw new ArgumentOutOfRangeException(nameof(asOf), "Görünüm anı referans zamanından önce olmalı (karar günü görünmez).");
        }

        var day = DayGrid.EndOfDayAtOrBefore(asOf);
        var visible = observations
            .Where(o => o.SubjectId == subject.Id && o.KnownAt <= day)
            .OrderBy(o => o.KnownAt)
            .ToList();
        return new AsOfView(subject.Id, day, visible);
    }
}

/// <summary>
/// Bir sinyal: görünümden bir puan üretir. Büyük puan "daha şüpheli" demektir. Puan bir yargı değildir; eşikle
/// birlikte yalnızca "alarm" ya da "alarm yok" olur.
/// </summary>
/// <remarks>Uygulamalar deterministik olmalı: Aynı görünüm her zaman aynı puanı verir.</remarks>
public interface ISignal
{
    string Name { get; }

    double Score(AsOfView view);
}

/// <summary>
/// Bir birim için tahmin.
/// </summary>
/// <param name="SubjectId">Opak kimlik.</param>
/// <param name="Score">Referans zamanına kadarki değerlendirmelerde görülen EN YÜKSEK puan. Canlı bir sistem bir kez
/// alarm verdiyse o alarm verilmiştir; bu yüzden eşik hep bu en yüksek puana göre seçilir ve uygulanır.</param>
/// <param name="FirstAlarmAt">Puanın eşiği ilk geçtiği değerlendirme anı. Eşik yoksa ya da geçilmediyse <c>null</c>.</param>
public sealed record Prediction(string SubjectId, double Score, DateTimeOffset? FirstAlarmAt)
{
    public bool Alarmed => FirstAlarmAt is not null;
}
