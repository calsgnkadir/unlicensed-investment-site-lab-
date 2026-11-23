using System.Diagnostics;
using Fraud.Domain.Signals;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Security.Tests.Infrastructure;
using Fraud.Signals;

namespace Fraud.Security.Tests.Signals;

/// <summary>
/// Hafta 7 sinyalleri: gelecek sızıntısı (görünüm dışı veri, pencere sınırları, ad gözlemi), uzun ad ile DoS ve
/// kilitli testte sinyal kaynaklarının (marka listesi) dondurulmuş olması. Gerçek alan adı yok.
/// </summary>
public sealed class SignalAttackTests
{
    private static readonly TimeSpan Tr = TimeSpan.FromHours(3);
    private static readonly DateTimeOffset Reference = new(2026, 6, 10, 0, 0, 0, Tr);

    private static Subject Control(string id) =>
        new(id, SubjectGroup.ControlRandom, Subgroup.FrameNeutral, Reference, null, id);

    private static Observation Name(string id, string name) =>
        new(id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue, name);

    private static Observation Cert(string id, DateTimeOffset knownAt, string? value = null) =>
        new(id, ObservationKinds.Certificate, knownAt, knownAt, value);

    private static AsOfView ViewAt(Subject s, DateTimeOffset asOf, IEnumerable<Observation> obs) => AsOfView.Create(s, asOf, obs);

    [Fact]
    public void Burst_counts_only_certificates_known_inside_the_window_and_before_the_view()
    {
        var s = Control("s-a");
        var asOf = Reference.AddDays(-1).AddTicks(-1);
        var obs = new List<Observation> { Name("s-a", "ornek.example") };
        for (var i = 0; i < 4; i++)
        {
            obs.Add(Cert("s-a", asOf.AddDays(-2), "c" + i));
        }

        obs.Add(Cert("s-a", asOf.AddDays(-31), "eski"));     // pencere dışı (30 gün)
        obs.Add(Cert("s-a", Reference.AddDays(5), "gelecek")); // görünümden sonra
        obs.Add(Cert("s-b", asOf.AddDays(-1), "baska"));       // başka birim

        var burst = new CertificateBurstSignal(windowDays: 30, minCertificates: 5);

        Assert.Equal(0, burst.Score(ViewAt(s, asOf, obs)));
        obs.Add(Cert("s-a", asOf.AddDays(-3), "c4"));
        Assert.Equal(1, burst.Score(ViewAt(s, asOf, obs)));
    }

    [Fact]
    public void New_certificate_signal_cannot_see_a_certificate_issued_after_the_view()
    {
        var lexicon = FinanceLexicon.Load(File.ReadAllBytes(Path.Combine(RepoFiles.Root, FinanceLexicon.RelativePath)));
        var s = Control("s-a");
        var asOf = Reference.AddDays(-1).AddTicks(-1);
        var term = lexicon.Match("yatirimborsa.example") is { } m ? "yatirimborsa.example" : "borsa.example";
        var obs = new List<Observation> { Name("s-a", term), Cert("s-a", asOf.AddDays(-400)), Cert("s-a", Reference.AddDays(2)) };

        // Görünümde tek bilinen sertifika 400 gün önce: "yeni" değil. Kararın ertesi günkü sertifika görünmez.
        Assert.Equal(0, new NewCertificateLexiconSignal(lexicon, 28).Score(ViewAt(s, asOf, obs)));
    }

    [Fact]
    public void Name_observation_without_time_does_not_crash_or_start_the_evaluation_at_min_value()
    {
        var s = Control("s-a");
        var obs = new[] { Name("s-a", "borsa1.example") };

        var prediction = Predictor.Predict(new NumberedNameSignal(), s, obs, threshold: 1);

        Assert.Equal(1, prediction.Score);
        Assert.NotNull(prediction.FirstAlarmAt);
        Assert.True(prediction.FirstAlarmAt > Reference.AddDays(-2), "Değerlendirme yalnızca son görünüm gününde olmalı");
    }

