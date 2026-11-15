using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Observability;

namespace Fraud.Collectors.StaticCt;

/// <summary>
/// Static CT izleme metrikleri (<see cref="Meter"/> adı <see cref="MeterName"/>). Prometheus'a <c>ct-follow</c>
/// sürecinin iç portundan (9464) çıkar.
/// </summary>
/// <remarks>
/// <para>
/// Tek etiket <c>log</c>: yapılandırmadaki log kısa adı (ör. <c>sycamore2027h1</c>); hata sayacında ek olarak
/// <c>error_kind</c>. Etiketler <see cref="MetricLabels"/> ile kurulur; alan adı, URL veya sertifika alanı hiçbir
/// etikete girmez (Kural 4).
/// </para>
/// <para>
/// Adlar Prometheus biçimindedir ve birim adın sonundadır. Dışa aktarıcı sayaçlara <c>_total</c> ekler
/// (ör. <c>fraud_ct_entries_processed</c> → <c>fraud_ct_entries_processed_total</c>).
/// </para>
/// <para>Kapsama oranı = işlenen / (işlenen + atlanan); iki sayaçtan hesaplanır.</para>
/// </remarks>
public sealed class StaticCtMetrics : IDisposable
{
    public const string MeterName = "Fraud.Collectors.StaticCt";

