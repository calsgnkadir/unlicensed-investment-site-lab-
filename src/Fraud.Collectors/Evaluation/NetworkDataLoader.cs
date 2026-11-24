using System.Net;
using Fraud.Collectors.Network;
using Fraud.Collectors.RipeStat;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Network;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Evaluation;

/// <summary>
/// Ağ betimlemesinin girdisini veritabanından okur (Hafta 8). Ağa hiç istek atmaz: Yalnızca toplayıcıların daha önce yazdığı
/// dns_snapshots, ip_asn_snapshots ve port_snapshots satırları. Alan adı ve IP yalnızca bellekte durur.
/// </summary>
/// <remarks>
/// <para><b>Yalnızca DoH:</b> IP'ler RIPEstat ve InternetDB'ye de yalnızca DoH cevaplarından gider
/// (<see cref="NetworkTargetQuery.ResolvedIpsAsync"/>). Aynı kural burada da geçerli: Düz UDP/TCP cevabını servis sağlayıcı
/// değiştirmiş olabilir. Hafta 4 ölçümünde sonuç türleri üç yolda birebir aynı çıktı, yani DoH'a indirmek sayıları değiştirmez.</para>
/// <para><b>CDN/bulut listesi:</b> Hafta 4 kodunun kullandığı liste (<see cref="RipeStatOptions.CdnAsns"/>). Kayıttaki
/// <c>cdn_or_cloud</c> işareti toplama günündeki listeyle yazılmıştır; liste sonradan genişlerse eski satır güncellenmez. Bu yüzden
/// iki kaynak birlikte okunur ve biri "CDN" diyorsa CDN sayılır (tutucu: yanlışlıkla "aynı ağ" bağı kurulmaz).</para>
/// <para><b>Herkese açık olmayan IP:</b> DNS cevabındaki 127.0.0.1 ya da 10.x gibi adresler atılır. Park edilmiş pek çok ad
/// aynı yerel adrese çözülür; bunlar "aynı ağ" olarak bağlansaydı ilgisiz adlar tek kümeye düşerdi.</para>
/// </remarks>
public sealed class NetworkDataLoader(FraudDbContext db, IDomainNameNormalizer normalizer, IDictionary<long, string> cdnAsns, Func<IPAddress, bool> isAllowed)
{
    /// <param name="subjectIdOfName">Kayıtlı alan adı → opak birim kimliği (ad gözlemlerinden). Bu eşlemede olmayan ad yüklenmez.</param>
    public async Task<NetworkData> LoadAsync(IReadOnlyDictionary<string, string> subjectIdOfName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subjectIdOfName);
        var names = subjectIdOfName.Keys.ToList();
        var domainRows = await db.Domains
            .Where(d => names.Contains(d.RegistrableDomain))
            .Select(d => new { d.Id, d.RegistrableDomain })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var subjectOfDomainId = domainRows.ToDictionary(d => d.Id, d => subjectIdOfName[d.RegistrableDomain]);
        var domainIds = subjectOfDomainId.Keys.ToList();

        var dnsRows = await db.DnsSnapshots
            .Where(s => domainIds.Contains(s.DomainId) && s.Transport == DnsTransport.Doh)
            .Select(s => new { s.DomainId, s.QueriedAt, s.QueryType, s.Outcome, s.ErrorKind, s.BlockMarker, s.Answers, s.Location })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var dns = new List<DnsFact>(dnsRows.Count);
        foreach (var r in dnsRows)
        {
            // Engel işaretli cevaptaki IP barındırma bilgisi değildir: IP listesine girmez (sonuç türü yine "engel işareti" olarak sayılır).
            var ips = r.BlockMarker
                ? []
                : r.Answers.Where(a => a.Type is "A" or "AAAA").Select(a => PublicIp(a.Value)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            var ns = r.Answers.Where(a => a.Type == "NS" && a.Value is not null)
                .Select(a => normalizer.Normalize(a.Value!).Value?.RegistrableDomain)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            dns.Add(new DnsFact(subjectOfDomainId[r.DomainId], r.QueriedAt, r.QueryType, r.Outcome, r.ErrorKind, r.BlockMarker, ips, ns));
        }

        var wanted = dns.SelectMany(f => f.Ips).ToHashSet(StringComparer.Ordinal);

        // IP tabloları küçüktür (yalnızca listedeki ve kontrol grubundaki adların IP'leri); hepsi okunup bellekte süzülür.
        var asnRows = await db.IpAsnSnapshots
            .Select(s => new { s.Ip, s.QueriedAt, s.Outcome, s.Asns, s.CdnOrCloud })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var portRows = await db.PortSnapshots
            .Select(s => new { s.Ip, s.QueriedAt, s.Outcome, s.Ports })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var asn = asnRows
            .Where(r => wanted.Contains(Text(r.Ip)) && r.Outcome == CollectionOutcome.Success && r.Asns.Length > 0)
            .Select(r => new IpAsnFact(Text(r.Ip), r.QueriedAt, r.Asns,
                r.CdnOrCloud is not null || RipeStatCollector.CdnOrCloud(r.Asns, cdnAsns) is not null))
            .ToList();
        var ports = portRows
            .Where(r => wanted.Contains(Text(r.Ip)) && r.Outcome != CollectionOutcome.Error)
            .Select(r => new PortFact(Text(r.Ip), r.QueriedAt, r.Outcome == CollectionOutcome.Success, r.Ports))
            .ToList();

        return new NetworkData(dns, asn, ports)
        {
            Locations = [.. dnsRows.Select(r => r.Location).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            IpAsnErrors = asnRows.Count(r => wanted.Contains(Text(r.Ip)) && r.Outcome == CollectionOutcome.Error),
            PortErrors = portRows.Count(r => wanted.Contains(Text(r.Ip)) && r.Outcome == CollectionOutcome.Error),
        };
    }

    /// <summary>Ad gözlemlerinden kayıtlı alan adı → birim kimliği eşlemesi.</summary>
    public static Dictionary<string, string> SubjectIdsByName(IEnumerable<Observation> observations) =>
        observations
            .Where(o => o.Kind == ObservationKinds.Name && o.Value is not null)
            .GroupBy(o => o.Value!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().SubjectId, StringComparer.Ordinal);

    private static string Text(IPAddress ip) => NetworkAddress.Canonical(ip).ToString();

    private string? PublicIp(string? value) =>
        IPAddress.TryParse(value, out var ip) && isAllowed(NetworkAddress.Canonical(ip)) ? Text(ip) : null;
}