    [Fact]
    public void Signals_read_only_the_view_and_never_another_subjects_name()
    {
        // Görünüm, başka birimin ad gözlemini içermez; sinyal yalnızca kendi adını görür.
        var s = Control("s-a");
        var asOf = Reference.AddDays(-1).AddTicks(-1);
        var view = ViewAt(s, asOf, [Name("s-b", "garanti1.example"), Name("s-a", "temiz.example")]);

        Assert.Equal("temiz.example", SubjectName.Registrable(view));
        Assert.Equal(0, new NumberedNameSignal().Score(view));
    }

    [Fact]
    public void Brand_similarity_is_bounded_on_hostile_labels()
    {
        var brands = BrandList.Create(Enumerable.Range(0, 200).Select(i => "markaadi" + (char)('a' + (i % 26)) + (char)('a' + (i / 26))));
        var signal = new BrandSimilaritySignal(brands);
        var stopwatch = Stopwatch.StartNew();

        foreach (var label in new[] { new string('a', 63), new string('ı', 63), "xn--" + new string('a', 59), new string('0', 10_000), "rnrnrnrnvvvv" })
        {
            _ = signal.ScoreLabel(label);
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Süre {stopwatch.Elapsed}");
        Assert.DoesNotContain("Regex", RepoFiles.Read("src/Fraud.Signals/BrandSimilaritySignal.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Regex", RepoFiles.Read("src/Fraud.Signals/BrandList.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_list_stays_private_and_its_names_are_never_printed()
    {
        Assert.StartsWith("data/private/", BrandList.DomesticRelativePath, StringComparison.Ordinal);
        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/SignalCommands.cs") + RepoFiles.Read("src/Fraud.Evaluation/Running/Ablation.cs");

        // Çıktı satırları yalnızca sayı ve özet; etiket ya da token listesi basılmaz.
        Assert.DoesNotMatch(@"\{(label|labels|names|name|token|brands\.Tokens|brands\.Plain)\}", code);
        Assert.DoesNotMatch(@"Write(Line)?Async\([^;]*\+\s*(label|name|token)\b", code);
        Assert.DoesNotMatch(@"string\.Join\([^;]*(labels|names|Tokens|Plain)\b", code);
    }

    // ---- Açık bulgular ----

    [Fact]
    public void Locked_run_binds_the_domestic_brand_list_to_the_frozen_version()
    {
        // Bulgu S1: Marka listesi sinyal kuralının parçasıdır, ama git dışı bir dosyadır. Kilitli test temiz worktree'de derlenir;
        // yapılandırmanın özeti kilit kaydına girer; brands.txt ise girmez ve hiçbir dondurulmuş değerle karşılaştırılmaz.
        // Dondurma (F) ile kilitli çalıştırma (F + 28 gün sonrası) arasında listeye test penceresindeki SPK kararlarından
        // (herkese açık) markalar eklenip sonra kilitli test "ilk kez" çalıştırılabilir; sonuç sızıntılı olur ve kayıt bunu
        // göstermez. Listenin SHA-256'sı dondurma commit'inde izlenen bir dosyada (ör. yapılandırmada) sabitlenmeli, kilitli
        // ve ileriye dönük kipte eşleşmezse çalışma durmalı, özet kilit kaydına yazılmalı. Daha basit yol: listeyi kilitli kipte
        // dosyadan değil, taahhüdü zaten olan G1 üyelerinden yeniden üretmek.
        var recordProperties = typeof(LockedRunRecord).GetProperties().Select(p => p.Name).ToList();
        var config = typeof(Fraud.Evaluation.SignalConfig).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains(recordProperties, n => n.Contains("Brand", StringComparison.Ordinal) || n.Contains("Resource", StringComparison.Ordinal));
        Assert.Contains(config, n => n.Contains("Brand", StringComparison.Ordinal) && n.Contains("Sha", StringComparison.Ordinal));
    }
}
