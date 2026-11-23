using Fraud.Domain.DomainNames;

namespace Fraud.Domain.Control;

/// <summary>Kontrol grubu (KONTROL_GRUBU_YONTEMI.md Bölüm 2–5). Öncelik sırası G1 &gt; G2 &gt; G3 &gt; G4.</summary>
public enum ControlGroup
{
    /// <summary>G1: Resmi kaynakta web adresi yazılı lisanslı kurumlar (bugün yalnızca BDDK bankaları).</summary>
    G1 = 1,

    /// <summary>G2: Wikidata'dan konusu iktisat ya da finans olan haber kuruluşları (Değişiklik kaydı 2026-11-21; Radar yolu kullanılmıyor).</summary>
    G2 = 2,

    /// <summary>G3: Yeni ve geniş finans sözlüğüyle eşleşen zor negatifler (haftalık, çerçeve K ve N).</summary>
    G3 = 3,

    /// <summary>G4a: Tranco'dan tohumlu 300 köklü site.</summary>
    G4a = 4,

    /// <summary>G4b: Tarafsız CT örnekleminden yaş sınıfı kotalı rastgele (haftalık, çerçeve N).</summary>
    G4b = 5,

    /// <summary>SPK "faaliyette bulunanlar" kripto listesi. Yetki listesi DEĞİLDİR; G1'e girmez (karar 5).</summary>
    CryptoActive = 6,
}

/// <summary>Örnekleme çerçevesi: K = kelime filtreli akış (ct_candidates), N = tarafsız örneklem (ct_neutral_sample).</summary>
public enum SamplingFrame
{
    K = 1,
    N = 2,
}

/// <summary>
/// Bir kontrol grubu üyesi (tablo: control_members). Alan adı başına tek satır: Bir alan adı yalnızca bir gruptadır.
/// </summary>
/// <remarks>
/// <para>
/// Kural 4: Bu tablo SPK veya USOM listesinde OLMAYAN adlara bağlıdır. Hiçbir log, metrik, rapor veya API ucu bu adları
/// basmaz; dışarıya yalnızca grup başına sayı çıkar.
/// </para>
/// <para>
/// Tarihler ayrıdır: <see cref="AssignedAt"/> bu sistemin üyeyi seçtiği an; <see cref="FirstLoggedAt"/> ve
/// <see cref="FirstCertificateAt"/> kaynağın (CT logu, crt.sh) tarihleridir.
/// </para>
/// </remarks>
public sealed class ControlMember
{
    // EF Core için.
    private ControlMember()
    {
        Seed = string.Empty;
    }

    public ControlMember(
        DomainName domain,
        ControlGroup group,
        SamplingFrame? frame,
        DateTimeOffset assignedAt,
        string? week,
        string seed,
        string? sourceListId,
        string? commitmentId,
        DateTimeOffset? firstLoggedAt = null,
        DateTimeOffset? firstCertificateAt = null,
        string? dictionarySubset = null,
        bool? turkeyLinked = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        if (frame is not null && group is not (ControlGroup.G3 or ControlGroup.G4b))
        {
            throw new ArgumentException("Çerçeve yalnızca G3 ve G4b için anlamlı.", nameof(frame));
        }

        if (turkeyLinked is not null && group != ControlGroup.G2)
        {
            throw new ArgumentException("Türkiye etiketi yalnızca G2 için anlamlı.", nameof(turkeyLinked));
        }

        Domain = domain;
        Group = group;
        Frame = frame;
        AssignedAt = assignedAt;
        Week = week;
        Seed = seed;
        SourceListId = sourceListId;
        CommitmentId = commitmentId;
        FirstLoggedAt = firstLoggedAt;
        FirstCertificateAt = firstCertificateAt;
        DictionarySubset = dictionarySubset;
        TurkeyLinked = turkeyLinked;
    }

    public long Id { get; private set; }

    public long DomainId { get; private set; }

    public DomainName Domain { get; private set; } = null!;

    public ControlGroup Group { get; private set; }

    public SamplingFrame? Frame { get; private set; }

    /// <summary>Bu sistemin üyeyi seçtiği an.</summary>
    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>ISO hafta (ör. <c>2026-W47</c>), <c>first_logged_at</c> sütununa göre. Tek seferlik gruplarda boş.</summary>
    public string? Week { get; private set; }

    /// <summary>Seçim tohumu. Gruba ve haftaya (ya da liste kimliğine) göre sabit kuralla üretilir.</summary>
    public string Seed { get; private set; }

    /// <summary>Kaynak liste kimliği (ör. Tranco liste kimliği, BDDK sayfasının SHA-256 öneki).</summary>
    public string? SourceListId { get; private set; }

    /// <summary>Üyeyi kayda geçiren taahhüdün adı (data/private/commitments ve docs/evaluation/commitments).</summary>
    public string? CommitmentId { get; private set; }

    /// <summary>CT akışında ilk görülen kaydın log zamanı (G3, G4b).</summary>
    public DateTimeOffset? FirstLoggedAt { get; private set; }

    /// <summary>Seçim sırasında crt.sh'ten okunan ilk sertifikanın başlangıcı (G3, G4b). Yaş sınıfı buna dayanır.</summary>
    public DateTimeOffset? FirstCertificateAt { get; private set; }

    /// <summary>G3: Eşleşen terim baseline alt kümesinde mi ("baseline") yoksa yalnızca geniş sözlükte mi ("wide").</summary>
    public string? DictionarySubset { get; private set; }

    /// <summary>
    /// G2: Wikidata kaydında ülke (P17) Türkiye mi. SÜZGEÇ DEĞİL, ETİKET: Sonuç hem bütün G2 için hem bu alt küme için ayrı
    /// raporlanır (Değişiklik kaydı 2026-11-21). Diğer gruplarda <c>null</c>.
    /// </summary>
    public bool? TurkeyLinked { get; private set; }
}

/// <summary>
/// Bir liste kurma işleminin kaydı (tablo: control_builds). (grup, hafta anahtarı) tekildir: Aynı hafta ya da aynı tek
/// seferlik grup ikinci kez kurulamaz. Yalnızca sayılar tutulur.
/// </summary>
public sealed class ControlBuild
{
    /// <summary>Tek seferlik grupların hafta anahtarı.</summary>
    public const string OneShot = "-";

    // EF Core için.
    private ControlBuild()
    {
        WeekKey = string.Empty;
        Seed = string.Empty;
        Counts = string.Empty;
    }

    public ControlBuild(
        ControlGroup group, string weekKey, DateTimeOffset builtAt, string seed, string? sourceListId, string? commitmentId,
        int selected, string counts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(weekKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNegative(selected);
        Group = group;
        WeekKey = weekKey;
        BuiltAt = builtAt;
        Seed = seed;
        SourceListId = sourceListId;
        CommitmentId = commitmentId;
        Selected = selected;
        Counts = counts;
    }

    public long Id { get; private set; }

    public ControlGroup Group { get; private set; }

    /// <summary>ISO hafta (G3, G4b) ya da <see cref="OneShot"/>.</summary>
    public string WeekKey { get; private set; }

    public DateTimeOffset BuiltAt { get; private set; }

    public string Seed { get; private set; }

    public string? SourceListId { get; private set; }

    public string? CommitmentId { get; private set; }

    public int Selected { get; private set; }

    /// <summary>Eleme kodlarının sayıları (JSON). Alan adı yok.</summary>
    public string Counts { get; private set; }
}
