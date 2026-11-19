using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;

namespace Fraud.Evaluation.Tests;

/// <summary>Gelecek görünmez, kilitli test bir kez çalışır, bootstrap deterministik, "ne zaman bilinir" kuralı doğru.</summary>
public sealed class SafetyTests
{
    private static readonly DateTimeOffset Decision = new(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
    private const string CommitA = "0123456789abcdef0123456789abcdef01234567";
    private const string CommitB = "89abcdef0123456789abcdef0123456789abcdef";

    private static Subject Positive() =>
        new("p", SubjectGroup.Positive, Subgroup.PositiveNew, Decision, new BulletinNumber(2026, 31), "p");

    [Fact]
    public void AsOf_view_never_shows_the_future()
    {
        var observations = new List<Observation>
        {
            new("p", ObservationKinds.Certificate, Decision.AddDays(-30), Decision.AddDays(100)),
            new("p", ObservationKinds.Archive, Decision.AddDays(-1), Decision.AddDays(100)),
            new("p", ObservationKinds.Dns, Decision.AddDays(170), Decision.AddDays(170)),     // bugünkü DNS
            new("other", ObservationKinds.Certificate, Decision.AddDays(-50), Decision),       // başka birim
        };

        var view = AsOfView.Create(Positive(), Decision.AddDays(-10), observations);

        Assert.Single(view.Observations);
        Assert.All(view.Observations, o => Assert.True(o.KnownAt <= view.AsOf));
        Assert.Throws<ArgumentOutOfRangeException>(() => AsOfView.Create(Positive(), Decision, observations));
        Assert.Throws<ArgumentOutOfRangeException>(() => AsOfView.Create(Positive(), Decision.AddDays(1), observations));
    }

    [Fact]
    public void Predictor_scores_only_before_the_reference_time()
    {
        var observations = new List<Observation>
        {
            new("p", ObservationKinds.Certificate, Decision.AddDays(-30), Decision),
            new("p", ObservationKinds.Dns, Decision.AddDays(1), Decision.AddDays(1)),
        };
        var spy = new SpySignal();

        Predictor.Predict(spy, Positive(), observations, threshold: 0);

        Assert.All(spy.Seen, v => Assert.True(v.AsOf < Decision));
        Assert.DoesNotContain(spy.Seen, v => v.Observations.Any(o => o.Kind == ObservationKinds.Dns));
    }

    [Fact]
    public void Network_records_never_reach_a_historical_view()
    {
        // KnownAt = queried_at: Bugün sorulmuş DNS, Mayıs kararından önceki hiçbir görünümde yok.
        var dns = new Observation("p", ObservationKinds.Dns, KnownAtPolicy.Network(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero)), DateTimeOffset.UtcNow);
        Assert.Empty(AsOfView.Create(Positive(), Decision.AddTicks(-1), [dns]).Observations);
    }

    [Fact]
    public void Known_at_policy_follows_the_configured_rules()
    {
        var captured = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var observed = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(captured.AddDays(7), KnownAtPolicy.Archive(captured, observed, KnownAtPolicy.DefaultArchiveDelay, prospective: false));
        Assert.Equal(observed, KnownAtPolicy.Archive(captured, observed, KnownAtPolicy.DefaultArchiveDelay, prospective: true));
        Assert.Equal(captured, KnownAtPolicy.Certificate(captured));
    }

