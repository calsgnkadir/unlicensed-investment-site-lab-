using Fraud.Evaluation;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Splitting;

namespace Fraud.Security.Tests.Evaluation;

/// <summary>
/// Ölçüm bütünlüğü (Kural 7): kilitli test bir kez, sinyal etiketi göremez, yapılandırma güvenilmez girdidir, rapor
/// tek birimi açmaz. Kilit dosyaları test başına geçici klasörde; data/private'a dokunulmaz.
/// </summary>
public sealed class EvaluationIntegrityAttackTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("locked-run-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string LockPath => Path.Combine(_dir.FullName, "locked-run.json");

    [Fact]
    public void Second_locked_run_on_the_same_lock_file_is_refused_or_marked_as_rerun()
    {
        var first = LockedRunGuard.Acquire(LockPath, "cfg", Commit, Now);
        Assert.False(first.IsRerun);

        Assert.Throws<LockedRunAlreadyUsedException>(() => LockedRunGuard.Acquire(LockPath, "cfg", Commit, Now.AddHours(1)));
        Assert.Throws<LockedRunAlreadyUsedException>(() => LockedRunGuard.Acquire(LockPath, "cfg", Commit, Now, allowRerun: true, rerunReason: " "));

        var rerun = LockedRunGuard.Acquire(LockPath, "cfg2", Commit, Now.AddDays(1), allowRerun: true, rerunReason: "veri düzeltmesi");
        Assert.True(rerun.IsRerun);
        Assert.Equal(Now, rerun.First.StartedAt);
    }

    [Fact]
    public void Concurrent_first_runs_produce_exactly_one_first_ticket()
    {
        var results = Enumerable.Range(0, 16).AsParallel().Select(_ =>
        {
            try
            {
                return LockedRunGuard.Acquire(LockPath, "cfg", Commit, Now).IsRerun ? "rerun" : "first";
            }
            catch (LockedRunAlreadyUsedException)
            {
                return "refused";
            }
            catch (IOException)
            {
                return "io";
            }
            catch (System.Text.Json.JsonException)
            {
                return "partial"; // diğer süreç henüz yazarken okundu: yine "ilk" sayılmaz
            }
        }).ToList();

        Assert.Equal(1, results.Count(r => r == "first"));
        Assert.DoesNotContain("rerun", results);
    }

    [Fact]
    public void Locked_mode_cannot_run_without_a_ticket_and_a_freeze_date()
    {
        var config = new EvaluationConfig();
        var signal = new Fraud.Evaluation.Signals.RandomSignal("x");

        Assert.Throws<InvalidOperationException>(() => EvaluationRunner.Run(config, EvaluationMode.Locked, [], [], signal, lockTicket: null));
    }

    [Fact]
    public void Signal_view_contains_only_the_subjects_own_observations_known_before_the_reference()
    {
        var reference = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.FromHours(3));
        var subject = new Subject("s-a", SubjectGroup.ControlRandom, Subgroup.FrameNeutral, reference, null, "s-a");
        var observations = new[]
        {
            new Observation("s-a", ObservationKinds.Certificate, reference.AddDays(-10), reference.AddDays(30)),
            new Observation("s-a", ObservationKinds.Dns, reference, reference),                 // karar anı: görünmez
            new Observation("s-a", ObservationKinds.Archive, reference.AddDays(1), reference.AddDays(1)),
            new Observation("s-b", ObservationKinds.Certificate, reference.AddDays(-20), reference.AddDays(-20)), // başka birim
        };

        var view = AsOfView.Create(subject, reference.AddTicks(-1), observations);

        Assert.Single(view.Observations);
        Assert.All(view.Observations, o => Assert.Equal("s-a", o.SubjectId));
        Assert.Throws<ArgumentOutOfRangeException>(() => AsOfView.Create(subject, reference, observations));

        // Görünümde grup, alt grup, bülten veya küme yok: yalnızca kimlik, an ve gözlemler.
        Assert.Equal(["AsOf", "Observations", "SubjectId"],
            typeof(AsOfView).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("""{"targetFalseAlarmRate":0}""")]
    [InlineData("""{"targetFalseAlarmRate":1}""")]
    [InlineData("""{"baseRate":-0.1}""")]
    [InlineData("""{"bootstrapIterations":5}""")]
    [InlineData("""{"calibrationGroups":["Positive"]}""")]
    [InlineData("""{"calibrationGroups":[]}""")]
    [InlineData("""{"tuningBulletins":{"from":"2026/1","to":"2026/30"}}""")]   // ayar testle çakışıyor
    [InlineData("""{"tuningBulletins":{"from":"x","to":"y"}}""")]
    [InlineData("""{"archiveKnownDelayDays":-1}""")]
    public void Config_values_out_of_range_are_rejected(string json)
    {
        Assert.ThrowsAny<Exception>(() => EvaluationConfig.Parse(json));
    }

    // ---- Açık bulgular ----

    [Fact]
    public void Config_cannot_move_the_locked_run_lock_file()
    {
        // Bulgu E1 (a): LockFile ayarlanabilir. "lockFile" değiştirilmiş (veya çalışma klasörü değiştirilmiş) bir
        // yapılandırmayla Locked kip yeni bir dosyada CreateNew ile "ilk" bilet alır; rapor YENİDEN ÇALIŞTIRMA demez.
        var config = EvaluationConfig.Parse("""{"lockFile":"baska/yer/locked-run.json"}""");

        Assert.Equal(new EvaluationConfig().LockFile, config.LockFile);
    }

    [Fact]
    public void Locked_run_commit_must_look_like_a_git_commit()
    {
        // Bulgu E1 (b): Kilit kaydındaki commit, "hangi kodla çalıştı" sorusunun tek cevabı. Serbest metin kabul edilirse
        // ("son", "HEAD", uydurma bir SHA) kayıt hiçbir şey kanıtlamaz. En az 40 karakter onaltılık olmalı; ideal olan
        // komutun git rev-parse HEAD ile karşılaştırması ve çalışma ağacının temiz olması.
        Assert.ThrowsAny<ArgumentException>(() => LockedRunGuard.Acquire(LockPath, "cfg", "son-surum", Now));
        Assert.ThrowsAny<ArgumentException>(() => LockedRunGuard.Acquire(Path.Combine(_dir.FullName, "b.json"), "cfg", "deadbeef", Now));
    }

    [Fact]
    public void Signal_cannot_tell_positives_from_controls_by_the_view_time()
    {
        // Bulgu E4: Pozitif referansı karar günü 00:00 (UTC+3); kontrol referansı ilk sertifika + gecikme + 1 sn, yani
        // günün herhangi bir saati. Görünümün son anı referans - 1 tik. Saatine bakan bir sinyal (ör. "gece yayımlanan
        // sertifika" gibi bir özellik yazarken AsOf'u kullanan) etiketi kusursuz ayırır ve ölçüm sessizce şişer.
        var subjects = new List<Subject>();
        var observations = new List<Observation>();
        for (var i = 0; i < 20; i++)
        {
            var decision = new DateTimeOffset(2026, 1, 5 + i, 0, 0, 0, TimeSpan.FromHours(3));
            var first = decision.AddDays(-30 - i).AddMinutes(37 * i);
            var id = "p" + i;
            subjects.Add(new Subject(id, SubjectGroup.Positive, Subgroup.PositiveNew, decision, new BulletinNumber(2026, 1 + (i % 18)), id,
                firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, first));
        }

        for (var i = 0; i < 20; i++)
        {
            var first = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero).AddHours(13 * i + 5).AddMinutes(11 * i);
            var id = "c" + i;
            subjects.Add(new Subject(id, SubjectGroup.ControlRandom, Subgroup.FrameNeutral, null, null, id, firstCertificateAt: first));
            observations.Add(new Observation(id, ObservationKinds.Certificate, first, first));
        }

        var assigned = ReferenceTimeAssigner.Assign(subjects, new EvaluationConfig()).Subjects;
        var leaky = new ClockSignal();
        var scores = assigned.Where(s => s.ReferenceTime is not null)
            .ToDictionary(s => s.Id, s => Predictor.Predict(leaky, s, observations, threshold: null).Score);

        var positives = scores.Where(kv => kv.Key.StartsWith('p')).Select(kv => kv.Value).ToList();
        var controls = scores.Where(kv => kv.Key.StartsWith('c')).Select(kv => kv.Value).ToList();
        Assert.NotEmpty(controls);

        // Saatten bağımsız olması gereken ayrım: Pozitiflerin hepsi 1, kontrollerin hepsi 0 olmamalı.
        Assert.False(positives.All(s => s == 1) && controls.All(s => s == 0), "Görünüm anı etiketi açık ediyor");
    }

    [Fact]
    public void Tiny_strata_do_not_reveal_individual_predictions()
    {
        // Bulgu E6: "birim sayısı az" notuyla bile 1/1 veya 0/1 yazılıyor. Tabakada tek bir kontrol varsa ve okuyan
        // onun hangi site olduğunu biliyorsa (ör. ruhsatlı tek aracı kurum), "bu siteye alarm verdi" sonucu adıyla
        // çıkarılabilir (Kural 4, çıkarımla). Küçük hücrelerde sayı bastırılmalı (ör. "< 5") ya da tabaka birleştirilmeli.
        var tiny = new StratumResult(SubjectGroup.ControlLicensed, Subgroup.FrameNeutral, false,
            new RateResult(1, 1, null, null, null), null, null, null);
        var report = new EvaluationReport(EvaluationMode.Historical, "random", 0.5, 0.01, 50,
            new Dictionary<SplitPart, int> { [SplitPart.Historical] = 10 }, 0, 10, 0, 10, 1,
            new RateResult(5, 10, 0.5, null, null), [], [tiny], new EarlyWarning(0, null, null, null), 0.001, 2000, null);

        var text = report.ToText();

        Assert.DoesNotContain("1/1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_has_upper_bounds()
    {
        // Bulgu E7 (düşük): Yapılandırma bir dosyadır ve kilitli testte özetiyle kayda girer. BootstrapIterations
        // 2 milyar olabilir; Locked kipte kilit ÖNCE alındığı için böyle bir çalıştırma kilitli testi "kullanır" ve
        // bitmez. Boyut sınırı da yok (File.ReadAllBytesAsync).
        Assert.ThrowsAny<Exception>(() => EvaluationConfig.Parse("""{"bootstrapIterations":2000000000}"""));
        Assert.ThrowsAny<Exception>(() => EvaluationConfig.Parse("""{"lockedWindowDays":100000}"""));
    }

    /// <summary>Saldırgan sinyal: Yalnızca görünüm anının saatine bakar (UTC 20:59:59.9999999 = TR gece yarısı - 1 tik).</summary>
    private sealed class ClockSignal : ISignal
    {
        public string Name => "clock";

        public double Score(AsOfView view) =>
            view.AsOf.UtcDateTime.TimeOfDay == new TimeSpan(20, 59, 59) + TimeSpan.FromTicks(TimeSpan.TicksPerSecond - 1) ? 1 : 0;
    }
}
