using System.Text.Json;
using System.Text.Json.Serialization;
using Fraud.Evaluation.Model;

namespace Fraud.Evaluation;

/// <summary>Test kipleri (ROADMAP Hafta 6, "Üç ayrı sınav").</summary>
public enum EvaluationMode
{
    /// <summary>Geçmiş veri testi: 2026/20–2026/77 bültenleri. Bir kez çalışır, kirlenme riski raporda yazılır.</summary>
    Historical = 1,

    /// <summary>Kilitli test: F ile F+28 gün arası. Yalnızca BİR kez çalışır (<see cref="Running.LockedRunGuard"/>).</summary>
    Locked = 2,

    /// <summary>İleriye dönük test: Alarm, karardan önce tarihli taahhütten önce verilmiş olmalı.</summary>
    Prospective = 3,
}

/// <summary>Kontrolün puanlanacağı anın nasıl seçileceği.</summary>
public enum ControlReferencePolicy
{
    /// <summary>
    /// Varsayılan. Referans = ilk sertifika + ayar pozitiflerinin (ilk sertifika → karar) gecikmelerinden tohumlu
    /// çekilmiş bir gecikme. Pozitif ve kontrol ilk sertifikadan aynı dağılımda uzaklıkta puanlanır.
    /// </summary>
    MatchedLag = 1,

    /// <summary>
    /// Referans = ilk sertifika anı. YALNIZCA sızıntıyı göstermek için: "sertifika yaşı" gibi sinyaller bu düzende
    /// etiketi tanım gereği bulur (pozitif karar gününde, kontrol ilk gününde puanlanır).
    /// </summary>
    FirstCertificate = 2,
}

/// <summary>[From, To] bülten aralığı (iki uç dahil).</summary>
public sealed class BulletinRange
{
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public bool Contains(BulletinNumber bulletin) => bulletin.IsBetween(BulletinNumber.Parse(From), BulletinNumber.Parse(To));
}

/// <summary>Sinyal seçimi. Hafta 6'da yalnızca sahte sinyal var.</summary>
public sealed class SignalConfig
{
    public string Name { get; set; } = "random";

    public string Seed { get; set; } = "random-2026-11-15";
}

