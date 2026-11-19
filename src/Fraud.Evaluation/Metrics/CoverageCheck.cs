using System.Globalization;
using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Metrics;

/// <summary>
/// Toplama asimetrisi denetimi: Bir grupta "bu türden en az bir kaydı var mı" oranı pozitiflerinkinden çok farklıysa
/// uyarı üretir.
/// </summary>
/// <remarks>
/// Neden: Toplayıcılar şu an yalnızca pozitifler için sertifika, arşiv ve DNS topluyor; kontrol listeleri kurulunca
/// onlar da kapsanacak. O zamana kadar "sertifika kaydı var mı" bilgisi tek başına etiketi ele verir: Hiçbir sinyal
/// gerekmeden pozitifleri ayırır. Bu denetim sinyale değil veriye bakar ve her çalıştırmada rapora girer.
/// </remarks>
public static class CoverageCheck
{
    public static readonly IReadOnlyList<string> Kinds = [ObservationKinds.Certificate, ObservationKinds.Archive, ObservationKinds.Dns];

    /// <param name="subjects">Ölçüme giren birimler (referansı olanlar).</param>
    /// <param name="observations">Bütün gözlemler.</param>
    /// <param name="maxGap">İzin verilen en büyük oran farkı (ör. 0,20).</param>
    /// <returns>Uyarı satırları. Yalnızca grup adı, tür ve oranlar; birim yok.</returns>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<Subject> subjects, IReadOnlyList<Observation> observations, double maxGap)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(observations);
        // Mentör 5.4/3: Yalnızca sinyalin görebildiği kayıtlar sayılır: KnownAt, birimin SON görünüm anından (referanstan
        // önceki günün son anı) küçük ya da ona eşit olmalı. Bugün toplanmış ama geçmiş görünümde olmayan bir kayıt
        // kapsamayı olduğundan iyi gösterirdi.
        var lastView = subjects
            .Where(s => s.ReferenceTime is not null)
            .ToDictionary(s => s.Id, s => DayGrid.EndOfDayAtOrBefore(s.ReferenceTime!.Value.AddTicks(-1)), StringComparer.Ordinal);
        var kindsBySubject = observations
            .Where(o => lastView.TryGetValue(o.SubjectId, out var last) && o.KnownAt <= last)
            .GroupBy(o => o.SubjectId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Kind).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        double Share(IEnumerable<Subject> group, string kind)
        {
            var list = group.ToList();
            return list.Count == 0 ? 0 : (double)list.Count(s => kindsBySubject.TryGetValue(s.Id, out var k) && k.Contains(kind)) / list.Count;
        }

        var positives = subjects.Where(s => s.IsPositive).ToList();
        var warnings = new List<string>();
        if (positives.Count == 0)
        {
            return warnings;
        }

        foreach (var group in subjects.Where(s => !s.IsPositive).GroupBy(s => s.Group).OrderBy(g => g.Key))
        {
            foreach (var kind in Kinds)
            {
                var p = Share(positives, kind);
                var c = Share(group, kind);
                if (Math.Abs(p - c) > maxGap)
                {
                    warnings.Add(string.Create(CultureInfo.InvariantCulture,
                        $"UYARI veri kapsaması: '{kind}' kaydı olan pay pozitiflerde {p:P0}, {group.Key} grubunda {c:P0}. Bu fark tek başına etiketi ele verebilir; sonuç güvenilmez."));
                }
            }
        }

        return warnings;
    }
}
