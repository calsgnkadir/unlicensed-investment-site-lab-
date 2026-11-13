using System.Globalization;
using System.Text;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Dns;

/// <summary>Bir çözücü için UDP/TCP cevabının DoH cevabıyla karşılaştırması. Yalnızca sayı.</summary>
/// <param name="DomainsComparedUdp">A veya AAAA sorusunda hem UDP hem DoH cevabı (hata olmayan) olan alan adı sayısı.</param>
/// <param name="DomainsDifferingUdp">Bunlardan en az bir soruda sonuç türü veya IP kümesi farklı olanlar.</param>
public sealed record ResolverComparison(
    string Resolver, int DomainsComparedUdp, int DomainsDifferingUdp, int DomainsComparedTcp, int DomainsDifferingTcp);

/// <summary>Bir taşıma yolunun toplamları (bütün çözücüler ve türler birlikte).</summary>
public sealed record TransportSummary(
    DnsTransport Transport, int Queries, int Errors, int NxDomain, int BlockMarkers, int TruncatedToTcp)
{
    /// <summary>NXDOMAIN / hata olmayan sorgu. Hata olmayan sorgu yoksa boş.</summary>
    public double? NxDomainRate => Queries - Errors == 0 ? null : (double)NxDomain / (Queries - Errors);
}

/// <summary>
/// UDP, TCP ve DoH cevaplarını karşılaştıran özet (<c>dns-compare</c>). Alan adı ve IP YAZMAZ, yalnızca sayı.
/// </summary>
/// <remarks>
/// Amaç: Ev bağlantısındaki servis sağlayıcı düz DNS'e karışıyor mu? DoH cevabı yolda değiştirilemez; aynı çözücüye
/// UDP ile sorulan sorunun cevabı farklıysa bu araya girme belirtisidir. Kanıt değildir: Büyük siteler her sorguda
/// farklı IP alt kümesi döndürebilir (yük dağıtımı), bu da "farklı" sayılır.
/// </remarks>
public sealed record DnsCompareReport(
    DateOnly? Day, string Location, IReadOnlyList<ResolverComparison> Resolvers, IReadOnlyList<TransportSummary> Transports)
{
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        if (Day is null)
        {
            sb.AppendLine(c, $"DNS karşılaştırması: '{Location}' konumunda ölçüm yok.");
            return sb.ToString();
        }

        sb.AppendLine(c, $"DNS karşılaştırması, gün {Day:yyyy-MM-dd} (UTC), konum {Location}");
        sb.AppendLine();
        sb.AppendLine("[Çözücü başına: düz DNS ile DoH aynı cevabı verdi mi? (A ve AAAA)]");
        foreach (var r in Resolvers)
        {
            sb.AppendLine(c, $"  {r.Resolver}:");
            sb.AppendLine(c, $"    UDP vs DoH: karşılaştırılan alan adı {r.DomainsComparedUdp}, farklı {r.DomainsDifferingUdp}");
            sb.AppendLine(c, $"    TCP vs DoH: karşılaştırılan alan adı {r.DomainsComparedTcp}, farklı {r.DomainsDifferingTcp}");
        }

        sb.AppendLine();
        sb.AppendLine("[Yol başına toplam (bütün çözücüler ve türler)]");
        foreach (var t in Transports)
        {
            sb.AppendLine(c, $"  {t.Transport}: sorgu {t.Queries}, hata {t.Errors}, NXDOMAIN {t.NxDomain} " +
                             $"(oran {(t.NxDomainRate is { } r ? r.ToString("P1", c) : "-")}), engel işareti {t.BlockMarkers}");
        }

        var tc = Transports.Where(t => t.Transport == DnsTransport.Udp).Sum(t => t.TruncatedToTcp);
        sb.AppendLine(c, $"  Kesik UDP cevabı (TC) sonrası TCP'ye geçiş: {tc}");
        sb.AppendLine();
        sb.AppendLine("Not: \"Farklı\" araya girme kanıtı değildir; yük dağıtımı da farklı IP döndürebilir. Hata alan sorgular");
        sb.AppendLine("karşılaştırmaya girmez. Sonuç yalnızca bu konumdan ve bu günde görülen cevabı anlatır.");
        return sb.ToString();
    }
}

