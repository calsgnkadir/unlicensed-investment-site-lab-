using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Signals;
using Fraud.Evaluation.Splitting;

namespace Fraud.Evaluation.Tests;

/// <summary>"Bitti sayılır" (ROADMAP Hafta 6): sahte sinyal bütün hattan geçer ve şansa yakın sonuç verir.</summary>
public sealed class PipelineTests
{
    private static EvaluationConfig Config(ControlReferencePolicy policy = ControlReferencePolicy.MatchedLag) => new()
    {
        // α = %10: 300 test pozitifinde recall'un şans düzeyi (≈ 0,10) ile ayırt edilebilir olması için.
        TargetFalseAlarmRate = 0.10,
        BootstrapIterations = 200,
        ControlReference = policy,
    };

    [Fact]
    public void Random_signal_goes_through_the_whole_pipeline_and_scores_near_chance()
    {
        var (subjects, observations) = Synthetic.Build(tuningPositives: 300, testPositives: 300, controls: 3000);

        var report = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new RandomSignal("rs-1"), now: Synthetic.Now);

        Assert.Equal(300, report.TestPositives);
        Assert.True(report.TestControls > 300);
        // Şans: recall ≈ α = 0,10. 300 pozitifte standart sapma ≈ 0,017; ±0,06 (≈ 3,5 sapma) bandı.
        Assert.InRange(report.Recall.Rate!.Value, 0.04, 0.16);
        Assert.All(report.Strata.Where(s => !s.LinkedToPositive), s => Assert.InRange(s.FalseAlarms.Rate!.Value, 0.05, 0.15));
        Assert.NotNull(report.Recall.ClusterInterval);
        Assert.NotNull(report.Recall.BulletinInterval);
        var text = report.ToText();
        Assert.Contains("yatırım tavsiyesi değildir", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_inputs_give_the_same_report()
    {
        var (subjects, observations) = Synthetic.Build(100, 100, 500);

        var a = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new RandomSignal("rs-2"), now: Synthetic.Now).ToText();
        var b = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new RandomSignal("rs-2"), now: Synthetic.Now).ToText();

        Assert.Equal(a, b);
    }

    [Fact]
    public void Certificate_age_signal_is_near_chance_with_matched_lag()
    {
        // Pozitif ve kontrol ilk sertifikadan aynı dağılımda uzaklıkta puanlanınca "yaş" etiketi bulamaz.
        var (subjects, observations) = Synthetic.Build(300, 300, 3000);

        var report = EvaluationRunner.Run(Config(), EvaluationMode.Historical, subjects, observations, new CertificateAgeSignal(), now: Synthetic.Now);

        Assert.InRange(report.Recall.Rate!.Value, 0.03, 0.18);
    }

    [Fact]
    public void Certificate_age_signal_finds_the_label_when_controls_are_scored_at_their_first_certificate()
    {
        // Sızıntının kendisi: Kontrol ilk sertifika gününde puanlanırsa yaş sinyali neredeyse her pozitifi "yakalar".
        // Bu test, varsayılanın neden MatchedLag olduğunu gösterir.
        var (subjects, observations) = Synthetic.Build(300, 300, 3000);

        var report = EvaluationRunner.Run(
            Config(ControlReferencePolicy.FirstCertificate), EvaluationMode.Historical, subjects, observations, new CertificateAgeSignal(),
            now: Synthetic.Now);

        Assert.True(report.Recall.Rate > 0.9);
    }

    [Fact]
    public void Lag_pool_uses_only_tuning_positives()
    {
        var (subjects, _) = Synthetic.Build(50, 50, 200);
        var config = Config();
        var baseline = ReferenceTimeAssigner.Assign(subjects, config);

        // Test pozitiflerinin ilk sertifikası değiştirilir: Kontrollerin referansı hiç değişmemeli.
        var changed = subjects
            .Select(s => s.IsPositive && !config.TuningBulletins.Contains(s.Bulletin!.Value)
                ? new Subject(s.Id, s.Group, s.Subgroup, s.ReferenceTime, s.Bulletin, s.ClusterId, firstCertificateAt: s.ReferenceTime!.Value.AddDays(-1000))
                : s)
            .ToList();
        var after = ReferenceTimeAssigner.Assign(changed, config);

        Assert.Equal(50, baseline.LagPoolSize);
        Assert.Equal(
            baseline.Subjects.Where(s => !s.IsPositive).Select(s => s.ReferenceTime),
            after.Subjects.Where(s => !s.IsPositive).Select(s => s.ReferenceTime));
    }

    [Fact]
    public void Locked_mode_refuses_to_run_without_a_ticket()
    {
        var (subjects, observations) = Synthetic.Build(20, 20, 100);
        var config = Config();
        config.FreezeDate = Synthetic.Year.AddMonths(11);

        Assert.Throws<InvalidOperationException>(() =>
            EvaluationRunner.Run(config, EvaluationMode.Locked, subjects, observations, new RandomSignal("x")));
    }
}
