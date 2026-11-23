using System.Reflection;
using System.Text;
using Fraud.Domain.Signals;

namespace Fraud.Signals;

/// <summary>
/// Marka benzerliği sinyalinin belirteç listesi: yerli (G1'deki BDDK bankalarının soldaki etiketleri) + yabancı (gömülü, donmuş).
/// </summary>
/// <remarks>
/// <para>Yerli liste ÖZEL veridir: <c>data/private/signals/brands.txt</c> içinde durur ve <c>signals-brands</c> komutuyla
/// control_members tablosundaki G1 üyelerinden üretilir. Repoya yalnızca bu kod girer.</para>
/// <para>Yabancı liste <c>foreign_brands.txt</c> derlemeye gömülüdür (kaynak ve sınırlar dosyanın başında).</para>
/// <para>Her belirteç <see cref="Normalize"/>'dan geçer; etiket de aynı fonksiyondan geçtiği için iki taraf aynı dilde
/// karşılaştırılır. Normalleştirilmiş hali <see cref="MinTokenLength"/>'ten kısa olan belirteç alınmaz.</para>
/// </remarks>
public sealed class BrandList
{
    /// <summary>En kısa belirteç. 4 ve altı (ör. "hsbc", "qnb") alt dize olarak çok sayıda ilgisiz ada takılır.</summary>
    public const int MinTokenLength = 5;

    /// <summary>Yerli listenin depo köküne göre yolu (gizli).</summary>
    public const string DomesticRelativePath = "data/private/signals/brands.txt";

    private const string ForeignResource = "Fraud.Signals.foreign_brands.txt";

    private BrandList(IReadOnlyList<string> tokens, IReadOnlySet<string> plain, int domesticCount, int foreignCount)
    {
        Tokens = tokens;
        Plain = plain;
        DomesticCount = domesticCount;
        ForeignCount = foreignCount;
    }

    /// <summary>Normalleştirilmiş, tekrarsız belirteçler (sıralı: sonuç sıradan bağımsız olsun).</summary>
    public IReadOnlyList<string> Tokens { get; }

    /// <summary>
    /// Markaların YALNIZCA harf katlanmış yazımı (tire ve görsel benzer karakter dokunulmamış). "Etiket markanın kendisi mi"
    /// sorusu bununla cevaplanır: <c>0rnekbank</c> ya da <c>ornek-bank</c> markanın kendisi değil, taklididir.
    /// </summary>
    public IReadOnlySet<string> Plain { get; }

    public int DomesticCount { get; }

    public int ForeignCount { get; }

    /// <summary>Yerli satırlar ve gömülü yabancı listeden kurar.</summary>
    public static BrandList Create(IEnumerable<string> domesticLines)
    {
        ArgumentNullException.ThrowIfNull(domesticLines);
        var domesticLinesList = domesticLines.ToList();
        var foreignLines = ReadLines(ForeignText()).ToList();
        var domestic = Clean(domesticLinesList);
        var foreign = Clean(foreignLines);
        var all = domestic.Concat(foreign).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var plain = domesticLinesList.Concat(foreignLines)
            .Select(FinanceLexicon.Fold)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToHashSet(StringComparer.Ordinal);
        return new BrandList(all, plain, domestic.Count, foreign.Count);
    }

    /// <summary>Gömülü yabancı listenin ham metni (testler kuralları bununla denetler).</summary>
    public static string ForeignText()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ForeignResource)
            ?? throw new InvalidOperationException("Gömülü yabancı marka listesi bulunamadı.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Boş ve <c>#</c> ile başlayan satırları atar.</summary>
    public static IEnumerable<string> ReadLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
    }

    /// <summary>
    /// Karşılaştırma biçimi: Türkçe harf katlama, tire silme, sonra görsel benzer karakterleri tek biçime indirme.
    /// </summary>
    /// <remarks>
    /// Eşlemeler: rn → m, vv → w (önce çok harfliler); 0 → o, 1 → i, l → i, 3 → e, 4 → a, 5 → s, 7 → t, 8 → b; birkaç
    /// Kiril harfi (а е о р с х у і) Latin karşılığına. "l → i" gerçek "l"yi de değiştirir; sorun değil, çünkü belirteç de aynı
    /// fonksiyondan geçer (<c>wellsfargo</c> → <c>weiisfargo</c>, <c>we11sfargo</c> → <c>weiisfargo</c>). Çevrilemeyen IDN → <c>null</c>.
    /// </remarks>
    public static string? Normalize(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var folded = FinanceLexicon.Fold(label);
        if (folded is null)
        {
            return null;
        }

        var s = folded.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("rn", "m", StringComparison.Ordinal)
            .Replace("vv", "w", StringComparison.Ordinal);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '0' or 'о' => 'o',
                '1' or 'l' or 'і' => 'i',
                '3' or 'е' => 'e',
                '4' or 'а' => 'a',
                '5' => 's',
                '7' => 't',
                '8' => 'b',
                'р' => 'p',
                'с' => 'c',
                'х' => 'x',
                'у' => 'y',
                _ => c,
            });
        }

        return sb.ToString();
    }

    private static List<string> Clean(IEnumerable<string> lines) =>
        [.. lines
            .Select(Normalize)
            .Where(t => t is not null && t.Length >= MinTokenLength)
            .Select(t => t!)
            .Distinct(StringComparer.Ordinal)];
}
