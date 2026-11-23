using System.Globalization;
using Fraud.Domain.Signals;
using Fraud.Evaluation;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;

namespace Fraud.Signals.Tests;

/// <summary>Ablasyon, toplam sinyali, katalog ve yapılandırma.</summary>
public sealed class AblationTests
{
    private static readonly DateTimeOffset Now = new(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static EvaluationConfig Config() => new() { TargetFalseAlarmRate = 0.10, BootstrapIterations = 200 };

    /// <summary>
    /// Uydurma veri: pozitiflerin yarısının adında "yatirim" var, kontrollerin yirmide birinde. Adlar yalnızca <c>.example</c>.
    /// Tarih düzeni Fraud.Evaluation.Tests/Synthetic.cs ile aynı (bülten numarası × 4 gün).
    /// </summary>
    private static (List<Subject> Subjects, List<Observation> Observations) Build()
    {
        var subjects = new List<Subject>();
        var observations = new List<Observation>();
        for (var i = 0; i < 200; i++)
        {
            var id = "p" + i.ToString("D4", CultureInfo.InvariantCulture);
            var number = i < 100 ? 1 + (i % 18) : 20 + (i % 58);
            var decision = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3)).AddDays(number * 4);
            var first = decision.AddDays(-20 - (i % 40));
            subjects.Add(new Subject(id, SubjectGroup.Positive, Subgroup.PositiveNew, decision, new BulletinNumber(2026, number), id,
                firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, first, "c-" + id));
            observations.Add(new Observation(id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                (i % 2 == 0 ? "ornekyatirim" : "ornek") + i.ToString(CultureInfo.InvariantCulture) + ".example"));
        }

        for (var i = 0; i < 600; i++)
        {
            var id = "c" + i.ToString("D4", CultureInfo.InvariantCulture);
            var first = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i % 365);
            var group = i % 2 == 0 ? SubjectGroup.ControlHardNegative : SubjectGroup.ControlRandom;
            subjects.Add(new Subject(id, group, Subgroup.FrameNeutral, referenceTime: null, bulletin: null, id, firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, first, "c-" + id));
            observations.Add(new Observation(id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                (i % 20 == 0 ? "denemeborsa" : "deneme") + "x" + i.ToString(CultureInfo.InvariantCulture) + ".test"));
        }

        return (subjects, observations);
    }

    private static ISignal[] Parts() => [new BaselineSignal(), new NumberedNameSignal(), new CertificateBurstSignal(30, 5)];

    [Fact]
    public void Ablation_is_deterministic_and_prints_no_names()
    {
        var (subjects, observations) = Build();

        var a = Ablation.ToText(Ablation.Run(Config(), subjects, observations, Parts(), Now));
        var b = Ablation.ToText(Ablation.Run(Config(), subjects, observations, Parts(), Now));

        Assert.Equal(a, b);
        Assert.DoesNotContain(".example", a, StringComparison.Ordinal);
        Assert.DoesNotContain(".test", a, StringComparison.Ordinal);
        Assert.DoesNotContain("ornek", a, StringComparison.Ordinal);
        Assert.Contains("TANIM GEREĞİ", a, StringComparison.Ordinal); // baseline varsa K notu yazılır
        Assert.Contains("yatırım tavsiyesi değildir", a, StringComparison.Ordinal);
    }

    [Fact]
    public void Ablation_has_each_part_the_sum_and_each_part_removed()
    {
        var (subjects, observations) = Build();

        var rows = Ablation.Run(Config(), subjects, observations, Parts(), Now);

        Assert.Equal(
            ["baseline", "numbered", "cert-burst", "TOPLAM(hepsi)", "TOPLAM − baseline", "TOPLAM − numbered", "TOPLAM − cert-burst"],
            rows.Select(r => r.Variant));
        // Elle beklenen: Pozitiflerin yarısında "yatirim" var, ayar kontrollerinin ~%5'inde "borsa". α = %10 olduğundan
        // baseline'ın eşiği 1'in altında kalır ve test pozitiflerinin yaklaşık yarısını yakalar.
        var baseline = rows[0].Report;
        Assert.InRange(baseline.Recall.Rate!.Value, 0.4, 0.6);
    }

    [Fact]
    public void Sum_adds_binary_parts()
    {
        var view = Views.At(Views.EndOf(5, 1), Views.Name("ornekyatirim24.example"));
        var sum = new SumSignal([new BaselineSignal(), new NumberedNameSignal(), new CertificateBurstSignal(30, 5)]);
        Assert.Equal(2, sum.Score(view)); // baseline 1 + numaralı 1 + patlama 0
        Assert.Equal("sum(baseline+numbered+cert-burst)", sum.Name);
    }

    [Fact]
    public void Catalog_builds_configured_signals_and_requires_resources()
    {
        var config = new SignalConfig { Name = "sum", Parts = ["baseline", "brand", "new-cert-lexicon", "cert-burst"] };
        Assert.True(SignalCatalog.NeedsBrands(config));
        Assert.True(SignalCatalog.NeedsLexicon(config));
        Assert.Throws<InvalidOperationException>(() => SignalCatalog.Create(config, new SignalResources(null, null)));

        var resources = new SignalResources(FinanceLexicon.Parse("yatirim\n"), BrandList.Create(["ornekbank"]));
        Assert.Equal("sum(baseline+brand+new-cert-lexicon+cert-burst)", SignalCatalog.Create(config, resources).Name);

        var single = new SignalConfig { Name = "baseline" };
        Assert.False(SignalCatalog.NeedsBrands(single)); // parçalar yalnızca "sum" ya da ablasyonda okunur
        Assert.Equal("baseline", SignalCatalog.Create(single, new SignalResources(null, null)).Name);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("or")]
    public void Config_rejects_unknown_signal(string name) =>
        Assert.Throws<InvalidOperationException>(() => new SignalConfig { Name = name }.Validate());

    [Fact]
    public void Config_rejects_nested_or_random_parts_and_bad_numbers()
    {
        Assert.Throws<InvalidOperationException>(() => new SignalConfig { Name = "sum", Parts = ["baseline", "sum"] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SignalConfig { Name = "sum", Parts = ["random"] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SignalConfig { Name = "sum", Parts = ["baseline", "baseline"] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SignalConfig { Name = "sum", Parts = [] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SignalConfig { Name = "cert-burst", BurstMinCertificates = 1 }.Validate());
    }

    [Theory]
    [InlineData(EvaluationMode.Locked)]
    [InlineData(EvaluationMode.Prospective)]
    public void Ablation_only_runs_in_historical_mode(EvaluationMode mode)
    {
        var config = new EvaluationConfig
        {
            FreezeDate = Now,
            TimestampAuthorityRootFile = "tsa.pem",
            Signal = new SignalConfig { Name = "sum", Ablation = true },
        };
        Assert.Throws<InvalidOperationException>(() => config.ValidateFor(mode));
        config.ValidateFor(EvaluationMode.Historical);
    }
}
