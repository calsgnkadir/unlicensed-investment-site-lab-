using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;

namespace Fraud.Evaluation.Tests;

/// <summary>Elle hesaplanmış küçük örnekler. Beklenen değerin hesabı her testin yorumunda.</summary>
public sealed class MetricsTests
{
    private static readonly DateTimeOffset T = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Subject Positive(string id, int bulletin, string? cluster = null) =>
        new(id, SubjectGroup.Positive, Subgroup.PositiveNew, T, new BulletinNumber(2026, bulletin), cluster ?? id);

    private static Subject Control(string id, SubjectGroup group = SubjectGroup.ControlHardNegative, Subgroup subgroup = Subgroup.FrameNeutral, bool linked = false) =>
        new(id, group, subgroup, T, null, id, linked);

    private static MetricsInput Input(List<Subject> subjects, HashSet<string> alarmed, double? daily = null) => new(
        subjects,
        subjects.ToDictionary(s => s.Id, s => new Prediction(s.Id, 0, alarmed.Contains(s.Id) ? T.AddDays(-10) : null)),
        BaseRate: 0.001, DailyNewDomains: daily, MinCountForRates: 10, BootstrapIterations: 200, BootstrapSeed: "m");

    [Fact]
    public void Recall_false_alarm_rate_and_adjusted_precision_match_hand_calculation()
    {
        // 10 pozitif, 5'i yakalandı → recall = 5/10 = 0,5.
        // 100 kontrol (Çerçeve N), 2'si alarm → f = 2/100 = 0,02.
        // Örneklem içi precision = 5 / (5 + 2) = 0,714285...
        // PPV = r·π / (r·π + f·(1−π)) = 0,5·0,001 / (0,0005 + 0,02·0,999) = 0,0005 / 0,02048 = 0,0244140625.
        // Günlük yanlış alarm = f × N_tüm = 0,02 × 5.000 = 100.
        var subjects = new List<Subject>();
        subjects.AddRange(Enumerable.Range(0, 10).Select(i => Positive("p" + i, 20 + i)));
        subjects.AddRange(Enumerable.Range(0, 100).Select(i => Control("c" + i)));
        var alarmed = new HashSet<string> { "p0", "p1", "p2", "p3", "p4", "c0", "c1" };

        var (recall, _, strata, _) = MetricsCalculator.Compute(Input(subjects, alarmed, daily: 5000));

        Assert.Equal((5, 10, 0.5), (recall.Hits, recall.Total, recall.Rate!.Value));
        var s = Assert.Single(strata);
        Assert.Equal((2, 100), (s.FalseAlarms.Hits, s.FalseAlarms.Total));
        Assert.Equal(0.02, s.FalseAlarms.Rate!.Value, 12);
        Assert.Equal(5.0 / 7.0, s.SamplePrecision!.Value, 12);
        Assert.Equal(0.0244140625, s.AdjustedPrecision!.Value, 12);
        Assert.Equal(100, s.ExpectedDailyFalseAlarms!.Value, 9);
    }

    [Fact]
    public void Fewer_than_ten_positives_give_raw_counts_only()
    {
        // 9 pozitif, 3'ü yakalandı: "3/9" yazılır; oran, aralık ve precision yok.
        var subjects = Enumerable.Range(0, 9).Select(i => Positive("p" + i, 20 + i)).ToList();
        subjects.AddRange(Enumerable.Range(0, 50).Select(i => Control("c" + i)));

        var (recall, _, strata, _) = MetricsCalculator.Compute(Input(subjects, ["p0", "p1", "p2", "c0"]));

        Assert.Equal((3, 9), (recall.Hits, recall.Total));
        Assert.Null(recall.Rate);
        Assert.Null(recall.ClusterInterval);
        Assert.Null(strata[0].AdjustedPrecision);
        Assert.Null(strata[0].SamplePrecision);
    }

