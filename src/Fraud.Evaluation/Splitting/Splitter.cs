using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Splitting;

/// <summary>Bir birimin düştüğü bölme. Sayı sırası "erken"den "geç"e: Çakışmada küçük olan kazanır.</summary>
public enum SplitPart
{
    /// <summary>Ayar verisi: Eşik ve sinyal seçimi yalnızca burada yapılır.</summary>
    Tuning = 1,

    /// <summary>Geçmiş veri testi.</summary>
    Historical = 2,

    /// <summary>Kilitli test (F, F + 28 gün).</summary>
    Locked = 3,

    /// <summary>Hiçbir bölmeye girmez (ör. aralık dışındaki bülten). Sayılır, kullanılmaz.</summary>
    Excluded = 9,
}

/// <summary>Bölme sonucu.</summary>
/// <param name="Parts">Birim kimliği → bölme.</param>
/// <param name="MovedForIntegrity">Aynı bülten ya da küme iki tarafa düştüğü için en erken bölmeye taşınan birim sayısı.</param>
/// <param name="TiedToTuning">Ayardaki bir birimle aynı bileşende (bülten ya da küme) olan birimler, dışarıda kalanlar
/// dahil. İleriye dönük teste giremezler (mentör maddesi 2).</param>
/// <param name="PositivesWithoutCertificate">İlk sertifikası bilinmediği için dışarıda kalan pozitif (mentör maddesi 8).</param>
/// <param name="ControlsAfterFreeze">Referansı F'den sonra olduğu için ayardan ve testten çıkarılan kontrol (mentör maddesi 3).</param>
public sealed record SplitResult(
    IReadOnlyDictionary<string, SplitPart> Parts,
    int MovedForIntegrity,
    IReadOnlySet<string> TiedToTuning,
    int PositivesWithoutCertificate,
    int ControlsAfterFreeze)
{
    public SplitPart PartOf(string subjectId) => Parts[subjectId];

    public int Count(SplitPart part) => Parts.Values.Count(p => p == part);
}

/// <summary>
/// Onaylı bölme kuralları (KONTROL_GRUBU_YONTEMI.md Bölüm 7, karar 1 ve 2).
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Pozitif: Bülten 2026/1–18 → ayar, 2026/20–77 → geçmiş test, karar tarihi [F, F+28] → kilitli. Diğerleri dışarıda.</item>
/// <item>Çerçeveli kontrol (K/N): Referansı [F, F+28] içindeyse kilitli; F'den sonraysa dışarıda. Değilse ilk
/// sertifika ayı M'deki pozitiflerin ayar : test oranıyla, küme özetine göre ayar ya da teste gider. M'de pozitif
/// yoksa "eşleşmesiz" sayılır ve yalnızca ayara (yanlış alarm ayarı) girer.</item>
/// <item>Zamanla eşleşmeyen kontrol (G1, G2, G4a, kripto listesi): Küme özetiyle %70 ayar, %30 test; kilitli teste girmez.</item>
/// <item>Bütünlük: Aynı bülteni ya da aynı kümeyi paylaşan birimler tek bileşendir. Bileşen birden fazla bölmeye
/// düştüyse bütünü EN ERKEN bölmeye (ayar) taşınır. Böylece bir bülten veya operatör iki tarafta birden görünmez.
/// Taşınanlar sayılır; değerlendirme havuzu küçülse de yeniden bölünmez.</item>
/// <item>Dışarıda kalan birimler de bileşene girer (mentör maddesi 2): Ayardaki bir birimle bağlı olan hiçbir birim,
/// dışarıda kalmış olsa bile, ileriye dönük teste giremez (<see cref="SplitResult.TiedToTuning"/>).</item>
/// <item>Sertifika kuralı iki tarafta aynı (mentör maddesi 8): İlk sertifikası bilinmeyen kontrol referans alamaz,
/// ilk sertifikası bilinmeyen pozitif de dışarıda kalır. "Sertifika var mı" bilgisi grubu ayıramasın.</item>
/// <item>F sınırı (mentör maddesi 3): F ayarlıysa referansı F'den sonra olan kontrol yalnızca kilitli pencereye
/// girebilir; değilse dışarıda kalır. Ayar ve geçmiş test hiçbir zaman F sonrasını görmez.</item>
/// </list>
/// <para>CDN kenarları kümelemede zaten yoktur (<see cref="ClusterBuilder"/>); burada küme kimliği hazır gelir.</para>
/// </remarks>
public static class Splitter
{
    public static SplitResult Split(IReadOnlyList<Subject> subjects, EvaluationConfig config)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(config);

        // 1) Pozitifler bülten ve karar tarihine göre.
        var parts = new Dictionary<string, SplitPart>(StringComparer.Ordinal);
        var withoutCertificate = 0;
        foreach (var s in subjects.Where(s => s.IsPositive))
        {
            if (s.FirstCertificateAt is null)
            {
                withoutCertificate++;
                parts[s.Id] = SplitPart.Excluded;
                continue;
            }

            parts[s.Id] = PositivePart(s, config);
        }

