using System.Net;
using Fraud.Collectors.Network;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.InternetDb;

/// <summary>InternetDB sonucunu port_snapshots tablosuna yazar. vulns ve hostnames hiç saklanmaz.</summary>
public sealed class InternetDbCollector(IInternetDbClient client, InternetDbOptions options, TimeProvider timeProvider) : IIpSourceCollector
{
    public CollectionSource Source => CollectionSource.InternetDb;

    public TimeSpan CacheTtl => options.CacheTtl;

    public TimeSpan DnsLookback => options.DnsLookback;

    public async Task<HashSet<IPAddress>> RecentlyAnsweredAsync(FraudDbContext db, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ips = await db.PortSnapshots
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

        var snapshot = new PortSnapshot(
            ip, queriedAt, location, lookup.Outcome, lookup.ErrorKind, lookup.HttpStatus,
            lookup.Host?.Ports ?? [], lookup.Host?.Tags ?? [], lookup.Host?.Cpes ?? []);

        var stored = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO port_snapshots
                (ip, queried_at, measured_on, location, outcome, error_kind, http_status, ports, tags, cpes)
            VALUES
                ({snapshot.Ip}, {snapshot.QueriedAt}, {snapshot.MeasuredOn}, {snapshot.Location}, {snapshot.Outcome.ToString()},
                 {snapshot.ErrorKind.ToString()}, {snapshot.HttpStatus}, {snapshot.Ports}, {snapshot.Tags}, {snapshot.Cpes})
            ON CONFLICT (ip, measured_on) DO UPDATE SET
                queried_at = EXCLUDED.queried_at, location = EXCLUDED.location, outcome = EXCLUDED.outcome,
                error_kind = EXCLUDED.error_kind, http_status = EXCLUDED.http_status, ports = EXCLUDED.ports,
                tags = EXCLUDED.tags, cpes = EXCLUDED.cpes
            WHERE port_snapshots.outcome = 'Error'
            """, CancellationToken.None).ConfigureAwait(false);

        return new IpCollectResult(lookup.Outcome, lookup.ErrorKind, lookup.Attempts, stored > 0);
    }
}
