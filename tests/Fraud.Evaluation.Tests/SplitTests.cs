using Fraud.Evaluation.Model;
using Fraud.Evaluation.Splitting;

namespace Fraud.Evaluation.Tests;

/// <summary>Bölmede sızıntı yok: aynı bülten ya da küme iki tarafa düşmez; sınırlar onaylı kurallarla aynı.</summary>
public sealed class SplitTests
{
    private static readonly DateTimeOffset T = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static Subject Positive(string id, int bulletin, string? cluster = null, DateTimeOffset? first = null) =>
        new(id, SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(bulletin), new BulletinNumber(2026, bulletin),
            cluster ?? id, firstCertificateAt: first ?? T.AddMonths(-3));

    [Theory]
    [InlineData(1, SplitPart.Tuning)]
    [InlineData(18, SplitPart.Tuning)]
    [InlineData(19, SplitPart.Excluded)]
    [InlineData(20, SplitPart.Historical)]
    [InlineData(77, SplitPart.Historical)]
    [InlineData(78, SplitPart.Excluded)]
    public void Positive_bulletin_ranges_follow_decision_1(int bulletin, SplitPart expected)
    {
        var split = Splitter.Split([Positive("p", bulletin)], new EvaluationConfig());
        Assert.Equal(expected, split.PartOf("p"));
    }

    [Fact]
    public void Bulletins_compare_as_numbers_not_text() =>
        Assert.True(new BulletinNumber(2026, 9) < BulletinNumber.Parse("2026/12"));

    [Fact]
    public void A_cluster_spanning_both_sides_moves_entirely_to_tuning()
    {
        // p1 ayar bülteninde, p2 test bülteninde ama ikisi aynı küme "k". p3 de p2 ile aynı bültende (2026/30):
        // bülten bütünlüğü için o da ayara taşınır. Taşınan: p2 ve p3 = 2.
        var subjects = new List<Subject> { Positive("p1", 5, "k"), Positive("p2", 30, "k"), Positive("p3", 30), Positive("p4", 40) };

        var split = Splitter.Split(subjects, new EvaluationConfig());

        Assert.Equal(SplitPart.Tuning, split.PartOf("p2"));
        Assert.Equal(SplitPart.Tuning, split.PartOf("p3"));
        Assert.Equal(SplitPart.Historical, split.PartOf("p4"));
        Assert.Equal(2, split.MovedForIntegrity);
    }

    [Fact]
    public void No_bulletin_or_cluster_appears_on_both_sides_in_a_large_random_case()
    {
        var (subjects, _) = Synthetic.Build(200, 200, 1000);
        // Rastgele kümeler: Her 7 birimden biri bir başkasıyla aynı kümede.
        var clustered = subjects.Select((s, i) => i % 7 == 0
            ? (s.IsPositive
                ? new Subject(s.Id, s.Group, s.Subgroup, s.ReferenceTime, s.Bulletin, "shared-" + (i % 13), firstCertificateAt: s.FirstCertificateAt)
                : new Subject(s.Id, s.Group, s.Subgroup, s.ReferenceTime, null, "shared-" + (i % 13), firstCertificateAt: s.FirstCertificateAt))
            : s).ToList();
        var config = new EvaluationConfig();
        var assigned = ReferenceTimeAssigner.Assign(clustered, config).Subjects;

        var split = Splitter.Split(assigned, config);

        var used = assigned.Where(s => split.PartOf(s.Id) != SplitPart.Excluded).ToList();
        Assert.All(used.GroupBy(s => s.ClusterId), g => Assert.Single(g.Select(s => split.PartOf(s.Id)).Distinct()));
        Assert.All(used.Where(s => s.Bulletin is not null).GroupBy(s => s.Bulletin), g => Assert.Single(g.Select(s => split.PartOf(s.Id)).Distinct()));
        Assert.True(split.Count(SplitPart.Historical) > 0);
    }

    [Fact]
    public void Locked_window_is_F_to_F_plus_28_days_and_untimed_controls_never_enter_it()
    {
        var freeze = new DateTimeOffset(2026, 12, 6, 0, 0, 0, TimeSpan.Zero);
        var config = new EvaluationConfig { FreezeDate = freeze };
        Subject Late(string id, DateTimeOffset decision) =>
            new(id, SubjectGroup.Positive, Subgroup.PositiveNew, decision, new BulletinNumber(2026, 90), id, firstCertificateAt: decision.AddDays(-40));
        Subject Ctrl(string id, Subgroup subgroup, DateTimeOffset reference) =>
            new(id, SubjectGroup.ControlRandom, subgroup, reference, null, id, firstCertificateAt: reference.AddDays(-5));

        var split = Splitter.Split(
        [
            Late("in", freeze.AddDays(10)), Late("edge", freeze.AddDays(28)), Late("after", freeze.AddDays(29)),
            Ctrl("n-in", Subgroup.FrameNeutral, freeze.AddDays(3)), Ctrl("n-after", Subgroup.FrameNeutral, freeze.AddDays(40)),
            Ctrl("tranco", Subgroup.None, freeze.AddDays(3)),
        ], config);

        Assert.Equal(SplitPart.Locked, split.PartOf("in"));
        Assert.Equal(SplitPart.Locked, split.PartOf("edge"));
        Assert.Equal(SplitPart.Excluded, split.PartOf("after"));
        Assert.Equal(SplitPart.Locked, split.PartOf("n-in"));
        Assert.Equal(SplitPart.Excluded, split.PartOf("n-after"));
        // Zamanla eşleşmeyen kontrol F'den sonra hiçbir bölmeye girmez (mentör maddesi 3); kilitli teste de girmez.
        Assert.Equal(SplitPart.Excluded, split.PartOf("tranco"));
        Assert.Equal(2, split.ControlsAfterFreeze);
    }

    [Fact]
    public void Controls_in_months_without_positives_go_to_tuning_only()
    {
        var control = new Subject("c", SubjectGroup.ControlHardNegative, Subgroup.FrameNeutral, T, null, "c",
            firstCertificateAt: new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero));

        var split = Splitter.Split([Positive("p", 30), control], new EvaluationConfig());

        Assert.Equal(SplitPart.Tuning, split.PartOf("c"));
    }

    [Fact]
    public void Cdn_edges_do_not_join_clusters()
    {
        var clusters = ClusterBuilder.Build(["a", "b", "c", "d"],
        [
            new ClusterEdge("a", "b", ClusterEdgeKind.SharedCertificate),
            new ClusterEdge("b", "c", ClusterEdgeKind.SharedNetwork, ViaCdnOrCloud: true),
            new ClusterEdge("c", "d", ClusterEdgeKind.NameStem),
        ]);

        Assert.Equal(clusters["a"], clusters["b"]);
        Assert.Equal(clusters["c"], clusters["d"]);
        Assert.NotEqual(clusters["a"], clusters["c"]);
    }
}
