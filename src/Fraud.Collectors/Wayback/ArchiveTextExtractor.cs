using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.Wayback;

/// <summary>Arşiv sayfasından çıkarılan başlık ve görünür metin. İkisi de güvenilmez veridir.</summary>
public sealed record ArchiveText(string Title, string Text);

/// <summary>
/// Arşivlenmiş HTML'den başlığı ve görünür metni çıkarır. HTML çalıştırılmaz, ayrıştırıcı kütüphane kullanılmaz;
/// yalnızca metin işlemi yapılır.
/// </summary>
/// <remarks>
/// Sıra önemlidir:
/// <list type="number">
/// <item>Wayback araç çubuğu bloğu ve HTML yorumları silinir (yorum içindeki etiket metne karışmasın diye önce).</item>
/// <item>Başlık alınır, sonra <c>title</c>, <c>head</c>, <c>script</c>, <c>style</c>, <c>noscript</c>, <c>template</c>,
/// <c>svg</c> öğeleri içerikleriyle silinir. Kapanmamış <c>script</c> benzeri öğe sayfa sonuna kadar silinir
/// (tarayıcı da öyle okur). Kapanmamış <c>head</c> silinmez, çünkü HTML'de <c>&lt;/head&gt;</c> isteğe bağlıdır.</item>
/// <item>Kalan etiketler silinir; blok etiketleri satır sonuna döner ki metin okunabilsin. Kaynaktaki satır sonları boşluk sayılır.</item>
/// <item>HTML varlıkları (<c>&amp;amp;</c>, <c>&amp;#39;</c>) çözülür. Çözülen <c>&lt;</c> artık düz metindir, etiket değildir.</item>
/// <item>Kontrol karakterleri ve yön değiştiren Unicode karakterleri silinir: Dosya terminalde açılınca
/// kaçış dizisi çalışmasın, metin ters yönde gösterilip okuyanı yanıltmasın.</item>
/// <item>Boşluklar daraltılır.</item>
/// </list>
/// Bütün düzenli ifadeler <see cref="RegexOptions.NonBacktracking"/> ile çalışır: Süre girdi boyuna doğrusal
/// büyür, kötü niyetli sayfa geri izleme patlaması (ReDoS) yaratamaz. Ayrıca süre sınırı vardır.
/// </remarks>
public static class ArchiveTextExtractor
{
    public const int DefaultMaxTextChars = 4000;
    public const int DefaultMaxTitleChars = 300;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    // id_ kipinde araç çubuğu eklenmez; başka kipte çekilmiş sayfa gelirse diye yine de silinir.
    private static readonly Regex WaybackToolbar = new(
        "<!--\\s*BEGIN WAYBACK TOOLBAR INSERT\\s*-->.*?(<!--\\s*END WAYBACK TOOLBAR INSERT\\s*-->|\\z)", Options, MatchTimeout);

    // Kapanmamış yorum sayfa sonuna kadar yorumdur.
    private static readonly Regex Comment = new("<!--.*?(-->|\\z)", Options, MatchTimeout);

    private static readonly Regex Title = new("<title(\\s[^>]*)?>(.*?)</title\\s*>", Options, MatchTimeout);

    private static readonly Regex[] RemovedElements =
    [
        // Önce iç öğeler (svg içinde style olabilir), sonra kapsayanlar.
        Element("script", untilEndIfUnclosed: true),
        Element("style", untilEndIfUnclosed: true),
        Element("noscript", untilEndIfUnclosed: true),
        Element("template", untilEndIfUnclosed: true),
        Element("svg", untilEndIfUnclosed: true),
        Element("title", untilEndIfUnclosed: false),
        Element("head", untilEndIfUnclosed: false),
    ];

    private static readonly Regex BlockTag = new(
        "</?(p|div|br|li|ul|ol|tr|td|th|table|h[1-6]|section|article|header|footer|nav|form|label|button|option|blockquote|pre|hr)(\\s[^>]*)?/?>",
        Options, MatchTimeout);

    private static readonly Regex AnyTag = new("<[^>]*>", Options, MatchTimeout);

    // Satır içindeki her türlü boşluk (NBSP dahil) tek boşluğa iner; satır sonları korunur.
    private static readonly Regex InlineSpace = new("[^\\S\\n]+", Options, MatchTimeout);

    private static readonly Regex BlankLines = new(" ?\\n[ \\n]*", Options, MatchTimeout);

    /// <summary>
    /// Başlık ve görünür metni çıkarır, sınırsız uzunlukta döndürür. Kişi verisi temizliği çıkarılan metinde yapılır,
    /// kısaltma ondan sonra (<see cref="Cap"/>); yoksa kesim noktasında yarım kalan bir numara temizlikten kaçabilir.
    /// </summary>
    /// <exception cref="RegexMatchTimeoutException">Sayfa süre sınırında işlenemedi.</exception>
    public static ArchiveText Extract(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return new ArchiveText(string.Empty, string.Empty);
        }

        // Kaynaktaki satır sonları görünür değildir; satır sonunu yalnızca blok etiketleri üretir.
        var text = html.ReplaceLineEndings(" ");
        text = WaybackToolbar.Replace(text, " ");
        text = Comment.Replace(text, " ");

        var titleMatch = Title.Match(text);
        var title = titleMatch.Success ? Clean(AnyTag.Replace(titleMatch.Groups[2].Value, " "), keepLines: false) : string.Empty;

        foreach (var element in RemovedElements)
        {
            text = element.Replace(text, "\n");
        }

        text = BlockTag.Replace(text, "\n");
        text = AnyTag.Replace(text, " ");
        return new ArchiveText(title, Clean(text, keepLines: true));
    }

    /// <summary>Başlık ve metni ayrı sınırlarla kısaltır.</summary>
    public static ArchiveText Cap(ArchiveText page, int maxTitleChars = DefaultMaxTitleChars, int maxTextChars = DefaultMaxTextChars)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new ArchiveText(Cap(page.Title, maxTitleChars), Cap(page.Text, maxTextChars));
    }

    /// <summary>En fazla <paramref name="maxChars"/> karakter. Vekil çift (emoji gibi) ortadan bölünmez.</summary>
    public static string Cap(string value, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);
        if (value.Length <= maxChars)
        {
            return value;
        }

        var cut = maxChars;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut].TrimEnd();
    }

    private static Regex Element(string name, bool untilEndIfUnclosed)
    {
        var end = untilEndIfUnclosed ? $"(</{name}\\s*>|\\z)" : $"</{name}\\s*>";
        return new Regex($"<{name}(\\s[^>]*)?>.*?{end}", Options, MatchTimeout);
    }

    private static string Clean(string value, bool keepLines)
    {
        var decoded = WebUtility.HtmlDecode(value);
        var sb = new StringBuilder(decoded.Length);
        foreach (var c in decoded)
        {
            if (c == '\n' || c == '\r' || c == '\t')
            {
                sb.Append(keepLines && c == '\n' ? '\n' : ' ');
            }
            else if (!IsUnsafe(c))
            {
                sb.Append(c);
            }
        }

        var text = InlineSpace.Replace(sb.ToString(), " ");
        text = BlankLines.Replace(text, "\n");
        return text.Trim(' ', '\n');
    }

    /// <summary>Kontrol karakterleri, sıfır genişlikli ve yön değiştiren biçim karakterleri.</summary>
    private static bool IsUnsafe(char c)
    {
        var category = char.GetUnicodeCategory(c);
        return category is UnicodeCategory.Control or UnicodeCategory.Format
            || c is (char)0x2028 or (char)0x2029;
    }
}
