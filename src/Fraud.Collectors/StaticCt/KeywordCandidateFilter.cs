using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fraud.Domain.CertificateTransparency;

namespace Fraud.Collectors.StaticCt;

/// <summary>Saklama filtresi ayarları (bölüm: <c>CandidateFilter</c>).</summary>
public sealed class CandidateFilterOptions
{
    public const string SectionName = "CandidateFilter";

    /// <summary>
    /// Aşama 0 ölçümündeki (docs/phase0/STATIC_CT_HACIM.md) liste. Ölçülen geçiş oranı (~%0,08) yalnızca bu
    /// liste için geçerlidir; terim eklemek oranı ve disk ihtiyacını değiştirir, yeniden ölçülmelidir.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultTerms = Fraud.Domain.Signals.BaselineTerms.All;

    /// <summary>Yapılandırmadan gelen terimler. Boşsa <see cref="DefaultTerms"/> kullanılır.</summary>
    public IList<string> Terms { get; } = [];

    public IReadOnlyList<string> EffectiveTerms => Terms.Count > 0 ? [.. Terms] : DefaultTerms;
}

/// <summary>
/// Varsayılan saklama filtresi: DNS adında terimlerden biri alt dize olarak geçiyorsa kayıt saklanır.
/// </summary>
/// <remarks>
/// <para>
/// Bu bir sinyal veya puan değildir. "trade", "capital" gibi terimler meşru sitelerde de çok geçer; filtre bilerek
/// geniş tutulur, yanlış alarm oranı bilinmez. Amaç yalnızca günde milyonlarca kaydın küçük bir kısmını saklamaktır.
/// </para>
/// <para>
/// Türkçe karakterli (IDN, <c>xn--</c>) adlar Unicode'a çevrilip Türkçe harfler ASCII karşılığına indirgenerek
/// de denenir: "yatırım" içeren bir IDN ad "yatirim" terimine takılır.
/// </para>
/// </remarks>
public sealed partial class KeywordCandidateFilter : ICandidateFilter
{
    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = false };

    private readonly string[] _terms;

    public KeywordCandidateFilter(CandidateFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var terms = options.EffectiveTerms
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (terms.Length == 0 || terms.Any(t => !TermPattern().IsMatch(t)))
        {
            throw new InvalidOperationException("CandidateFilter:Terms yalnızca küçük ASCII harf, rakam ve tire içermeli (3-63 karakter).");
        }

        _terms = terms;
    }

    public IReadOnlyList<string> Terms => _terms;

    public IReadOnlyList<string> Match(IReadOnlyList<string> dnsNames)
    {
        ArgumentNullException.ThrowIfNull(dnsNames);

        SortedSet<string>? matched = null;
        foreach (var name in dnsNames)
        {
            AddMatches(name, ref matched);
            if (name.Contains("xn--", StringComparison.Ordinal) && ToFoldedUnicode(name) is { } folded)
            {
                AddMatches(folded, ref matched);
            }
        }

        return matched is null ? [] : [.. matched];
    }

    private void AddMatches(string name, ref SortedSet<string>? matched)
    {
        foreach (var term in _terms)
        {
            if (name.Contains(term, StringComparison.Ordinal))
            {
                (matched ??= new SortedSet<string>(StringComparer.Ordinal)).Add(term);
            }
        }
    }

    private static string? ToFoldedUnicode(string name)
    {
        string unicode;
        try
        {
            unicode = Idn.GetUnicode(name.StartsWith("*.", StringComparison.Ordinal) ? name[2..] : name);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var sb = new StringBuilder(unicode.Length);
        foreach (var c in unicode.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'ı' or 'î' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ü' or 'û' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                'â' => 'a',
                _ => c,
            });
        }

        return sb.ToString();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,62}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TermPattern();
}
