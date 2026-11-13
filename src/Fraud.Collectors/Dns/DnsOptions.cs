using System.Net;
using System.Text.RegularExpressions;
using Fraud.Collectors.Network;
using Fraud.Domain.Network;

namespace Fraud.Collectors.Dns;

/// <summary>DNS ölçüm ayarları (bölüm: <c>Dns</c>).</summary>
/// <remarks>
/// Liste ayarlarında yapılandırma boşsa koddaki varsayılan kullanılır (<c>Effective...</c>). Yapılandırma bağlayıcısı
/// listeye ekleme yaptığı için varsayılan doğrudan listeye konmaz; yoksa ayardaki değerler varsayılanın sonuna eklenirdi.
/// </remarks>
public sealed partial class DnsOptions
{
    public const string SectionName = "Dns";

    /// <summary>
    /// Ölçüm konumu, ör. <c>tr-ev-ubuntu</c>. ZORUNLU, varsayılanı yok: Konumu yazılmamış bir DNS kaydı
    /// yorumlanamaz (aynı ad farklı servis sağlayıcıdan farklı cevap alabilir). Kişisel bilgi yazılmaz.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>Kullanılacak çözücülerin adları. Yalnızca <see cref="DnsResolverCatalog"/> içindekiler.</summary>
    public IList<string> Resolvers { get; } = [];

    public IList<DnsTransport> Transports { get; } = [];

    public IList<DnsRecordType> QueryTypes { get; } = [];

    /// <summary>
    /// Bilinen engel sayfası IP'leri. Cevapta biri varsa kayıt "engel işareti" alır. Varsayılan, OONI parmak izi
    /// listesinde Türkiye için geçtiği özetlenen adres (doğrulanmadı; HAFTA4_AG_KAYNAKLARI Bölüm 3.1).
    /// </summary>
    public IList<string> KnownBlockPageIps { get; } = [];

    /// <summary>İki DNS paketinin gönderimi arasındaki en kısa süre, bütün yollar için ortak. 1 saniyeden az olamaz.</summary>
    public TimeSpan MinQueryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Tek bir denemenin süresi.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Zaman aşımı ve ağ hatasında tekrar deneme sayısı (0 veya 1).</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Bir alan adı bu süre içinde ölçüldüyse yeniden ölçülmez. Günlük tur için 20 saat.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(20);

    public IReadOnlyList<string> EffectiveResolvers =>
        Resolvers.Count > 0 ? [.. Resolvers.Distinct(StringComparer.OrdinalIgnoreCase)] : ["cloudflare", "google"];

    public IReadOnlyList<DnsTransport> EffectiveTransports =>
        Transports.Count > 0 ? [.. Transports.Distinct()] : [DnsTransport.Udp, DnsTransport.Tcp, DnsTransport.Doh];

    /// <summary>Varsayılan A, AAAA ve NS. CNAME ayrıca sorulmaz: A sorusunun cevabı CNAME zincirini zaten içerir.</summary>
    public IReadOnlyList<DnsRecordType> EffectiveQueryTypes =>
        QueryTypes.Count > 0 ? [.. QueryTypes.Distinct()] : [DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.NS];

    public IReadOnlySet<IPAddress> EffectiveBlockPageIps =>
        (KnownBlockPageIps.Count > 0 ? KnownBlockPageIps : ["195.175.254.2"])
            .Select(ip => NetworkAddress.Canonical(IPAddress.Parse(ip)))
            .ToHashSet();

    /// <summary>DNS komutu veya DNS içeren worker başlamadan önce çağrılır. Hatalı ayarda başlamaz.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Location) || !LocationPattern().IsMatch(Location))
        {
            throw new InvalidOperationException(
                "Dns:Location zorunlu: küçük harf, rakam ve tire, en fazla 64 karakter (ör. tr-ev-ubuntu).");
        }

        if (MinQueryInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException("Dns:MinQueryInterval 1 saniyeden kısa olamaz.");
        }

        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(30))
        {
            throw new InvalidOperationException("Dns:Timeout 0-30 saniye arasında olmalı.");
        }

        if (MaxRetries is < 0 or > 1)
        {
            throw new InvalidOperationException("Dns:MaxRetries 0 veya 1 olmalı.");
        }

        if (EffectiveQueryTypes.Any(t => t is not (DnsRecordType.A or DnsRecordType.AAAA or DnsRecordType.CNAME or DnsRecordType.NS)))
        {
            throw new InvalidOperationException("Dns:QueryTypes yalnızca A, AAAA, CNAME ve NS olabilir.");
        }

        if (KnownBlockPageIps.Any(ip => !IPAddress.TryParse(ip, out _)))
        {
            throw new InvalidOperationException("Dns:KnownBlockPageIps geçersiz IP içeriyor.");
        }

        // Bilinmeyen çözücü adı burada hata verir.
        DnsResolverCatalog.Select(EffectiveResolvers);
    }

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,63}\z")]
    private static partial Regex LocationPattern();
}
