using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fraud.Collectors.Evaluation;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Evaluation;

/// <summary>
/// İleriye dönük testin zaman damgası (E10). Testte kendi kök sertifikamızı ve TSA'mızı kurup RFC 3161 cevabını elle
/// üretiyoruz; ağ yok. Amaç: doğrulayıcının yanlış dosyayı, yanlış nonce'u, yanlış kökü ve EKU'suz imzalayanı reddetmesi.
/// </summary>
public sealed class TimestampAttackTests : IDisposable
{
    private static readonly DateTimeOffset NotBefore = new(2019, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NotAfter = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Tsa _tsa = Tsa.Create("Saldirgan Kok", eku: true);

    public void Dispose() => _tsa.Dispose();

    private static byte[] PublicFile(string text = "{\"Version\":1}") => Encoding.UTF8.GetBytes(text);

    private static (byte[] Tsq, Rfc3161TimestampRequest Request) RequestFor(byte[] file, long nonce = 0x1234_5678)
    {
        var nonceBytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(nonceBytes, nonce);
        var request = Rfc3161TimestampRequest.CreateFromHash(SHA256.HashData(file), HashAlgorithmName.SHA256, null, nonceBytes, true);
        return (request.Encode(), request);
    }

    private IReadOnlySet<string> Pinned => new HashSet<string>(StringComparer.Ordinal) { TimestampVerifier.Fingerprint(_tsa.Root) };

    [Fact]
    public void Valid_token_from_a_pinned_root_verifies_and_reports_the_fingerprint()
    {
        var file = PublicFile();
        var (tsq, request) = RequestFor(file);
        var at = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        var tsr = _tsa.Respond(request, at);

        var result = TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer, Pinned);

        Assert.Equal(at, result.Time);
        Assert.Equal(TimestampVerifier.Fingerprint(_tsa.Root), result.RootFingerprint);
    }

