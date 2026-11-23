namespace Fraud.Evaluation.Model;

/// <summary>Gözlem türleri. Hangi türün "ne zaman bilinir" sayıldığı <see cref="KnownAtPolicy"/> içinde.</summary>
public static class ObservationKinds
{
    public const string Certificate = "certificate";
    public const string Archive = "archive";
    public const string Dns = "dns";
    public const string IpAsn = "ip_asn";
    public const string Port = "port";

    /// <summary>
    /// Birimin kayıtlı alan adı (Value). Ad zaman bilgisi taşımaz: <see cref="DateTimeOffset.MinValue"/> ile her görünümde
    /// vardır. Ad sinyalleri (baseline, marka, numaralı ad) buradan okur. Yalnızca bellekte; hiçbir çıktıya yazılmaz (Kural 4).
    /// </summary>
    public const string Name = "name";
}

/// <summary>
/// Bir birim hakkında zaman damgalı tek bir bilgi.
/// </summary>
/// <param name="SubjectId">Birimin opak kimliği.</param>
/// <param name="Kind">Tür (<see cref="ObservationKinds"/>).</param>
/// <param name="KnownAt">Bu bilginin herkesçe bilinebilir olduğu en erken an. Gelecek sızıntısı bununla engellenir.</param>
/// <param name="ObservedAt">Bu sistemin bilgiyi topladığı an. Denetim içindir, filtrede kullanılmaz (tek istisna:
/// ileriye dönük testte arşiv için <c>KnownAt = ObservedAt</c>).</param>
/// <param name="Value">Kısa değer: sertifikada sertifika kimliği, <see cref="ObservationKinds.Name"/> gözleminde kayıtlı alan adı.
/// Kişi verisi taşımaz. Ad yalnızca bellekte durur; rapor ve metin çıktıları <c>Value</c>'yu hiç basmaz (Kural 4).</param>
public sealed record Observation(string SubjectId, string Kind, DateTimeOffset KnownAt, DateTimeOffset ObservedAt, string? Value = null);

/// <summary>
/// "Bu kayıt ne zaman bilinir sayılır?" kuralı. Saf fonksiyonlar; yükleyici her kaydı bunlardan geçirir.
/// </summary>
/// <remarks>
/// Kural "o tarihte bilinen" sorusunu tutucu cevaplar: Emin olmadığımızda bilinme anını geç sayarız. Geç saymak
/// yalnızca sinyali zayıflatır; erken saymak ise geleceği sızdırır ve sonucu şişirir.
/// </remarks>
public static class KnownAtPolicy
{
    /// <summary>Wayback kaydının dizine girme gecikmesine karşı varsayılan pay (yapılandırılabilir).</summary>
    public static readonly TimeSpan DefaultArchiveDelay = TimeSpan.FromDays(7);

    /// <summary>
    /// Sertifika (crt.sh ve CT): <c>not_before</c>. VARSAYIM: CT logları sertifikayı neredeyse anında yayımlar
    /// (Let's Encrypt ön sertifikayı vermeden önce loga yazar). Geriye tarihli ya da geç loglanan sertifika için bu
    /// varsayım küçük bir sızıntı payı bırakır; raporda sınır olarak yazılır.
    /// </summary>
    public static DateTimeOffset Certificate(DateTimeOffset notBefore) => notBefore;

    /// <summary>
    /// Sertifika, kipe göre. İleriye dönük testte <c>observed_at</c>: Taahhütten sonra çekilmiş bir sertifika "taahhütten
    /// önce biliniyordu" sayılmasın (mentör maddesi 1). Geçmiş testlerinde <see cref="Certificate(DateTimeOffset)"/>.
    /// </summary>
    public static DateTimeOffset Certificate(DateTimeOffset notBefore, DateTimeOffset observedAt, bool prospective) =>
        prospective ? observedAt : notBefore;

    /// <summary>
    /// Arşiv: Geçmiş testlerinde <c>captured_at + gecikme</c> (dizine geç girme payı). İleriye dönük testte
    /// <c>observed_at</c>: Yalnızca bizim gerçekten gördüğümüz an bilinir sayılır.
    /// </summary>
    public static DateTimeOffset Archive(DateTimeOffset capturedAt, DateTimeOffset observedAt, TimeSpan delay, bool prospective) =>
        prospective ? observedAt : capturedAt + delay;

    /// <summary>
    /// DNS, RIPEstat, InternetDB: sorgu anı. Geçmiş bu kaynaklarda yok; bu yüzden geçmiş veri testinde referans
    /// zamanından önce hiçbir kayıt çıkmaz ve bu veriler orada kendiliğinden kullanılamaz.
    /// </summary>
    public static DateTimeOffset Network(DateTimeOffset queriedAt) => queriedAt;
}