    /// <summary>Tile indirme süresi kovaları (saniye). Normal tile ~0,1-1 sn; tekrar denemeli istek dakikaları bulabilir.</summary>
    private static readonly double[] FetchBuckets = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120];

    /// <summary>Kaydın loga girişi ile işlenmesi arasındaki süre kovaları (saniye).</summary>
    private static readonly double[] LagBuckets = [10, 30, 60, 120, 300, 600, 1800, 3600, 7200, 21600, 86400];

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, LogGauges> _gauges = new(StringComparer.Ordinal);
    private readonly Counter<long> _processed;
    private readonly Counter<long> _skipped;
    private readonly Counter<long> _candidates;
    private readonly Counter<long> _neutralSample;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _bytes;
    private readonly Histogram<double> _entryLag;
    private readonly Histogram<double> _tileFetch;

    public StaticCtMetrics(IMeterFactory? meterFactory = null, TimeProvider? timeProvider = null)
    {
        _ownsMeter = meterFactory is null;
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _timeProvider = timeProvider ?? TimeProvider.System;

        _processed = _meter.CreateCounter<long>("fraud_ct_entries_processed", null, "Sertifikası okunup filtreden geçirilen kayıt");
        _skipped = _meter.CreateCounter<long>("fraud_ct_entries_skipped", null, "Okunamayan, indirilemeyen veya doğrulanamayan kayıt");
        _candidates = _meter.CreateCounter<long>("fraud_ct_candidates", null, "ct_candidates tablosuna ilk kez yazılan aday");
        _neutralSample = _meter.CreateCounter<long>("fraud_ct_neutral_sample", null,
            "Tarafsız örneğe (ct_neutral_sample) ilk kez giren kayıtlı alan adı. Alan adı etikete girmez, yalnızca sayı.");
        _errors = _meter.CreateCounter<long>("fraud_ct_errors", null, "Hatayla biten tur (hata türüne göre)");
        _retries = _meter.CreateCounter<long>("fraud_ct_http_retries", null, "Tekrar denenen HTTP isteği (ilk deneme sayılmaz)");
        _bytes = _meter.CreateCounter<long>("fraud_ct_http_body_bytes", "By",
            "Okunan yanıt gövdesi (HTTP katmanında açılmış bayt; ağdaki gzip baytı daha azdır)");
        _entryLag = _meter.CreateHistogram("fraud_ct_entry_lag_seconds", "s",
            "Tile'daki en yeni kaydın loga girişi ile işlenmesi arasındaki süre",
            tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = LagBuckets });
        _tileFetch = _meter.CreateHistogram("fraud_ct_tile_fetch_duration_seconds", "s",
            "Bir veri tile'ının indirilme süresi (tekrar denemeler ve hız sınırı beklemesi dahil)",
            tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = FetchBuckets });

        _meter.CreateObservableGauge("fraud_ct_checkpoint_size", () => Observe(g => g.CheckpointSize), null,
            "Son okunan imzalı checkpoint'in boyutu (kayıt)");
        _meter.CreateObservableGauge("fraud_ct_next_index", () => Observe(g => g.NextIndex), null,
            "Sıradaki işlenecek kayıt indeksi");
        _meter.CreateObservableGauge("fraud_ct_lag_entries", () => Observe(g => g.CheckpointSize is { } size && g.NextIndex is { } next ? size - next : null), null,
            "Geride kalan kayıt: checkpoint boyutu - sıradaki indeks");
        _meter.CreateObservableGauge("fraud_ct_lag_seconds", ObserveLagSeconds, "s",
            "Tahmini gecikme: şimdi - işlenen en yeni kaydın log zamanı (yoksa son ilerlemeden bu yana geçen süre)");
        _meter.CreateObservableGauge("fraud_ct_last_success_timestamp_seconds", () => ObserveTime(g => g.LastSuccess), "s",
            "Son hatasız turun bittiği an (Unix saniye). İlerleme olmasa da (yetişmiş) tur başarılı sayılır.");
        _meter.CreateObservableGauge("fraud_ct_last_progress_timestamp_seconds", () => ObserveTime(g => g.LastProgress), "s",
            "Son yazılan aralığın (indeks ilerledi) zamanı (Unix saniye)");
        _meter.CreateObservableGauge("fraud_ct_follower_started_timestamp_seconds", () => ObserveTime(g => g.FollowerStarted), "s",
            "Bu logun izleme döngüsünün başladığı an (Unix saniye). Her etkin log için tur bitmeden de vardır; "
            + "hiç başarılı tur bitirmeyen logu uyarının görebilmesi için (M4).");
    }

    public void Processed(string log, long examined, long skipped, long newCandidates)
    {
        var tag = LogTag(log);
        _processed.Add(examined, tag);
        _skipped.Add(skipped, tag);
        _candidates.Add(newCandidates, tag);
    }

    /// <summary>Tarafsız örneğe ilk kez giren alan adı sayısı (dışa aktarılan ad: fraud_ct_neutral_sample_total).</summary>
    public void NeutralSampled(string log, long newDomains) => _neutralSample.Add(newDomains, LogTag(log));

    public void Skipped(string log, long entries) => _skipped.Add(entries, LogTag(log));

    public void Failure(string log, CollectionErrorKind kind) =>
        _errors.Add(1, LogTag(log), MetricLabels.Tag("error_kind", kind));

    public void Lag(string log, TimeSpan lag) => _entryLag.Record(Math.Max(0, lag.TotalSeconds), LogTag(log));

    /// <summary>Son checkpoint ve sıradaki indeks. Geride kalan kayıt bu ikisinden hesaplanır.</summary>
    public void Position(string log, long checkpointSize, long nextIndex)
    {
        var g = Gauges(log);
        lock (g)
        {
            g.CheckpointSize = checkpointSize;
            g.NextIndex = nextIndex;
        }
    }

    /// <summary>Etkin log için izleme döngüsü başladı (seri, ilk tur bitmeden oluşur).</summary>
    public void FollowerStarted(string log)
    {
        var g = Gauges(log);
        lock (g)
        {
            g.FollowerStarted = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>Tur hatasız bitti. <paramref name="advanced"/>: indeks ilerledi; <paramref name="newestEntry"/>: işlenen en yeni kaydın log zamanı.</summary>
    public void BatchSucceeded(string log, bool advanced, DateTimeOffset? newestEntry)
    {
        var now = _timeProvider.GetUtcNow();
        var g = Gauges(log);
        lock (g)
        {
            g.LastSuccess = now;
            if (advanced)
            {
                g.LastProgress = now;
            }

            if (newestEntry is { } newest && (g.NewestEntry is null || newest > g.NewestEntry))
            {
                g.NewestEntry = newest;
            }
        }
    }

    /// <summary>Bir veri tile'ının indirilme süresi.</summary>
    public void TileFetched(string log, TimeSpan elapsed) => _tileFetch.Record(Math.Max(0, elapsed.TotalSeconds), LogTag(log));

    /// <summary>Bu log için HTTP gözlemcisi (tekrar deneme ve okunan bayt).</summary>
    public IHttpFetchObserver HttpObserver(string log) => new LogHttpObserver(this, LogTag(log));

    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private static KeyValuePair<string, object?> LogTag(string log) => MetricLabels.Tag("log", log);

    private LogGauges Gauges(string log) => _gauges.GetOrAdd(log, _ => new LogGauges());

    private IEnumerable<Measurement<long>> Observe(Func<LogGauges, long?> select)
    {
        foreach (var (log, g) in _gauges)
        {
            long? value;
            lock (g)
            {
                value = select(g);
            }

            if (value is { } v)
            {
                yield return new Measurement<long>(v, LogTag(log));
            }
        }
    }

    private IEnumerable<Measurement<double>> ObserveTime(Func<LogGauges, DateTimeOffset?> select)
    {
        foreach (var (log, g) in _gauges)
        {
            DateTimeOffset? value;
            lock (g)
            {
                value = select(g);
            }

            if (value is { } v)
            {
                yield return new Measurement<double>(v.ToUnixTimeMilliseconds() / 1000.0, LogTag(log));
            }
        }
    }

    private IEnumerable<Measurement<double>> ObserveLagSeconds()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (log, g) in _gauges)
        {
            DateTimeOffset? reference;
            lock (g)
            {
                reference = g.NewestEntry ?? g.LastProgress;
            }

            if (reference is { } r)
            {
                yield return new Measurement<double>(Math.Max(0, (now - r).TotalSeconds), LogTag(log));
            }
        }
    }

    private sealed class LogGauges
    {
        public long? CheckpointSize { get; set; }

        public long? NextIndex { get; set; }

        public DateTimeOffset? LastSuccess { get; set; }

        public DateTimeOffset? LastProgress { get; set; }

        public DateTimeOffset? NewestEntry { get; set; }

        public DateTimeOffset? FollowerStarted { get; set; }
    }

    private sealed class LogHttpObserver(StaticCtMetrics owner, KeyValuePair<string, object?> tag) : IHttpFetchObserver
    {
        public void Fetched(HttpFetchResult result, TimeSpan elapsed)
        {
            if (result.Attempts > 1)
            {
                owner._retries.Add(result.Attempts - 1, tag);
            }

            if (result.Body is { Length: > 0 } body)
            {
                owner._bytes.Add(body.Length, tag);
            }
        }
    }
}
