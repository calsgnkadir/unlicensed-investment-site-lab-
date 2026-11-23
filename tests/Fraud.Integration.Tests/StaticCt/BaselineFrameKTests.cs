using Fraud.Collectors.StaticCt;
using Fraud.Domain.Signals;
using Fraud.Evaluation.Model;
using Fraud.Signals;

namespace Fraud.Integration.Tests.StaticCt;

/// <summary>
/// Baseline sinyali ile K çerçevesinin süzgeci (ct_candidates, KeywordCandidateFilter) aynı terimleri kullanır. Bu yüzden
/// K'dan gelen bir ad baseline'da tanım gereği 1 alır; tek istisna terimin yalnızca alt alan adında geçmesidir.
/// </summary>
public sealed class BaselineFrameKTests
{
    private static double Baseline(string registrable)
    {
        var subject = new Subject("s-k", SubjectGroup.ControlHardNegative, Subgroup.FrameTermConditioned,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(3)), bulletin: null, "s-k");
        var name = new Observation("s-k", ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue, registrable);
        return new BaselineSignal().Score(AsOfView.Create(subject, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(3)), [name]));
    }

    [Fact]
    public void Filter_and_baseline_read_the_same_terms() =>
        Assert.Equal(BaselineTerms.All, new KeywordCandidateFilter(new CandidateFilterOptions()).Terms);

    [Fact]
    public void Every_registrable_name_the_filter_keeps_alarms_in_baseline()
    {
        var filter = new KeywordCandidateFilter(new CandidateFilterOptions());
        // Uydurma kayıtlı adlar: her terim başta, ortada, sonda; ayrıca hiçbir terim içermeyen bir ad.
        var names = BaselineTerms.All
            .SelectMany(t => new[] { t + "x.example", "on" + t + "son.example", "on-" + t + ".test" })
            .Append("ornek.example")
            .ToList();

        var kept = names.Where(n => filter.Match([n]).Count > 0).ToList();

        Assert.Equal(names.Count - 1, kept.Count); // yalnızca "ornek.example" süzgeçten geçmez
        Assert.All(kept, n => Assert.Equal(1, Baseline(n)));
        Assert.Equal(0, Baseline("ornek.example"));
    }

    [Fact]
    public void Term_only_in_a_subdomain_passes_the_filter_but_not_baseline()
    {
        // Süzgeç DNS adının tamamına bakar; baseline yalnızca kayıtlı adın soldaki etiketine. Bilinen, raporda yazılı fark.
        Assert.NotEmpty(new KeywordCandidateFilter(new CandidateFilterOptions()).Match(["yatirim.ornek.example"]));
        Assert.Equal(0, Baseline("ornek.example"));
    }
}