    [Fact]
    public void Strata_separate_groups_frames_and_positive_linked_controls()
    {
        var subjects = new List<Subject>
        {
            Control("a", SubjectGroup.ControlHardNegative, Subgroup.FrameNeutral),
            Control("b", SubjectGroup.ControlHardNegative, Subgroup.FrameTermConditioned),
            Control("c", SubjectGroup.ControlHardNegative, Subgroup.FrameNeutral, linked: true),
            Control("d", SubjectGroup.ControlLicensed, Subgroup.None),
            Control("e", SubjectGroup.ControlCryptoActive, Subgroup.None),
        };

        var (_, _, strata, _) = MetricsCalculator.Compute(Input(subjects, []));

        Assert.Equal(5, strata.Count);
        Assert.Single(strata, s => s.LinkedToPositive);
        Assert.Contains(strata, s => s.Subgroup == Subgroup.FrameTermConditioned);
        Assert.Contains(strata, s => s.Group == SubjectGroup.ControlCryptoActive);
    }

    [Fact]
    public void Adjusted_precision_is_not_written_when_no_false_alarm_was_seen() =>
        Assert.Null(MetricsCalculator.AdjustedPrecision(0.5, 0, 0.001));

    [Fact]
    public void Early_warning_median_min_max_match_hand_calculation()
    {
        // Karar T. İlk alarmlar T−30, T−10, T−20 gün; biri alarm yok. Sıralı: 10, 20, 30 → ortanca 20, en az 10, en çok 30.
        var positives = new List<Subject> { Positive("a", 20), Positive("b", 21), Positive("c", 22), Positive("d", 23) };
        var predictions = new Dictionary<string, Prediction>
        {
            ["a"] = new("a", 1, T.AddDays(-30)),
            ["b"] = new("b", 1, T.AddDays(-10)),
            ["c"] = new("c", 1, T.AddDays(-20)),
            ["d"] = new("d", 0, null),
        };

        // Hesabı göstermek için en küçük sayı 1 verildi.
        var early = MetricsCalculator.EarlyWarningOf(positives, predictions, minCount: 1);
        Assert.Equal((3, 20.0, 10.0, 30.0), (early.AlarmedPositives, early.MedianDays!.Value, early.MinDays!.Value, early.MaxDays!.Value));

        // Gerçek kural (mentör maddesi 6): 10'dan az yakalanan pozitifte süre yazılmaz, yalnızca sayı.
        var raw = MetricsCalculator.EarlyWarningOf(positives, predictions, minCount: 10);
        Assert.Equal(3, raw.AlarmedPositives);
        Assert.Null(raw.MedianDays);
        Assert.Null(raw.MinDays);
    }

    [Fact]
    public void Threshold_selection_matches_hand_calculation()
    {
        // Puanlar 0,9 0,8 0,7 0,6 0,5; α = 0,2 → izin verilen alarm = floor(0,2·5) = 1 → eşik 0,8'in hemen üstü.
        List<double> scores = [0.5, 0.9, 0.7, 0.6, 0.8];
        var t = ThresholdSelector.Select(scores, 0.2);
        Assert.Equal(Math.BitIncrement(0.8), t);
        Assert.Equal(1, scores.Count(s => s >= t));

        // Eşitlik: 0,8 iki kez. Eşik 0,8'in üstünde kalır; alarm 1 (≤ α·n), 3 değil.
        List<double> tiedScores = [0.9, 0.8, 0.8, 0.5, 0.4];
        var tied = ThresholdSelector.Select(tiedScores, 0.2);
        Assert.Equal(1, tiedScores.Count(s => s >= tied));

        // 0,29·100 kayan nokta artığı: izin verilen alarm 29 olmalı (28 değil).
        var hundred = Enumerable.Range(0, 100).Select(i => (double)i).ToList();
        Assert.Equal(29, hundred.Count(s => s >= ThresholdSelector.Select(hundred, 0.29)));
    }
}
