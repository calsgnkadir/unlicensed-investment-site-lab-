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
        // Bu veride TOPLAM eşiği 1'in üstünde (numbered G4'te sık 1 alır). İki parça olduğu için "TOPLAM − x" satırlarında da
        // tek parça kalır: Sabit eşikli sürüm hiçbir satırda uygulanmaz, gerekçe yazılır (DECISIONS 2026-11-24, ikinci kayıt).
        // Eskiden "TOPLAM − x" satırları burada sabit eşikle çalışıyor ve fark hep "−TOPLAM recall" çıkıyordu.
        Assert.True(full.Report.Threshold > 1);
        Assert.All(rows.Where(r => r.Variant.StartsWith("TOPLAM − ", StringComparison.Ordinal)), r =>
        {
            Assert.Null(r.AtFullThreshold);
            Assert.NotNull(r.AtFullThresholdSkipped);
        });
        Assert.All(rows.Where(r => r.Variant != Ablation.FullName),
            r => Assert.True(r.AtFullThreshold is not null ^ r.AtFullThresholdSkipped is not null));
        Assert.Contains("kendi eşiğiyle", text, StringComparison.Ordinal);
        Assert.Contains("TOPLAM eşiğiyle", text, StringComparison.Ordinal);
        Assert.Contains("EŞİĞİN DEĞİŞMESİNİ", text, StringComparison.Ordinal);
    }

    /// <summary>Her birime 1 veren uydurma parça: G4'ün tamamı 1 alır, böylece TOPLAM eşiği 1'in üstüne çıkar.</summary>
    private sealed class AlwaysOneSignal : ISignal
    {
        public string Name => "always-one";

        public double Score(AsOfView view) => 1;
    }

    /// <summary>
    /// DECISIONS 2026-11-24: G4 grubunda Çerçeve K taşıyan birim eşik kümesine girmez. K adları terim taşır (baseline'da 1);
    /// eşiğe girselerdi eşik 1'in üstüne çıkar ve baseline susardı. Sonuç, K birimleri hiç yokmuş gibi olmalı.
    /// </summary>
    [Fact]
    public void Frame_k_in_g4_does_not_enter_threshold_set()
    {
        var (subjects, observations) = Build(0, Subgroup.FrameNeutral, _ => string.Empty);
        var (withK, withKObs) = Build(0, Subgroup.FrameNeutral, _ => string.Empty);
        for (var i = 0; i < 1200; i++)
        {
            var id = "k" + i.ToString("D4", CultureInfo.InvariantCulture);
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i % 365);
            withK.Add(new Subject(id, SubjectGroup.ControlRandom, Subgroup.FrameTermConditioned, null, null, id, firstCertificateAt: first));
            withKObs.Add(new Observation(id, ObservationKinds.Certificate, first, first, "c-" + id));
            withKObs.Add(new Observation(id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                "on" + "borsa" + "x" + i.ToString(CultureInfo.InvariantCulture) + ".test"));
        }

        var without = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new BaselineSignal(), now: Now);
        var with = EvaluationRunner.Run(Config(), EvaluationMode.Historical, withK, withKObs, new BaselineSignal(), now: Now);

        Assert.True(with.Threshold <= 1, "eşik 1'in üstünde: K birimleri eşik kümesine girdi");
        Assert.Equal(without.Threshold, with.Threshold);
        Assert.Equal(without.TuningCalibrationControls, with.TuningCalibrationControls);
        // K birimleri yine raporda, ayrı tabakada görünür.
        Assert.Contains(with.Strata, s => s.Group == SubjectGroup.ControlRandom && s.Subgroup == Subgroup.FrameTermConditioned);
    }

    /// <summary>DECISIONS 2026-11-24: Rapor eşik kümesini kaynağa göre döker (G4a çerçevesiz, G4b Çerçeve N); sayılar n ile tutar.</summary>
    [Fact]
    public void Report_prints_threshold_set_composition_by_source()
    {
        var (subjects, observations) = Build(0, Subgroup.FrameNeutral, _ => string.Empty);
        for (var i = 0; i < 100; i++)
        {
            var id = "a" + i.ToString("D4", CultureInfo.InvariantCulture);
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i % 365);
            subjects.Add(new Subject(id, SubjectGroup.ControlRandom, Subgroup.None, null, null, id, firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, first, "c-" + id));
        }

        var report = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new BaselineSignal(), now: Now);

        var g4a = report.CalibrationComposition.Single(c => c.Source == "G4a").Count;
        var g4b = report.CalibrationComposition.Single(c => c.Source == "G4b").Count;
        Assert.True(g4a >= EvaluationReport.SmallCellLimit && g4b >= EvaluationReport.SmallCellLimit);
        Assert.Equal(report.TuningCalibrationControls, g4a + g4b);
        Assert.Contains("Eşik kümesi (ayar): G4a=" + g4a.ToString(CultureInfo.InvariantCulture) + ", G4b=" + g4b.ToString(CultureInfo.InvariantCulture),
            report.ToText(), StringComparison.Ordinal);
    }

    /// <summary>Küçük hücre: 5'ten küçük sayı "&lt;5", öteki de "-" (n basılı olduğu için tümleyen gizlenen sayıyı verirdi).</summary>
    [Fact]
    public void Composition_hides_small_cell_and_its_complement()
    {
        Assert.Equal("G4a=<5, G4b=-", EvaluationReport.CompositionText([("G4a", 3), ("G4b", 200)]));
        Assert.Equal("G4a=<5, G4b=<5", EvaluationReport.CompositionText([("G4a", 0), ("G4b", 4)]));
        Assert.Equal("G4a=5, G4b=200", EvaluationReport.CompositionText([("G4a", 5), ("G4b", 200)]));
    }

    /// <summary>
    /// DECISIONS 2026-11-24: TOPLAM eşiği 1'in üstündeyse tek parçalı satırda "TOPLAM eşiğiyle" farkı uygulanmaz (0/1 puan o eşiğe
    /// ulaşamaz, fark hep "−TOPLAM recall" olurdu). Parça çıkarılmış satırlarda fark yine hesaplanır.
    /// </summary>
    [Fact]
    public void Ablation_marks_single_part_fixed_threshold_as_not_applicable_when_sum_threshold_above_one()
    {
        var (subjects, observations) = Build(0, Subgroup.FrameNeutral, _ => string.Empty);
        ISignal[] parts = [new AlwaysOneSignal(), new BaselineSignal(), new NumberedNameSignal()];

        var rows = Ablation.Run(Config(), subjects, observations, parts, Now);
        var text = Ablation.ToText(rows);

        var full = rows.Single(r => r.Variant == Ablation.FullName);
        Assert.True(full.Report.Threshold > 1);
        foreach (var part in parts)
        {
            var single = rows.Single(r => r.Variant == part.Name);
            Assert.Null(single.AtFullThreshold);
            Assert.NotNull(single.AtFullThresholdSkipped);
            var leaveOut = rows.Single(r => r.Variant == "TOPLAM − " + part.Name);
            Assert.NotNull(leaveOut.AtFullThreshold);
            Assert.Null(leaveOut.AtFullThresholdSkipped);
        }

        Assert.Contains("TOPLAM eşiğiyle uygulanmaz", text, StringComparison.Ordinal);
    }

    /// <summary>Her birime 0 veren uydurma parça: TOPLAM eşiğini yükseltmez.</summary>
    private sealed class AlwaysZeroSignal : ISignal
    {
        public string Name => "always-zero";

        public double Score(AsOfView view) => 0;
    }

    /// <summary>
    /// DECISIONS 2026-11-24 (ikinci kayıt): "Uygulanmaz" kararı parça sayısından ve eşikten gelir, sonuçtan gelmez. İki parça,
    /// TOPLAM eşiği 1 ya da altında: Tek parçalı satır o eşiğe ulaşabilir, bu yüzden "TOPLAM − x" satırlarında sabit eşikli
    /// sürüm yine çalışır.
    /// </summary>
    [Fact]
    public void Ablation_keeps_fixed_threshold_for_one_remaining_part_when_sum_threshold_at_most_one()
    {
        var (subjects, observations) = Build(0, Subgroup.FrameNeutral, _ => string.Empty);
        ISignal[] parts = [new BaselineSignal(), new AlwaysZeroSignal()];

        var rows = Ablation.Run(Config(), subjects, observations, parts, Now);

        var full = rows.Single(r => r.Variant == Ablation.FullName);
        Assert.True(full.Report.Threshold <= 1);
        Assert.All(rows.Where(r => r.Variant != Ablation.FullName), r =>
        {
            Assert.NotNull(r.AtFullThreshold);
            Assert.Null(r.AtFullThresholdSkipped);
        });
    }

    [Fact]
    public void Fixed_threshold_is_refused_outside_historical_mode() =>
        Assert.Throws<InvalidOperationException>(() => EvaluationRunner.Run(
            new EvaluationConfig { FreezeDate = Now }, EvaluationMode.Locked, [], [], new BaselineSignal(), null, Now, fixedThreshold: 1));
}
