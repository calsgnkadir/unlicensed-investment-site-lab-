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
/// Böylece <c>emailAddress=</c>, adres ve telefon gibi alanlar saklanmaz.</item>
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
        foreach (var part in SplitDistinguishedName(distinguishedName))
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
        return joined.Length <= MaxIssuerLength ? joined : joined[..MaxIssuerLength];
    }

    /// <summary>Virgülle ayırır; tırnak içindeki ve <c>\</c> ile kaçırılmış virgüller bölmez.</summary>
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
            else if (c == ',' && !inQuotes)
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
    [GeneratedRegex(@"^(\*\.)?[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?(?:\.[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex DnsNamePattern();
}
