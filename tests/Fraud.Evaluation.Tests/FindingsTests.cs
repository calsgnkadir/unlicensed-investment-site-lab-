using System.Text;
using Fraud.Evaluation.Commitments;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Splitting;

namespace Fraud.Evaluation.Tests;

/// <summary>
/// Hafta 6 denetim bulgularının (THREAT_MODEL 5.3, E1–E7) ve mentör listesinin (HAFTA6_OLCUM_OKUMA 5.2) düzeltmeleri.
/// </summary>
public sealed class FindingsTests
{
    private static readonly DateTimeOffset Tr0 = new(2026, 3, 10, 0, 0, 0, TimeSpan.FromHours(3));

    // ---- E4: görünüm anı etiket taşımaz ----

    [Fact]
    public void Day_grid_snaps_to_the_last_tick_of_a_turkish_day()
    {
        var afternoon = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.FromHours(3));
        var endOf9 = Tr0.AddTicks(-1);

        Assert.Equal(endOf9, DayGrid.EndOfDayAtOrBefore(afternoon));
        Assert.Equal(endOf9, DayGrid.EndOfDayAtOrBefore(endOf9));
        Assert.Equal(endOf9.AddDays(1), DayGrid.EndOfDayAtOrAfter(afternoon));
        Assert.Equal(Tr0.AddDays(1), DayGrid.NextDayStartAfter(afternoon));
        Assert.Equal(Tr0.AddDays(1), DayGrid.NextDayStartAfter(Tr0)); // tam gün başı: SONRAKİ gün başı
        Assert.Equal(Tr0, DayGrid.DayStartAtOrAfter(Tr0));
    }

    [Fact]
    public void Every_view_time_has_the_same_clock_time_for_positives_and_controls()
    {
        var (subjects, observations) = Synthetic.Build(40, 0, 200);
        var assigned = ReferenceTimeAssigner.Assign(subjects, new EvaluationConfig(), Synthetic.Now).Subjects;
        var spy = new ClockSpy();

        foreach (var s in assigned.Where(s => s.ReferenceTime is not null))
        {
            Predictor.Predict(spy, s, observations, threshold: null);
        }

        // Bütün görünümler TR gününün son anında: UTC 20:59:59.9999999. Saat hiçbir şey söylemez.
        Assert.Single(spy.TimesOfDay);
        Assert.All(assigned.Where(s => !s.IsPositive && s.ReferenceTime is not null),
            s => Assert.Equal(DayGrid.DayStart(s.ReferenceTime!.Value), s.ReferenceTime));
    }

    [Fact]
    public void Predictor_evaluates_every_day_so_a_growing_score_alarms_on_time()
    {
        // Mentör maddesi 4: Tek gözlem 100 gün önce. Yaş sinyali eşiği (30 gün) 30. günde geçer; eskiden yalnızca gözlem
        // anında ve son günde değerlendirdiğimiz için ilk alarm son güne kayardı.
        var reference = Tr0.AddDays(100);
        var subject = new Subject("p", SubjectGroup.Positive, Subgroup.PositiveNew, reference, new BulletinNumber(2026, 20), "p",
            firstCertificateAt: Tr0.AddHours(5));
        var cert = new Observation("p", ObservationKinds.Certificate, Tr0.AddHours(5), Tr0);

        var prediction = Predictor.Predict(new Signals.CertificateAgeSignal(), subject, [cert], threshold: 30);

        Assert.Equal(Tr0.AddDays(31).AddTicks(-1), prediction.FirstAlarmAt);
    }

    // ---- E6 ve mentör 6: küçük hücre ----

    [Fact]
    public void Small_counts_are_suppressed_in_the_report_text()
    {
        Assert.Equal("<5", EvaluationReport.Cell(0));
        Assert.Equal("<5", EvaluationReport.Cell(4));
        Assert.Equal("5", EvaluationReport.Cell(5));
    }

    [Fact]
    public void Sample_precision_follows_the_raw_count_rule_for_small_strata()
    {
        var t = Tr0;
        var subjects = Enumerable.Range(0, 10).Select(i => new Subject("p" + i, SubjectGroup.Positive, Subgroup.PositiveNew, t, new BulletinNumber(2026, 20), "p" + i)).ToList();
        subjects.Add(new Subject("c", SubjectGroup.ControlLicensed, Subgroup.None, t, null, "c"));
        var predictions = subjects.ToDictionary(s => s.Id, s => new Prediction(s.Id, 1, t.AddDays(-1)));

        var (_, _, strata, _) = MetricsCalculator.Compute(new MetricsInput(subjects, predictions, 0.001, null, 10, 100, "s"));

        Assert.Null(Assert.Single(strata).SamplePrecision);
    }

    // ---- E7: yapılandırma sınırları ----

    [Theory]
    [InlineData("""{"bootstrapIterations":20001}""")]
    [InlineData("""{"lockedWindowDays":366}""")]
    [InlineData("""{"bilinmeyenAlan":1}""")]
    [InlineData("""{"signal":{"name":"random","seed":"x","fazla":1}}""")]
    [InlineData("""{"controlReference":7}""")]
    [InlineData("""{"calibrationGroups":[3]}""")]
    [InlineData("""{"prospectiveCommitmentAt":"2026-12-01T00:00:00Z"}""")]
    public void Config_rejects_out_of_bound_unknown_or_numeric_enum_values(string json) =>
        Assert.ThrowsAny<Exception>(() => EvaluationConfig.Parse(json));

    [Fact]
    public void Config_file_size_is_limited_and_lock_file_cannot_be_moved()
    {
        var big = "{\"seed\":\"" + new string('a', EvaluationConfig.MaxFileBytes) + "\"}";
        Assert.ThrowsAny<Exception>(() => EvaluationConfig.Parse(big));

        // E1 (a): "lockFile" yazılsa bile yol değişmez.
        Assert.Equal("data/private/evaluation/locked-run.json", EvaluationConfig.Parse("""{"lockFile":"baska/yer.json"}""").LockFile);
    }

    [Fact]
    public void Locked_mode_validation_happens_before_any_lock_is_taken()
    {
        // Mentör maddesi 5: ValidateFor kilitten önce çağrılır ve F eksikse durur.
        Assert.Throws<InvalidOperationException>(() => new EvaluationConfig().ValidateFor(EvaluationMode.Locked));
        new EvaluationConfig { FreezeDate = Tr0 }.ValidateFor(EvaluationMode.Locked);
    }

    // ---- Mentör 2, 3, 7, 8: bölme ve referans ----

    [Fact]
    public void Excluded_units_tied_to_a_tuning_positive_never_enter_the_prospective_test()
    {
        // p-ayar ayar bülteninde; p-sonra 2026/90 bülteninde (hiçbir aralıkta değil, dışarıda) ama aynı küme "k".
        var subjects = new List<Subject>
        {
            new("p-ayar", SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(5), new BulletinNumber(2026, 5), "k",
                firstCertificateAt: Synthetic.DecisionOf(5).AddDays(-50)),
            new("p-sonra", SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(90), new BulletinNumber(2026, 90), "k",
                firstCertificateAt: Synthetic.DecisionOf(90).AddDays(-50)),
            new("p-bagimsiz", SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(91), new BulletinNumber(2026, 91), "z",
                firstCertificateAt: Synthetic.DecisionOf(91).AddDays(-50)),
        };

        var split = Splitter.Split(subjects, new EvaluationConfig());
        var report = EvaluationRunner.RunProspective(new EvaluationConfig(), subjects, [], new HashSet<string> { "p-sonra", "p-bagimsiz" },
            Synthetic.DecisionOf(60), Synthetic.Now);

        Assert.Contains("p-sonra", split.TiedToTuning);
        Assert.DoesNotContain("p-bagimsiz", split.TiedToTuning);
        Assert.Equal(1, report.TestPositives);
        Assert.Equal(1, report.Recall.Hits);
        Assert.True(double.IsNaN(report.Threshold));
    }

    [Fact]
    public void Control_reference_never_falls_after_now_or_the_freeze_date()
    {
        var (subjects, _) = Synthetic.Build(50, 0, 300);
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var assignment = ReferenceTimeAssigner.Assign(subjects, new EvaluationConfig(), now);

        Assert.True(assignment.ControlsBeyondNow > 0);
        Assert.All(assignment.Subjects.Where(s => !s.IsPositive && s.ReferenceTime is not null), s => Assert.True(s.ReferenceTime <= now));

        var freeze = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var config = new EvaluationConfig { FreezeDate = freeze };
        var split = Splitter.Split(assignment.Subjects, config);
        Assert.All(assignment.Subjects.Where(s => !s.IsPositive && s.ReferenceTime >= freeze),
            s => Assert.True(split.PartOf(s.Id) is SplitPart.Locked or SplitPart.Excluded));
        Assert.True(split.ControlsAfterFreeze > 0);
    }

    [Fact]
    public void First_certificate_after_the_decision_is_unknown_age_and_missing_certificate_is_excluded()
    {
        Assert.Equal(Subgroup.PositiveUnknownAge, Subject.PositiveAge(Tr0, Tr0.AddDays(3)));
        Assert.Equal(Subgroup.PositiveNew, Subject.PositiveAge(Tr0, Tr0.AddDays(-365)));
        Assert.Equal(Subgroup.PositiveEstablished, Subject.PositiveAge(Tr0, Tr0.AddDays(-366)));

        var noCert = new Subject("p", SubjectGroup.Positive, Subgroup.PositiveUnknownAge, Synthetic.DecisionOf(30), new BulletinNumber(2026, 30), "p");
        var split = Splitter.Split([noCert], new EvaluationConfig());
        Assert.Equal(SplitPart.Excluded, split.PartOf("p"));
        Assert.Equal(1, split.PositivesWithoutCertificate);
    }

    // ---- Toplama asimetrisi ----

    [Fact]
    public void Coverage_gap_between_positives_and_a_control_group_is_reported()
    {
        var subjects = new List<Subject>
        {
            new("p1", SubjectGroup.Positive, Subgroup.PositiveNew, Tr0, new BulletinNumber(2026, 20), "p1"),
            new("p2", SubjectGroup.Positive, Subgroup.PositiveNew, Tr0, new BulletinNumber(2026, 20), "p2"),
            new("c1", SubjectGroup.ControlRandom, Subgroup.FrameNeutral, Tr0, null, "c1"),
            new("c2", SubjectGroup.ControlRandom, Subgroup.FrameNeutral, Tr0, null, "c2"),
        };
        var observations = new List<Observation>
        {
            new("p1", ObservationKinds.Archive, Tr0.AddDays(-5), Tr0),
            new("p2", ObservationKinds.Archive, Tr0.AddDays(-5), Tr0),
            new("p1", ObservationKinds.Certificate, Tr0.AddDays(-5), Tr0),
            new("c1", ObservationKinds.Certificate, Tr0.AddDays(-5), Tr0),
        };

        var warnings = CoverageCheck.Warnings(subjects, observations, 0.2);

        // Arşiv: pozitif %100, kontrol %0 → uyarı. Sertifika: ikisi de %50 → uyarı yok.
        var warning = Assert.Single(warnings);
        Assert.Contains("archive", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("c1", warning, StringComparison.Ordinal);
    }

    // ---- E5 ve mentör 1: taahhüt zinciri ve açıklama denetimi ----

    [Fact]
    public void Previous_link_is_verified_and_must_be_a_public_commitment()
    {
        var (first, _) = CommitmentTool.Create(["a.example", "b.example"], Tr0, null);
        var firstBytes = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(first));
        var (second, _) = CommitmentTool.Create(["c.example"], Tr0.AddDays(1), firstBytes);

        Assert.True(CommitmentTool.VerifyPrevious(second, firstBytes));
        var altered = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(first with { Count = 3 }));
        Assert.False(CommitmentTool.VerifyPrevious(second, altered));
        Assert.Throws<ArgumentException>(() => CommitmentTool.Create(["d.example"], Tr0, Encoding.UTF8.GetBytes("herhangi bir dosya")));
    }

    [Fact]
    public void Opening_must_match_the_public_commitment_entry_by_entry()
    {
        var (pub, opening) = CommitmentTool.Create(["a.example", "b.example", "c.example"], Tr0, null);
        Assert.True(CommitmentTool.VerifyOpening(pub, opening));

        // Sonradan listeye kayıt eklemek ya da bir kaydı değiştirmek tutmaz.
        var changed = opening with { Entries = [.. opening.Entries.Select(e => e.Index == 0 ? e with { Record = "x.example" } : e)] };
        Assert.False(CommitmentTool.VerifyOpening(pub, changed));
        Assert.False(CommitmentTool.VerifyOpening(pub with { Count = 2 }, opening));
    }

    // ---- Mentör 5.4 ----

    [Fact]
    public void Small_cells_hide_rate_interval_and_complement()
    {
        // İsabet < 5: "<5/40". Oran (%7,5) yazılsaydı 3 geri hesaplanırdı.
        var few = EvaluationReport.Format(new RateResult(3, 40, 0.075, new Interval(0.01, 0.2), null));
        Assert.StartsWith("<5/40", few, StringComparison.Ordinal);
        Assert.DoesNotContain("%", few, StringComparison.Ordinal);
        Assert.DoesNotContain("7.5", few, StringComparison.Ordinal);

        // Tümleyen < 5: "11/12" tek kalan birimi ele verir. "≥8/12" yazılır, 11 yazılmaz.
        var most = EvaluationReport.Format(new RateResult(11, 12, 11.0 / 12, null, null));
        Assert.StartsWith("≥8/12", most, StringComparison.Ordinal);
        Assert.DoesNotContain("11", most, StringComparison.Ordinal);
        Assert.DoesNotContain("%", most, StringComparison.Ordinal);

        // İkisi de ≥ 5: tam sayı, oran ve aralık.
        var normal = EvaluationReport.Format(new RateResult(20, 40, 0.5, new Interval(0.4, 0.6), null));
        Assert.StartsWith("20/40 = 50%", normal, StringComparison.Ordinal);
    }

    [Fact]
    public void Precision_is_hidden_when_the_stratum_cell_is_small()
    {
        var tiny = new StratumResult(SubjectGroup.ControlRandom, Subgroup.FrameNeutral, false,
            new RateResult(2, 100, 0.02, null, null), 0.7, 0.0244, 100);
        var report = new EvaluationReport(EvaluationMode.Historical, "random", 0.5, 0.01, 50,
            new Dictionary<SplitPart, int> { [SplitPart.Historical] = 110 }, 0, 10, 0, 10, 100,
            new RateResult(5, 10, 0.5, null, null), [], [tiny], new EarlyWarning(0, null, null, null), 0.001, 2000, null);

        var text = report.ToText();

        Assert.DoesNotContain("0.0244", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0.7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("günlük yanlış alarm", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prospective_test_excludes_units_without_a_certificate()
    {
        var c = Synthetic.DecisionOf(60);
        var subjects = new List<Subject>
        {
            new("p-sertifikali", SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(90), new BulletinNumber(2026, 90), "a",
                firstCertificateAt: Synthetic.DecisionOf(90).AddDays(-20)),
            new("p-sertifikasiz", SubjectGroup.Positive, Subgroup.PositiveUnknownAge, Synthetic.DecisionOf(91), new BulletinNumber(2026, 91), "b"),
        };

        var report = EvaluationRunner.RunProspective(new EvaluationConfig(), subjects, [], new HashSet<string>(), c, Synthetic.Now);

        Assert.Equal(1, report.TestPositives);
    }

    [Fact]
    public void Coverage_counts_only_records_visible_before_the_reference()
    {
        var subjects = new List<Subject>
        {
            new("p1", SubjectGroup.Positive, Subgroup.PositiveNew, Tr0, new BulletinNumber(2026, 20), "p1"),
            new("c1", SubjectGroup.ControlRandom, Subgroup.FrameNeutral, Tr0, null, "c1"),
        };
        // Pozitifin arşiv kaydı referanstan SONRA biliniyor: sinyal göremez, kapsama sayılmaz. İki taraf da %0 → uyarı yok.
        var observations = new List<Observation> { new("p1", ObservationKinds.Archive, Tr0.AddDays(2), Tr0.AddDays(2)) };

        Assert.Empty(CoverageCheck.Warnings(subjects, observations, 0.2));

        // Aynı kayıt referanstan önce bilinseydi fark %100 olur ve uyarı çıkar.
        var visible = new List<Observation> { new("p1", ObservationKinds.Archive, Tr0.AddDays(-2), Tr0) };
        Assert.Single(CoverageCheck.Warnings(subjects, visible, 0.2));
    }

    private sealed class ClockSpy : ISignal
    {
        public HashSet<TimeSpan> TimesOfDay { get; } = [];

        public string Name => "clock-spy";

        public double Score(AsOfView view)
        {
            TimesOfDay.Add(view.AsOf.UtcDateTime.TimeOfDay);
            return 0;
        }
    }
}
