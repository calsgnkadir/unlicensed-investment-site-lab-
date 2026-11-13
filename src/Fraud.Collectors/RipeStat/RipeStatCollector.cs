using System.Net;
using Fraud.Collectors.Network;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.RipeStat;

/// <summary>RIPEstat sonucunu ip_asn_snapshots tablosuna yazar ve CDN/bulut işaretini koyar.</summary>
public sealed class RipeStatCollector(IRipeStatClient client, RipeStatOptions options, TimeProvider timeProvider) : IIpSourceCollector
{
    public CollectionSource Source => CollectionSource.RipeStat;

    public TimeSpan CacheTtl => options.CacheTtl;

    public TimeSpan DnsLookback => options.DnsLookback;

    public async Task<HashSet<IPAddress>> RecentlyAnsweredAsync(FraudDbContext db, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ips = await db.IpAsnSnapshots
            .Where(s => s.Outcome != CollectionOutcome.Error && s.QueriedAt >= since)
            .Select(s => s.Ip)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. ips];
    }

    public async Task<IpCollectResult> CollectAsync(FraudDbContext db, IPAddress ip, string location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var queriedAt = timeProvider.GetUtcNow();
        var lookup = await client.LookupAsync(ip, cancellationToken).ConfigureAwait(false);

        var asns = lookup.Info?.Asns ?? [];
        var snapshot = new IpAsnSnapshot(
            ip, queriedAt, location, lookup.Outcome, lookup.ErrorKind, lookup.HttpStatus,
            asns, lookup.Info?.Prefix, CdnOrCloud(asns, options.CdnAsns));

        var stored = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ip_asn_snapshots
                (ip, queried_at, measured_on, location, outcome, error_kind, http_status, asns, prefix, cdn_or_cloud)
            VALUES
                ({snapshot.Ip}, {snapshot.QueriedAt}, {snapshot.MeasuredOn}, {snapshot.Location}, {snapshot.Outcome.ToString()},
                 {snapshot.ErrorKind.ToString()}, {snapshot.HttpStatus}, {snapshot.Asns}, {snapshot.Prefix}, {snapshot.CdnOrCloud})
            ON CONFLICT (ip, measured_on) DO UPDATE SET
                queried_at = EXCLUDED.queried_at, location = EXCLUDED.location, outcome = EXCLUDED.outcome,
                error_kind = EXCLUDED.error_kind, http_status = EXCLUDED.http_status, asns = EXCLUDED.asns,
                prefix = EXCLUDED.prefix, cdn_or_cloud = EXCLUDED.cdn_or_cloud
            WHERE ip_asn_snapshots.outcome = 'Error'
            """, CancellationToken.None).ConfigureAwait(false);

        return new IpCollectResult(lookup.Outcome, lookup.ErrorKind, lookup.Attempts, stored > 0);
    }

    /// <summary>ASN'lerden biri bilinen bir CDN veya bulut sağlayıcısınınsa adını döner (en küçük ASN önce).</summary>
    public static string? CdnOrCloud(IReadOnlyList<long> asns, IDictionary<long, string> map)
    {
        ArgumentNullException.ThrowIfNull(asns);
        ArgumentNullException.ThrowIfNull(map);
        foreach (var asn in asns.Order())
        {
            if (map.TryGetValue(asn, out var provider))
            {
                return provider;
            }
        }

        return null;
    }
}
