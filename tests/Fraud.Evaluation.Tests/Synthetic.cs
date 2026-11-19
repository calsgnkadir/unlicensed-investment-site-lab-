using System.Globalization;
using Fraud.Evaluation;
using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Tests;

/// <summary>
/// Uydurma, deterministik ölçüm verisi. Gerçek alan adı yok; kimlikler "p0001", "c0001" gibi.
/// </summary>
internal static class Synthetic
{
    public static readonly DateTimeOffset Year = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gerçek veride olduğu gibi her şeyin sonrasında kalan "şimdi" (kontrol referansının üst sınırı).</summary>
    public static readonly DateTimeOffset Now = new(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Bülten numarasından uydurma karar tarihi: yılbaşından numara × 4 gün sonra, TR gün başı (yükleyici gibi).</summary>
    public static DateTimeOffset DecisionOf(int bulletinNumber) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)).AddDays(bulletinNumber * 4);

    /// <summary>Tohumlu gecikme: 10 ile 310 gün arası.</summary>
    public static TimeSpan LagOf(string id) => TimeSpan.FromDays(10 + Math.Floor(SeededHash.Unit("lag-test", id) * 300));

    public static (List<Subject> Subjects, List<Observation> Observations) Build(int tuningPositives, int testPositives, int controls)
    {
        var subjects = new List<Subject>();
        var observations = new List<Observation>();

        for (var i = 0; i < tuningPositives + testPositives; i++)
        {
            var id = "p" + i.ToString("D4", CultureInfo.InvariantCulture);
            var number = i < tuningPositives ? 1 + (i % 18) : 20 + (i % 58);
            var decision = DecisionOf(number);
            var first = decision - LagOf(id);
            subjects.Add(new Subject(id, SubjectGroup.Positive, Subgroup.PositiveNew, decision, new BulletinNumber(2026, number), id,
                firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, Year.AddYears(1)));
        }

        for (var i = 0; i < controls; i++)
        {
            var id = "c" + i.ToString("D4", CultureInfo.InvariantCulture);
            // İlk sertifika: pozitiflerin ilk sertifika aylarıyla örtüşen bir yıl (2025-06 … 2026-06).
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Math.Floor(SeededHash.Unit("first-test", id) * 365));
            var group = i % 2 == 0 ? SubjectGroup.ControlHardNegative : SubjectGroup.ControlRandom;
            subjects.Add(new Subject(id, group, Subgroup.FrameNeutral, referenceTime: null, bulletin: null, id, firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, Year.AddYears(1)));
        }

        return (subjects, observations);
    }
}
