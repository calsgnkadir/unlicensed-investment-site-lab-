using System.Globalization;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Signals;

namespace Fraud.Evaluation.Tests;

/// <summary>
/// DECISIONS 2026-11-24 (G2 raporu): G2 hem tamamı hem Türkiye bağlantılı alt kümesi için yazılır, küçük hücre ve tümleyen
/// kuralıyla. Kurulum 100'den azsa "yeterli örnek yok" yazılır, oran yazılmaz. G2 eşik kümesine girmez.
/// </summary>
public sealed class G2ReportTests
{
    private static EvaluationConfig Config() => new() { TargetFalseAlarmRate = 0.10, BootstrapIterations = 200 };

    /// <summary>Uydurma veri + <paramref name="g2"/> G2 birimi; her ikinci birim Türkiye bağlantılı.</summary>
    private static (List<Subject> Subjects, List<Observation> Observations) WithG2(int g2, bool belowMinimum)
    {
        var (subjects, observations) = Synthetic.Build(tuningPositives: 300, testPositives: 300, controls: 3000);
        for (var i = 0; i < g2; i++)
        {
            var id = "g" + i.ToString("D4", CultureInfo.InvariantCulture);
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Math.Floor(SeededHash.Unit("first-test", id) * 365));
            subjects.Add(new Subject(id, SubjectGroup.ControlFinanceNews, Subgroup.None, null, null, id, firstCertificateAt: first)
            {
                TurkeyLinked = i % 2 == 0,
                BuildBelowMinimum = belowMinimum,
            });
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, Synthetic.Year.AddYears(1)));
        }

        return (subjects, observations);
    }

    private static string G2Lines(string text) =>
        string.Join('\n', text.Split('\n').Where(l => l.Contains(nameof(SubjectGroup.ControlFinanceNews), StringComparison.Ordinal)));

    [Fact]
    public void G2_does_not_enter_the_threshold_set()
    {
        var (without, withoutObs) = WithG2(0, belowMinimum: false);
        var (with, withObs) = WithG2(600, belowMinimum: false);

        var a = EvaluationRunner.Run(Config(), EvaluationMode.Historical, without, withoutObs, new RandomSignal("g2"), now: Synthetic.Now);
        var b = EvaluationRunner.Run(Config(), EvaluationMode.Historical, with, withObs, new RandomSignal("g2"), now: Synthetic.Now);

        Assert.Equal(a.Threshold, b.Threshold);
        Assert.Equal(a.TuningCalibrationControls, b.TuningCalibrationControls);
        Assert.Equal(a.CalibrationComposition, b.CalibrationComposition);
    }

    [Fact]
    public void G2_is_reported_for_all_and_for_the_turkey_linked_subset()
    {
        var (subjects, observations) = WithG2(600, belowMinimum: false);

        var report = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new RandomSignal("g2"), now: Synthetic.Now);

        var g2 = report.Strata.Where(s => s.Group == SubjectGroup.ControlFinanceNews).ToList();
        Assert.Equal(2, g2.Count);
        var whole = g2.Single(s => !s.TurkeyLinkedSubset);
        var subset = g2.Single(s => s.TurkeyLinkedSubset);
        Assert.True(subset.FalseAlarms.Total < whole.FalseAlarms.Total);
        Assert.NotNull(subset.FalseAlarms.Rate);
        // Alt kümede türetilmiş değer yok (precision, günlük alarm).
        Assert.Null(subset.SamplePrecision);
        Assert.Null(subset.AdjustedPrecision);
        // Satırlar sırayla: önce tamamı, hemen sonra alt kümesi.
        Assert.Equal(report.Strata.ToList().IndexOf(whole) + 1, report.Strata.ToList().IndexOf(subset));

        var lines = G2Lines(report.ToText()).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("Türkiye bağlantılı alt küme", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("precision", lines[1], StringComparison.Ordinal);
        Assert.Contains("precision", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Insufficient_g2_build_prints_no_rate_anywhere()
    {
        var (subjects, observations) = WithG2(90, belowMinimum: true);

        var report = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new RandomSignal("g2"), now: Synthetic.Now);

        var g2 = report.Strata.Where(s => s.Group == SubjectGroup.ControlFinanceNews).ToList();
        Assert.Equal(2, g2.Count);
        Assert.All(g2, s =>
        {
            Assert.True(s.BuildBelowMinimum);
            Assert.Null(s.FalseAlarms.Rate);
            Assert.Null(s.FalseAlarms.ClusterInterval);
            Assert.Null(s.SamplePrecision);
            Assert.Null(s.AdjustedPrecision);
        });

        var lines = G2Lines(report.ToText());
        Assert.Contains(EvaluationReport.BelowMinimumText, lines, StringComparison.Ordinal);
        Assert.DoesNotContain("%", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("precision", lines, StringComparison.Ordinal);

        // Ablasyon tablosu aynı biçimleyiciyi kullanır.
        var ablation = Ablation.ToText(Ablation.Run(Config(), subjects, observations, [new RandomSignal("a"), new RandomSignal("b")], Synthetic.Now));
        var ablationLines = G2Lines(ablation);
        Assert.Contains(EvaluationReport.BelowMinimumText, ablationLines, StringComparison.Ordinal);
        Assert.DoesNotContain("%", ablationLines, StringComparison.Ordinal);
    }

    private static StratumResult Whole(int hits, int total) =>
        new(SubjectGroup.ControlFinanceNews, Subgroup.None, false, new RateResult(hits, total, (double)hits / total, null, null), null, null, null);

    private static StratumResult Subset(int hits, int total) =>
        new(SubjectGroup.ControlFinanceNews, Subgroup.None, false, new RateResult(hits, total, (double)hits / total, null, null), null, null, null)
        {
            TurkeyLinkedSubset = true,
        };

    [Fact]
    public void Turkey_subset_hides_numbers_when_it_or_its_complement_is_small()
    {
        // Tümleyen (tamamı − alt küme) 3/40: alt küme 27/60 basılsaydı 30 − 27 = 3 geri bulunurdu.
        var subset = Subset(27, 60);
        var text = EvaluationReport.FormatStratum(subset, [Whole(30, 100), subset]);
        Assert.DoesNotContain("27", text, StringComparison.Ordinal);
        Assert.DoesNotContain("%", text, StringComparison.Ordinal);
        Assert.Contains("test birimi=60", text, StringComparison.Ordinal);

        // Alt kümenin kendisi küçük (isabet 3): kuyruk ya da sayı yok.
        var small = Subset(3, 60);
        var smallText = EvaluationReport.FormatStratum(small, [Whole(30, 100), small]);
        Assert.DoesNotContain("3/", smallText, StringComparison.Ordinal);
        Assert.DoesNotContain("≥", smallText, StringComparison.Ordinal);

        // Tümleyenin birim sayısı küçük (97 − 95 = 2): alt kümenin birim sayısı da yazılmaz.
        var almostAll = Subset(40, 95);
        var almostAllText = EvaluationReport.FormatStratum(almostAll, [Whole(45, 97), almostAll]);
        Assert.Contains("test birimi=-", almostAllText, StringComparison.Ordinal);

        // İkisi de büyük: normal biçim.
        var big = Subset(20, 60);
        Assert.StartsWith("20/60 = ", EvaluationReport.FormatStratum(big, [Whole(30, 100), big]), StringComparison.Ordinal);
    }

    [Fact]
    public void Turkey_label_is_only_for_g2() =>
        Assert.Throws<ArgumentException>(() => new Subject("c", SubjectGroup.ControlRandom, Subgroup.None, null, null, "c") { TurkeyLinked = true });

    [Fact]
    public void Below_minimum_flag_is_only_for_controls() =>
        Assert.Throws<ArgumentException>(() => new Subject("p", SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(1),
            new BulletinNumber(2026, 1), "p") { BuildBelowMinimum = true });
}
