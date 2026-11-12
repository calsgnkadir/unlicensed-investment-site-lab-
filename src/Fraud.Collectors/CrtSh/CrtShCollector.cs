using Fraud.Collectors.Collection;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.CrtSh;

/// <summary>crt.sh sonuçlarını certificates ve certificate_domains tablolarına yazar.</summary>
public sealed class CrtShCollector(ICrtShClient client, CrtShOptions options, TimeProvider timeProvider)
    : SourceCollector<CrtShCertificate>(timeProvider)
{
    public override CollectionSource Source => CollectionSource.CrtSh;

    public override TimeSpan CacheTtl => options.CacheTtl;

    protected override Task<SourceResult<CrtShCertificate>> FetchAsync(string host, CancellationToken cancellationToken) =>
        client.SearchAsync(host, cancellationToken);

    /// <remarks>
    /// Her sertifika için üç adım:
    /// <list type="number">
    /// <item>Ekle; crt.sh kimliği zaten varsa hiçbir şey yapma (<c>ON CONFLICT DO NOTHING</c>). Etkilenen satır 1 ise yeni.</item>
    /// <item>Zaten vardıysa ve bu sorguda yeni ad geldiyse, adları birleştir. Ad listesi yalnızca büyür.</item>
    /// <item>Sertifika ile bu alan adı arasındaki bağlantıyı ekle; varsa dokunma.</item>
    /// </list>
    /// <c>observed_at</c> ilk görülme anıdır; tekrar görmek onu değiştirmez.
    /// </remarks>
    protected override async Task<int> StoreAsync(
        FraudDbContext db, long domainId, IReadOnlyList<CrtShCertificate> items, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var c in items)
        {
            var names = c.Names.ToArray();

            inserted += await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO certificates
                    (crtsh_id, serial_number, issuer_ca_id, issuer_name, common_name, names, not_before, not_after, logged_at, observed_at)
                VALUES
                    ({c.CrtShId}, {c.SerialNumber}, {c.IssuerCaId}, {c.IssuerName}, {c.CommonName}, {names},
                     {c.NotBefore}, {c.NotAfter}, {c.LoggedAt}, {observedAt})
                ON CONFLICT (crtsh_id) DO NOTHING
                """, cancellationToken).ConfigureAwait(false);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE certificates
                SET names = ARRAY(SELECT DISTINCT n FROM unnest(names || {names}) AS n ORDER BY n)
                WHERE crtsh_id = {c.CrtShId} AND NOT (names @> {names})
                """, cancellationToken).ConfigureAwait(false);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO certificate_domains (certificate_id, domain_id, observed_at)
                SELECT id, {domainId}, {observedAt} FROM certificates WHERE crtsh_id = {c.CrtShId}
                ON CONFLICT (certificate_id, domain_id) DO NOTHING
                """, cancellationToken).ConfigureAwait(false);
        }

        return inserted;
    }
}
