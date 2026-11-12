using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.CrtSh;

/// <summary>
/// Sertifika alanlarından kişi verisini ayıklar (proje kuralı: veri setinde kişi verisi tutulmaz).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Adlar: yalnızca DNS adı biçimindeki değerler kalır. E-posta (S/MIME), boşluklu metin, kişi adı düşer.</item>
/// <item>Issuer: yalnızca C, O, OU ve CN alanları kalır; içinde <c>@</c> geçen alan düşer.
/// Böylece <c>emailAddress=</c>, adres ve telefon gibi alanlar saklanmaz. Çok değerli RDN (<c>+</c>) de bölünür;
/// izinli bir alanın içinde başka alan saklanamaz. Kontrol ve yön karakterleri temizlenir.</item>
/// </list>
/// Kural "izin listesi" mantığıyla yazılır: Tanımadığımız şeyi saklamayız.
/// </remarks>
public static partial class CertificateFieldSanitizer
{
    private static readonly HashSet<string> AllowedIssuerAttributes = new(StringComparer.OrdinalIgnoreCase) { "C", "O", "OU", "CN" };

    public const int MaxIssuerLength = 1024;

    /// <summary>crt.sh <c>name_value</c> alanı (satır sonuyla ayrılmış adlar) içinden DNS adlarını döner.</summary>
    public static IReadOnlyList<string> SanitizeNames(string? nameValue)
    {
        if (string.IsNullOrWhiteSpace(nameValue))
        {
            return [];
        }

        return nameValue
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLowerInvariant())
            .Where(IsDnsName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Tek bir değer DNS adıysa küçük harfli halini, değilse <c>null</c> döner.</summary>
    public static string? SanitizeName(string? value)
    {
        var name = value?.Trim().ToLowerInvariant();
        return name is not null && IsDnsName(name) ? name : null;
    }

    public static bool IsDnsName(string value) =>
        value.Length is > 0 and <= 253 && DnsNamePattern().IsMatch(value);

    /// <summary>
    /// Issuer DN'ini (ör. <c>C=US, O="Ornek, Inc.", CN=R3</c>) alanlara ayırır, izinli alanları birleştirir.
    /// </summary>
    public static string SanitizeIssuer(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var part in SplitDistinguishedName(RemoveControlAndFormatCharacters(distinguishedName)))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (AllowedIssuerAttributes.Contains(key) && value.Length > 0 && !value.Contains('@', StringComparison.Ordinal))
            {
                kept.Add(key.ToUpperInvariant() + "=" + value);
            }
        }

        var joined = string.Join(", ", kept);
        if (joined.Length <= MaxIssuerLength)
        {
            return joined;
        }

        // Kesme noktası bir vekil çiftinin (ör. emoji) ortasına düşerse yalnız yüksek vekil kalmasın.
        var cut = char.IsHighSurrogate(joined[MaxIssuerLength - 1]) ? MaxIssuerLength - 1 : MaxIssuerLength;
        return joined[..cut];
    }

    /// <summary>
    /// Kontrol karakterlerini (NUL, CR, LF ...) boşluğa çevirir, biçim karakterlerini (Unicode Cf: yön işaretleri
    /// U+202A-U+202E ve U+2066-U+2069, sıfır genişlikli karakterler) siler. NUL PostgreSQL metin alanına yazılamaz;
    /// CR/LF log ve panelde sahte satır, yön işaretleri ters çevrilmiş metin üretir.
    /// </summary>
    private static string RemoveControlAndFormatCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                builder.Append(' ');
            }
            else if (char.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// RDN'lere ayırır: virgülde ve çok değerli RDN'in <c>+</c> işaretinde (ör. <c>CN=X + telephoneNumber=...</c>).
    /// Tırnak içindeki ve <c>\</c> ile kaçırılmış işaretler bölmez. <c>+</c> yalnızca ardından bir alan adı ve
    /// <c>=</c> geliyorsa ayırıcı sayılır; böylece <c>telephoneNumber=+90...</c> değerindeki <c>+</c> bölmez.
    /// </summary>
    private static List<string> SplitDistinguishedName(string dn)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < dn.Length; i++)
        {
            var c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                current.Append(c).Append(dn[i + 1]);
                i++;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if (!inQuotes && (c == ',' || (c == '+' && AttributeStart().IsMatch(dn.AsSpan(i + 1)))))
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    // İsteğe bağlı "*." öneki, ardından en az iki etiket. Etiket: harf, rakam, tire, alt çizgi; 1-63 karakter.
    // Sonda "$" değil "\z": .NET'te "$" sondaki satır sonundan önce de eşleşir ("ad\n" geçerli sayılırdı).
    [GeneratedRegex(@"^(\*\.)?[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?(?:\.[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex DnsNamePattern();

    // Bir RDN alanının başlangıcı: "UID=" gibi bir ad ya da "2.5.4.3=" gibi bir OID, ardından "=".
    [GeneratedRegex(@"^\s*(?:[A-Za-z][A-Za-z0-9-]*|[0-9]+(?:\.[0-9]+)*)\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeStart();
}
