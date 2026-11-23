using System.Globalization;
using Fraud.Evaluation;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;

namespace Fraud.Signals.Tests;

/// <summary>
/// DECISIONS 2026-11-21: Eşik yalnızca G4 ayar kontrollerinden seçilir. G3 ve Çerçeve K eşiğe girmez, ayrı raporlanır.
/// </summary>
public sealed class ThresholdTests
{
    private static readonly DateTimeOffset Now = new(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EvaluationConfig Config() => new() { TargetFalseAlarmRate = 0.10, BootstrapIterations = 200 };

    /// <summary>
    /// Uydurma veri. Pozitiflerin yarısının adında "yatirim" var. G4 (Çerçeve N) adlarında terim yok. G3 adları
    /// <paramref name="g3Name"/>'den gelir; Çerçeve K ise bu adlar terim taşır (K tanım gereği süzgeçten geçmiştir).
    /// </summary>
    private static (List<Subject> Subjects, List<Observation> Observations) Build(int g3Count, Subgroup g3Frame, Func<int, string> g3Name)
    {
        var subjects = new List<Subject>();
        var observations = new List<Observation>();
        void Add(Subject s, string name, DateTimeOffset first)
        {
            subjects.Add(s);
            observations.Add(new Observation(s.Id, ObservationKinds.Certificate, first, first, "c-" + s.Id));
            observations.Add(new Observation(s.Id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue, name));
        }

        for (var i = 0; i < 200; i++)
        {
            var id = "p" + i.ToString("D4", CultureInfo.InvariantCulture);
            var number = i < 100 ? 1 + (i % 18) : 20 + (i % 58);
            var decision = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)).AddDays(number * 4);
            var first = decision.AddDays(-20 - (i % 40));
            Add(new Subject(id, SubjectGroup.Positive, Subgroup.PositiveNew, decision, new BulletinNumber(2026, number), id, firstCertificateAt: first),
                (i % 2 == 0 ? "ornekyatirim" : "ornek") + "x" + i.ToString(CultureInfo.InvariantCulture) + ".example", first);
        }

        for (var i = 0; i < 400; i++)
        {
            var id = "r" + i.ToString("D4", CultureInfo.InvariantCulture);
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i % 365);
            Add(new Subject(id, SubjectGroup.ControlRandom, Subgroup.FrameNeutral, null, null, id, firstCertificateAt: first),
                "deneme" + "x" + i.ToString(CultureInfo.InvariantCulture) + ".test", first);
        }

        for (var i = 0; i < g3Count; i++)
        {
            var id = "h" + i.ToString("D4", CultureInfo.InvariantCulture);
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i % 365);
            Add(new Subject(id, SubjectGroup.ControlHardNegative, g3Frame, null, null, id, firstCertificateAt: first), g3Name(i), first);
        }

        return (subjects, observations);
    }

    /// <summary>
    /// Ayar kontrollerinin çoğu Çerçeve K (hepsi baseline'da 1). Eski kuralda eşik 1'in üstüne çıkar ve baseline hiç alarm vermezdi.
    /// Yeni kuralda eşik G4'ten seçilir: baseline pozitiflerin yaklaşık yarısını yakalar, K'nın %100 yanlış alarmı ayrı satırda görünür.
    /// </summary>
    [Fact]
    public void Frame_K_heavy_tuning_set_does_not_silence_baseline()
    {
        var (subjects, observations) = Build(1200, Subgroup.FrameTermConditioned,
            i => "on" + "borsa" + "x" + i.ToString(CultureInfo.InvariantCulture) + ".test");

        var report = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new BaselineSignal(), now: Now);

        Assert.True(report.Threshold <= 1, "eşik 1'in üstünde: baseline susturuldu");
        Assert.InRange(report.Recall.Rate!.Value, 0.4, 0.6);
        var frameK = report.Strata.Single(s => s.Group == SubjectGroup.ControlHardNegative && s.Subgroup == Subgroup.FrameTermConditioned);
        Assert.Equal(1.0, frameK.FalseAlarms.Rate); // tanım gereği; ölçüm değil, ayrı raporlanır
        var g4 = report.Strata.Single(s => s.Group == SubjectGroup.ControlRandom && !s.LinkedToPositive);
        Assert.Equal(0.0, g4.FalseAlarms.Rate);
    }

    /// <summary>G3'ün boyutu ve puanları ne olursa olsun eşik ve G4 sonucu değişmez.</summary>
    [Fact]
    public void Threshold_does_not_depend_on_g3()
    {
        var (few, fewObs) = Build(300, Subgroup.FrameNeutral, i => "yatirimx" + i.ToString(CultureInfo.InvariantCulture) + ".test"); // G3 hep 1
        var (many, manyObs) = Build(900, Subgroup.FrameNeutral, i => "sadex" + i.ToString(CultureInfo.InvariantCulture) + ".test"); // G3 hep 0

        var a = EvaluationRunner.Run(Config(), EvaluationMode.Historical, few, fewObs, new BaselineSignal(), now: Now);
        var b = EvaluationRunner.Run(Config(), EvaluationMode.Historical, many, manyObs, new BaselineSignal(), now: Now);

        Assert.Equal(a.Threshold, b.Threshold);
        Assert.Equal(a.Recall.Rate, b.Recall.Rate);
        Assert.Equal(1.0, a.Strata.Single(s => s.Group == SubjectGroup.ControlHardNegative).FalseAlarms.Rate);
        Assert.Equal(0.0, b.Strata.Single(s => s.Group == SubjectGroup.ControlHardNegative).FalseAlarms.Rate);
    }

    [Theory]
    [InlineData("""{"calibrationGroups":["ControlHardNegative"]}""")]
    [InlineData("""{"calibrationGroups":["ControlHardNegative","ControlRandom"]}""")]
    [InlineData("""{"calibrationGroups":["ControlLicensed","ControlRandom"]}""")]
    public void Config_rejects_any_calibration_group_but_g4(string json) =>
        Assert.Throws<InvalidOperationException>(() => EvaluationConfig.Parse(json));

    [Fact]
    public void Ablation_reports_both_own_threshold_and_fixed_sum_threshold_deltas()
    {
        var (subjects, observations) = Build(100, Subgroup.FrameNeutral, i => "sadex" + i.ToString(CultureInfo.InvariantCulture) + ".test");
        ISignal[] parts = [new BaselineSignal(), new NumberedNameSignal()];

        var rows = Ablation.Run(Config(), subjects, observations, parts, Now);
        var text = Ablation.ToText(rows);

        var full = rows.Single(r => r.Variant == Ablation.FullName);
        Assert.Null(full.AtFullThreshold);
        Assert.All(rows.Where(r => r.Variant != Ablation.FullName), r => Assert.Equal(full.Report.Threshold, r.AtFullThreshold!.Threshold));
        Assert.Contains("kendi eşiğiyle", text, StringComparison.Ordinal);
        Assert.Contains("TOPLAM eşiğiyle", text, StringComparison.Ordinal);
        Assert.Contains("EŞİĞİN DEĞİŞMESİNİ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_threshold_is_refused_outside_historical_mode() =>
        Assert.Throws<InvalidOperationException>(() => EvaluationRunner.Run(
            new EvaluationConfig { FreezeDate = Now }, EvaluationMode.Locked, [], [], new BaselineSignal(), null, Now, fixedThreshold: 1));
}
