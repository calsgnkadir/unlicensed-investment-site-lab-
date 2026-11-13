using System.Net;
using Fraud.Domain.Collection;

namespace Fraud.Domain.Network;

/// <summary>
/// Bir IP için hazır pasif port verisi (tablo: port_snapshots). Kaynak: Shodan InternetDB. Siteye biz bağlanmayız.
/// </summary>
/// <remarks>
/// Bilerek saklanmayan alanlar: <c>vulns</c> (proje zafiyet araştırması değil) ve <c>hostnames</c>
/// (aynı IP'yi paylaşan üçüncü kişilerin alan adları; Kural 4). Bu tablo gizlidir, yalnızca toplu sayı yayımlanır.
/// CDN arkasındaki IP'de görünen portlar sitenin değil CDN'indir.
/// </remarks>
public sealed class PortSnapshot
{
    // EF Core için.
    private PortSnapshot()
    {
        Ip = IPAddress.None;
        Location = string.Empty;
        Ports = [];
        Tags = [];
        Cpes = [];
    }

    public PortSnapshot(
        IPAddress ip,
        DateTimeOffset queriedAt,
        string location,
        CollectionOutcome outcome,
        CollectionErrorKind errorKind,
        int? httpStatus,
        IReadOnlyList<int> ports,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> cpes)
    {
        ArgumentNullException.ThrowIfNull(ip);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(cpes);

        // InternetDB 200 cevabında port listesi boş olabilir; bu yine "kaynak cevap verdi" demektir (Success).
        // Empty yalnızca 404 "No information available" içindir ve veri taşımaz.
        NetworkSnapshotRules.CheckErrorKind(outcome, errorKind);
        if (outcome != CollectionOutcome.Success && (ports.Count > 0 || tags.Count > 0 || cpes.Count > 0))
        {
            throw new ArgumentException("Boş veya hatalı sonuçta port, etiket ya da CPE olmaz.", nameof(ports));
        }

        if (ports.Any(p => p is < 0 or > 65535))
        {
            throw new ArgumentOutOfRangeException(nameof(ports), "Port 0-65535 arasında olmalı.");
        }

        Ip = ip;
        QueriedAt = queriedAt;
        MeasuredOn = DateOnly.FromDateTime(queriedAt.UtcDateTime);
        Location = location;
        Outcome = outcome;
        ErrorKind = errorKind;
        HttpStatus = httpStatus;
        Ports = [.. ports];
        Tags = [.. tags];
        Cpes = [.. cpes];
    }

    public long Id { get; private set; }

    public IPAddress Ip { get; private set; }

    public DateTimeOffset QueriedAt { get; private set; }

    /// <summary>Sorgunun UTC günü. Aynı IP aynı gün iki kez kaydedilmez.</summary>
    public DateOnly MeasuredOn { get; private set; }

    public string Location { get; private set; }

    /// <summary>Success: 200 cevabı. Empty: 404 "No information available". Error: sonuç bilinmiyor.</summary>
    public CollectionOutcome Outcome { get; private set; }

    public CollectionErrorKind ErrorKind { get; private set; }

    public int? HttpStatus { get; private set; }

    public int[] Ports { get; private set; }

    /// <summary>InternetDB etiketleri (ör. <c>cdn</c>, <c>self-signed</c>). Yalnızca küçük harf, rakam ve tire.</summary>
    public string[] Tags { get; private set; }

    /// <summary>Yazılım tanımları (CPE 2.2 biçimi, ör. <c>cpe:/a:nginx:nginx</c>).</summary>
    public string[] Cpes { get; private set; }
}
