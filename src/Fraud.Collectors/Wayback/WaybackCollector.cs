using Fraud.Collectors.Collection;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Wayback;

/// <summary>CDX satırlarını archive_snapshots tablosuna yazar.</summary>
public sealed class WaybackCollector(IWaybackCdxClient client, WaybackCdxOptions options, TimeProvider timeProvider)
    : SourceCollector<CdxCapture>(timeProvider)
{
    public override CollectionSource Source => CollectionSource.WaybackCdx;

    public override TimeSpan CacheTtl => options.CacheTtl;

    protected override Task<SourceResult<CdxCapture>> FetchAsync(string host, CancellationToken cancellationToken) =>
        client.ListCapturesAsync(host, cancellationToken);

    /// <remarks>
    /// Anahtar (domain_id, capture_key). Aynı satır ikinci kez gelirse <c>ON CONFLICT DO NOTHING</c> ile atlanır;
    /// etkilenen satır sayısı yalnızca yeni kayıtları sayar.
    /// </remarks>
    protected override async Task<int> StoreAsync(
        FraudDbContext db, long domainId, IReadOnlyList<CdxCapture> items, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var s in items)
        {
            inserted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO archive_snapshots
                    (domain_id, capture_key, cdx_timestamp, captured_at, original_url, query_removed, status_code, mime_type, digest, observed_at)
                VALUES
                    ({domainId}, {s.CaptureKey}, {s.Timestamp}, {s.CapturedAt}, {s.OriginalUrl}, {s.QueryRemoved},
                     {s.StatusCode}, {s.MimeType}, {s.Digest}, {observedAt})
                ON CONFLICT (domain_id, capture_key) DO NOTHING
                """, cancellationToken).ConfigureAwait(false);
        }

        return inserted;
    }
}