    [Fact]
    public void The_same_valid_token_is_rejected_when_its_root_is_not_pinned()
    {
        // E12: Doğru imzalı, doğru zincirli bir damga bile, kökü sabitlenmiş listede değilse reddedilir.
        var file = PublicFile();
        var (tsq, request) = RequestFor(file);
        var tsr = _tsa.Respond(request, new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
        using var other = Tsa.Create("Sabitlenmis Baska Kok", eku: true);

        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer, new HashSet<string>(StringComparer.Ordinal)).Time);
        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer,
            new HashSet<string>(StringComparer.Ordinal) { TimestampVerifier.Fingerprint(other.Root) }).Time);
        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer,
            new HashSet<string>(StringComparer.Ordinal) { TimestampVerifier.Fingerprint(_tsa.Root).ToUpperInvariant() }).Time);
        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer).Time); // gömülü liste (bugün boş)
    }

    [Fact]
    public void Wrong_file_wrong_nonce_wrong_root_or_tampered_response_are_rejected_even_with_a_pinned_root()
    {
        var file = PublicFile();
        var (tsq, request) = RequestFor(file);
        var at = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        var tsr = _tsa.Respond(request, at);
        Assert.NotNull(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer, Pinned).Time);

        // Başka bir dosya (açık taahhüt sonradan değişti).
        Assert.Null(TimestampVerifier.Verify(PublicFile("{\"Version\":2}"), tsq, tsr, _tsa.RootDer, Pinned).Time);

        // Başka bir isteğin cevabı (nonce farklı).
        var (otherTsq, _) = RequestFor(file, nonce: 0x9999);
        Assert.Null(TimestampVerifier.Verify(file, otherTsq, tsr, _tsa.RootDer, Pinned).Time);

        // İmzalayan başka bir köke zincirleniyor, ama verilen kök (sabitli) bu: zincir kurulmaz.
        using var other = Tsa.Create("Baska Kok", eku: true);
        var (tsq2, request2) = RequestFor(file, nonce: 0x7777);
        var foreign = other.Respond(request2, at);
        Assert.Null(TimestampVerifier.Verify(file, tsq2, foreign, _tsa.RootDer, Pinned).Time);

        // Cevabın bir baytı değişti.
        var tampered = (byte[])tsr.Clone();
        tampered[^20] ^= 0x01;
        Assert.Null(TimestampVerifier.Verify(file, tsq, tampered, _tsa.RootDer, Pinned).Time);
    }

    [Fact]
    public void Signer_without_the_timestamping_eku_is_rejected_even_with_a_pinned_root()
    {
        using var noEku = Tsa.Create("EKU yok", eku: false);
        var file = PublicFile();
        var (tsq, request) = RequestFor(file);
        var tsr = noEku.Respond(request, new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));

        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, noEku.RootDer,
            new HashSet<string>(StringComparer.Ordinal) { TimestampVerifier.Fingerprint(noEku.Root) }).Time);
    }

    [Fact]
    public void Self_made_root_with_a_backdated_time_is_not_accepted()
    {
        // E12 (kapalı): Kendi kökünü ve TSA'sını kuran biri 2020 tarihli bir damga üretir. Kök sabitlenmiş listede
        // olmadığı için (gömülü liste ya da başka bir kökün parmak izi) reddedilir.
        var file = PublicFile();
        var (tsq, request) = RequestFor(file);
        var tsr = _tsa.Respond(request, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var pinnedOther = Tsa.Create("Gercek TSA Kok", eku: true);

        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer).Time);
        Assert.Null(TimestampVerifier.Verify(file, tsq, tsr, _tsa.RootDer,
            new HashSet<string>(StringComparer.Ordinal) { TimestampVerifier.Fingerprint(pinnedOther.Root) }).Time);
    }

    [Fact(Skip = "açık: E13 ileriye dönük kip kaynak/derleme denetiminden geçmiyor; tsa_roots.txt yerelde değiştirilip kendi kökü eklenerek E12 atlatılır")]
    public void Prospective_mode_runs_only_from_a_verified_clean_build()
    {
        // Bulgu E13: Gömülü liste, ÇALIŞAN ikilinin derlendiği dosyadır. Kilitli kip temiz worktree'den derlenir; ileriye
        // dönük kip ise hiçbir denetim yapmaz (CheckSourceStateAsync / orkestratör çağrılmıyor). tsa_roots.txt'e kendi
        // kökünün parmak izini ekleyip (commit etmeden) derleyen biri, kendi TSA'sıyla geriye tarihli damga üretir ve
        // sonuç "doğrulanmış zaman damgası" diye basılır. Ayrıca "kök taahhütten ÖNCE commit edilmeli" kuralı kodda
        // denetlenmiyor. İleriye dönük kip de LockedRunOrchestrator'dan (temiz worktree, HEAD) geçmeli; parmak izinin
        // tsa_roots.txt'e eklendiği commit'in tarihi damga zamanından önce olmalı (git log --format=%cI -S<parmak_izi>).
        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/EvaluationCommands.cs");
        var prospective = code[code.IndexOf("if (mode == EvaluationMode.Prospective)", StringComparison.Ordinal)..];
        prospective = prospective[..prospective.IndexOf("var data = await loader.LoadAsync", StringComparison.Ordinal)];

        Assert.Matches("CheckSourceStateAsync|CheckFrozenStateAsync|LockedRunOrchestrator|Orchestrat", prospective);
    }

    [Fact]
    public void Locked_run_proves_the_running_code_not_just_a_clean_tree()
    {
        // Bulgu E11: (a) .gitignore'daki out/, publish/, logs/ gibi klasörler proje içinde de yok sayılır, ama SDK'nın
        // varsayılan derleme kalıbı (**/*.cs, yalnızca bin/ ve obj/ hariç) onları derler. src/Fraud.Evaluation/out/X.cs
        // eklenir, derlenir; git status --porcelain boş, ls-files -v temiz, HEAD aynı. (b) InformationalVersion'daki +sha
        // derleme sırasında git'ten okunur; değiştirilmiş bir ağaçtan -p:SourceRevisionId=<HEAD> ile derlenen ikili HEAD'i
        // söyler. (c) .git/config içindeki core.fsmonitor kancası git status'a "değişiklik yok" dedirtebilir; C:\dev
        // başka hesaplarca yazılabildiği için bu kanca onların kodunu da çalıştırır. Kilitli test, HEAD'den temiz bir
        // geçici worktree'de (git worktree add --detach) derlenip orada çalışmalı; en azından git status --ignored ile
        // src/ ve tests/ altındaki yok sayılan dosyalar reddedilmeli ve git çağrıları -c core.fsmonitor= ile yapılmalı.
        Assert.True(GitCheckIgnore("src/Fraud.Evaluation/out/Ek.cs"), "Ön koşul: bu yol git tarafından yok sayılıyor");

        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/RepoContext.cs")
                   + RepoFiles.Read("src/Fraud.Collectors/Evaluation/EvaluationCommands.cs");
        Assert.Matches(@"--ignored|worktree"", ""add|""worktree""", code);
        Assert.Contains("core.fsmonitor", code, StringComparison.Ordinal);
    }

    private static bool GitCheckIgnore(string relativePath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = RepoFiles.Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "check-ignore", "-q", "--", relativePath })
        {
            psi.ArgumentList.Add(a);
        }

        using var git = System.Diagnostics.Process.Start(psi)!;
        git.WaitForExit(10_000);
        return git.ExitCode == 0;
    }

    /// <summary>Test için kök sertifika + TSA sertifikası ve RFC 3161 cevap üreticisi.</summary>
    private sealed class Tsa : IDisposable
    {
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _signer;

        private Tsa(X509Certificate2 root, X509Certificate2 signer)
        {
            _root = root;
            _signer = signer;
        }

        public byte[] RootDer => _root.RawData;

        public X509Certificate2 Root => _root;

        public static Tsa Create(string name, bool eku)
        {
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=" + name, rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            var root = rootRequest.CreateSelfSigned(NotBefore, NotAfter);

            var signerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var signerRequest = new CertificateRequest("CN=" + name + " TSA", signerKey, HashAlgorithmName.SHA256);
            signerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            signerRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            if (eku)
            {
                signerRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], critical: true));
            }

            using var issued = signerRequest.Create(root, NotBefore, NotAfter, [0x01, 0x02, 0x03, 0x04]);
            var signer = issued.CopyWithPrivateKey(signerKey);
            return new Tsa(root, signer);
        }

        public byte[] Respond(Rfc3161TimestampRequest request, DateTimeOffset genTime)
        {
            var info = new Rfc3161TimestampTokenInfo(
                new Oid("1.2.3.4.1"), request.HashAlgorithmId, request.GetMessageHash(), new byte[] { 0x05 }, genTime,
                nonce: request.GetNonce());
            var content = new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode());
            var cms = new SignedCms(content, detached: false);
            var cmsSigner = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _signer)
            {
                IncludeOption = X509IncludeOption.EndCertOnly,
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            };
            cmsSigner.SignedAttributes.Add(new AsnEncodedData("1.2.840.113549.1.9.16.2.47", SigningCertificateV2(_signer)));
            cms.ComputeSignature(cmsSigner);

            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteInteger(0); // granted
                }

                writer.WriteEncodedValue(cms.Encode());
            }

            return writer.Encode();
        }

        /// <summary>SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 }; ESSCertIDv2 = { certHash } (sha256 varsayılan).</summary>
        private static byte[] SigningCertificateV2(X509Certificate2 certificate)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    using (writer.PushSequence())
                    {
                        writer.WriteOctetString(SHA256.HashData(certificate.RawData));
                    }
                }
            }

            return writer.Encode();
        }

        public void Dispose()
        {
            _root.Dispose();
            _signer.Dispose();
        }
    }
}
