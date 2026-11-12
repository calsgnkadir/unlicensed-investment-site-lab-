using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.Text;
using Fraud.Collectors.CrtSh;
using Fraud.Domain.CertificateTransparency;

namespace Fraud.Collectors.StaticCt;

/// <summary>Sertifikadan saklama için gereken alanlar. Kişi verisi içermez.</summary>
/// <param name="Issuer">Temizlenmiş issuer: yalnızca C, O, OU, CN; e-posta içeren değer düşer.</param>
/// <param name="DnsNames">SAN dNSName değerleri: DNS adı biçiminde olanlar, küçük harf, tekrarsız, sıralı.</param>
public sealed record CertificateSummary(DateTimeOffset NotBefore, DateTimeOffset NotAfter, string Issuer, IReadOnlyList<string> DnsNames);

/// <summary>
/// X.509 sertifikasını veya TBSCertificate'i <see cref="AsnReader"/> ile okur (yönetilen kod, işletim sistemi
/// sertifika deposu veya zincir doğrulaması yok).
/// </summary>
/// <remarks>
/// Sertifikanın imzası doğrulanmaz; bu bilgi logun Merkle ağacıyla doğrulanır, sertifikanın kendisiyle değil.
/// Ortak ad (CN) okunmaz: Tarayıcılar yalnızca SAN'a bakar ve CN'de kişi adı olabilir.
/// Okunamayan sertifika için <c>null</c> döner; bu kayıt "atlanan" sayılır.
/// </remarks>
public static class CertificateSummaryReader
{
    private const string SubjectAltNameOid = "2.5.29.17";

    private static readonly Dictionary<string, string> AllowedIssuerAttributes = new(StringComparer.Ordinal)
    {
        ["2.5.4.6"] = "C",
        ["2.5.4.10"] = "O",
        ["2.5.4.11"] = "OU",
        ["2.5.4.3"] = "CN",
    };

    private static readonly Asn1Tag ExtensionsTag = new(TagClass.ContextSpecific, 3, isConstructed: true);
    private static readonly Asn1Tag VersionTag = new(TagClass.ContextSpecific, 0, isConstructed: true);
    private static readonly Asn1Tag DnsNameTag = new(TagClass.ContextSpecific, 2);

    public static CertificateSummary? TryRead(TileEntry entry) =>
        entry.IsTbsCertificate ? TryReadTbs(entry.Certificate) : TryReadCertificate(entry.Certificate);

