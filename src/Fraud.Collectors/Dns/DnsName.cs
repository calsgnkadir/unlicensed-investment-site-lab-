using System.Globalization;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Sorguya girecek alan adını hazırlar ve paketteki uzunluğunu hesaplar.
/// </summary>
/// <remarks>
/// DNS paketinde ad, noktalarla değil "uzunluk + etiket" dizisiyle yazılır (RFC 1035 Bölüm 3.1):
/// <code>
/// ornek.example  =>  05 'o' 'r' 'n' 'e' 'k'  07 'e' 'x' 'a' 'm' 'p' 'l' 'e'  00
/// </code>
/// Sondaki 00, kök (root) etikettir ve adın bittiğini söyler.
/// </remarks>
public static class DnsName
{
    // UseStd3AsciiRules: Etikette yalnızca harf, rakam ve tire kabul edilir (RFC 1123 Bölüm 2.1 "LDH" kuralı).
    // AllowUnassigned = false: Unicode'da henüz atanmamış karakterler reddedilir (IDNA2003 "stored string" kuralı).
    private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = true };

    /// <summary>
    /// Adı sorguda kullanılacak biçime çevirir: ASCII (Punycode), küçük harf, sondaki nokta yok.
    /// Kurala uymayan adda <see cref="ArgumentException"/> fırlatır; geçersiz bir ad hiçbir zaman pakete girmez.
    /// </summary>
    /// <example><c>Bücher.Example.</c> =&gt; <c>xn--bcher-kva.example</c></example>
    public static string ToQueryName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // "ornek.example." ile "ornek.example" aynı addır; sondaki nokta kökü gösterir (RFC 1034 Bölüm 3.1).
        var trimmed = name.EndsWith('.') ? name[..^1] : name;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Ad boş olamaz.", nameof(name));
        }

        string ascii;
        try
        {
            // Türkçe karakterli ad (ör. "örnek") DNS'te "xn--rnek-zoa" gibi Punycode ile yazılır (RFC 3492).
            ascii = Idn.GetAscii(trimmed);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("Ad IDNA kurallarına uymuyor.", nameof(name), ex);
        }

        // DNS büyük-küçük harf ayırmaz (RFC 4343). Karşılaştırma ve kayıt kolay olsun diye hep küçük harf.
        ascii = ascii.ToLowerInvariant();

        foreach (var label in ascii.Split('.'))
        {
            // Boş etiket "a..b" gibi bir yazımdan gelir; 63'ten uzun etiket uzunluk baytına sığmaz (RFC 1035 Bölüm 2.3.4).
            if (label.Length is 0 or > DnsWire.MaxLabelLength)
            {
                throw new ArgumentException("Etiket 1-63 karakter olmalı.", nameof(name));
            }

            if (!label.All(IsLdh) || label.StartsWith('-') || label.EndsWith('-'))
            {
                throw new ArgumentException("Etiket yalnızca harf, rakam ve tire içerebilir; tireyle başlayıp bitemez.", nameof(name));
            }
        }

        if (WireLength(ascii) > DnsWire.MaxNameWireLength)
        {
            throw new ArgumentException("Ad paketteki haliyle 255 baytı aşıyor.", nameof(name));
        }

        return ascii;
    }

    /// <summary>
    /// ASCII adın paketteki uzunluğu. Her nokta bir uzunluk baytına dönüşür; başa bir uzunluk baytı,
    /// sona kök için bir 00 eklenir. Bu yüzden sonuç "karakter sayısı + 2"dir.
    /// </summary>
    /// <example><c>a.bc</c> (4 karakter) =&gt; <c>01 'a' 02 'b' 'c' 00</c> (6 bayt).</example>
    public static int WireLength(string asciiName)
    {
        ArgumentNullException.ThrowIfNull(asciiName);
        return asciiName.Length + 2;
    }

    /// <summary>Adı paket biçiminde <paramref name="destination"/> içine yazar ve yazılan bayt sayısını döner.</summary>
    public static int Write(string asciiName, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(asciiName);
        var position = 0;
        foreach (var label in asciiName.Split('.'))
        {
            destination[position++] = (byte)label.Length;
            foreach (var c in label)
            {
                destination[position++] = (byte)c; // ToQueryName yalnızca ASCII bıraktığı için tek bayt.
            }
        }

        destination[position++] = 0; // kök etiket: ad bitti
        return position;
    }

    private static bool IsLdh(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
}
