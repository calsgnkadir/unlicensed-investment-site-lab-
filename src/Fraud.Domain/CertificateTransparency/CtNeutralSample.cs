namespace Fraud.Domain.CertificateTransparency;

/// <summary>
/// Tarafsız CT örnekleminde bir kayıtlı alan adı (tablo: ct_neutral_sample). Alan adı başına tek satır.
/// </summary>
/// <remarks>
/// <para>
/// Neden var: <see cref="CtCandidate"/> tablosu yalnızca kelime filtresine takılan kayıtları tutar. Oradan seçilen
/// kontrol grubu, aynı kelimeleri kullanan baseline için "tanım gereği" yanlış alarm olur (döngüsellik,
/// docs/research/KONTROL_GRUBU_YONTEMI.md Bölüm 4.3). Bu tablo kelimeye hiç bakmadan, alan adının tuzlu özetiyle
/// seçilen yaklaşık %1'lik bir örneği tutar (Çerçeve N).
/// </para>
/// <para>
/// Kural 4: Bu tablo SPK veya USOM listesinde OLMAYAN adlar içerir. Hiçbir log, metrik, API ucu veya rapor bu adları
/// basmaz; dışarıya yalnızca sayılar çıkar.
/// </para>
/// <para>
/// Tarihler ayrıdır: <see cref="FirstLoggedAt"/> ve <see cref="FirstNotBefore"/> kaynağın (log ve sertifika)
/// tarihleridir; <see cref="FirstSeenAt"/> bu sistemin kaydı ilk okuduğu andır. "O tarihte bilinen" sorusu kaynak
/// tarihiyle, "ne zaman topladık" sorusu gözlem tarihiyle cevaplanır.
/// </para>
/// </remarks>
public sealed class CtNeutralSample
{
    // EF Core için.
    private CtNeutralSample()
    {
        RegistrableDomain = string.Empty;
        FirstLogName = string.Empty;
        LastLogName = string.Empty;
    }

    public CtNeutralSample(
        string registrableDomain,
        string logName,
        long entryIndex,
        DateTimeOffset loggedAt,
        DateTimeOffset? notBefore,
        DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrableDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);

        RegistrableDomain = registrableDomain;
        FirstSeenAt = observedAt;
        FirstLoggedAt = loggedAt;
        FirstNotBefore = notBefore;
        FirstLogName = logName;
        FirstEntryIndex = entryIndex;
        LastSeenAt = observedAt;
        LastLogName = logName;
        LastEntryIndex = entryIndex;
        SeenCount = 1;
    }

    public long Id { get; private set; }

    /// <summary>Kayıtlı alan adı (eTLD+1, ASCII). Tabloda tekildir.</summary>
    public string RegistrableDomain { get; private set; }

    /// <summary>Bu sistemin bu alan adını örneklemde ilk gördüğü an.</summary>
    public DateTimeOffset FirstSeenAt { get; private set; }

    /// <summary>İlk görülen kaydın loga girdiği an (kaynağın tarihi).</summary>
    public DateTimeOffset FirstLoggedAt { get; private set; }

    /// <summary>İlk görülen sertifikanın geçerlilik başlangıcı (kaynağın tarihi).</summary>
    public DateTimeOffset? FirstNotBefore { get; private set; }

    public string FirstLogName { get; private set; }

    public long FirstEntryIndex { get; private set; }

    /// <summary>Bu alan adını taşıyan son kaydın okunduğu an.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Son sayılan kaydın logu ve indeksi. Aynı kaydın iki kez sayılmasını önler (bkz. EfCtStateStore).</summary>
    public string LastLogName { get; private set; }

    public long LastEntryIndex { get; private set; }

    /// <summary>Bu alan adını taşıyan kaç log kaydı görüldü (ön sertifika ve son sertifika ayrı sayılır).</summary>
    public long SeenCount { get; private set; }
}
