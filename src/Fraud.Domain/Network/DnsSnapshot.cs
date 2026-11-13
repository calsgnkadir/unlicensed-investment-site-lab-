using Fraud.Domain.DomainNames;

namespace Fraud.Domain.Network;

/// <summary>
/// Bir alan adı için tek bir DNS sorgusunun kaydı (tablo: dns_snapshots).
/// Bir ölçüm turunda aynı alan adı her çözücüye, her taşıma yoluna ve her kayıt türüne ayrı sorulur;
/// her biri ayrı satırdır.
/// </summary>
/// <remarks>
/// <para>
/// Zaman, ölçüm konumu, çözücü, taşıma yolu ve hata durumu her satırda zorunludur (NETWORK_PLAN Bölüm 5).
/// Aynı soru farklı konumdan veya farklı yoldan farklı cevap alabilir; Türkiye'de engel DNS cevabı
/// değiştirilerek uygulanabildiği için bu ayrım ölçümün kendisidir.
/// </para>
/// <para>
/// Bu kayıt "bugün bu konumdan görünen cevap"tır. Karar tarihindeki DNS durumunu göstermez; geçmiş testinde
/// özellik olarak kullanılmaz (ROADMAP Hafta 6, gelecek sızıntısı).
/// </para>
/// </remarks>
public sealed class DnsSnapshot
{
    // EF Core için.
    private DnsSnapshot()
    {
        Location = string.Empty;
        Resolver = string.Empty;
        QueryType = string.Empty;
        Answers = [];
    }

    public DnsSnapshot(
        long domainId,
        DateTimeOffset queriedAt,
        string location,
        string resolver,
        DnsTransport transport,
        string queryType,
        int? rcode,
        DnsOutcome outcome,
        DnsErrorKind errorKind,
        IReadOnlyList<DnsAnswer> answers,
        long? negativeTtl,
        bool truncated,
        int latencyMs,
        int attempts,
        bool blockMarker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryType);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentOutOfRangeException.ThrowIfNegative(latencyMs);
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);

        // Aynı kurallar veritabanında CHECK kısıtı olarak da var. Amaç: Hata hiçbir zaman "kayıt yok" gibi görünmesin.
        switch (outcome)
        {
            case DnsOutcome.Error when errorKind == DnsErrorKind.None:
                throw new ArgumentException("Hata sonucunda hata türü zorunludur.", nameof(errorKind));
            case DnsOutcome.Error when answers.Count > 0 || blockMarker:
                throw new ArgumentException("Hata sonucunda cevap kaydı ve engel işareti olmaz.", nameof(answers));
            case not DnsOutcome.Error when errorKind != DnsErrorKind.None:
                throw new ArgumentException("Hata olmayan sonuçta hata türü olmaz.", nameof(errorKind));
            case DnsOutcome.Success when rcode != 0 || answers.Count == 0:
                throw new ArgumentException("Başarılı sonuç NOERROR ve en az bir cevap kaydı ister.", nameof(outcome));
            case DnsOutcome.Empty when rcode != 0 || answers.Count != 0:
                throw new ArgumentException("Boş sonuç yalnızca NOERROR ve sıfır cevapla yazılır.", nameof(outcome));
            case DnsOutcome.NxDomain when rcode != 3:
                throw new ArgumentException("NXDOMAIN sonucu RCODE 3 ister.", nameof(outcome));
        }

        if (blockMarker && outcome != DnsOutcome.Success)
        {
            throw new ArgumentException("Engel işareti yalnızca cevap içeren sonuçta olabilir.", nameof(blockMarker));
        }

        DomainId = domainId;
        QueriedAt = queriedAt;
        MeasuredOn = DateOnly.FromDateTime(queriedAt.UtcDateTime);
        Location = location;
        Resolver = resolver;
        Transport = transport;
        QueryType = queryType;
        Rcode = rcode;
        Outcome = outcome;
        ErrorKind = errorKind;
        Answers = answers;
        NegativeTtl = negativeTtl;
        Truncated = truncated;
        LatencyMs = latencyMs;
        Attempts = attempts;
        BlockMarker = blockMarker;
    }

    public long Id { get; private set; }

    public long DomainId { get; private set; }

    public DomainName Domain { get; private set; } = null!;

    /// <summary>Sorgunun gönderildiği an (UTC).</summary>
    public DateTimeOffset QueriedAt { get; private set; }

    /// <summary>
    /// <see cref="QueriedAt"/>'in UTC günü. Aynı gün aynı soru ikinci kez sorulursa yeni satır açılmaz
    /// (tekil indeks); yalnızca önceki satır hataysa yenisiyle değiştirilir.
    /// </summary>
    public DateOnly MeasuredOn { get; private set; }

    /// <summary>Ölçüm konumu (ör. <c>tr-ev-ubuntu</c>). Yapılandırma: <c>Dns:Location</c>.</summary>
    public string Location { get; private set; }

    /// <summary>Çözücünün kısa adı (ör. <c>cloudflare</c>, <c>google</c>). Adresi koddaki izin listesindedir.</summary>
    public string Resolver { get; private set; }

    public DnsTransport Transport { get; private set; }

    /// <summary>Sorulan kayıt türü (ör. <c>A</c>).</summary>
    public string QueryType { get; private set; }

    /// <summary>Cevaptaki RCODE (0 NOERROR, 3 NXDOMAIN ...). Geçerli cevap hiç gelmediyse boş.</summary>
    public int? Rcode { get; private set; }

    public DnsOutcome Outcome { get; private set; }

    public DnsErrorKind ErrorKind { get; private set; }

    /// <summary>Saklanan cevap kayıtları, cevaptaki sırayla. CNAME zinciri korunur. Hata sonucunda boş.</summary>
    public IReadOnlyList<DnsAnswer> Answers { get; private set; }

    /// <summary>
    /// NXDOMAIN veya boş cevapta, yetki bölümündeki SOA kaydından hesaplanan olumsuz önbellek süresi (saniye).
    /// RFC 2308 Bölüm 5: SOA kaydının TTL'i ile SOA MINIMUM alanından küçük olanı.
    /// </summary>
    public long? NegativeTtl { get; private set; }

    /// <summary>UDP cevabında TC (kesik) biti vardı; sonuç aynı çözücüye TCP ile yeniden sorularak alındı.</summary>
    public bool Truncated { get; private set; }

    /// <summary>İlk paketin gönderilmesinden sonucun gelmesine kadar geçen süre (tekrar deneme ve TCP'ye geçiş dahil).</summary>
    public int LatencyMs { get; private set; }

    /// <summary>Gönderilen sorgu paketi sayısı (tekrar deneme ve TCP'ye geçiş dahil).</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Cevapta bilinen bir engel sayfası IP'si var (<c>Dns:KnownBlockPageIps</c>). Böyle bir cevap barındırma bilgisi
    /// sayılmaz; RIPEstat ve InternetDB'ye gönderilmez.
    /// </summary>
    public bool BlockMarker { get; private set; }
}
