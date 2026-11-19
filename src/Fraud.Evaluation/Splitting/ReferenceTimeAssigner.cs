using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Splitting;

/// <summary>Referans ataması sonucu. Yalnızca sayılar ve opak birimler.</summary>
/// <param name="Subjects">Kontrollerin referans zamanı doldurulmuş birimler.</param>
/// <param name="LagPoolSize">Gecikmelerin çekildiği ayar pozitifi sayısı.</param>
/// <param name="ControlsWithoutCertificate">İlk sertifikası bilinmediği için referans atanamayan kontrol. Ölçüme girmez.</param>
/// <param name="ControlsBeyondNow">Referansı "şimdi"den sonraya düştüğü için ölçümden çıkarılan kontrol (mentör maddesi 3).</param>
public sealed record ReferenceAssignment(IReadOnlyList<Subject> Subjects, int LagPoolSize, int ControlsWithoutCertificate, int ControlsBeyondNow);

/// <summary>
/// Kontrolün puanlanacağı anı seçer. Sorun: Pozitif karar gününde puanlanır (ilk sertifikadan ortanca 148 gün sonra).
/// Kontrol ilk sertifika gününde puanlansaydı "sertifika yaşı" gibi her sinyal iki grubu tanım gereği ayırırdı.
/// </summary>
/// <remarks>
/// <para>Eşleşmiş gecikme (varsayılan):</para>
/// <code>
/// havuz  = ayar pozitiflerinde (karar − ilk sertifika), küçükten büyüğe sıralı
/// i      = floor(SeededHash.Unit(tohum, "lag|" + kontrol_kimliği) × havuz_boyu)
/// referans = ilk sertifika + havuz[i]'den SONRAKİ ilk TR gün başı
/// </code>
/// <para>
/// Böylece kontrollerin "ilk sertifikadan uzaklık" dağılımı, pozitiflerinkine eşit olur. Havuz YALNIZCA ayar
/// pozitiflerinden kurulur; test pozitiflerinin hiçbir bilgisi kontrol referanslarına girmez (sızıntı olmasın).
/// </para>
/// <para>
/// Gün başına yuvarlama (E4): Pozitifin referansı da TR gün başıdır. İkisi aynı ızgarada olunca görünüm anının saati
/// etiket taşımaz. Yükleyicinin verdiği bir referans da aynı ızgaraya yukarı yuvarlanır.
/// </para>
/// <para>
/// "Şimdi" sınırı (mentör maddesi 3): Referansı <paramref name="now"/>'dan sonraya düşen kontrol çıkarılır. Yoksa böyle
/// bir kontrol bugünkü DNS kaydını "karardan önce" görür, pozitif göremez; "DNS kaydı var mı" grubu ele verirdi.
/// FreezeDate sınırı bölmede uygulanır (<see cref="Splitter"/>).
/// </para>
/// </remarks>
public static class ReferenceTimeAssigner
{
    public static ReferenceAssignment Assign(IReadOnlyList<Subject> subjects, EvaluationConfig config, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(config);

        var lags = subjects
            .Where(s => s.IsPositive && s.FirstCertificateAt is not null && config.TuningBulletins.Contains(s.Bulletin!.Value))
            .Select(s => s.ReferenceTime!.Value - s.FirstCertificateAt!.Value)
            .Where(lag => lag >= TimeSpan.Zero)
            .Order()
            .ToList();

        int withoutCertificate = 0, beyondNow = 0;
        var result = new List<Subject>(subjects.Count);
        foreach (var s in subjects)
        {
            if (s.IsPositive)
            {
                result.Add(s);
                continue;
            }

            DateTimeOffset? reference = s.ReferenceTime is { } given ? DayGrid.DayStartAtOrAfter(given) : null;
            if (reference is null && s.FirstCertificateAt is { } first
                && !(config.ControlReference == ControlReferencePolicy.MatchedLag && lags.Count == 0))
            {
                var lag = config.ControlReference switch
                {
                    ControlReferencePolicy.MatchedLag => lags[(int)(SeededHash.Unit(config.Seed, "lag|" + s.Id) * lags.Count)],
                    _ => TimeSpan.Zero,
                };
                reference = DayGrid.NextDayStartAfter(first + lag);
            }
            else if (reference is null && s.FirstCertificateAt is null)
            {
                withoutCertificate++;
            }

            // Havuz boşsa (ayar pozitifi yok) referans atanamaz; bu durum LagPoolSize = 0 olarak raporda görünür.

            if (reference is { } r && now is { } limit && r > limit)
            {
                beyondNow++;
                reference = null;
            }

            result.Add(s with { ReferenceTime = reference });
        }

        return new ReferenceAssignment(result, lags.Count, withoutCertificate, beyondNow);
    }
}
