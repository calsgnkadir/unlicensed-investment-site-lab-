using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Fraud.Collectors.Evaluation;

/// <summary>Zaman damgası doğrulama sonucu. <see cref="Time"/> boşsa <see cref="Problem"/> nedenini söyler.</summary>
public sealed record TimestampVerification(DateTimeOffset? Time, string Problem);

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

    public static TimestampVerification Verify(byte[] publicFile, byte[] tsq, byte[] tsr, byte[] tsaRootCertificate)
    {
        ArgumentNullException.ThrowIfNull(publicFile);
        ArgumentNullException.ThrowIfNull(tsq);
        ArgumentNullException.ThrowIfNull(tsr);
        ArgumentNullException.ThrowIfNull(tsaRootCertificate);
        var hash = SHA256.HashData(publicFile);

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

        return new TimestampVerification(token.TokenInfo.Timestamp, string.Empty);
    }

    private static TimestampVerification Fail(string problem) => new(null, problem);

    /// <summary>Kök sertifika PEM ("-----BEGIN CERTIFICATE-----") ya da DER olabilir.</summary>
    private static X509Certificate2 LoadRoot(byte[] bytes)
    {
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        return text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
            ? X509Certificate2.CreateFromPem(text)
            : X509CertificateLoader.LoadCertificate(bytes);
    }
}
