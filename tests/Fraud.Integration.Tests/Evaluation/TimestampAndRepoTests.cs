using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fraud.Collectors.Evaluation;
using Fraud.Evaluation.Commitments;
using Fraud.Infrastructure.DomainNames;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Evaluation;

/// <summary>
/// E8 (git kökü), E9 (git yolu) ve E10 (doğrulanmış zaman damgası). Zaman damgası kuruluşu (TSA) bu testte kendi
/// imzaladığımız sahte bir kuruluştur; ağa istek gitmez.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TimestampAndRepoTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset GenTime = new(2026, 11, 20, 8, 30, 0, TimeSpan.Zero);
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("tsa-");

    public void Dispose() => _dir.Delete(recursive: true);

    // ---- E10 ----

    [Fact]
    public void Valid_timestamp_gives_the_tsa_time_not_the_declared_time()
    {
        using var tsa = FakeTsa.Create();
        var publicFile = Encoding.UTF8.GetBytes("""{"Root":"00"}""");
        var tsq = CommitmentTool.TimestampRequest(publicFile, 4242);

        var result = TimestampVerifier.Verify(publicFile, tsq, tsa.Respond(tsq, GenTime), tsa.Root.RawData, tsa.Pins);

        Assert.Equal(GenTime, result.Time);
        Assert.Equal(TimestampVerifier.Fingerprint(tsa.Root), result.RootFingerprint);

        // E12: Aynı damga, kök sabitlenmemişse (gömülü liste boş) kabul edilmez.
        var unpinned = TimestampVerifier.Verify(publicFile, tsq, tsa.Respond(tsq, GenTime), tsa.Root.RawData);
        Assert.Null(unpinned.Time);
        Assert.Contains("sabitlenmiş", unpinned.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_root_changed_file_or_foreign_response_never_gives_a_time()
    {
        using var tsa = FakeTsa.Create();
        using var other = FakeTsa.Create();
        var publicFile = Encoding.UTF8.GetBytes("""{"Root":"00"}""");
        var tsq = CommitmentTool.TimestampRequest(publicFile, 4242);
        var tsr = tsa.Respond(tsq, GenTime);

        // Başka bir kökün imzaladığı TSA: zincir tutmaz.
        Assert.Null(TimestampVerifier.Verify(publicFile, tsq, tsr, other.Root.RawData, tsa.Pins).Time);
        // Kökü sabitlenmiş olsa bile başka kökün imzaladığı TSA zincirlenmez.
        Assert.Null(TimestampVerifier.Verify(publicFile, tsq, tsr, other.Root.RawData, other.Pins).Time);
        // Açık dosya sonradan değişti (ör. CreatedAt geriye çekildi): özet tutmaz.
        Assert.Null(TimestampVerifier.Verify(Encoding.UTF8.GetBytes("""{"Root":"01"}"""), tsq, tsr, tsa.Root.RawData, tsa.Pins).Time);
        // Başka bir isteğe (başka nonce) verilmiş cevap.
        var foreign = tsa.Respond(CommitmentTool.TimestampRequest(publicFile, 999), GenTime);
        Assert.Null(TimestampVerifier.Verify(publicFile, tsq, foreign, tsa.Root.RawData, tsa.Pins).Time);
        // Bozuk cevap.
        Assert.Null(TimestampVerifier.Verify(publicFile, tsq, [.. tsr.Take(tsr.Length - 3)], tsa.Root.RawData, tsa.Pins).Time);
    }

    [Fact]
    public async Task Prospective_run_prints_a_result_only_with_a_verified_timestamp()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var tsa = FakeTsa.Create();
        File.WriteAllText(Path.Combine(_dir.FullName, RepoContext.MarkerFile), "<Solution />");
        var repo = RepoContext.At(_dir.FullName);
        var records = Path.Combine(_dir.FullName, "records.txt");
        await File.WriteAllLinesAsync(records, ["aday-1.example", "aday-2.example"], ct);
        var publicOut = Path.Combine(_dir.FullName, "public.json");
        using var output = new StringWriter();
        Assert.Equal(0, await EvaluationCommands.CommitCreateAsync(
            EvaluationCommands.ParseArgs(["--records", records, "--name", "ileri", "--public-out", publicOut]), output, ct, repo));
        var opening = Path.Combine(_dir.FullName, "data", "private", "commitments", "ileri.opening.json");

        var rootFile = Path.Combine(_dir.FullName, "tsa-root.cer");
        await File.WriteAllBytesAsync(rootFile, tsa.Root.RawData, ct);
        var config = Path.Combine(_dir.FullName, "eval.json");
        await File.WriteAllTextAsync(config, "{ \"timestampAuthorityRootFile\": " + System.Text.Json.JsonSerializer.Serialize(rootFile) + " }", ct);
        string[] args = ["--mode", "Prospective", "--config", config, "--commitment", publicOut, "--opening", opening];
        var normalizer = new PublicSuffixDomainNormalizer();
        var pinnedBefore = new EvaluationCommands.TestHooks(tsa.Pins, _ => (new string('c', 40), GenTime.AddDays(-30)));
        Task<int> Run(TextWriter w, EvaluationCommands.TestHooks hooks) =>
            EvaluationCommands.EvalRunCoreAsync(db, normalizer, EvaluationCommands.ParseArgs(args), w, repo, hooks, ct);

        // E13: Kancasız (komut satırının yolu) ileriye dönük kip temiz derleme ister. Geçici kök git deposu değil: durur.
        using (var noBuild = new StringWriter())
        {
            Assert.Equal(64, await EvaluationCommands.EvalRunAsync(db, normalizer, EvaluationCommands.ParseArgs(args), noBuild, ct, repo));
            Assert.DoesNotContain("Recall", noBuild.ToString(), StringComparison.Ordinal);
        }

        // .tsr yok: sonuç basılmaz.
        using (var noTsr = new StringWriter())
        {
            Assert.Equal(2, await Run(noTsr, pinnedBefore));
            Assert.DoesNotContain("Recall", noTsr.ToString(), StringComparison.Ordinal);
        }

        // Başka kökün damgası: sonuç basılmaz.
        using var other = FakeTsa.Create();
        await File.WriteAllBytesAsync(publicOut + ".tsr", other.Respond(await File.ReadAllBytesAsync(publicOut + ".tsq", ct), GenTime), ct);
        using (var wrong = new StringWriter())
        {
            Assert.Equal(2, await Run(wrong, pinnedBefore));
            Assert.Contains("zaman damgası doğrulanamadı", wrong.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Recall", wrong.ToString(), StringComparison.Ordinal);
        }

        // Doğru damga: zaman genTime'dan gelir.
        File.Delete(publicOut + ".tsr");
        await File.WriteAllBytesAsync(publicOut + ".tsr", tsa.Respond(await File.ReadAllBytesAsync(publicOut + ".tsq", ct), GenTime), ct);
        using var ok = new StringWriter();
        Assert.Equal(0, await Run(ok, pinnedBefore));
        Assert.Contains("commit'inde", ok.ToString(), StringComparison.Ordinal);
        Assert.Contains("genTime)=2026-11-20 08:30:00Z", ok.ToString(), StringComparison.Ordinal);
        Assert.Contains("TSA kökü SHA-256=" + TimestampVerifier.Fingerprint(tsa.Root), ok.ToString(), StringComparison.Ordinal);
        Assert.Contains("Recall", ok.ToString(), StringComparison.Ordinal);

        // E12: Sabitlenmiş liste boşsa (gömülü varsayılan) ileriye dönük test hiç çalışmaz.
        using var empty = new StringWriter();
        Assert.Equal(2, await Run(empty, pinnedBefore with { PinnedTsaRoots = new HashSet<string>() }));
        Assert.Contains("Sabitlenmiş TSA kökü yok", empty.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Recall", empty.ToString(), StringComparison.Ordinal);

        // E13: Kök damgadan SONRA commit edildiyse ya da commit'i bulunamazsa sonuç basılmaz.
        using var pinnedAfter = new StringWriter();
        Assert.Equal(2, await Run(pinnedAfter, pinnedBefore with { PinCommit = _ => (new string('d', 40), GenTime.AddMinutes(1)) }));
        Assert.Contains("damga zamanından önce değil", pinnedAfter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Recall", pinnedAfter.ToString(), StringComparison.Ordinal);
        using var notPinned = new StringWriter();
        Assert.Equal(2, await Run(notPinned, pinnedBefore with { PinCommit = _ => null }));
        Assert.DoesNotContain("Recall", notPinned.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Pinned_root_list_is_strict_and_the_shipped_list_is_empty()
    {
        Assert.Empty(PinnedTsaRoots.Default);
        var fp = new string('a', 64);
        Assert.Equal([fp], PinnedTsaRoots.Parse("# yorum" + Environment.NewLine + fp + "  # TSA adı" + Environment.NewLine));
        Assert.Throws<InvalidDataException>(() => PinnedTsaRoots.Parse(new string('A', 64)));
        Assert.Throws<InvalidDataException>(() => PinnedTsaRoots.Parse("kisa"));
    }

    // ---- E11 ----

    [Fact]
    public void Ignored_files_under_src_or_tests_are_found_except_build_outputs()
    {
        var porcelain = string.Join("\n",
            "!! src/Fraud.Evaluation/out/Ek.cs",
            "!! src/Fraud.Evaluation/bin/Debug/x.dll",
            "!! src/Fraud.Evaluation/obj/project.assets.json",
            "!! tests/Fraud.Evaluation.Tests/publish/y.cs",
            "!! data/private/liste.txt",
            "?? src/yeni.cs");

        Assert.Equal(["src/Fraud.Evaluation/out/Ek.cs", "tests/Fraud.Evaluation.Tests/publish/y.cs"], RepoContext.FindIgnoredSourcePaths(porcelain));
    }

    [Fact]
    public void Locked_run_inner_arguments_point_into_the_temporary_worktree()
    {
        var root = Directory.CreateDirectory(Path.Combine(_dir.FullName, "ana")).FullName;
        File.WriteAllText(Path.Combine(root, RepoContext.MarkerFile), "");
        var repo = RepoContext.At(root);
        var worktree = Path.Combine(_dir.FullName, "gecici");
        var args = EvaluationCommands.ParseArgs(["--commit", new string('a', 40), "--clusters", Path.Combine(root, "data", "private", "k.csv")]);

        var inner = LockedRunOrchestrator.InnerArguments(repo, Fraud.Evaluation.EvaluationMode.Locked, worktree, Path.Combine(root, "config", "eval.json"), args).ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal("Locked", inner["mode"]);
        Assert.Equal(Path.Combine(worktree, "config", "eval.json"), inner["config"]);
        Assert.Equal(Path.Combine(root, "data", "private", "k.csv"), inner["clusters"]);
        Assert.True(LockedRunOrchestrator.IsInside(Path.Combine(worktree, "src", "a.dll"), worktree));
        Assert.False(LockedRunOrchestrator.IsInside(Path.Combine(root, "src", "a.dll"), worktree));
    }

    [Fact]
    public void Pin_commit_of_an_unknown_fingerprint_is_not_found_in_the_real_repo()
    {
        var repo = RepoContext.Find(AppContext.BaseDirectory);

        Assert.Null(repo.PinCommit(new string('a', 64)));
        Assert.Null(repo.PinCommit("--format=x")); // biçimsiz girdi git'e hiç gitmez
    }

    [Fact]
    public void Prospective_inner_arguments_carry_the_commitment_files_as_full_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(_dir.FullName, "ana2")).FullName;
        File.WriteAllText(Path.Combine(root, RepoContext.MarkerFile), "");
        var args = EvaluationCommands.ParseArgs(["--commit", new string('a', 40), "--commitment", "docs/c.json", "--opening", "o.json"]);

        var inner = LockedRunOrchestrator.InnerArguments(RepoContext.At(root), Fraud.Evaluation.EvaluationMode.Prospective,
            Path.Combine(_dir.FullName, "gecici"), Path.Combine(root, "eval.json"), args).ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal("Prospective", inner["mode"]);
        Assert.Equal(Path.GetFullPath("docs/c.json"), inner["commitment"]);
        Assert.True(Path.IsPathFullyQualified(inner["opening"]));
    }

    // ---- E8 ----

    [Fact]
    public void Real_repo_root_is_the_git_top_level_and_the_lock_lives_in_the_git_common_dir()
    {
        var repo = RepoContext.Find(AppContext.BaseDirectory);

        Assert.True(File.Exists(Path.Combine(repo.Root, RepoContext.MarkerFile)));
        var lockPath = repo.LockFilePath();
        Assert.EndsWith(Path.Combine("fraud-lab", "locked-run.json"), lockPath, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar, lockPath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_file_in_an_ignored_folder_inside_the_repo_is_not_a_root()
    {
        // bin/ git dışıdır: Buraya konan işaret dosyası çalışma ağacını "temiz" bırakırdı. Kök sayılmamalı.
        var fake = Path.Combine(AppContext.BaseDirectory, "sahte-kok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fake);
        try
        {
            File.WriteAllText(Path.Combine(fake, RepoContext.MarkerFile), "");
            Assert.Throws<InvalidOperationException>(() => RepoContext.Find(fake));
        }
        finally
        {
            Directory.Delete(fake, recursive: true);
        }
    }

    // ---- E9 ----

    [Fact]
    public void Git_is_resolved_only_from_absolute_path_entries_outside_the_working_directory()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var cwd = Directory.CreateDirectory(Path.Combine(_dir.FullName, "calisma")).FullName;
        var inside = Directory.CreateDirectory(Path.Combine(cwd, "alt")).FullName;
        var trusted = Directory.CreateDirectory(Path.Combine(_dir.FullName, "guvenilir")).FullName;
        foreach (var d in new[] { cwd, inside, trusted })
        {
            File.WriteAllText(Path.Combine(d, name), "");
        }

        var sep = Path.PathSeparator.ToString();
        // Göreli girdi ("." ve "alt"), çalışma klasörü ve onun alt klasörü atlanır; güvenilir klasör seçilir.
        var path = string.Join(sep, ".", "alt", cwd, inside, trusted);
        Assert.Equal(Path.Combine(trusted, name), GitExecutable.Resolve(null, path, cwd));
        Assert.Throws<InvalidOperationException>(() => GitExecutable.Resolve(null, string.Join(sep, ".", cwd, inside), cwd));

        // Ayarla verilen yol tam ve var olmalı.
        Assert.Equal(Path.Combine(trusted, name), GitExecutable.Resolve(Path.Combine(trusted, name), string.Empty, cwd));
        Assert.Throws<InvalidOperationException>(() => GitExecutable.Resolve(name, path, cwd));
        Assert.Throws<InvalidOperationException>(() => GitExecutable.Resolve(Path.Combine(trusted, "yok.exe"), path, cwd));
    }

    /// <summary>Kendi imzaladığımız sahte zaman damgası kuruluşu: kök sertifika + zaman damgası EKU'lu TSA sertifikası.</summary>
    private sealed class FakeTsa : IDisposable
    {
        private FakeTsa(X509Certificate2 root, X509Certificate2 tsa)
        {
            Root = root;
            Tsa = tsa;
        }

        public X509Certificate2 Root { get; }

        public X509Certificate2 Tsa { get; }

        /// <summary>Bu testin kendi kökünü sabitleyen liste ("testte kendi kökünü listeye ekleyen yapılandırma").</summary>
        public IReadOnlySet<string> Pins => new HashSet<string>(StringComparer.Ordinal) { TimestampVerifier.Fingerprint(Root) };

        public static FakeTsa Create()
        {
            var notBefore = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var notAfter = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=Test TSA Kok", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            var root = rootRequest.CreateSelfSigned(notBefore, notAfter);

            using var tsaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var tsaRequest = new CertificateRequest("CN=Test TSA", tsaKey, HashAlgorithmName.SHA256);
            tsaRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            tsaRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            tsaRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], true));
            using var tsaPublic = tsaRequest.Create(root, notBefore, notAfter, RandomNumberGenerator.GetBytes(8));
            return new FakeTsa(root, tsaPublic.CopyWithPrivateKey(tsaKey));
        }

        /// <summary>TimeStampResp ::= SEQUENCE { status SEQUENCE { 0 }, timeStampToken (CMS SignedData, TSTInfo) }.</summary>
        public byte[] Respond(byte[] tsq, DateTimeOffset genTime)
        {
            Assert.True(Rfc3161TimestampRequest.TryDecode(tsq, out var request, out _));
            var info = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4.1"), request!.HashAlgorithmId, request.GetMessageHash(),
                RandomNumberGenerator.GetBytes(8), genTime, nonce: request.GetNonce());
            var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()));
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, Tsa)
            {
                IncludeOption = X509IncludeOption.EndCertOnly,
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            };

            // RFC 5816 signingCertificateV2: imzalayan sertifikanın SHA-256'sı (doğrulama bunu arar).
            var essWriter = new AsnWriter(AsnEncodingRules.DER);
            using (essWriter.PushSequence())
            using (essWriter.PushSequence())
            using (essWriter.PushSequence())
            {
                essWriter.WriteOctetString(SHA256.HashData(Tsa.RawData));
            }

            signer.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), essWriter.Encode()));
            cms.ComputeSignature(signer);

            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteInteger(0);
                }

                writer.WriteEncodedValue(cms.Encode());
            }

            return writer.Encode();
        }

        public void Dispose()
        {
            Root.Dispose();
            Tsa.Dispose();
        }
    }
}
