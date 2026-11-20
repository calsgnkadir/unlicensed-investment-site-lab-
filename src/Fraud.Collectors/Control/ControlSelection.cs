using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fraud.Domain.DomainNames;

namespace Fraud.Collectors.Control;

/// <summary>
/// Kontrol listesi seçiminin saf kuralları (KONTROL_GRUBU_YONTEMI.md 1.3, KONTROL_LISTESI_KURULUM.md 4). Ağ ve veritabanı yok.
/// </summary>
public static class ControlSeeds
{
    /// <summary>Sıralama anahtarı: <c>SHA-256(tohum + "|" + kayıtlı_alan_adı)</c>, onaltılık. Rastgele sayı üreteci yok.</summary>
    public static string Key(string seed, string registrableDomain)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(registrableDomain);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + registrableDomain)));
    }

    /// <summary>Adları tohumlu anahtara göre artan sıralar. Aynı girdi ve tohum her makinede aynı sırayı verir.</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> registrableDomains, string seed) =>
        [.. registrableDomains.Distinct(StringComparer.Ordinal).OrderBy(d => Key(seed, d), StringComparer.Ordinal)];

    /// <summary>
    /// Tohum, gruba ve haftaya (ya da kaynak liste kimliğine) göre SABİT kuralla üretilir; komut satırından verilemez.
    /// Böylece "beğenilmeyen sonuç için başka tohum dene" yolu kapalıdır.
    /// </summary>
    public static string For(string group, string key) => "kontrol-" + group.ToLowerInvariant() + "-" + key;
}

/// <summary>ISO 8601 haftası (Pazartesi başlar). Hafta tabakası <c>first_logged_at</c> sütununa göre, UTC.</summary>
public static class IsoWeek
{
    /// <summary>Örnek: 2026-11-18 → <c>2026-W47</c>.</summary>
    public static string Of(DateTimeOffset time)
    {
        var day = time.UtcDateTime;
        return string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(day)}-W{ISOWeek.GetWeekOfYear(day):D2}");
    }

    /// <summary>Haftanın [başlangıç, bitiş) aralığı, UTC.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Range(string week)
    {
        if (!TryParse(week, out var year, out var number))
        {
            throw new FormatException("Hafta 'yyyy-Www' biçiminde olmalı (ör. 2026-W47).");
        }

        var start = new DateTimeOffset(ISOWeek.ToDateTime(year, number, DayOfWeek.Monday), TimeSpan.Zero);
        return (start, start.AddDays(7));
    }

    public static bool TryParse(string? week, out int year, out int number)
    {
        year = number = 0;
        return week is { Length: 8 } && week[4] == '-' && week[5] == 'W'
               && int.TryParse(week[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year)
               && int.TryParse(week[6..], NumberStyles.None, CultureInfo.InvariantCulture, out number)
               && year >= 2026 && number >= 1 && number <= ISOWeek.GetWeeksInYear(year);
    }
}

/// <summary>
/// Kota yuvarlama: en büyük kalan yöntemi (Hamilton). Değişiklik kaydı 2026-11-20, madde 8.
/// </summary>
/// <remarks>
/// Elle örnek (G4b, 60 ve %23 / %51 / %26): ham 13,8 / 30,6 / 15,6 → taban 13 / 30 / 15 (toplam 58) → kalan 2 birim,
/// kalanları en büyük olanlara: 0,8 (birinci) ve 0,6'lardan sırada önce gelen (ikinci) → 14 / 31 / 15. Eşitlikte sıradaki
/// ilk sınıf kazanır; kural deterministiktir.
/// </remarks>
public static class QuotaRounding
{
    public static int[] LargestRemainder(int total, IReadOnlyList<double> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        var raw = shares.Select(s => total * s / shares.Sum()).ToArray();
        var result = raw.Select(r => (int)Math.Floor(r)).ToArray();
        var order = Enumerable.Range(0, raw.Length).OrderByDescending(i => raw[i] - result[i]).ThenBy(i => i).ToList();
        var remaining = total - result.Sum(); // döngüden ÖNCE bir kez hesaplanır
        for (var k = 0; k < remaining; k++)
        {
            result[order[k]]++;
        }

        return result;
    }
}

/// <summary>Yaş sınıfı: ilk sertifika ile CT'de ilk görülme arasındaki süre (≤ 28 gün, 29–365 gün, &gt; 365 gün).</summary>
public enum AgeClass
{
    New = 0,
    Middle = 1,
    Old = 2,
}

