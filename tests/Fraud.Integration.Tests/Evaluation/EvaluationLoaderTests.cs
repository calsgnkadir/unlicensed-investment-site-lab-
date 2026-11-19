using Fraud.Collectors.Evaluation;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Domain.OfficialActions;
using Fraud.Evaluation;
using Fraud.Evaluation.Commitments;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Signals;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Integration.Tests.Evaluation;

/// <summary>Ölçüm yükleyicisi gerçek PostgreSQL üzerinde (Testcontainers). Alan adları uydurmadır (.example).</summary>
[Trait("Category", "Integration")]
public sealed class EvaluationLoaderTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fraud-eval-it-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static DomainName Domain(string host) =>
        new(new NormalizedDomain(host, host, null, "example", host), Now);

    private static async Task<(DomainName A, DomainName B)> SeedAsync(FraudDbContext db, CancellationToken ct)
    {
        var a = Domain("poz-a.example");
        var b = Domain("poz-b.example");
        db.Domains.AddRange(a, b);
        var url = new Uri("https://spk.gov.tr/bulten/ornek");
        // A iki bültende: en erken karar (2026/12, 4 Mart) geçerli olmalı.
        db.OfficialActions.Add(new OfficialAction(a, OfficialActionSource.SpkBulletin, "2026/31", new DateOnly(2026, 5, 15), OfficialActionCategory.Other, url, a.Host, Now));
        db.OfficialActions.Add(new OfficialAction(a, OfficialActionSource.SpkBulletin, "2026/12", new DateOnly(2026, 3, 4), OfficialActionCategory.Other, url, a.Host, Now));
        db.OfficialActions.Add(new OfficialAction(b, OfficialActionSource.SpkBulletin, "2026/45", new DateOnly(2026, 7, 16), OfficialActionCategory.Crypto, url, b.Host, Now));
        await db.SaveChangesAsync(ct);

        // A'nın ilk sertifikası kararından 60 gün önce (yeni), B'nin hiç sertifikası yok (eski sayılır).
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO certificates (crtsh_id, serial_number, issuer_ca_id, issuer_name, common_name, names, not_before, not_after, logged_at, is_precertificate, observed_at)
            VALUES (1, '01', NULL, 'C=US, O=Test CA', NULL, ARRAY['poz-a.example'], {new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero)}, {new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero)}, NULL, NULL, {Now})
            """, ct);
        var certId = await db.Certificates.Select(c => c.Id).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO certificate_domains (certificate_id, domain_id, observed_at) VALUES ({certId}, {a.Id}, {Now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO archive_snapshots (domain_id, capture_key, cdx_timestamp, captured_at, original_url, query_removed, status_code, mime_type, digest, observed_at)
            VALUES ({a.Id}, {new string('a', 64)}, '20260210000000', {new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero)}, 'https://poz-a.example/', false, 200, 'text/html', NULL, {Now})
            """, ct);
        db.DnsSnapshots.Add(new DnsSnapshot(a.Id, Now, "test-lab", "cloudflare", DnsTransport.Doh, "A", 3, DnsOutcome.NxDomain, DnsErrorKind.None,
            [], null, false, 10, 1, false));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (a, b);
    }

    [Fact]
    public async Task Loader_builds_opaque_subjects_with_earliest_bulletin_and_known_at_rules()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db, ct);
        Directory.CreateDirectory(_dir);
        var controls = Path.Combine(_dir, "controls.csv");
        await File.WriteAllLinesAsync(controls,
        [
            "registrable_domain,group,subgroup,cluster,linked_to_positive,first_certificate_at,reference_time",
            "kontrol-a.example,ControlHardNegative,FrameNeutral,,false,2026-02-01T00:00:00Z,",
            "poz-a.example,ControlRandom,FrameNeutral,,false,2026-02-01T00:00:00Z,", // E01: pozitifle çakışıyor
            "bozuk-satir,Positive,PositiveNew,,false,,",                              // reddedilir
        ], ct);

        var loader = new EvaluationDataLoader(db, new PublicSuffixDomainNormalizer(), new byte[32]);
        var data = await loader.LoadAsync(controls, null, KnownAtPolicy.DefaultArchiveDelay, prospective: false, ct);

        Assert.Equal(new EvaluationLoadCounts(2, 1, 1, 1, 1, 1, 1), data.Counts);
        Assert.All(data.Subjects, s => Assert.StartsWith("s-", s.Id, StringComparison.Ordinal));
        Assert.All(data.Subjects, s => Assert.DoesNotContain("example", s.Id, StringComparison.Ordinal));

        var positives = data.Subjects.Where(s => s.IsPositive).OrderBy(s => s.ReferenceTime).ToList();
        var a = positives[0];
        Assert.Equal(new BulletinNumber(2026, 12), a.Bulletin);
        // Karar tarihi Türkiye saatiyle gün başı.
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.FromHours(3)), a.ReferenceTime);
        Assert.Equal(Subgroup.PositiveNew, a.Subgroup);
        Assert.Equal(Subgroup.PositiveUnknownAge, positives[1].Subgroup); // sertifikası yok: yaşı bilinmiyor, bölmede dışarıda kalır

        var ofA = data.Observations.Where(o => o.SubjectId == a.Id).ToList();
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), ofA.Single(o => o.Kind == ObservationKinds.Certificate).KnownAt);
        Assert.Equal(new DateTimeOffset(2026, 2, 17, 0, 0, 0, TimeSpan.Zero), ofA.Single(o => o.Kind == ObservationKinds.Archive).KnownAt);
        // Bugünkü DNS geçmiş görünümüne girmez.
        var dns = ofA.Single(o => o.Kind == ObservationKinds.Dns);
        Assert.Equal(Now, dns.KnownAt);
        Assert.DoesNotContain(AsOfView.Create(a, a.ReferenceTime!.Value.AddTicks(-1), ofA).Observations, o => o.Kind == ObservationKinds.Dns);

        // Uçtan uca: Sahte sinyal yüklenen veriyle hattan geçer (az veri: yalnızca ham sayı).
        var report = EvaluationRunner.Run(new EvaluationConfig { BootstrapIterations = 100, CalibrationGroups = [SubjectGroup.ControlHardNegative] },
            EvaluationMode.Historical, data.Subjects, data.Observations, new RandomSignal("it"));
        Assert.Null(report.Recall.Rate);
    }

    [Fact]
    public async Task Prospective_archive_records_are_known_only_when_observed()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db, ct);

        var data = await new EvaluationDataLoader(db, new PublicSuffixDomainNormalizer(), new byte[32])
            .LoadAsync(null, null, KnownAtPolicy.DefaultArchiveDelay, prospective: true, ct);

        Assert.Equal(Now, data.Observations.Single(o => o.Kind == ObservationKinds.Archive).KnownAt);
        Assert.Equal(Now, data.Observations.Single(o => o.Kind == ObservationKinds.Certificate).KnownAt); // mentör 1
    }

    [Fact]
    public async Task Commit_commands_write_the_opening_only_under_data_private_and_verify_one_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = TempRepo();
        var records = Path.Combine(_dir, "records.txt");
        // Mentör maddesi 10: Kayıtlar normalleştirilir (www., büyük harf, alt alan adı → kayıtlı alan adı).
        await File.WriteAllLinesAsync(records, ["WWW.Kayit-1.example", "panel.kayit-2.example", "kayit-3.example"], ct);
        var publicOut = Path.Combine(_dir, "public", "test.json");
        var commitments = Path.Combine(repo.Root, "data", "private", "commitments");
        using var output = new StringWriter();

        Assert.Equal(0, await EvaluationCommands.CommitCreateAsync(
            EvaluationCommands.ParseArgs(["--records", records, "--name", "deneme", "--public-out", publicOut]), output, ct, repo));
        var opening = Path.Combine(commitments, "deneme.opening.json");
        Assert.True(File.Exists(opening));
        Assert.True(File.Exists(publicOut + ".tsq"));
        Assert.DoesNotContain("kayit-", await File.ReadAllTextAsync(publicOut, ct), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(commitments, "*.tmp"));

        // E3: Aynı adla ikinci taahhüt eskisinin üzerine yazmaz.
        var before = await File.ReadAllTextAsync(opening, ct);
        Assert.Equal(2, await EvaluationCommands.CommitCreateAsync(
            EvaluationCommands.ParseArgs(["--records", records, "--name", "deneme", "--public-out", publicOut + "2"]), output, ct, repo));
        Assert.Equal(before, await File.ReadAllTextAsync(opening, ct));

        var index = CommitmentTool.FromJson<CommitmentOpening>(before).Entries.Single(e => e.Record == "kayit-1.example").Index;
        var indexText = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0, await EvaluationCommands.CommitProveAsync(
            EvaluationCommands.ParseArgs(["--opening", opening, "--index", indexText]), output, ct, repo));
        var proof = Path.Combine(commitments, "deneme.proof-" + indexText + ".json");
        Assert.Equal(0, await EvaluationCommands.CommitVerifyAsync(EvaluationCommands.ParseArgs(["--public", publicOut, "--proof", proof]), output, ct));
        // Sorgulanan ad farklı yazılsa da aynı normalleştirme uygulanır.
        Assert.Equal(0, await EvaluationCommands.CommitVerifyAsync(
            EvaluationCommands.ParseArgs(["--public", publicOut, "--proof", proof, "--record", "https://www.KAYIT-1.example/giris"]), output, ct));
        Assert.Equal(2, await EvaluationCommands.CommitVerifyAsync(
            EvaluationCommands.ParseArgs(["--public", publicOut, "--proof", proof, "--record", "kayit-2.example"]), output, ct));

        // E5: zincir. İkinci taahhüt birinciye bağlanır; birincisi değiştirilirse bağ tutmaz.
        var records2 = Path.Combine(_dir, "records2.txt");
        await File.WriteAllLinesAsync(records2, ["kayit-9.example"], ct);
        var publicOut2 = Path.Combine(_dir, "public", "test2.json");
        Assert.Equal(0, await EvaluationCommands.CommitCreateAsync(
            EvaluationCommands.ParseArgs(["--records", records2, "--name", "deneme2", "--public-out", publicOut2, "--previous", publicOut]), output, ct, repo));
        Assert.Equal(0, await EvaluationCommands.CommitProveAsync(
            EvaluationCommands.ParseArgs(["--opening", Path.Combine(commitments, "deneme2.opening.json"), "--index", "0"]), output, ct, repo));
        var proof2 = Path.Combine(commitments, "deneme2.proof-0.json");
        Assert.Equal(0, await EvaluationCommands.CommitVerifyAsync(
            EvaluationCommands.ParseArgs(["--public", publicOut2, "--proof", proof2, "--previous", publicOut]), output, ct));
        await File.AppendAllTextAsync(publicOut, " ", ct);
        Assert.Equal(2, await EvaluationCommands.CommitVerifyAsync(
            EvaluationCommands.ParseArgs(["--public", publicOut2, "--proof", proof2, "--previous", publicOut]), output, ct));

        // Ekran çıktısı hiçbir kaydı içermez.
        Assert.DoesNotContain("kayit-", output.ToString(), StringComparison.OrdinalIgnoreCase);

        // Bozulmuş kanıt reddedilir.
        var tampered = CommitmentTool.FromJson<InclusionProofFile>(await File.ReadAllTextAsync(proof, ct)) with { Index = index == 0 ? 1 : 0 };
        await File.WriteAllTextAsync(proof, CommitmentTool.ToJson(tampered), ct);
        Assert.Equal(2, await EvaluationCommands.CommitVerifyAsync(EvaluationCommands.ParseArgs(["--public", publicOut, "--proof", proof]), output, ct));
    }

    [Fact]
    public async Task Locked_run_refuses_without_a_matching_clean_commit_and_takes_no_lock()
    {
        // E1 ve mentör 5: Geçici kök bir git deposu değil; HEAD okunamaz. Kilit alınmadan durmalı. Veritabanına hiç
        // dokunulmaz (bağlam null verilir: dokunulsaydı test istisnayla düşerdi).
        var ct = TestContext.Current.CancellationToken;
        var repo = TempRepo();
        var config = Path.Combine(_dir, "eval.json");
        await File.WriteAllTextAsync(config, """{ "freezeDate": "2026-12-06T00:00:00+03:00" }""", ct);
        using var output = new StringWriter();
        var lockFile = Path.Combine(repo.Root, "data", "private", "evaluation", "locked-run.json");

        Assert.Equal(64, await EvaluationCommands.EvalRunAsync(null!, new PublicSuffixDomainNormalizer(),
            EvaluationCommands.ParseArgs(["--mode", "Locked", "--config", config, "--commit", "son"]), output, ct, repo));
        Assert.Equal(64, await EvaluationCommands.EvalRunAsync(null!, new PublicSuffixDomainNormalizer(),
            EvaluationCommands.ParseArgs(["--mode", "Locked", "--config", config, "--commit", new string('a', 40)]), output, ct, repo));
        Assert.False(File.Exists(lockFile));

        // F yoksa yapılandırma hatası kilitten ÖNCE verilir.
        await File.WriteAllTextAsync(config, "{}", ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => EvaluationCommands.EvalRunAsync(null!, new PublicSuffixDomainNormalizer(),
            EvaluationCommands.ParseArgs(["--mode", "Locked", "--config", config, "--commit", new string('a', 40)]), output, ct, repo));
        Assert.False(File.Exists(lockFile));
    }

    [Fact]
    public async Task Control_csv_parsing_is_strict()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        var controls = Path.Combine(_dir, "controls.csv");
        await File.WriteAllLinesAsync(controls,
        [
            "registrable_domain,group,subgroup,cluster,linked_to_positive,first_certificate_at,reference_time",
            "k1.example,ControlRandom,FrameNeutral,,True,2026-02-01T00:00:00Z,",   // kabul: harf duyarsız bool
            "k2.example,7,FrameNeutral,,false,,",                                  // ret: sayı olarak grup
            "k3.example,ControlRandom,FrameNeutral,,yes,,",                        // ret: bool değil
            "k4.example,ControlRandom,PositiveUnknownAge,,false,,",                // ret: pozitif alt grubu
            "k5.example,ControlRandom,1,,false,,",                                 // ret: sayı olarak alt grup
        ], ct);

        var data = await new EvaluationDataLoader(db, new PublicSuffixDomainNormalizer(), new byte[32])
            .LoadAsync(controls, null, KnownAtPolicy.DefaultArchiveDelay, prospective: false, ct);

        Assert.Equal((1, 4), (data.Counts.Controls, data.Counts.ControlsRejected));
        Assert.True(Assert.Single(data.Subjects).LinkedToPositive);
    }

    private RepoContext TempRepo()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, RepoContext.MarkerFile), "<Solution />");
        return RepoContext.At(_dir);
    }
}
