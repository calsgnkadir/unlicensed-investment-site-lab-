using System.Net;
using Fraud.Domain.Collection;

namespace Fraud.Domain.Network;

/// <summary>
/// Bir IP adresinin o anki ağ bilgisi: hangi ASN'den, hangi önekle duyuruluyor (tablo: ip_asn_snapshots).
/// Kaynak: RIPEstat <c>network-info</c>. Kaynak yalnızca güncel durumu verir; geçmiş tarih için kullanılmaz.
/// </summary>
/// <remarks>
/// RIPEstat'a alan adı gitmez, yalnızca IP gider. IP'ler yalnızca DoH ile alınmış ve engel işareti taşımayan DNS
/// cevaplarından gelir. Alan adına bağlantı dns_snapshots üzerinden kurulur; bu tabloda alan adı yoktur.
/// </remarks>
public sealed class IpAsnSnapshot
{
    // EF Core için.
    private IpAsnSnapshot()
    {
        Ip = IPAddress.None;
        Location = string.Empty;
        Asns = [];
    }

    public IpAsnSnapshot(
        IPAddress ip,
        DateTimeOffset queriedAt,
        string location,
        CollectionOutcome outcome,
        CollectionErrorKind errorKind,
        int? httpStatus,
        IReadOnlyList<long> asns,
        string? prefix,
        string? cdnOrCloud)
    {
        ArgumentNullException.ThrowIfNull(ip);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(asns);
        NetworkSnapshotRules.CheckErrorKind(outcome, errorKind);
        var hasData = asns.Count > 0 || prefix is not null;
        if ((outcome == CollectionOutcome.Success) != hasData)
        {
            throw new ArgumentException("ASN veya önek yalnızca başarılı sonuçta olur ve başarılı sonuçta zorunludur.", nameof(outcome));
        }

        Ip = ip;
        QueriedAt = queriedAt;
        MeasuredOn = DateOnly.FromDateTime(queriedAt.UtcDateTime);
        Location = location;
        Outcome = outcome;
        ErrorKind = errorKind;
        HttpStatus = httpStatus;
        Asns = [.. asns];
        Prefix = prefix;
        CdnOrCloud = cdnOrCloud;
    }

    public long Id { get; private set; }

    public IPAddress Ip { get; private set; }

    public DateTimeOffset QueriedAt { get; private set; }

    /// <summary>Sorgunun UTC günü. Aynı IP aynı gün iki kez kaydedilmez.</summary>
    public DateOnly MeasuredOn { get; private set; }

    /// <summary>İsteğin çıktığı yer (<c>Collection:MeasurementLocation</c>).</summary>
    public string Location { get; private set; }

    /// <summary>Success: ASN veya önek var. Empty: RIPEstat cevap verdi ama IP duyurulmuyor. Error: sonuç bilinmiyor.</summary>
    public CollectionOutcome Outcome { get; private set; }

    public CollectionErrorKind ErrorKind { get; private set; }

    public int? HttpStatus { get; private set; }

    /// <summary>Öneki duyuran ASN'ler (çoğunlukla tek). Birden fazlaysa MOAS durumu.</summary>
    public long[] Asns { get; private set; }

    /// <summary>En özel duyurulan önek, ör. <c>192.0.2.0/24</c>.</summary>
    public string? Prefix { get; private set; }

    /// <summary>
    /// ASN bilinen bir CDN veya bulut sağlayıcısına aitse sağlayıcının kısa adı (ör. <c>cloudflare</c>, <c>aws</c>).
    /// Bu adresler "aynı ağ" kümesine sokulmaz: Aynı ASN aynı sahip demek değildir.
    /// </summary>
    public string? CdnOrCloud { get; private set; }
}

/// <summary>IP tabanlı ağ kayıtlarının ortak kuralı.</summary>
internal static class NetworkSnapshotRules
{
    /// <summary>Hata sonucu her zaman bir hata türü taşır; hata olmayan sonuç taşımaz.</summary>
    public static void CheckErrorKind(CollectionOutcome outcome, CollectionErrorKind errorKind)
    {
        if ((outcome == CollectionOutcome.Error) != (errorKind != CollectionErrorKind.None))
        {
            throw new ArgumentException("Hata türü yalnızca hata sonucunda olur ve orada zorunludur.", nameof(errorKind));
        }
    }
}
