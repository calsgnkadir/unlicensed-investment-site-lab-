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
        // Kontroller control_members tablosundan: biri G4b/N (eşik yalnızca G4'ten seçilir), biri sonradan pozitif olmuş (E01) bir G4a üyesi.
        var kontrol = Domain("kontrol-a.example");
        db.Domains.Add(kontrol);
        db.ControlMembers.Add(new Fraud.Domain.Control.ControlMember(kontrol, Fraud.Domain.Control.ControlGroup.G4b, Fraud.Domain.Control.SamplingFrame.N,
            Now, "2026-W05", "kontrol-g4b-2026-W05", null, "kontrol-g4b-2026-w05", firstCertificateAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
        var pozA = await db.Domains.SingleAsync(d => d.Host == "poz-a.example", ct);
        db.ControlMembers.Add(new Fraud.Domain.Control.ControlMember(pozA, Fraud.Domain.Control.ControlGroup.G4a, null, Now, null, "kontrol-g4a-x", "AB12C", "kontrol-g4a"));
        await db.SaveChangesAsync(ct);

        var loader = new EvaluationDataLoader(db, new PublicSuffixDomainNormalizer(), new byte[32]);
        var data = await loader.LoadAsync(null, KnownAtPolicy.DefaultArchiveDelay, prospective: false, ct);

        Assert.Equal(new EvaluationLoadCounts(2, 1, 0, 1, 1, 1, 1), data.Counts);
        var control = Assert.Single(data.Subjects, s => !s.IsPositive);
        Assert.Equal((SubjectGroup.ControlRandom, Subgroup.FrameNeutral, "2026-W05", "kontrol-g4b-2026-w05"),
            (control.Group, control.Subgroup, control.Week, control.CommitmentId));
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
        // Hafta 7: Sertifika gözleminin değeri iç kimliktir (patlama sinyali tekrarı saymasın); ad gözlemi zaman taşımaz.
        Assert.StartsWith("c-", ofA.Single(o => o.Kind == ObservationKinds.Certificate).Value, StringComparison.Ordinal);
        var name = ofA.Single(o => o.Kind == ObservationKinds.Name);
        Assert.Equal(("poz-a.example", DateTimeOffset.MinValue), (name.Value, name.KnownAt));
        Assert.All(data.Subjects, s => Assert.Single(data.Observations, o => o.SubjectId == s.Id && o.Kind == ObservationKinds.Name));
        // Bugünkü DNS geçmiş görünümüne girmez.
        var dns = ofA.Single(o => o.Kind == ObservationKinds.Dns);
        Assert.Equal(Now, dns.KnownAt);
        Assert.DoesNotContain(AsOfView.Create(a, a.ReferenceTime!.Value.AddTicks(-1), ofA).Observations, o => o.Kind == ObservationKinds.Dns);

        // Uçtan uca: Sahte sinyal yüklenen veriyle hattan geçer (az veri: yalnızca ham sayı).
        var report = EvaluationRunner.Run(new EvaluationConfig { BootstrapIterations = 100 },
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
            .LoadAsync(null, KnownAtPolicy.DefaultArchiveDelay, prospective: true, ct);

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
    public async Task Signals_brands_writes_g1_labels_privately_and_eval_run_ablation_prints_no_names()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db, ct);
        var bank = Domain("ornekbank.example");
        db.Domains.Add(bank);
        db.ControlMembers.Add(new Fraud.Domain.Control.ControlMember(bank, Fraud.Domain.Control.ControlGroup.G1, null, Now, null, "kontrol-g1", null, "kontrol-g1"));
        // Eşik için bir ayar kontrolü (ilk testteki gibi G4b/N; eşik yalnızca G4'ten seçilir).
        var kontrol = Domain("kontrol-a.example");
        db.Domains.Add(kontrol);
        db.ControlMembers.Add(new Fraud.Domain.Control.ControlMember(kontrol, Fraud.Domain.Control.ControlGroup.G4b, Fraud.Domain.Control.SamplingFrame.N,
            Now, "2026-W05", "kontrol-g4b-2026-W05", null, "kontrol-g4b-2026-w05", firstCertificateAt: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync(ct);
        var repo = TempRepo();
        using var output = new StringWriter();

        Assert.Equal(0, await SignalCommands.BuildBrandsAsync(db, repo, output, ct));
        var brands = Path.Combine(repo.Root, "data", "private", "signals", "brands.txt");
        Assert.Contains("ornekbank", await File.ReadAllLinesAsync(brands, ct));
        // İkinci çalıştırma aynı dosyayı üretir (idempotent).
        var first = await File.ReadAllBytesAsync(brands, ct);
        Assert.Equal(0, await SignalCommands.BuildBrandsAsync(db, repo, output, ct));
        Assert.Equal(first, await File.ReadAllBytesAsync(brands, ct));

        var config = Path.Combine(_dir, "eval.json");
        await File.WriteAllTextAsync(config, """
            { "bootstrapIterations": 100,
              "signal": { "name": "sum", "parts": [ "baseline", "brand", "cert-burst" ], "ablation": true } }
            """, ct);
        Assert.Equal(0, await EvaluationCommands.EvalRunAsync(db, new PublicSuffixDomainNormalizer(),
            EvaluationCommands.ParseArgs(["--mode", "Historical", "--config", config]), output, ct, repo));

        var text = output.ToString();
        Assert.Contains("TOPLAM(hepsi)", text, StringComparison.Ordinal);
        Assert.Contains("Marka listesi: yerli=1", text, StringComparison.Ordinal);
        // Kural 4: Ne G1 adı ne pozitif adı ekrana çıkar.
        Assert.DoesNotContain("example", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ornekbank", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eval_run_refuses_brand_signal_without_the_private_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = TempRepo();
        var config = Path.Combine(_dir, "eval.json");
        await File.WriteAllTextAsync(config, """{ "signal": { "name": "brand" } }""", ct);
        using var output = new StringWriter();

        // Veritabanına dokunulmadan durur (bağlam null).
        Assert.Equal(2, await EvaluationCommands.EvalRunAsync(null!, new PublicSuffixDomainNormalizer(),
            EvaluationCommands.ParseArgs(["--mode", "Historical", "--config", config]), output, ct, repo));
        Assert.Contains("signals-brands", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// S1: Kilitli kipte yerli marka listesi dosyadan değil, doğrulanmış G1 taahhüdünden üretilir; özeti yapılandırmadaki
    /// dondurulmuş değerle aynı olmalı. Dosyaya sonradan eklenen marka kilitli çalıştırmaya giremez.
    /// </summary>
    [Fact]
    public async Task Locked_mode_builds_the_brand_list_from_the_g1_commitment_not_the_file()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var repo = TempRepo();
        foreach (var host in new[] { "ornekbank.example", "deneme-katilim.example" })
        {
            var domain = Domain(host);
            db.Domains.Add(domain);
            db.ControlMembers.Add(new Fraud.Domain.Control.ControlMember(domain, Fraud.Domain.Control.ControlGroup.G1, null, Now, null, "kontrol-g1-x", "x", "kontrol-g1"));
        }

        await db.SaveChangesAsync(ct);
        var (publicCommitment, opening) = CommitmentTool.Create(["deneme-katilim.example", "ornekbank.example"], Now, null);
        var publicPath = Path.Combine(repo.Root, "docs", "evaluation", "commitments", "kontrol-g1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        await File.WriteAllTextAsync(publicPath, CommitmentTool.ToJson(publicCommitment), ct);
        var openingPath = repo.PrivatePath("data/private/commitments/kontrol-g1.opening.json");
        Directory.CreateDirectory(Path.GetDirectoryName(openingPath)!);
        await File.WriteAllTextAsync(openingPath, CommitmentTool.ToJson(opening), ct);

        // Saldırı: Dondurmadan sonra gizli dosyaya bir marka eklendi.
        var brandsPath = repo.PrivatePath("data/private/signals/brands.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(brandsPath)!);
        await File.WriteAllTextAsync(brandsPath, "# yorum\nornekbank\ndeneme-katilim\neklenen-marka\n", ct);

        var frozen = SignalCommands.CanonicalSha256(["ornekbank", "deneme-katilim"]);
        var config = new SignalConfig { Name = "brand", DomesticBrandsSha256 = frozen };
        using var output = new StringWriter();

        var locked = await SignalCommands.LoadResourcesAsync(config, EvaluationMode.Locked, db, repo, output, ct);
        Assert.NotNull(locked);
        Assert.Equal(frozen, locked.DomesticBrandsSha256);
        Assert.Equal(2, locked.Resources.Brands!.DomesticCount); // "eklenen-marka" yok

        // Geçmiş veri testi dosyadan okur: özet farklıdır ve raporda görünür.
        var historical = await SignalCommands.LoadResourcesAsync(config, EvaluationMode.Historical, db, repo, output, ct);
        Assert.NotEqual(frozen, historical!.DomesticBrandsSha256);

        // Dondurulmuş özet dosyanınki olsaydı kilitli kip durur.
        var wrong = new SignalConfig { Name = "brand", DomesticBrandsSha256 = historical.DomesticBrandsSha256 };
        Assert.Null(await SignalCommands.LoadResourcesAsync(wrong, EvaluationMode.Locked, db, repo, output, ct));
        Assert.Contains("dondurulmuş değerle aynı değil", output.ToString(), StringComparison.Ordinal);

        // Taahhüt dışında bir G1 üyesi eklenirse liste üretilmez.
        var extra = Domain("yeni-banka.example");
        db.Domains.Add(extra);
        db.ControlMembers.Add(new Fraud.Domain.Control.ControlMember(extra, Fraud.Domain.Control.ControlGroup.G1, null, Now, null, "kontrol-g1-x", "x", "kontrol-g1"));
        await db.SaveChangesAsync(ct);
        Assert.Null(await SignalCommands.LoadResourcesAsync(config, EvaluationMode.Locked, db, repo, output, ct));

        // Açıklama dosyası değiştirilirse taahhüt doğrulanmaz.
        await File.WriteAllTextAsync(openingPath, CommitmentTool.ToJson(opening).Replace("ornekbank", "ornekbamk", StringComparison.Ordinal), ct);
        Assert.Null(await SignalCommands.LoadResourcesAsync(config, EvaluationMode.Locked, db, repo, output, ct));
        Assert.DoesNotContain("example", output.ToString(), StringComparison.Ordinal); // Kural 4
    }

    [Fact]
    public void Locked_brand_signal_requires_a_frozen_list_hash_and_the_lock_records_it()
    {
        var config = new EvaluationConfig { FreezeDate = Now, Signal = new SignalConfig { Name = "brand" } };
        Assert.Throws<InvalidOperationException>(() => config.ValidateFor(EvaluationMode.Locked));
        config.ValidateFor(EvaluationMode.Historical); // geçmiş veri testinde zorunlu değil

        config.Signal.DomesticBrandsSha256 = new string('a', 64);
        config.ValidateFor(EvaluationMode.Locked);
        config.Signal.DomesticBrandsSha256 = "kisa";
        Assert.Throws<InvalidOperationException>(() => config.ValidateFor(EvaluationMode.Historical));

        Directory.CreateDirectory(_dir);
        var lockFile = Path.Combine(_dir, "kilit.json");
        var ticket = LockedRunGuard.Acquire(lockFile, "cfg", new string('b', 40), Now, brandListSha256: new string('c', 64));
        Assert.Equal(new string('c', 64), ticket.Current.BrandListSha256);
        Assert.Contains(new string('c', 64), File.ReadAllText(lockFile), StringComparison.Ordinal);
    }

    /// <summary>
    /// DECISIONS 2026-11-21: Ön sertifika ile son sertifika crt.sh'te iki kayıttır ama aynı veriliştir (aynı yayıncı ve seri).
    /// Yükleyici ikisine aynı kimliği verir; patlama sinyali onları bir kez sayar.
    /// </summary>
    [Fact]
    public async Task Precertificate_and_final_certificate_count_as_one_issuance()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var c = Domain("poz-c.example");
        db.Domains.Add(c);
        db.OfficialActions.Add(new OfficialAction(c, OfficialActionSource.SpkBulletin, "2026/40", new DateOnly(2026, 6, 20), OfficialActionCategory.Other,
            new Uri("https://spk.gov.tr/bulten/ornek"), c.Host, Now));
        await db.SaveChangesAsync(ct);
        var notBefore = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = notBefore.AddDays(90);
        // Aynı veriliş: seri bir kayıtta "00:ab:12", ötekinde "AB12" yazılmış; biri ön sertifika.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO certificates (crtsh_id, serial_number, issuer_ca_id, issuer_name, common_name, names, not_before, not_after, logged_at, is_precertificate, observed_at)
            VALUES (11, '00:ab:12', 7, 'C=US, O=Test CA', NULL, ARRAY['poz-c.example'], {notBefore}, {notAfter}, NULL, TRUE, {Now}),
                   (12, 'AB12', 7, 'C=US, O=Test CA', NULL, ARRAY['poz-c.example'], {notBefore}, {notAfter}, NULL, FALSE, {Now})
            """, ct);
        foreach (var id in await db.Certificates.Select(x => x.Id).ToListAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO certificate_domains (certificate_id, domain_id, observed_at) VALUES ({id}, {c.Id}, {Now})", ct);
        }

        var data = await new EvaluationDataLoader(db, new PublicSuffixDomainNormalizer(), new byte[32])
            .LoadAsync(null, KnownAtPolicy.DefaultArchiveDelay, prospective: false, ct);

        var certs = data.Observations.Where(o => o.Kind == ObservationKinds.Certificate).ToList();
        Assert.Equal(2, certs.Count);
        Assert.Single(certs.Select(o => o.Value).Distinct());
        Assert.DoesNotContain("Test CA", certs[0].Value, StringComparison.Ordinal); // yayıncı metni gözleme girmez

        // Patlama sinyali: eşik 2 sertifika olsa bile tek veriliş alarm vermez.
        var subject = data.Subjects.Single();
        var view = AsOfView.Create(subject, subject.ReferenceTime!.Value.AddTicks(-1), data.Observations);
        Assert.Equal(0, new Fraud.Signals.CertificateBurstSignal(windowDays: 30, minCertificates: 2).Score(view));
    }

    [Theory]
    [InlineData("00:AB:12", "ab12")]
    [InlineData(" 0ab12 ", "AB12")]
    public void Issuance_id_ignores_serial_formatting(string a, string b) =>
        Assert.Equal(EvaluationDataLoader.IssuanceId("CA", a), EvaluationDataLoader.IssuanceId("CA", b));

    [Fact]
    public void Issuance_id_depends_on_issuer_and_serial()
    {
        Assert.NotEqual(EvaluationDataLoader.IssuanceId("CA-1", "ab"), EvaluationDataLoader.IssuanceId("CA-2", "ab"));
        Assert.NotEqual(EvaluationDataLoader.IssuanceId("CA-1", "ab"), EvaluationDataLoader.IssuanceId("CA-1", "ac"));
    }

    [Fact]
    public void Control_groups_map_to_evaluation_groups_and_frames()
    {
        Assert.Equal(SubjectGroup.ControlLicensed, EvaluationDataLoader.GroupOf(Fraud.Domain.Control.ControlGroup.G1));
        Assert.Equal(SubjectGroup.ControlFinanceNews, EvaluationDataLoader.GroupOf(Fraud.Domain.Control.ControlGroup.G2));
        Assert.Equal(SubjectGroup.ControlHardNegative, EvaluationDataLoader.GroupOf(Fraud.Domain.Control.ControlGroup.G3));
        Assert.Equal(SubjectGroup.ControlRandom, EvaluationDataLoader.GroupOf(Fraud.Domain.Control.ControlGroup.G4a));
        Assert.Equal(SubjectGroup.ControlRandom, EvaluationDataLoader.GroupOf(Fraud.Domain.Control.ControlGroup.G4b));
        Assert.Equal(SubjectGroup.ControlCryptoActive, EvaluationDataLoader.GroupOf(Fraud.Domain.Control.ControlGroup.CryptoActive));
        Assert.Equal(Subgroup.FrameTermConditioned, EvaluationDataLoader.FrameOf(Fraud.Domain.Control.SamplingFrame.K));
        Assert.Equal(Subgroup.None, EvaluationDataLoader.FrameOf(null));
    }

    private RepoContext TempRepo()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, RepoContext.MarkerFile), "<Solution />");
        return RepoContext.At(_dir);
    }
}