/// <summary>
/// Ölçüm yapılandırması. Dosyanın SHA-256'sı kilit kaydına yazılır; kilitli testten sonra değişirse raporda görünür.
/// </summary>
/// <remarks>
/// Varsayılanlar Kadircan'ın onayına sunulan değerlerdir. Bölme aralıkları onaylıdır (KONTROL_GRUBU_YONTEMI.md 10.1,
/// karar 1 ve 2). Eşik, bootstrap sayısı, taban oran, kontrol referans kuralı ve arşiv payı onay bekler.
/// </remarks>
public sealed class EvaluationConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // Sayı olarak yazılmış enum ("7") kabul edilmez; yalnızca tanımlı adlar.
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        // Bilinmeyen alan reddedilir (E7): Yanlış yazılmış bir ayar sessizce varsayılana düşmesin.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Yapılandırma dosyasının en büyük boyutu (E7). Gerçek dosya 2 KB'tan küçük.</summary>
    public const int MaxFileBytes = 64 * 1024;

    /// <summary>Üst sınırlar (E7): Kilitli testi "kullanan" ama hiç bitmeyen bir çalıştırma olmasın.</summary>
    public const int MaxBootstrapIterations = 20_000;

    public const int MaxLockedWindowDays = 365;

    /// <summary>Bölme ve gecikme çekimi için tohum. Seçimden ÖNCE yazılır.</summary>
    public string Seed { get; set; } = "eval-2026-11-15";

    /// <summary>Karar 1: 2026/1–2026/18 ayar verisi.</summary>
    public BulletinRange TuningBulletins { get; set; } = new() { From = "2026/1", To = "2026/18" };

    /// <summary>Karar 1: 2026/20–2026/77 geçmiş veri testi.</summary>
    public BulletinRange HistoricalBulletins { get; set; } = new() { From = "2026/20", To = "2026/77" };

    /// <summary>Dondurma tarihi F (Hafta 8 commit'i). Kilitli test için zorunlu.</summary>
    public DateTimeOffset? FreezeDate { get; set; }

    /// <summary>Karar 2: Kilitli test penceresi F'den F + 28 güne kadar.</summary>
    public int LockedWindowDays { get; set; } = 28;

    /// <summary>Zamanla eşleşmeyen kontrollerde (G1, G2, G4a) ayar payı (7.3 madde 5: %70 / %30).</summary>
    public double ControlTuningShare { get; set; } = 0.70;

    public ControlReferencePolicy ControlReference { get; set; } = ControlReferencePolicy.MatchedLag;

    /// <summary>Eşik, ayar kontrollerinde bu yanlış alarm oranına göre seçilir. Varsayılan %1 (onay bekliyor).</summary>
    public double TargetFalseAlarmRate { get; set; } = 0.01;

    /// <summary>Eşiğin seçildiği kontrol grupları. Varsayılan G3 + G4 (onay bekliyor). G1 kolay negatiftir, girmez.</summary>
    public IReadOnlyList<SubjectGroup> CalibrationGroups { get; set; } = [SubjectGroup.ControlHardNegative, SubjectGroup.ControlRandom];

    /// <summary>Bootstrap tekrar sayısı. Varsayılan 2000 (onay bekliyor).</summary>
    public int BootstrapIterations { get; set; } = 2000;

    /// <summary>
    /// Taban oran π (yaygınlık). Varsayılan 0,001. ÖLÇÜLMEDİ, VARSAYIMDIR (KONTROL_GRUBU_YONTEMI.md 8.3 örneği).
    /// Gerçek değer ct_candidates ve tarafsız örneklemden ölçülünce değişir.
    /// </summary>
    public double BaseRate { get; set; } = 0.001;

    /// <summary>Günde ilk kez görülen yeni kayıtlı alan adı sayısı (N_tüm). Ölçülmediyse boş; günlük alarm yazılmaz.</summary>
    public double? DailyNewDomains { get; set; }

    /// <summary>Bundan az birimde oran ve aralık verilmez, yalnızca ham sayı (ROADMAP Hafta 6).</summary>
    public int MinCountForRates { get; set; } = 10;

    /// <summary>Wayback kaydı için dizine girme payı (gün). Varsayılan 7 (onay bekliyor).</summary>
    public double ArchiveKnownDelayDays { get; set; } = 7;

    public SignalConfig Signal { get; set; } = new();

    /// <summary>
    /// Bir grubun "veri var mı" oranı pozitiflerinkinden bu kadar farklıysa rapor uyarı verir. "Veri var mı" bilgisi tek
    /// başına etiketi ele verebilir (kontroller için toplayıcılar henüz çalışmıyor).
    /// </summary>
    public double CoverageWarningGap { get; set; } = 0.20;

    /// <summary>
    /// İleriye dönük test: Zaman damgası kuruluşunun (TSA) kök sertifikası, depo köküne göre ya da tam yol (DER veya PEM).
    /// .tsr cevabının imzalayanı bu köke zincirlenmeli (E10). İşletim sisteminin güven deposu kullanılmaz.
    /// </summary>
    public string? TimestampAuthorityRootFile { get; set; }

    /// <summary>
    /// Kilitli test kaydının depo köküne göre yolu. SABİTTİR, yapılandırmadan okunmaz (E1): Başka bir yol yazan
    /// yapılandırma kilitli testi yeni bir dosyada "ilk kez" çalıştıramaz. JSON'da bu alan yazılsa da yok sayılır.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Örnek üyesi bilerek: JSON'daki 'lockFile' alanı bu salt okunur özelliğe eşlenir ve yok sayılır (E1).")]
    public string LockFile => "data/private/evaluation/locked-run.json";

    public TimeSpan LockedWindow => TimeSpan.FromDays(LockedWindowDays);

    public TimeSpan ArchiveKnownDelay => TimeSpan.FromDays(ArchiveKnownDelayDays);

    public static EvaluationConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
        {
            throw new InvalidOperationException("Yapılandırma dosyası " + MaxFileBytes + " bayttan büyük olamaz.");
        }

        var config = JsonSerializer.Deserialize<EvaluationConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException("Yapılandırma boş.");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        _ = BulletinNumber.Parse(TuningBulletins.From);
        _ = BulletinNumber.Parse(HistoricalBulletins.To);
        if (BulletinNumber.Parse(TuningBulletins.To) >= BulletinNumber.Parse(HistoricalBulletins.From))
        {
            throw new InvalidOperationException("Ayar bültenleri test bültenlerinden önce bitmeli (zamana göre bölme).");
        }

        if (!(TargetFalseAlarmRate > 0 && TargetFalseAlarmRate < 1) || !(BaseRate > 0 && BaseRate < 1)
            || !(ControlTuningShare > 0 && ControlTuningShare < 1))
        {
            throw new InvalidOperationException("Oranlar 0 ile 1 arasında olmalı.");
        }

        if (BootstrapIterations is < 100 or > MaxBootstrapIterations
            || LockedWindowDays is < 1 or > MaxLockedWindowDays
            || MinCountForRates is < 1 or > 1000
            || ArchiveKnownDelayDays is < 0 or > 365
            || !(CoverageWarningGap > 0 && CoverageWarningGap < 1)
            || DailyNewDomains is < 0
            || string.IsNullOrWhiteSpace(Seed) || Seed.Length > 200
            || CalibrationGroups.Count == 0 || CalibrationGroups.Contains(SubjectGroup.Positive)
            || CalibrationGroups.Any(g => !Enum.IsDefined(g))
            || !Enum.IsDefined(ControlReference))
        {
            throw new InvalidOperationException("Yapılandırma değerleri geçersiz ya da sınır dışında.");
        }
    }

    /// <summary>
    /// Kipe özel denetim. Kilitli testte kilit ALINMADAN önce çağrılır (mentör maddesi 5): Yapılandırma hatası tek
    /// çalıştırma hakkını harcamasın.
    /// </summary>
    public void ValidateFor(EvaluationMode mode)
    {
        Validate();
        if (mode == EvaluationMode.Locked && FreezeDate is null)
        {
            throw new InvalidOperationException("Kilitli test için freezeDate (F) gerekli.");
        }

        if (mode == EvaluationMode.Prospective && string.IsNullOrWhiteSpace(TimestampAuthorityRootFile))
        {
            throw new InvalidOperationException("İleriye dönük test için timestampAuthorityRootFile (TSA kök sertifikası) gerekli.");
        }

        if (Signal.Name != "random")
        {
            // Hafta 6'da yalnızca sahte sinyal var. Bilinmeyen sinyal adı kilidi harcamadan reddedilir.
            throw new InvalidOperationException("Bilinmeyen sinyal.");
        }
    }
}