        // 2) Ay başına pozitif sayıları (ilk sertifika ayına göre): kontrol oranı bundan çıkar.
        var tuningByMonth = CountByMonth(subjects, parts, SplitPart.Tuning);
        var testByMonth = CountByMonth(subjects, parts, SplitPart.Historical);

        // 3) Kontroller.
        var afterFreeze = 0;
        foreach (var s in subjects.Where(s => !s.IsPositive))
        {
            parts[s.Id] = ControlPart(s, config, tuningByMonth, testByMonth);
            if (parts[s.Id] == SplitPart.Excluded && config.FreezeDate is { } freeze && s.ReferenceTime >= freeze)
            {
                afterFreeze++;
            }
        }

        // 4) Bütünlük: bülten ya da küme paylaşanlar aynı bölmede.
        var (moved, tied) = EnforceIntegrity(subjects, parts);
        return new SplitResult(parts, moved, tied, withoutCertificate, afterFreeze);
    }

    private static SplitPart PositivePart(Subject s, EvaluationConfig config)
    {
        var bulletin = s.Bulletin!.Value;
        if (config.TuningBulletins.Contains(bulletin))
        {
            return SplitPart.Tuning;
        }

        if (config.HistoricalBulletins.Contains(bulletin))
        {
            return SplitPart.Historical;
        }

        return InLockedWindow(s.ReferenceTime, config) ? SplitPart.Locked : SplitPart.Excluded;
    }

    private static SplitPart ControlPart(
        Subject s, EvaluationConfig config, Dictionary<string, int> tuningByMonth, Dictionary<string, int> testByMonth)
    {
        if (s.ReferenceTime is not { } reference)
        {
            return SplitPart.Excluded;
        }

        // Küme özeti: Aynı kümedeki kontroller aynı sayıyı alır, dolayısıyla aynı tarafa düşer.
        var u = SeededHash.Unit(config.Seed, "split|" + s.ClusterId);
        if (config.FreezeDate is { } freeze && reference >= freeze)
        {
            // F sonrası: yalnızca çerçeveli kontrol ve yalnızca kilitli pencere. Diğerleri hiçbir bölmeye girmez.
            return s.IsTimeMatchedControl && InLockedWindow(reference, config) ? SplitPart.Locked : SplitPart.Excluded;
        }

        if (!s.IsTimeMatchedControl)
        {
            return u < config.ControlTuningShare ? SplitPart.Tuning : SplitPart.Historical;
        }

        if (s.FirstCertificateAt is not { } first)
        {
            return SplitPart.Excluded;
        }

        var month = MonthKey(first);
        var nTuning = tuningByMonth.GetValueOrDefault(month);
        var nTest = testByMonth.GetValueOrDefault(month);
        if (nTuning + nTest == 0)
        {
            // Eşleşmesiz ay: yalnızca yanlış alarm ayarı için.
            return SplitPart.Tuning;
        }

        return u < (double)nTuning / (nTuning + nTest) ? SplitPart.Tuning : SplitPart.Historical;
    }

    private static bool InLockedWindow(DateTimeOffset? time, EvaluationConfig config) =>
        config.FreezeDate is { } freeze && time is { } t && t >= freeze && t <= freeze + config.LockedWindow;

    private static Dictionary<string, int> CountByMonth(IReadOnlyList<Subject> subjects, Dictionary<string, SplitPart> parts, SplitPart part) =>
        subjects
            .Where(s => s.IsPositive && s.FirstCertificateAt is not null && parts[s.Id] == part)
            .GroupBy(s => MonthKey(s.FirstCertificateAt!.Value))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private static string MonthKey(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private static (int Moved, IReadOnlySet<string> TiedToTuning) EnforceIntegrity(IReadOnlyList<Subject> subjects, Dictionary<string, SplitPart> parts)
    {
        // Her birimi kendi kümesine, her pozitifi kendi bültenine bağla. "k:" ve "b:" önekleri iki ad alanını ayırır.
        var set = new DisjointSet();
        foreach (var s in subjects)
        {
            set.Union("u:" + s.Id, "k:" + s.ClusterId);
            if (s.Bulletin is { } bulletin)
            {
                set.Union("u:" + s.Id, "b:" + bulletin);
            }
        }

        var moved = 0;
        var tied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in subjects.GroupBy(s => set.Find("u:" + s.Id)))
        {
            var members = component.ToList();
            if (members.Any(s => parts[s.Id] == SplitPart.Tuning))
            {
                // Dışarıda kalanlar dahil bütün bileşen "ayara bağlı": ileriye dönük teste giremez.
                tied.UnionWith(members.Select(s => s.Id));
            }

            var used = members.Where(s => parts[s.Id] != SplitPart.Excluded).ToList();
            if (used.Count == 0)
            {
                continue;
            }

            var earliest = used.Min(s => parts[s.Id]);
            foreach (var s in used.Where(s => parts[s.Id] != earliest))
            {
                parts[s.Id] = earliest;
                moved++;
            }
        }

        return (moved, tied);
    }
}
