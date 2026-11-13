using System.Diagnostics.Metrics;
using Fraud.Collectors.Dns;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Observability;

namespace Fraud.Collectors.Monitoring;

/// <summary>
/// DNS ölçüm metrikleri. Etiketler: çözücü kısa adı (cloudflare, google), yol (Udp, Tcp, Doh), sonuç ve hata türü.
/// Sorgulanan ad ve cevaptaki IP hiçbir etikete girmez.
/// </summary>
/// <remarks>
/// <para>
/// "Son tur" göstergeleri (<c>fraud_dns_last_run_*</c>): Sayaçlar süreç açıldığından beri birikir; uyarı ise son
/// turun oranına bakmalı. Bu yüzden tur boyunca ayrı bir sayım tutulur ve tur iptal edilmeden bitince göstergelere
/// kopyalanır. İptal edilen yarım tur önceki değeri değiştirmez.
/// </para>
/// <para>Son tur sınıfları (<c>result</c> etiketi):</para>
/// <list type="bullet">
/// <item><c>answer</c>: NOERROR (kayıtlı veya boş cevap).</item>
/// <item><c>nxdomain</c>: Çözücü "ad yok" dedi. Gerçek bir cevaptır; kapatılmış sitelerde sık görülür.</item>
/// <item><c>servfail</c>: SERVFAIL. Çözücü cevap verdi ama adı çözemedi (ör. yetkili sunucu yok veya DNSSEC
/// bozuk). Engellenen ve terk edilen alan adlarında sık görülür; bizim ağımızın veya çözücünün arızası değildir.
/// Bu yüzden hata oranı uyarısına girmez, panelde ayrı gösterilir.</item>
/// <item><c>error</c>: Cevap alınamadı veya cevap geçersiz (zaman aşımı, bağlantı hatası, bozuk paket, REFUSED,
/// DoH HTTP hatası ...). Ölçüm altyapısının sorununu gösterir; uyarı bu orana bakar.</item>
/// </list>
/// İptal edilen sorgular hiçbir sınıfa girmez.
/// </remarks>
public sealed class DnsMetrics : IDisposable
{
    public const string MeterName = "Fraud.Collectors.Dns";

    /// <summary>Gecikme kovaları (saniye). UDP ~10-50 ms, DoH ilk bağlantıda 100+ ms, zaman aşımı 5 sn.</summary>
    private static readonly double[] LatencyBuckets = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly Counter<long> _queries;
    private readonly Counter<long> _blockMarkers;
    private readonly Histogram<double> _latency;
    private readonly Lock _gate = new();
    private Dictionary<(DnsTransport, string), long> _current = [];
    private Dictionary<(DnsTransport, string), long> _lastRun = [];
    private Dictionary<string, (int Compared, int Differing)> _mismatch = new(StringComparer.Ordinal);

    public DnsMetrics(IMeterFactory? meterFactory = null)
    {
        _ownsMeter = meterFactory is null;
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

        _queries = _meter.CreateCounter<long>("fraud_dns_queries", null, "DNS sorgusu (çözücü, yol, sonuç ve hata türüne göre)");
        _blockMarkers = _meter.CreateCounter<long>("fraud_dns_block_marker", null, "Cevabında bilinen engel sayfası IP'si olan sorgu");
        _latency = _meter.CreateHistogram("fraud_dns_query_duration_seconds", "s",
            "Sorgu süresi (tekrar deneme ve TCP'ye geçiş dahil, hız sınırı beklemesi hariç)",
            tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = LatencyBuckets });

        _meter.CreateObservableGauge("fraud_dns_last_run_queries", ObserveLastRun, null,
            "Son tamamlanan DNS turunda sorgu sayısı (yol ve sınıfa göre: answer, nxdomain, servfail, error)");
        _meter.CreateObservableGauge("fraud_dns_last_run_udp_doh_compared_domains", () => ObserveMismatch(m => m.Compared), null,
            "Son ölçüm gününde, A/AAAA sorusunda hem UDP hem DoH cevabı olan alan adı sayısı (çözücü başına)");
        _meter.CreateObservableGauge("fraud_dns_last_run_udp_doh_mismatch_domains", () => ObserveMismatch(m => m.Differing), null,
            "Bunlardan UDP ve DoH cevabı farklı olanlar. Araya girme kanıtı değildir (yük dağıtımı da farklı IP döndürür).");
    }

    /// <summary>Yeni tur: tur sayımı sıfırlanır.</summary>
    public void RunStarted()
    {
        lock (_gate)
        {
            _current = [];
        }
    }

    /// <summary>Tur bitti. İptal edilmediyse tur sayımı "son tur" göstergelerine geçer.</summary>
    public void RunFinished(bool cancelled)
    {
        lock (_gate)
        {
            if (!cancelled)
            {
                _lastRun = _current;
            }

            _current = [];
        }
    }

    /// <summary>Bir sorgunun sonucu.</summary>
    public void Query(string resolver, DnsTransport transport, DnsQueryResult result, bool blockMarker)
    {
        ArgumentNullException.ThrowIfNull(result);
        var resolverTag = MetricLabels.Tag("resolver", resolver);
        var transportTag = MetricLabels.Tag("transport", transport);
        _queries.Add(1, resolverTag, transportTag, MetricLabels.Tag("outcome", result.Outcome), MetricLabels.Tag("error_kind", result.ErrorKind));
        _latency.Record(Math.Max(0, result.Latency.TotalSeconds), transportTag);
        if (blockMarker)
        {
            _blockMarkers.Add(1, resolverTag, transportTag);
        }

        if (Classify(result) is { } cls)
        {
            lock (_gate)
            {
                _current[(transport, cls)] = _current.GetValueOrDefault((transport, cls)) + 1;
            }
        }
    }

    /// <summary>Son ölçüm gününün UDP-DoH karşılaştırması (dns-compare ile aynı hesap).</summary>
    public void Comparison(IEnumerable<ResolverComparison> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        var next = resolvers.ToDictionary(r => r.Resolver, r => (r.DomainsComparedUdp, r.DomainsDifferingUdp), StringComparer.Ordinal);
        lock (_gate)
        {
            _mismatch = next;
        }
    }

    /// <summary>Son tur sınıfı; iptal edilen sorguda <c>null</c>. Kural sınıf açıklamasında.</summary>
    public static string? Classify(DnsQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            DnsOutcome.Success or DnsOutcome.Empty => "answer",
            DnsOutcome.NxDomain => "nxdomain",
            _ when result.ErrorKind == DnsErrorKind.Cancelled => null,
            _ when result.ErrorKind == DnsErrorKind.ServerFailure => "servfail",
            _ => "error",
        };
    }

    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private IEnumerable<Measurement<long>> ObserveLastRun()
    {
        Dictionary<(DnsTransport, string), long> snapshot;
        lock (_gate)
        {
            snapshot = _lastRun;
        }

        return snapshot.Select(kv => new Measurement<long>(
            kv.Value, MetricLabels.Tag("transport", kv.Key.Item1), MetricLabels.Tag("result", kv.Key.Item2)));
    }

    private IEnumerable<Measurement<int>> ObserveMismatch(Func<(int Compared, int Differing), int> select)
    {
        Dictionary<string, (int Compared, int Differing)> snapshot;
        lock (_gate)
        {
            snapshot = _mismatch;
        }

        return snapshot.Select(kv => new Measurement<int>(select(kv.Value), MetricLabels.Tag("resolver", kv.Key)));
    }
}
