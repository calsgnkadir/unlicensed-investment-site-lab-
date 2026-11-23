using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fraud.Domain.Signals;

/// <summary>Sözlük eşleşmesi: hangi terim ve baseline alt kümesinde mi.</summary>
/// <param name="Term">Eşleşen ilk terim (dosyadaki sırayla).</param>
/// <param name="Baseline">Terim baseline'ın 8 teriminden biriyse <c>true</c>.</param>
public sealed record DictionaryMatch(string Term, bool Baseline)
{
    /// <summary>control_members.dictionary_subset değeri.</summary>
    public string Subset => Baseline ? "baseline" : "wide";
}

/// <summary>
/// Geniş finans sözlüğü v1 (docs/research/finans_sozlugu.txt, FINANS_SOZLUGU.md Bölüm 4). Dosya DONMUŞTUR: SHA-256'sı
/// tutmazsa sözlük yüklenmez ve G3 kurulmaz.
/// </summary>
/// <remarks>
/// Eşleştirme kuralları (sırayla):
/// <list type="number">
/// <item>Yalnızca kayıtlı alan adının SOLDAKİ etiketi (TLD ve alt alan adı aranmaz).</item>
/// <item><c>xn--</c> etiketi Unicode'a çevrilir; çevrilemezse eşleşmez.</item>
/// <item>Harf katlama: ı İ I î → i, ş → s, ğ → g, ü û → u, ö → o, ç → c, â → a; sonra NFD ve birleştirici işaretler atılır, küçük harf.</item>
/// <item>Tire korunur. <c>!</c> ile başlayan hariç tutma dizeleri önce etiketten silinir, sonra terimler alt dize olarak aranır.</item>
/// </list>
/// </remarks>
public sealed class FinanceLexicon
{
    /// <summary>Donmuş dosyanın SHA-256'sı (FINANS_SOZLUGU.md Bölüm 7).</summary>
    public const string FrozenSha256 = "65b9dcc4ac358d872e1579e6edbaef6f80a4a8c58e0e70109ade9c5876832f05";

    /// <summary>Depo köküne göre dosya yolu.</summary>
    public const string RelativePath = "docs/research/finans_sozlugu.txt";

    /// <summary>Baseline'ın 8 terimi: tek kaynak <see cref="Signals.BaselineTerms.All"/>.</summary>
    private static readonly HashSet<string> BaselineSet = [.. Signals.BaselineTerms.All];

    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = false };

    private readonly IReadOnlyList<string> _terms;
    private readonly IReadOnlyList<string> _exclusions;

    private FinanceLexicon(IReadOnlyList<string> terms, IReadOnlyList<string> exclusions)
    {
        _terms = terms;
        _exclusions = exclusions;
    }

    public int TermCount => _terms.Count;

    /// <summary>Dosyayı okur ve SHA-256'sını donmuş değerle karşılaştırır.</summary>
    /// <exception cref="InvalidOperationException">Özet tutmuyor: dosya değişmiş.</exception>
    public static FinanceLexicon Load(byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(fileBytes)), FrozenSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("finans_sozlugu.txt donmuş sürümle aynı değil (SHA-256 tutmuyor). G3 kurulmaz.");
        }

        return Parse(Encoding.UTF8.GetString(fileBytes));
    }

    /// <summary>Özet denetimi olmadan ayrıştırır (yalnızca testler için).</summary>
    public static FinanceLexicon Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var terms = new List<string>();
        var exclusions = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('!'))
            {
                exclusions.Add(line[1..]);
            }
            else
            {
                terms.Add(line);
            }
        }

        return new FinanceLexicon(terms, exclusions);
    }

    /// <summary>Kayıtlı alan adının soldaki etiketinde terim arar. Yoksa <c>null</c>.</summary>
    public DictionaryMatch? Match(string registrableDomain)
    {
        ArgumentNullException.ThrowIfNull(registrableDomain);
        var label = registrableDomain.Split('.')[0];
        var folded = Fold(label);
        if (folded is null)
        {
            return null;
        }

        foreach (var exclusion in _exclusions)
        {
            folded = folded.Replace(exclusion, string.Empty, StringComparison.Ordinal);
        }

        var term = _terms.FirstOrDefault(t => folded.Contains(t, StringComparison.Ordinal));
        return term is null ? null : new DictionaryMatch(term, BaselineSet.Contains(term));
    }

    /// <summary>Etiketi Unicode'a çevirir ve Türkçe harfleri katlar.</summary>
    public static string? Fold(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        string unicode;
        try
        {
            unicode = label.StartsWith("xn--", StringComparison.Ordinal) ? Idn.GetUnicode(label) : label;
        }
        catch (ArgumentException)
        {
            return null;
        }

        var sb = new StringBuilder(unicode.Length);
        foreach (var c in unicode)
        {
            sb.Append(c switch
            {
                'ı' or 'İ' or 'I' or 'î' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' or 'û' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                'â' => 'a',
                _ => c,
            });
        }

        var decomposed = sb.ToString().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(c);
            }
        }

        return result.ToString().ToLowerInvariant();
    }
}
