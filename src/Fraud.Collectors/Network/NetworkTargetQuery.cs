using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Dns;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Fetching;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Network;

/// <summary>
/// Ağ ölçümlerinin hedefleri: hangi alan adları DNS'e sorulur, hangi IP'ler RIPEstat ve InternetDB'ye gider.
/// </summary>
/// <remarks>
/// Kural 2: DNS yalnızca listedeki, aday ve kontrol grubundaki adlar için sorulur; InternetDB'ye yalnızca SPK/USOM
/// listesi ve kontrol grubunun IP'leri gider. Bugün yalnızca <see cref="DomainSet.OfficialActions"/> var.
/// Yeni grup eklenince yalnızca bu sınıf değişir; toplayıcılar grubu buradan alır.
/// </remarks>
public static class NetworkTargetQuery
{
    /// <summary>Gruptaki alan adları (kimlik ve ASCII ad), kimliğe göre sıralı.</summary>
    public static async Task<List<CollectionTarget>> DomainsAsync(FraudDbContext db, DomainSet set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (set != DomainSet.OfficialActions)
        {
            throw new NotSupportedException(set + " grubu henüz tanımlı değil.");
        }

        var rows = await db.OfficialActions
            .Select(a => new { a.DomainId, a.Domain.Host })
            .Distinct()
            .OrderBy(t => t.DomainId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => new CollectionTarget(r.DomainId, r.Host)).ToList();
    }

    /// <summary>
    /// Gruptaki alan adlarının <paramref name="since"/> sonrasında DoH ile alınmış A ve AAAA cevaplarındaki IP'ler.
    /// </summary>
    /// <remarks>
    /// Neden yalnızca DoH: Düz UDP/TCP cevabı servis sağlayıcı tarafından değiştirilmiş olabilir; o zaman RIPEstat'a
    /// engel sunucusunun IP'sini sorardık. Engel işaretli cevaplar da aynı nedenle alınmaz.
    /// Herkese açık olmayan adresler (ör. 127.0.0.1, 10.0.0.0/8) hiçbir kaynağa gönderilmez.
    /// </remarks>
    /// <param name="isAllowed">IP süzgeci. Üretimde <see cref="IpAddressPolicy.IsPublic"/>; testler belgeleme aralığına izin verebilir.</param>
    public static async Task<List<IPAddress>> ResolvedIpsAsync(
        FraudDbContext db, DomainSet set, DateTimeOffset since, Func<IPAddress, bool> isAllowed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(isAllowed);
        if (set != DomainSet.OfficialActions)
        {
            throw new NotSupportedException(set + " grubu henüz tanımlı değil.");
        }

        var a = nameof(DnsRecordType.A);
        var aaaa = nameof(DnsRecordType.AAAA);
        var answerLists = await db.DnsSnapshots
            .Where(s => s.Transport == DnsTransport.Doh
                        && s.Outcome == DnsOutcome.Success
                        && !s.BlockMarker
                        && s.QueriedAt >= since
                        && (s.QueryType == a || s.QueryType == aaaa)
                        && db.OfficialActions.Any(o => o.DomainId == s.DomainId))
            .Select(s => s.Answers)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ips = new HashSet<IPAddress>();
        foreach (var answer in answerLists.SelectMany(list => list))
        {
            if (answer.Type is not ("A" or "AAAA") || !IPAddress.TryParse(answer.Value, out var ip))
            {
                continue;
            }

            ip = NetworkAddress.Canonical(ip); // engel işaretiyle aynı yazım (THREAT_MODEL N4)
            if (isAllowed(ip))
            {
                ips.Add(ip);
            }
        }

        // Sabit sıra: aynı veriyle iki tur aynı sırada sorar (test ve hata ayıklama kolaylığı).
        return ips.OrderBy(ip => ip.AddressFamily).ThenBy(ip => ip.ToString(), StringComparer.Ordinal).ToList();
    }
}
