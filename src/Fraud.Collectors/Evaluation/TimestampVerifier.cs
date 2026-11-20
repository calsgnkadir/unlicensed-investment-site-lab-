using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Fraud.Collectors.Evaluation;

/// <summary>Zaman damgası doğrulama sonucu. <see cref="Time"/> boşsa <see cref="Problem"/> nedenini söyler.</summary>
public sealed record TimestampVerification(DateTimeOffset? Time, string Problem)
{
    /// <summary>Kullanılan kök sertifikanın SHA-256 parmak izi (rapora yazılır).</summary>
    public string? RootFingerprint { get; init; }
}

/// <summary>
/// Sabitlenmiş TSA kökleri (E12): git'te izlenen <c>Evaluation/tsa_roots.txt</c>, derlemeye gömülü. Her satır bir kök
/// sertifikanın SHA-256 parmak izi; <c>#</c> sonrası açıklamadır.
/// </summary>
public static class PinnedTsaRoots
{
    private const string ResourceName = "Fraud.Collectors.tsa_roots.txt";

    /// <summary>Derlemeye gömülü liste. Şu an boş: gerçek bir kök eklenene kadar ileriye dönük test çalışmaz.</summary>
    public static IReadOnlySet<string> Default { get; } = Load();

    /// <summary>Liste metnini ayrıştırır. Geçersiz satır (64 küçük harf onaltılık değil) istisna verir.</summary>
    public static IReadOnlySet<string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length != 64 || !line.All(char.IsAsciiHexDigitLower))
            {
                throw new InvalidDataException("tsa_roots.txt: Her satır 64 karakterlik küçük harf SHA-256 parmak izi olmalı.");
            }

            set.Add(line);
        }

        return set;
    }

    private static IReadOnlySet<string> Load()
    {
        using var stream = typeof(PinnedTsaRoots).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Gömülü kaynak yok: " + ResourceName);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}

/// <summary>
/// RFC 3161 zaman damgası cevabını (.tsr) çevrimdışı doğrular ve damga zamanını (genTime) verir (E10).
/// </summary>
/// <remarks>
/// <para>Adımlar:</para>
/// <list type="number">
/// <item>İstek (.tsq) çözülür; içindeki özet, açık dosyanın SHA-256'sı olmalı.</item>
/// <item><see cref="Rfc3161TimestampRequest.ProcessResponse"/>: Cevabın durumu "verildi" olmalı; içindeki belirteç bu
/// isteğe ait olmalı (aynı özet, aynı nonce).</item>
/// <item><see cref="Rfc3161TimestampToken.VerifySignatureForHash(ReadOnlySpan{byte}, HashAlgorithmName, out X509Certificate2?, X509Certificate2Collection?)"/>:
/// TSA imzası geçerli ve imzalayan sertifika zaman damgası için yetkili (EKU) olmalı.</item>
/// <item>İmzalayan sertifikanın zinciri, YAPILANDIRMADAKİ kök sertifikaya dayanmalı. İşletim sisteminin güven deposu
/// kullanılmaz; iptal listesi ağdan sorulmaz (ağ yok). Zincir damga anına göre değerlendirilir.</item>
/// </list>
/// <para>Ağa bağlanılmaz. Damgayı almak elle yapılır (README).</para>
/// </remarks>
public static class TimestampVerifier
{
    private const string TimeStampingEku = "1.3.6.1.5.5.7.3.8";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>Sabitlenmiş kök listesiyle (<see cref="PinnedTsaRoots.Default"/>) doğrular.</summary>
    public static TimestampVerification Verify(byte[] publicFile, byte[] tsq, byte[] tsr, byte[] tsaRootCertificate) =>
        Verify(publicFile, tsq, tsr, tsaRootCertificate, PinnedTsaRoots.Default);

    /// <summary>
    /// Verilen kök listesiyle doğrular. Kök sertifikanın parmak izi listede değilse damga hiç incelenmez (E12): Kendi kökünü
    /// ve TSA'sını kuran biri geriye tarihli bir damga üretebilirdi.
    /// </summary>
    public static TimestampVerification Verify(
        byte[] publicFile, byte[] tsq, byte[] tsr, byte[] tsaRootCertificate, IReadOnlySet<string> pinnedRootFingerprints)
    {
        ArgumentNullException.ThrowIfNull(publicFile);
        ArgumentNullException.ThrowIfNull(tsq);
        ArgumentNullException.ThrowIfNull(tsr);
        ArgumentNullException.ThrowIfNull(tsaRootCertificate);
        ArgumentNullException.ThrowIfNull(pinnedRootFingerprints);
        var hash = SHA256.HashData(publicFile);

        string fingerprint;
        try
        {
            using var rootForPin = LoadRoot(tsaRootCertificate);
            fingerprint = Fingerprint(rootForPin);
        }
        catch (CryptographicException)
        {
            return Fail("kök sertifika okunamadı");
        }

        if (pinnedRootFingerprints.Count == 0)
        {
            return Fail("sabitlenmiş TSA kökü yok (tsa_roots.txt boş)");
        }

        if (!pinnedRootFingerprints.Contains(fingerprint))
        {
            return Fail("kök sertifika sabitlenmiş listede değil (parmak izi " + fingerprint + ")");
        }

        if (!Rfc3161TimestampRequest.TryDecode(tsq, out var request, out _) || request is null)
        {
            return Fail("istek (.tsq) çözülemedi");
        }

        if (request.HashAlgorithmId.Value != Sha256Oid || !request.GetMessageHash().Span.SequenceEqual(hash))
        {
            return Fail("istekteki özet açık dosyanın SHA-256'sı değil");
        }

        Rfc3161TimestampToken token;
        try
        {
            token = request.ProcessResponse(tsr, out _);
        }
        catch (CryptographicException)
        {
            return Fail("cevap bu isteğe ait değil ya da damga verilmemiş");
        }

        if (!token.TokenInfo.GetMessageHash().Span.SequenceEqual(hash))
        {
            return Fail("damgadaki özet açık dosyanın SHA-256'sı değil");
        }

        if (!token.VerifySignatureForHash(hash, HashAlgorithmName.SHA256, out var signer, extraCandidates: null) || signer is null)
        {
            return Fail("TSA imzası geçersiz");
        }

        using (signer)
        using (var root = LoadRoot(tsaRootCertificate))
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(TimeStampingEku));
            chain.ChainPolicy.VerificationTime = token.TokenInfo.Timestamp.UtcDateTime;
            foreach (var extra in token.AsSignedCms().Certificates)
            {
                chain.ChainPolicy.ExtraStore.Add(extra);
            }

            if (!chain.Build(signer))
            {
                return Fail("TSA sertifikası yapılandırmadaki köke zincirlenmiyor");
            }
        }

        return new TimestampVerification(token.TokenInfo.Timestamp, string.Empty) { RootFingerprint = fingerprint };
    }

    private static TimestampVerification Fail(string problem) => new(null, problem);

    /// <summary>Kök sertifikanın SHA-256 parmak izi: DER baytlarının özeti, küçük harf onaltılık.</summary>
    public static string Fingerprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));
    }

    /// <summary>Kök sertifika PEM ("-----BEGIN CERTIFICATE-----") ya da DER olabilir.</summary>
    public static X509Certificate2 LoadRoot(byte[] bytes)
    {
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        return text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : X509CertificateLoader.LoadCertificate(bytes);
    }
}