    /// <summary>Tam sertifika: <c>SEQUENCE { tbsCertificate, signatureAlgorithm, signature }</c>.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Güvenilmez DER; ayrıştırıcının fırlatabileceği her istisna 'okunamadı' demektir, akışı durdurmamalı.")]
    public static CertificateSummary? TryReadCertificate(ReadOnlyMemory<byte> der)
    {
        try
        {
            var certificate = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
            return TryReadTbs(certificate.ReadEncodedValue());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Güvenilmez DER; ayrıştırıcının fırlatabileceği her istisna 'okunamadı' demektir, akışı durdurmamalı.")]
    public static CertificateSummary? TryReadTbs(ReadOnlyMemory<byte> tbsDer)
    {
        try
        {
            return ReadTbs(tbsDer);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static CertificateSummary ReadTbs(ReadOnlyMemory<byte> tbsDer)
    {
        // BER: Loglarda katı DER'e uymayan eski sertifikalar da var; DER, BER'in alt kümesidir.
        var outer = new AsnReader(tbsDer, AsnEncodingRules.BER);
        var tbs = outer.ReadSequence();
        if (tbs.PeekTag().HasSameClassAndValue(VersionTag))
        {
            tbs.ReadEncodedValue();
        }

        tbs.ReadEncodedValue(); // serialNumber
        tbs.ReadEncodedValue(); // signature (AlgorithmIdentifier)
        var issuer = FormatIssuer(tbs.ReadEncodedValue());
        var validity = tbs.ReadSequence();
        var notBefore = ReadTime(validity);
        var notAfter = ReadTime(validity);
        tbs.ReadEncodedValue(); // subject: kişi adı içerebilir, okunmaz.
        tbs.ReadEncodedValue(); // subjectPublicKeyInfo

        IReadOnlyList<string> names = [];
        while (tbs.HasData)
        {
            var tag = tbs.PeekTag();
            if (tag.HasSameClassAndValue(ExtensionsTag))
            {
                names = ReadSanDnsNames(tbs.ReadSequence(ExtensionsTag).ReadSequence());
            }
            else
            {
                tbs.ReadEncodedValue(); // issuerUniqueID, subjectUniqueID
            }
        }

        return new CertificateSummary(notBefore, notAfter, issuer, names);
    }

    private static DateTimeOffset ReadTime(AsnReader reader)
    {
        var tag = reader.PeekTag();
        if (tag.HasSameClassAndValue(Asn1Tag.UtcTime))
        {
            return reader.ReadUtcTime();
        }

        return reader.ReadGeneralizedTime();
    }

    private static List<string> ReadSanDnsNames(AsnReader extensions)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        while (extensions.HasData)
        {
            var extension = extensions.ReadSequence();
            var oid = extension.ReadObjectIdentifier();
            if (extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean))
            {
                extension.ReadBoolean(); // critical
            }

            var value = extension.ReadOctetString();
            if (oid != SubjectAltNameOid)
            {
                continue;
            }

            var generalNames = new AsnReader(value, AsnEncodingRules.BER).ReadSequence();
            while (generalNames.HasData)
            {
                if (!generalNames.PeekTag().HasSameClassAndValue(DnsNameTag))
                {
                    // IP adresi, e-posta (rfc822Name), URI ... okunmaz.
                    generalNames.ReadEncodedValue();
                    continue;
                }

                var name = SanitizeDnsName(generalNames.ReadOctetString(DnsNameTag));
                if (name is not null && names.Count < CtCandidate.MaxNames)
                {
                    names.Add(name);
                }
            }
        }

        return [.. names];
    }

    /// <summary>dNSName bir IA5String'dir. Yazdırılabilir ASCII dışı bayt varsa ad düşer; sonra ortak DNS adı kuralı uygulanır.</summary>
    private static string? SanitizeDnsName(byte[] value)
    {
        if (value.Length is 0 or > 253 || value.Any(b => b is < 0x21 or > 0x7E))
        {
            return null;
        }

        return CertificateFieldSanitizer.SanitizeName(Encoding.ASCII.GetString(value));
    }

    /// <summary>
    /// Issuer Name'ini izinli alanlardan (C, O, OU, CN) kurar ve ortak temizleyiciden geçirir.
    /// İzinsiz alanlar (e-posta, adres, telefon, seri no ...) hiç metne çevrilmez.
    /// </summary>
    private static string FormatIssuer(ReadOnlyMemory<byte> nameDer)
    {
        var parts = new List<string>();
        var rdns = new AsnReader(nameDer, AsnEncodingRules.BER).ReadSequence();
        while (rdns.HasData)
        {
            var rdn = rdns.ReadSetOf();
            while (rdn.HasData)
            {
                var attribute = rdn.ReadSequence();
                var oid = attribute.ReadObjectIdentifier();
                if (!AllowedIssuerAttributes.TryGetValue(oid, out var key))
                {
                    continue;
                }

                var value = ReadDirectoryString(attribute);
                if (value is not null)
                {
                    parts.Add(key + "=" + Quote(value));
                }
            }
        }

        return CertificateFieldSanitizer.SanitizeIssuer(string.Join(", ", parts));
    }

    private static string? ReadDirectoryString(AsnReader attribute)
    {
        var tag = attribute.PeekTag();
        if (tag.TagClass != TagClass.Universal)
        {
            return null;
        }

        return (UniversalTagNumber)tag.TagValue switch
        {
            UniversalTagNumber.UTF8String or UniversalTagNumber.PrintableString or UniversalTagNumber.IA5String
                or UniversalTagNumber.BMPString or UniversalTagNumber.VisibleString or UniversalTagNumber.T61String
                or UniversalTagNumber.UniversalString
                => attribute.ReadCharacterString((UniversalTagNumber)tag.TagValue),
            _ => null,
        };
    }

    /// <summary>Virgül, artı, tırnak veya ters bölü içeren değer tırnağa alınır; temizleyici DN'i buna göre böler.</summary>
    private static string Quote(string value)
    {
        if (value.IndexOfAny([',', '+', '"', '\\', '=']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