public static class AgeClasses
{
    /// <summary>Pozitiflerdeki dağılım (KONTROL_GRUBU_YONTEMI.md 4.4): %23 / %51 / %26.</summary>
    public static readonly IReadOnlyList<double> PositiveShares = [0.23, 0.51, 0.26];

    public static AgeClass Of(DateTimeOffset firstLoggedAt, DateTimeOffset firstCertificate)
    {
        var days = (firstLoggedAt - firstCertificate).TotalDays;
        return days <= 28 ? AgeClass.New : days <= 365 ? AgeClass.Middle : AgeClass.Old;
    }
}

/// <summary>
/// Kontrol üyelerinin haftalık DNS günü (Değişiklik kaydı 2026-11-20, madde 4): Üyeler iki ardışık güne KARIŞIK bölünür.
/// </summary>
/// <remarks>
/// Gün = <c>SHA-256("kontrol-dns-gun|" + kayıtlı_alan_adı)</c> ilk baytının tekliği (0 ya da 1). Gruba bakmaz, bu yüzden
/// hiçbir grup sistematik olarak başka güne düşmez. Aynı ad her hafta aynı güne düşer.
/// </remarks>
public static class ControlDnsSchedule
{
    public const string Salt = "kontrol-dns-gun";

    public static int Slot(string registrableDomain)
    {
        ArgumentNullException.ThrowIfNull(registrableDomain);
        return SHA256.HashData(Encoding.UTF8.GetBytes(Salt + "|" + registrableDomain))[0] & 1;
    }

    /// <summary>Bu üye bugün (Türkiye saatiyle) sorulmalı mı?</summary>
    public static bool IsDue(string registrableDomain, DateOnly today, IReadOnlyList<DayOfWeek> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count != 2)
        {
            throw new ArgumentException("İki gün verilmeli.", nameof(days));
        }

        return today.DayOfWeek == days[Slot(registrableDomain)];
    }
}

/// <summary>Eleme kodları (KONTROL_GRUBU_YONTEMI.md 1.2–1.6, kurulum belgesi 4). Her elenen sayılır.</summary>
public static class EliminationCodes
{
    public const string E00 = "E00_normallestirilemedi";
    public const string E01 = "E01_pozitifle_cakisiyor";
    public const string E02 = "E02_baska_grupta";
    public const string E06 = "E06_psl_ozel_bolum";
    public const string E10 = "E10_web_adresi_yok";
    public const string E20 = "E20_kaynak_hatasi";
    public const string Duplicate = "tekrar";
}

/// <summary>
/// Ortak süzgeç: ham adı kayıtlı alan adına indirger ve E00, E06, E01, E02 elemelerini uygular.
/// </summary>
/// <remarks>
/// E06: Ad bir kez tam Public Suffix List ile, bir kez yalnızca ICANN bölümüyle indirgenir. Sonuçlar farklıysa ad, özel
/// bölümdeki bir soneğin (ortak barındırma) altındadır ve kontrol grubuna girmez.
/// </remarks>
public sealed class ControlEligibility(
    IDomainNameNormalizer full, IDomainNameNormalizer icannOnly, IReadOnlySet<string> positives, IReadOnlySet<string> existingMembers)
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>Kod başına elenen sayısı.</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>Uygunsa kayıtlı alan adını, değilse <c>null</c> döner ve nedeni sayar.</summary>
    public string? Check(string raw)
    {
        var normalized = full.Normalize(raw).Value;
        if (normalized is null)
        {
            return Count(EliminationCodes.E00);
        }

        var name = normalized.RegistrableDomain;
        if (!_seen.Add(name))
        {
            return Count(EliminationCodes.Duplicate);
        }

        if (icannOnly.Normalize(name).Value?.RegistrableDomain != name)
        {
            return Count(EliminationCodes.E06);
        }

        if (positives.Contains(name))
        {
            return Count(EliminationCodes.E01);
        }

        return existingMembers.Contains(name) ? Count(EliminationCodes.E02) : name;
    }

    /// <summary>Kaynak hatası gibi dış nedenleri de aynı sayaçta toplar.</summary>
    public void CountExternal(string code) => _ = Count(code);

    private string? Count(string code)
    {
        _counts[code] = _counts.GetValueOrDefault(code) + 1;
        return null;
    }
}

/// <summary>Bir ilk-sertifika sorgusunun sonucu (seçim sırasında crt.sh'ten).</summary>
public readonly record struct FirstCertificateLookup(DateTimeOffset? FirstNotBefore, bool Failed);
