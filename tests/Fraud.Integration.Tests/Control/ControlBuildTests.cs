using System.Globalization;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Control;
using Fraud.Domain.Signals;
using Fraud.Collectors.Evaluation;
using Fraud.Collectors.Monitoring;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;
using Fraud.Domain.Control;
using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Control;

/// <summary>
/// Kontrol listesi kurma komutları gerçek PostgreSQL üzerinde (Testcontainers). Kaynaklar sahte; ağ yok. Adlar uydurma (.example).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ControlBuildTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 12, 30, 0, TimeSpan.Zero);
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("control-repo-");

    public void Dispose() => _root.Delete(recursive: true);

    private ControlContext Context(FraudDbContext db, StringWriter output, ITrancoClient? tranco = null, IRadarClient? radar = null,
        Func<string, CancellationToken, Task<FirstCertificateLookup>>? firstCertificate = null)
    {
        File.WriteAllText(Path.Combine(_root.FullName, RepoContext.MarkerFile), "");
        var lexicon = Path.Combine(_root.FullName, "docs", "research", "finans_sozlugu.txt");
        if (!File.Exists(lexicon))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lexicon)!);
            File.Copy(Path.Combine(ControlRuleTests.RepoRoot(), FinanceLexicon.RelativePath), lexicon);
        }

        return new ControlContext
        {
            Db = db,
            Full = new PublicSuffixDomainNormalizer(),
            IcannOnly = PublicSuffixDomainNormalizer.IcannOnly(),
            Repo = RepoContext.At(_root.FullName),
            Output = output,
            Time = new FakeTimeProvider(Now),
            Tranco = tranco,
            PinnedSources = new ControlSources("AB12C", new DateOnly(2026, 11, 21)),
            Radar = radar,
            FirstCertificate = firstCertificate,
        };
    }

    private static async Task SeedPositiveAsync(FraudDbContext db, string host)
    {
        var domain = new DomainName(new NormalizedDomain(host, host, null, "example", host), Now);
        db.Domains.Add(domain);
        db.OfficialActions.Add(new OfficialAction(domain, OfficialActionSource.SpkBulletin, "2026/12", new DateOnly(2026, 3, 4),
            OfficialActionCategory.Other, new Uri("https://spk.gov.tr/bulten/ornek"), host, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertNoNames(string output, params string[] names) =>
        Assert.All(names, n => Assert.DoesNotContain(n, output, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task G1_takes_every_bank_address_once_eliminates_positives_and_private_suffixes_and_refuses_a_second_build()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedPositiveAsync(db, "pozitif-ornek.example");
        using var output = new StringWriter();
        var ctx = Context(db, output);

        Assert.Equal(0, await ControlCommands.BuildG1Async(ctx, ControlRuleTests.Fixture("bddk-ornek.html"), ct));

        db.ChangeTracker.Clear();
        var members = await db.ControlMembers.Include(m => m.Domain).ToListAsync(ct);
        Assert.Equal(["ornekbanka-a.example", "ornekbanka-b.example", "ornekbanka-c.example"],
            members.Select(m => m.Domain.RegistrableDomain).Order(StringComparer.Ordinal));
        Assert.All(members, m => Assert.Equal((ControlGroup.G1, (SamplingFrame?)null, "kontrol-g1"), (m.Group, m.Frame, m.CommitmentId)));
        var build = await db.ControlBuilds.SingleAsync(ct);
        Assert.Contains("E01_pozitifle_cakisiyor", build.Counts, StringComparison.Ordinal);
        Assert.Contains("E06_psl_ozel_bolum", build.Counts, StringComparison.Ordinal);

        // Dosyalar: liste ve manifest gizli klasörde, açık taahhüt docs altında.
        Assert.True(File.Exists(Path.Combine(_root.FullName, "data", "private", "control", "kontrol-g1.txt")));
        Assert.True(File.Exists(Path.Combine(_root.FullName, "data", "private", "commitments", "kontrol-g1.opening.json")));
        Assert.True(File.Exists(Path.Combine(_root.FullName, "docs", "evaluation", "commitments", "kontrol-g1.json")));

        // İdempotent: ikinci kurma reddedilir, üye sayısı aynı kalır.
        using var again = new StringWriter();
        Assert.Equal(2, await ControlCommands.BuildG1Async(Context(db, again), ControlRuleTests.Fixture("bddk-ornek.html"), ct));
        Assert.Equal(3, await db.ControlMembers.CountAsync(ct));

        // Kural 4: Ekranda ad yok.
        AssertNoNames(output.ToString() + again, "ornekbanka", "pozitif-ornek", "github");
    }

    [Fact]
    public async Task G4a_selection_is_deterministic_and_respects_priority_over_g1()
    {
        var ct = TestContext.Current.CancellationToken;
        var tranco = new FakeTranco(File.ReadAllBytes(ControlRuleTests.Fixture("tranco-ornek.csv")));

        async Task<List<string>> BuildAsync(bool withG1Member)
        {
            await using var db = await postgres.CreateMigratedContextAsync();
            if (withG1Member)
            {
                var d = new DomainName(new NormalizedDomain("haber-ornek.example", "haber-ornek.example", null, "example", "haber-ornek.example"), Now);
                db.Domains.Add(d);
                db.ControlMembers.Add(new ControlMember(d, ControlGroup.G1, null, Now, null, "kontrol-g1-x", null, null));
                await db.SaveChangesAsync(ct);
            }

            using var output = new StringWriter();
            Assert.Equal(0, await ControlCommands.BuildG4aAsync(Context(db, output, tranco), ct));
            AssertNoNames(output.ToString(), "ornek.example", "github");
            db.ChangeTracker.Clear();
            return await db.ControlMembers.Where(m => m.Group == ControlGroup.G4a)
                .Select(m => m.Domain.RegistrableDomain).OrderBy(n => n).ToListAsync(ct);
        }

        var first = await BuildAsync(withG1Member: false);
        Directory.Delete(Path.Combine(_root.FullName, "data"), recursive: true);
        Directory.Delete(Path.Combine(_root.FullName, "docs", "evaluation"), recursive: true);
        var second = await BuildAsync(withG1Member: true);

        // 20 satır: 1 tekrar, 1 özel bölüm (github.io) → 18 aday; tamamı seçilir (300'den az).
        Assert.Equal(18, first.Count);
        Assert.Contains("haber-ornek.example", first);
        Assert.DoesNotContain("haber-ornek.example", second); // E02: G1'de
        Assert.Equal(first.Where(n => n != "haber-ornek.example"), second);
        Assert.Equal(2, tranco.Downloads); // gizli klasör silindiği için ikinci kurma listeyi yeniden indirdi (önbellek dosyası yok)
    }

    [Fact]
    public async Task G2_runs_the_prerequisite_trial_then_keeps_only_the_frozen_categories()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        // Önkoşul denemesi 100 ad ister: 200 satırlık uydurma liste. Her 10. ad "spor" (kategorisiz), gerisi kategorili
        // (%90 > %80, deneme geçer). "haber" ve "okul" kümede, "magaza" küme dışı.
        var stems = new[] { "haber", "okul", "magaza" };
        var lines = Enumerable.Range(1, 200).Select(i =>
            i.ToString(CultureInfo.InvariantCulture) + "," + (i % 10 == 0 ? "spor" : stems[i % 3]) + "-" + i.ToString(CultureInfo.InvariantCulture) + ".example");
        var tranco = new FakeTranco(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"));
        var radar = new FakeRadar(name =>
            name.StartsWith("haber", StringComparison.Ordinal) ? ["News & Media"]
            : name.StartsWith("okul", StringComparison.Ordinal) ? ["Educational Institutions", "Business"]
            : name.StartsWith("spor", StringComparison.Ordinal) ? []
            : ["Shopping"]);
        using var output = new StringWriter();

        Assert.Equal(0, await ControlCommands.BuildG2Async(Context(db, output, tranco, radar), ct));

        db.ChangeTracker.Clear();
        var names = await db.ControlMembers.Select(m => m.Domain.RegistrableDomain).ToListAsync(ct);
        var expected = Enumerable.Range(1, 200).Where(i => i % 10 != 0 && i % 3 is 0 or 1).Count();
        Assert.Equal(expected, names.Count);
        Assert.All(names, n => Assert.True(n.StartsWith("haber", StringComparison.Ordinal) || n.StartsWith("okul", StringComparison.Ordinal)));
        Assert.DoesNotContain("Not: 100'den az üye", output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root.FullName, "data", "private", "control", "g2-onkosul.json")));
        AssertNoNames(output.ToString(), "haber-ornek", "okul-ornek");
    }

    [Fact]
    public async Task G2_without_a_token_stops_and_a_failed_trial_is_final()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var tranco = new FakeTranco(File.ReadAllBytes(ControlRuleTests.Fixture("tranco-ornek.csv")));

        using var noToken = new StringWriter();
        Assert.Equal(64, await ControlCommands.BuildG2Async(Context(db, noToken, tranco), ct));
        Assert.Contains("Radar token'ı yok", noToken.ToString(), StringComparison.Ordinal);
        var trialFile = Path.Combine(_root.FullName, "data", "private", "control", "g2-onkosul.json");

        // Ön sorgu başarısız (token ya da izin): deneme başlamaz, kayıt yazılmaz; düzeltip yeniden denenebilir.
        using var preflight = new StringWriter();
        Assert.Equal(2, await ControlCommands.BuildG2Async(Context(db, preflight, tranco, new FakeRadar(_ => null)), ct));
        Assert.False(File.Exists(trialFile));

        // Deneme içinde hata (ön sorgu geçti): kayıt ilk sorgudan önce yazıldığı için deneme KULLANILDI (C2).
        using var errors = new StringWriter();
        var flaky = new FakeRadar(n => n == ControlCommands.PreflightDomain ? ["Technology"] : null);
        Assert.Equal(2, await ControlCommands.BuildG2Async(Context(db, errors, tranco, flaky), ct));
        Assert.Contains("Errored", File.ReadAllText(trialFile), StringComparison.Ordinal);

        // Tek kayıt dosyası (C1): Başka bir sabitlenmiş listeyle bile yeniden denenemez.
        using var rerun = new StringWriter();
        var other = Context(db, rerun, tranco, new FakeRadar(_ => ["News & Media"]));
        var rerunCtx = new ControlContext
        {
            Db = db, Full = other.Full, IcannOnly = other.IcannOnly, Repo = other.Repo, Output = rerun, Time = other.Time,
            Tranco = tranco, Radar = other.Radar, PinnedSources = new ControlSources("XY99Z", new DateOnly(2026, 11, 22)),
        };
        Assert.Equal(2, await ControlCommands.BuildG2Async(rerunCtx, ct));
        Assert.Contains("daha önce kullanıldı", rerun.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await db.ControlMembers.CountAsync(ct));
    }

    [Fact]
    public async Task Weekly_g3_and_g4b_select_from_their_week_with_quotas_frames_and_no_overlap()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var week = "2026-W47"; // 16–22 Kasım
        var inWeek = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

        // N havuzu: 40 sözlüklü ("yatirim"), 200 sözlüksüz; ayrıca önceki haftadan 5 sözlüklü (havuza girmemeli).
        long index = 0;
        foreach (var i in Enumerable.Range(0, 40))
        {
            db.CtNeutralSamples.Add(new CtNeutralSample(N("yatirim", i), "log1", index++, inWeek, inWeek, inWeek));
        }

        foreach (var i in Enumerable.Range(0, 200))
        {
            db.CtNeutralSamples.Add(new CtNeutralSample(N("sade", i), "log1", index++, inWeek, inWeek, inWeek));
        }

        foreach (var i in Enumerable.Range(0, 5))
        {
            db.CtNeutralSamples.Add(new CtNeutralSample(N("eskiyatirim", i), "log1", index++, before, before, before));
        }

        // K havuzu: 10 sözlüklü aday bu hafta; biri daha önceki bir haftada görülmüş (girmemeli); biri N'de de var.
        foreach (var i in Enumerable.Range(0, 10))
        {
            db.CtCandidates.Add(new CtCandidate("log1", index++, CtEntryType.X509, inWeek, inWeek, inWeek.AddDays(90), "C=US",
                ["www." + N("borsa", i)], ["borsa"], inWeek));
        }

        db.CtCandidates.Add(new CtCandidate("log1", index++, CtEntryType.X509, before, before, before.AddDays(90), "C=US", [N("borsa", 0)], ["borsa"], before));
        db.CtCandidates.Add(new CtCandidate("log1", index++, CtEntryType.X509, inWeek, inWeek, inWeek.AddDays(90), "C=US", [N("yatirim", 1)], ["yatirim"], inWeek));
        await db.SaveChangesAsync(ct);

        // Sahte crt.sh: Adın sonundaki sayıya göre yaş. %3 == 0 → 400 gün (eski), %3 == 1 → 100 gün (orta), %3 == 2 → 5 gün (yeni).
        // "-7" ile biten adlar için hata (E20: "yeni değil" sayılmaz).
        var lookups = 0;
        Task<FirstCertificateLookup> Lookup(string name, CancellationToken token)
        {
            lookups++;
            var n = int.Parse(name.Split('.')[0].Split('-')[^1], CultureInfo.InvariantCulture);
            if (n == 7)
            {
                return Task.FromResult(new FirstCertificateLookup(null, true));
            }

            var days = (n % 3) switch { 0 => 400, 1 => 100, _ => 5 };
            return Task.FromResult(new FirstCertificateLookup(inWeek.AddDays(-days), false));
        }

        // Hafta bitmeden kurulmaz.
        using (var early = new StringWriter())
        {
            var ctxEarly = Context(db, early, firstCertificate: Lookup) ;
            var earlyCtx = new ControlContext
            {
                Db = ctxEarly.Db, Full = ctxEarly.Full, IcannOnly = ctxEarly.IcannOnly, Repo = ctxEarly.Repo, Output = early,
                Time = new FakeTimeProvider(inWeek), FirstCertificate = Lookup,
            };
            Assert.Equal(2, await ControlCommands.BuildWeeklyAsync(earlyCtx, ControlGroup.G3, week, 600, ct));
        }

        using var g3Output = new StringWriter();
        Assert.Equal(0, await ControlCommands.BuildWeeklyAsync(Context(db, g3Output, firstCertificate: Lookup), ControlGroup.G3, week, 600, ct));
        using var g4bOutput = new StringWriter();
        Assert.Equal(0, await ControlCommands.BuildWeeklyAsync(Context(db, g4bOutput, firstCertificate: Lookup), ControlGroup.G4b, week, 600, ct));

        db.ChangeTracker.Clear();
        var members = await db.ControlMembers.Include(m => m.Domain).ToListAsync(ct);
        var g3 = members.Where(m => m.Group == ControlGroup.G3).ToList();
        var g4b = members.Where(m => m.Group == ControlGroup.G4b).ToList();

        // G3: yalnızca "yeni" (5 gün) ve sözlüklü; N ve K çerçeveleri; önceki haftanın adları yok.
        Assert.All(g3, m => Assert.Equal(AgeClass.New, AgeClasses.Of(m.FirstLoggedAt!.Value, m.FirstCertificateAt!.Value)));
        Assert.All(g3, m => Assert.Equal(week, m.Week));
        Assert.All(g3, m => Assert.DoesNotContain("eskiyatirim", m.Domain.RegistrableDomain, StringComparison.Ordinal));
        Assert.Contains(g3, m => m.Frame == SamplingFrame.N && m.DictionarySubset == "baseline");
        Assert.Contains(g3, m => m.Frame == SamplingFrame.K);
        Assert.DoesNotContain(g3, m => m.Domain.RegistrableDomain == N("borsa", 0)); // daha önce görülmüş
        // N'de 40 sözlüklü adın ~1/3'ü yeni: 13 (sayılar 2, 5, ..., 38); "-7" hatalı ama 7 % 3 = 1 (zaten yeni değil).
        Assert.Equal(13, g3.Count(m => m.Frame == SamplingFrame.N));

        // G4b: kota 14 / 31 / 15; G3'te olan hiçbir ad yok (E02); bir alan adı tek grupta.
        var classes = g4b.GroupBy(m => AgeClasses.Of(m.FirstLoggedAt!.Value, m.FirstCertificateAt!.Value)).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(classes.GetValueOrDefault(AgeClass.New) <= 14 && classes.GetValueOrDefault(AgeClass.Middle) <= 31 && classes.GetValueOrDefault(AgeClass.Old) <= 15);
        Assert.Equal(60, g4b.Count);
        Assert.Empty(g3.Select(m => m.DomainId).Intersect(g4b.Select(m => m.DomainId)));

        // İdempotent.
        using var again = new StringWriter();
        Assert.Equal(2, await ControlCommands.BuildWeeklyAsync(Context(db, again, firstCertificate: Lookup), ControlGroup.G3, week, 600, ct));

        // Kural 4: ekranda ad yok; yalnızca sayılar.
        AssertNoNames(g3Output.ToString() + g4bOutput + again, "yatirim-", "sade-", "borsa-");
        Assert.True(lookups > 0);
    }

    [Fact]
    public async Task Collection_runner_queries_control_members_once_for_crtsh_and_only_on_their_dns_day()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedPositiveAsync(db, "pozitif-ornek.example");
        var names = Enumerable.Range(0, 20).Select(i => N("uye", i)).ToList();
        foreach (var name in names)
        {
            var d = new DomainName(new NormalizedDomain(name, name, null, "example", name), Now);
            db.Domains.Add(d);
            db.ControlMembers.Add(new ControlMember(d, ControlGroup.G4a, null, Now, null, "kontrol-g4a-x", "AB12C", null));
        }

        await db.SaveChangesAsync(ct);
        var saturday = new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime();
        var time = new FakeTimeProvider(saturday);
        var crtSh = new CountingCollector(CollectionSource.CrtSh, TimeSpan.FromDays(7), time);
        var dns = new CountingCollector(CollectionSource.Dns, TimeSpan.FromHours(20), time);
        var runner = new CollectionRunner(db, [crtSh, dns], new CollectionOptions { MeasurementLocation = "test-lab" }, time,
            NullLogger<CollectionRunner>.Instance);

        var first = await runner.RunAsync(CollectionSource.CrtSh, force: false, ct);
        Assert.Equal((1, 20), (first.PositiveTargets, first.ControlTargets));
        Assert.Equal(21, crtSh.Calls);

        // 8 gün sonra: pozitif yeniden sorulur (7 gün önbellek), kontrol üyeleri sorulmaz (bir kez yeterli).
        time.Advance(TimeSpan.FromDays(8));
        var second = await runner.RunAsync(CollectionSource.CrtSh, force: false, ct);
        Assert.Equal(22, crtSh.Calls);
        Assert.Equal(20, second.SkippedFresh);

        // DNS: cumartesi yalnızca cumartesi üyeleri, pazar diğerleri; pozitif her gün.
        Task<CollectionRunSummary> DnsOn(DateTimeOffset day) =>
            new CollectionRunner(db, [crtSh, dns], new CollectionOptions { MeasurementLocation = "test-lab" }, new FakeTimeProvider(day),
                NullLogger<CollectionRunner>.Instance).RunAsync(CollectionSource.Dns, force: true, ct);
        var sat = await DnsOn(saturday);
        var sun = await DnsOn(saturday.AddDays(1));
        var mon = await DnsOn(saturday.AddDays(2));
        Assert.Equal(20, sat.ControlTargets + sun.ControlTargets);
        Assert.Equal(names.Count(n => ControlDnsSchedule.Slot(n) == 0), sat.ControlTargets);
        Assert.Equal((0, 1), (mon.ControlTargets, mon.PositiveTargets));
    }

    [Fact]
    public async Task G3a_trial_runs_three_attempts_once_and_counts_errors_separately()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedPositiveAsync(db, "pozitif-ornek.example"); // bülten 2026/12: ayar bölümü
        var positiveId = await db.Domains.Select(d => d.Id).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO certificates (crtsh_id, serial_number, issuer_ca_id, issuer_name, common_name, names, not_before, not_after, logged_at, is_precertificate, observed_at)
            VALUES (1, '01', NULL, 'C=US, O=Test CA', NULL, ARRAY['pozitif-ornek.example'], {new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)}, {new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero)}, NULL, NULL, {Now})
            """, ct);
        var certId = await db.Certificates.Select(c => c.Id).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO certificate_domains (certificate_id, domain_id, observed_at) VALUES ({certId}, {positiveId}, {Now})", ct);

        // 60 farklı kayıtlı alan adı, hepsi Ocak 2026'da: tam ve yeterli cevap.
        var rows = Enumerable.Range(0, 60).Select(i => "{\"id\":" + (1000 + i).ToString(CultureInfo.InvariantCulture)
            + ",\"serial_number\":\"0" + i.ToString(CultureInfo.InvariantCulture) + "\",\"issuer_name\":\"C=US, O=Test\",\"name_value\":\"" + N("deneme", i)
            + "\",\"not_before\":\"2026-01-10T00:00:00\",\"not_after\":\"2026-04-10T00:00:00\"}");
        var good = System.Text.Encoding.UTF8.GetBytes("[" + string.Join(',', rows) + "]");
        var responses = new Queue<Fraud.Collectors.Http.HttpFetchResult>(
        [
            new(good, 200, 1, CollectionErrorKind.None),
            new(null, null, 1, CollectionErrorKind.Timeout),            // hata: "sonuç yok" sayılmaz
            new(good, 200, 1, CollectionErrorKind.None),
        ]);
        var asked = new List<Uri>();
        Task<Fraud.Collectors.Http.HttpFetchResult> Fetch(Uri uri, CancellationToken token)
        {
            asked.Add(uri);
            return Task.FromResult(responses.Dequeue());
        }

        // Saatin ilk 10 dakikası: başlamaz.
        using var early = new StringWriter();
        var baseCtx = Context(db, early);
        var earlyCtx = new ControlContext
        {
            Db = db, Full = baseCtx.Full, IcannOnly = baseCtx.IcannOnly, Repo = baseCtx.Repo, Output = early,
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 30, 12, 5, 0, TimeSpan.Zero)),
        };
        Assert.Equal(75, await G3aTrial.RunAsync(earlyCtx, Fetch, ct));
        Assert.Empty(asked);

        using var output = new StringWriter();
        Assert.Equal(0, await G3aTrial.RunAsync(Context(db, output), Fetch, ct));
        Assert.Equal(3, asked.Count);
        Assert.All(asked, u => Assert.Equal("crt.sh", u.Host));
        Assert.Contains("GEÇTİ", output.ToString(), StringComparison.Ordinal); // 3'te 2
        Assert.Contains("Timeout", output.ToString(), StringComparison.Ordinal);
        AssertNoNames(output.ToString(), "deneme-", "pozitif-ornek");

        // Bir kez: ikinci çalıştırma reddedilir, istek gitmez.
        using var again = new StringWriter();
        Assert.Equal(2, await G3aTrial.RunAsync(Context(db, again), Fetch, ct));
        Assert.Equal(3, asked.Count);
    }

    [Fact]
    public void Group_target_metric_carries_only_the_two_fixed_group_labels()
    {
        using var capture = new Fraud.Integration.Tests.Monitoring.MetricCapture();
        using var metrics = new CollectionMetrics(capture.Factory);
        metrics.RunFinished(new CollectionRunSummary(CollectionSource.Dns, 21, 0, 21, 0, 0, 0, false) { PositiveTargets = 1, ControlTargets = 20 });

        Assert.Equal(1, capture.Sum("fraud_collection_group_targets", "group=positive"));
        Assert.Equal(20, capture.Sum("fraud_collection_group_targets", "group=control"));
        Assert.DoesNotContain(capture.AllTags(), t => t.StartsWith("group=", StringComparison.Ordinal) && t is not ("group=positive" or "group=control"));
    }

    private static string N(string stem, int i) => stem + "-" + i.ToString(CultureInfo.InvariantCulture) + ".example";

    private sealed class FakeTranco(byte[] body) : ITrancoClient
    {
        public int Downloads { get; private set; }

        public Task<TrancoListInfo?> ListInfoAsync(string listId, CancellationToken cancellationToken) =>
            Task.FromResult<TrancoListInfo?>(new TrancoListInfo(listId, Now, true));

        public Task<byte[]?> DownloadAsync(string listId, CancellationToken cancellationToken)
        {
            Downloads++;
            return Task.FromResult<byte[]?>(body);
        }
    }

    private sealed class FakeRadar(Func<string, IReadOnlyList<string>?> categories) : IRadarClient
    {
        public Task<RadarLookup> CategoriesAsync(string registrableDomain, CancellationToken cancellationToken) =>
            Task.FromResult(categories(registrableDomain) is { } c
                ? new RadarLookup(c, CollectionErrorKind.None, 200)
                : new RadarLookup(null, CollectionErrorKind.ClientError, 401));
    }

    /// <summary>Her hedefe "başarılı" collection_log satırı yazan sahte toplayıcı.</summary>
    /// <summary>
    /// Kayıt zamanı testin sahte saatinden gelir. Eskiden gerçek saat kullanılıyordu; test 2026-11-22'den sonra çalışınca
    /// "8 gün sonra" anı gerçek kayıt zamanından önceye düşüyor ve önbellek yanlışlıkla taze görünüyordu.
    /// </summary>
    private sealed class CountingCollector(CollectionSource source, TimeSpan cacheTtl, TimeProvider time) : ISourceCollector
    {
        public int Calls { get; private set; }

        public CollectionSource Source => source;

        public TimeSpan CacheTtl => cacheTtl;

        public async Task<CollectionLogEntry> CollectAsync(FraudDbContext db, CollectionTarget target, string location, CancellationToken cancellationToken)
        {
            Calls++;
            var now = time.GetUtcNow();
            var entry = new CollectionLogEntry(source, target.DomainId, location, now, now, 200, 1, CollectionOutcome.Success, CollectionErrorKind.None, 1, 0, 0, false);
            db.CollectionLog.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
            return entry;
        }
    }
}
