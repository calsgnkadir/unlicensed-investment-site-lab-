using System.Globalization;
using System.Net;
using Fraud.Domain.DomainNames;
using Nager.PublicSuffix;
using Nager.PublicSuffix.Models;
using Nager.PublicSuffix.RuleParsers;
using Nager.PublicSuffix.RuleProviders;

namespace Fraud.Infrastructure.DomainNames;

/// <summary>
/// Alan adı normalleştirici. Adımlar sırasıyla:
/// 1) boşlukları at, <c>ornek[.]com</c> yazımını geri çevir;
/// 2) tam URL ise şemayı, yolu, kullanıcı bilgisini ve portu atıp yalnızca ana bilgisayar adını al;
/// 3) sondaki tek noktayı at (<c>ornek.com.</c>);
/// 4) kültürden bağımsız küçük harfe çevir (Türkçe I/ı ve İ tuzakları için <see cref="ToLowerTurkishSafe"/>);
/// 5) IDN adı punycode'a (ASCII) çevir ve sözdizimini denetle;
/// 6) Public Suffix List ile genel soneki ve kayıtlı alan adını bul;
/// 7) baştaki <c>www.</c> etiketini at (geriye yalnızca genel sonek kalmıyorsa).
/// </summary>
/// <remarks>
/// Liste derlemeye gömülü sabit bir kopyadır; çalışırken internetten indirilmez.
/// Böylece aynı girdi her makinede ve her zaman aynı sonucu verir.
/// </remarks>
public sealed class PublicSuffixDomainNormalizer : IDomainNameNormalizer
{
    private const string EmbeddedListName = "Fraud.Infrastructure.public_suffix_list.dat";
    private const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;

    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true, AllowUnassigned = false };

    private readonly DomainParser _parser;
    private readonly DomainDataStructure _rules;

    public PublicSuffixDomainNormalizer()
        : this(LoadEmbeddedList())
    {
    }

    /// <param name="publicSuffixListText">Public Suffix List dosyasının içeriği.</param>
    public PublicSuffixDomainNormalizer(string publicSuffixListText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicSuffixListText);

        // Özel (PRIVATE) bölüm de dahil: "x.herokuapp.com" gibi adlarda her kiracı ayrı sahip sayılır.
        var rules = new TldRuleParser(TldRuleDivisionFilter.All).ParseRules(publicSuffixListText);
        var provider = new StaticRuleProvider(rules);
        _rules = provider.GetDomainDataStructure()
            ?? throw new InvalidOperationException("Public Suffix List kuralları yüklenemedi.");
        _parser = new DomainParser(provider);
    }

    public DomainNormalizationResult Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return DomainNormalizationResult.Failure(DomainNormalizationError.Empty);
        }

        var host = ExtractHost(input.Trim().Replace("[.]", ".", StringComparison.Ordinal));
        if (host is null)
        {
            return DomainNormalizationResult.Failure(DomainNormalizationError.Unparseable);
        }

        if (host.EndsWith('.'))
        {
            host = host[..^1];
        }

        if (host.StartsWith('[') || IPAddress.TryParse(host, out _))
        {
            return DomainNormalizationResult.Failure(DomainNormalizationError.IpAddress);
        }

        var ascii = ToAscii(ToLowerTurkishSafe(host));
        if (ascii is null || !IsValidAsciiHost(ascii))
        {
            return DomainNormalizationResult.Failure(DomainNormalizationError.InvalidSyntax);
        }

        // "www." yalnızca ardında hâlâ kayıtlı bir alan adı kalıyorsa atılır ("www.com.tr" gibi durumlar için).
        if (ascii.StartsWith("www.", StringComparison.Ordinal))
        {
            var withoutWww = Split(ascii[4..]);
            if (withoutWww is not null)
            {
                return DomainNormalizationResult.Success(withoutWww);
            }
        }

        var result = Split(ascii);
        return result is null
            ? DomainNormalizationResult.Failure(DomainNormalizationError.PublicSuffixOnly)
            : DomainNormalizationResult.Success(result);
    }

    /// <summary>
    /// ASCII adı genel sonek, kayıtlı alan adı ve alt alan adı olarak ayırır.
    /// Kayıtlı alan adı yoksa (ad kendisi bir sonekse) <c>null</c> döner.
    /// </summary>
    private NormalizedDomain? Split(string asciiHost)
    {
        // Listedeki kurallar Unicode yazıldığı için ayrıştırıcıya Unicode biçim verilir.
        var unicodeHost = Idn.GetUnicode(asciiHost);
        var labels = asciiHost.Split('.');

        int suffixLabelCount;
        if (!_rules.Nested.ContainsKey(unicodeHost[(unicodeHost.LastIndexOf('.') + 1)..]))
        {
            // Son etiket listede hiç yoksa PSL'nin varsayılan kuralı ("*") geçerlidir:
            // sonek yalnızca son etikettir. Kütüphane bu durumda sonuç vermediği için elle uygulanır.
            suffixLabelCount = 1;
        }
        else
        {
            DomainInfo? info;
            try
            {
                info = _parser.Parse(unicodeHost);
            }
            catch (Nager.PublicSuffix.Exceptions.ParseException)
            {
                return null;
            }

            if (info is null || string.IsNullOrEmpty(info.RegistrableDomain) || string.IsNullOrEmpty(info.TopLevelDomain))
            {
                return null;
            }

            suffixLabelCount = info.TopLevelDomain.Split('.').Length;
        }

        // Sonucu Unicode metinden değil etiket sayısından üretiriz; böylece çıktı her zaman
        // girdinin ASCII etiketlerinden oluşur.
        if (labels.Length <= suffixLabelCount)
        {
            return null;
        }

        var publicSuffix = string.Join('.', labels[^suffixLabelCount..]);
        var registrable = string.Join('.', labels[^(suffixLabelCount + 1)..]);
        var subdomain = labels.Length > suffixLabelCount + 1
            ? string.Join('.', labels[..^(suffixLabelCount + 1)])
            : null;

        return new NormalizedDomain(asciiHost, registrable, subdomain, publicSuffix, unicodeHost);
    }

    /// <summary>
    /// Girdi tam URL ya da "ornek.com/yol" biçiminde olabilir. Uri sınıfı yerine elle ayrıştırma
    /// kullanılır; Uri'nin kendi IDN ve büyük/küçük harf davranışına bağlı kalmamak için.
    /// </summary>
    private static string? ExtractHost(string value)
    {
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            value = value[(schemeEnd + 3)..];
        }
        else if (value.StartsWith("//", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        var pathStart = value.IndexOfAny(['/', '?', '#', '\\']);
        if (pathStart >= 0)
        {
            value = value[..pathStart];
        }

        var userInfoEnd = value.LastIndexOf('@');
        if (userInfoEnd >= 0)
        {
            value = value[(userInfoEnd + 1)..];
        }

        // IPv6 köşeli parantezliyse portu ayırmaya çalışmayız; IP olarak reddedilecek.
        if (!value.StartsWith('['))
        {
            var portStart = value.LastIndexOf(':');
            if (portStart >= 0)
            {
                var port = value[(portStart + 1)..];
                if (port.Length == 0 || !port.All(char.IsAsciiDigit))
                {
                    return null;
                }

                value = value[..portStart];
            }
        }

        return value.Length == 0 || value.Any(char.IsWhiteSpace) ? null : value;
    }

    /// <summary>
    /// Kültürden bağımsız küçük harf. Türkçe kültürde <c>"I".ToLower()</c> noktasız "ı" verir ve ad
    /// başka bir IDN ada dönüşür; bu yüzden <see cref="string.ToLowerInvariant"/> kullanılır.
    /// Büyük noktalı "İ" ayrıca "i" yapılır: IDN kuralları onu "i" + birleşik nokta (U+0307) yapar ve
    /// bu, görünüşte aynı ama farklı bir alan adıdır. Türkçe bültenlerde "İ" yalnızca "i"nin büyüğüdür.
    /// </summary>
    private static string ToLowerTurkishSafe(string host) =>
        host.Replace('İ', 'i').ToLowerInvariant();

    private static string? ToAscii(string lowerHost)
    {
        try
        {
            return Idn.GetAscii(lowerHost);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsValidAsciiHost(string host)
    {
        if (host.Length is 0 or > MaxHostLength)
        {
            return false;
        }

        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            if (!label.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string LoadEmbeddedList()
    {
        var assembly = typeof(PublicSuffixDomainNormalizer).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedListName)
            ?? throw new InvalidOperationException($"Gömülü kaynak bulunamadı: {EmbeddedListName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