/// <summary>dns_snapshots tablosundan <see cref="DnsCompareReport"/> üretir.</summary>
public sealed class DnsCompareReportBuilder(FraudDbContext db)
{
    /// <param name="day">Boşsa bu konumdaki en son ölçüm günü.</param>
    public async Task<DnsCompareReport> BuildAsync(string location, DateOnly? day, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        day ??= await db.DnsSnapshots
            .Where(s => s.Location == location)
            .Select(s => (DateOnly?)s.MeasuredOn)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        if (day is null)
        {
            return new DnsCompareReport(null, location, [], []);
        }

        var rows = await db.DnsSnapshots
            .AsNoTracking()
            .Where(s => s.Location == location && s.MeasuredOn == day)
            .Select(s => new Row(s.DomainId, s.Resolver, s.Transport, s.QueryType, s.Outcome, s.Answers, s.BlockMarker, s.Truncated))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var resolvers = rows.Select(r => r.Resolver).Distinct().Order(StringComparer.Ordinal)
            .Select(name => Compare(name, rows.Where(r => r.Resolver == name).ToList()))
            .ToList();

        var transports = rows.GroupBy(r => r.Transport).OrderBy(g => g.Key)
            .Select(g => new TransportSummary(
                g.Key,
                g.Count(),
                g.Count(r => r.Outcome == DnsOutcome.Error),
                g.Count(r => r.Outcome == DnsOutcome.NxDomain),
                g.Count(r => r.BlockMarker),
                g.Count(r => r.Truncated)))
            .ToList();

        return new DnsCompareReport(day, location, resolvers, transports);
    }

    private static ResolverComparison Compare(string resolver, List<Row> rows)
    {
        var (comparedUdp, differingUdp) = CompareWithDoh(rows, DnsTransport.Udp);
        var (comparedTcp, differingTcp) = CompareWithDoh(rows, DnsTransport.Tcp);
        return new ResolverComparison(resolver, comparedUdp, differingUdp, comparedTcp, differingTcp);
    }

    /// <summary>Alan adı başına: A ve AAAA sorularında <paramref name="plain"/> ile DoH cevabının "imzası" aynı mı.</summary>
    private static (int Compared, int Differing) CompareWithDoh(List<Row> rows, DnsTransport plain)
    {
        var compared = new HashSet<long>();
        var differing = new HashSet<long>();
        foreach (var group in rows.Where(r => r.QueryType is "A" or "AAAA").GroupBy(r => (r.DomainId, r.QueryType)))
        {
            var left = group.FirstOrDefault(r => r.Transport == plain);
            var doh = group.FirstOrDefault(r => r.Transport == DnsTransport.Doh);
            if (left is null || doh is null || left.Outcome == DnsOutcome.Error || doh.Outcome == DnsOutcome.Error)
            {
                continue;
            }

            compared.Add(group.Key.DomainId);
            if (Signature(left) != Signature(doh))
            {
                differing.Add(group.Key.DomainId);
            }
        }

        return (compared.Count, differing.Count);
    }

    /// <summary>Sonuç türü + sıralı IP kümesi. TTL ve kayıt sırası karşılaştırmaya girmez.</summary>
    private static string Signature(Row row) =>
        row.Outcome + "|" + string.Join(",", row.Answers
            .Where(a => a.Type is "A" or "AAAA" && a.Value is not null)
            .Select(a => a.Value!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    private sealed record Row(
        long DomainId, string Resolver, DnsTransport Transport, string QueryType, DnsOutcome Outcome,
        IReadOnlyList<DnsAnswer> Answers, bool BlockMarker, bool Truncated);
}
