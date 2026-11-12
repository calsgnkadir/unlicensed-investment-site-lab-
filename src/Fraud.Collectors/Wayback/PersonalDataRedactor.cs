using System.Globalization;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.Wayback;

/// <summary>
/// Arşivden çıkarılan metindeki kişi verisini ve iletişim bağlantılarını yer tutucuyla değiştirir (Kural 5).
/// </summary>
/// <remarks>
/// Temizlik bilerek fazla yakalar: On ve daha uzun her rakam dizisi telefon sayılır. Yanlışlıkla silinen bir
/// sipariş numarası araştırmaya zarar vermez; kaçan bir telefon numarası kural ihlalidir.
/// Kısa sayılar (yıl, fiyat, yüzde) korunur, çünkü iddia ve içerik incelemesinde gereklidir.
/// Sıra önemlidir: Önce bağlantılar (içlerinde numara olur), sonra e-posta, IBAN (içinde 24 rakam var), en son telefon.
/// Bütün ifadeler <see cref="RegexOptions.NonBacktracking"/> ile çalışır; uzun metinde süre doğrusal kalır.
/// </remarks>
public static class PersonalDataRedactor
{
    public const string Email = "[E-POSTA]";
    public const string Phone = "[TELEFON]";
    public const string Iban = "[IBAN]";
    public const string MessagingLink = "[MESAJ_BAGLANTISI]";
    public const string DomainPlaceholder = "[ALAN_ADI]";
    public const string Link = "[BAGLANTI]";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);
    private const RegexOptions Common = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    // t.me/kanal, telegram.me, wa.me/905..., api.whatsapp.com/send?phone=..., chat.whatsapp.com/..., whatsapp://, tg://
    private static readonly Regex Messaging = new(
        "((https?://)?(www\\.)?\\b(t|telegram)\\.me(/[^\\s]*)?|(https?://)?(www\\.)?\\bwa\\.me(/[^\\s]*)?" +
        "|(https?://)?((api|chat|web|www)\\.)?\\bwhatsapp\\.com(/[^\\s]*)?|\\b(whatsapp|tg)://[^\\s]*)",
        Common | RegexOptions.IgnoreCase, MatchTimeout);

    private static readonly Regex EmailAddress = new(
        "[\\w.%+\\-]+@[\\w\\-]+(\\.[\\w\\-]+)*\\.[\\w]{2,}", Common, MatchTimeout);

    // TR + 2 kontrol rakamı + 22 rakam; gruplar arasında tek boşluk olabilir.
    private static readonly Regex TurkishIban = new(
        "\\bTR ?\\d ?\\d( ?\\d){22}\\b", Common | RegexOptions.IgnoreCase, MatchTimeout);

    // Genel IBAN: iki büyük harf, iki rakam, 11-30 harf veya rakam (tek boşlukla gruplanabilir).
    private static readonly Regex GenericIban = new(
        "\\b[A-Z]{2}\\d{2}( ?[A-Z0-9]){11,30}\\b", Common, MatchTimeout);

    // Türkiye cep telefonu: +90 / 0090 / 0 önekli veya öneksiz 5xx, ardından 7 rakam; ayraç olarak boşluk, nokta, tire, parantez.
    private static readonly Regex TurkishMobile = new(
        "((\\+|00)90[ .\\-]?|\\b0[ .\\-]?)?\\(?5\\d\\d\\)?[ .\\-]?\\d{3}[ .\\-]?\\d\\d[ .\\-]?\\d\\d\\b", Common, MatchTimeout);

    // Genel: boşluk, nokta, tire veya parantezle (rakamlar arası en fazla 2 ayraç karakteri) bölünmüş, toplam en az 10 rakam. Satır sonunu aşmaz.
    private static readonly Regex LongDigitRun = new(
        "(\\+|00)?\\(?\\d([ .\\-()]{0,2}\\d){9,}\\)?", Common, MatchTimeout);

    private static readonly Regex AnyUrl = new(
        "(https?|ftp)://[^\\s]+|\\bwww\\.[^\\s]+", Common | RegexOptions.IgnoreCase, MatchTimeout);

    /// <summary>E-posta, telefon, IBAN ve mesajlaşma bağlantılarını değiştirir.</summary>
    /// <exception cref="RegexMatchTimeoutException">Metin süre sınırında işlenemedi.</exception>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = Messaging.Replace(text, MessagingLink);
        result = EmailAddress.Replace(result, Email);
        result = TurkishIban.Replace(result, Iban);
        result = GenericIban.Replace(result, Iban);
        result = TurkishMobile.Replace(result, Phone);
        result = LongDigitRun.Replace(result, Phone);
        return result;
    }

    /// <summary>
    /// Örnek dosyasına alan adı ve adres girmesin diye: Bütün <c>http(s)://</c> ve <c>www.</c> adresleri
    /// <see cref="Link"/>, örneğin kendi alan adı (alt alan adlarıyla birlikte) <see cref="DomainPlaceholder"/> olur.
    /// Başka kurumların adresi olmayan adları (ör. bir düzenleyicinin adı) metinde kalır.
    /// </summary>
    public static string RedactLinksAndDomain(string? text, string registrableDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrableDomain);
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = AnyUrl.Replace(text, Link);
        foreach (var form in DomainForms(registrableDomain))
        {
            var pattern = new Regex(
                "([\\w\\-]+\\.)*" + Regex.Escape(form) + "\\b",
                Common | RegexOptions.IgnoreCase, MatchTimeout);
            result = pattern.Replace(result, DomainPlaceholder);
        }

        return result;
    }

    /// <summary>Alan adının ASCII (punycode) ve Unicode yazımı; ikisi aynıysa tek.</summary>
    private static IEnumerable<string> DomainForms(string registrableDomain)
    {
        var domain = registrableDomain.Trim().TrimEnd('.').ToLowerInvariant();
        yield return domain;

        string unicode;
        try
        {
            unicode = new IdnMapping().GetUnicode(domain).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            yield break;
        }

        if (!string.Equals(unicode, domain, StringComparison.Ordinal))
        {
            yield return unicode;
        }
    }
}