    [Fact]
    public void Locked_run_lock_is_single_use_unless_a_rerun_is_explicitly_marked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fraud-eval-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "locked-run.json");
        try
        {
            // Commit 40 karakterlik git kimliği değilse kilit hiç oluşturulmaz (E1).
            Assert.Throws<ArgumentException>(() => LockedRunGuard.Acquire(file, "aa11", "son-surum", Decision));
            Assert.Throws<ArgumentException>(() => LockedRunGuard.Acquire(file, "aa11", "0123456", Decision));
            Assert.False(File.Exists(file));

            var first = LockedRunGuard.Acquire(file, "aa11", CommitA, Decision);
            Assert.False(first.IsRerun);

            var ex = Assert.Throws<LockedRunAlreadyUsedException>(() => LockedRunGuard.Acquire(file, "aa11", CommitA, Decision.AddDays(1)));
            Assert.Contains(CommitA, ex.Message, StringComparison.Ordinal);
            // Gerekçesiz yeniden çalıştırma da reddedilir.
            Assert.Throws<LockedRunAlreadyUsedException>(() => LockedRunGuard.Acquire(file, "aa11", CommitA, Decision, allowRerun: true));

            var rerun = LockedRunGuard.Acquire(file, "bb22", CommitB, Decision.AddDays(2), allowRerun: true, rerunReason: "veri hatası");
            Assert.True(rerun.IsRerun);
            Assert.Equal(CommitA, rerun.First.Commit);
            Assert.Equal("aa11", rerun.First.ConfigSha256);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Rerun_report_is_marked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fraud-eval-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "locked-run.json");
        try
        {
            LockedRunGuard.Acquire(file, "aa", CommitA, Decision);
            var ticket = LockedRunGuard.Acquire(file, "aa", CommitA, Decision, allowRerun: true, rerunReason: "deneme");
            var (subjects, observations) = Synthetic.Build(30, 0, 200);
            var config = new EvaluationConfig { FreezeDate = Synthetic.Year.AddMonths(10), BootstrapIterations = 100 };

            var text = EvaluationRunner.Run(config, EvaluationMode.Locked, subjects, observations, new Signals.RandomSignal("r"), ticket, Synthetic.Now).ToText();

            Assert.StartsWith("*** YENİDEN ÇALIŞTIRMA", text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Bootstrap_is_deterministic_for_a_seed_and_changes_with_the_seed()
    {
        // 20 birim, her birinde ardışık 3 satır; isabet her 3 satırda bir (i % 3 == 0) değil, i % 5 == 0 → birimler farklı oranda.
        var rows = Enumerable.Range(0, 60).Select(i => new BootstrapRow("k" + (i / 3), i % 5 == 0)).ToList();

        var a = Bootstrap.PercentileInterval(rows, 2000, "tohum-1");
        var b = Bootstrap.PercentileInterval(rows, 2000, "tohum-1");
        // Başka tohum: Oranlar 1/60'ın katları olduğu için yüzdelikler aynı çıkabilir; yalnızca geçerli bir aralık beklenir.
        var c = Bootstrap.PercentileInterval(rows, 2000, "tohum-2");

        Assert.Equal(a, b);
        Assert.InRange(12.0 / 60, c!.Low, c.High);
        // Nokta tahmini 12/60 = 0,2 aralığın içinde ve aralık bir noktaya çökmemiş.
        Assert.InRange(12.0 / 60, a!.Low, a.High);
        Assert.True(a.Low < a.High);
    }

    [Fact]
    public void Bootstrap_resamples_whole_units()
    {
        // Tek birim (bütün satırlar aynı kümede): Her tekrarda aynı birim çekilir, aralık noktaya çöker.
        var rows = Enumerable.Range(0, 30).Select(i => new BootstrapRow("tek", i < 12)).ToList();
        var interval = Bootstrap.PercentileInterval(rows, 500, "s");
        Assert.Equal(new Interval(0.4, 0.4), interval);
    }

    [Fact]
    public void Config_parses_and_rejects_overlapping_ranges()
    {
        var config = EvaluationConfig.Parse("""{ "seed": "x", "targetFalseAlarmRate": 0.02, "calibrationGroups": ["ControlRandom"], "controlReference": "MatchedLag" }""");
        Assert.Equal(0.02, config.TargetFalseAlarmRate);
        Assert.Equal([SubjectGroup.ControlRandom], config.CalibrationGroups);
        Assert.Equal(2000, config.BootstrapIterations);

        Assert.Throws<InvalidOperationException>(() => EvaluationConfig.Parse(
            """{ "tuningBulletins": { "from": "2026/1", "to": "2026/30" }, "historicalBulletins": { "from": "2026/20", "to": "2026/77" } }"""));
    }

    [Fact]
    public void Example_config_in_the_repo_parses_to_the_defaults()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FraudLab.slnx")))
        {
            dir = dir.Parent;
        }

        var json = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Fraud.Evaluation", "evaluation.example.json"));
        var parsed = EvaluationConfig.Parse(json);
        var defaults = new EvaluationConfig();

        Assert.Equal(
            (defaults.TargetFalseAlarmRate, defaults.BootstrapIterations, defaults.BaseRate, defaults.ArchiveKnownDelayDays, defaults.ControlReference),
            (parsed.TargetFalseAlarmRate, parsed.BootstrapIterations, parsed.BaseRate, parsed.ArchiveKnownDelayDays, parsed.ControlReference));
        Assert.Equal(defaults.CalibrationGroups, parsed.CalibrationGroups);
    }

    private sealed class SpySignal : ISignal
    {
        public List<AsOfView> Seen { get; } = [];

        public string Name => "spy";

        public double Score(AsOfView view)
        {
            Seen.Add(view);
            return 1;
        }
    }
}
