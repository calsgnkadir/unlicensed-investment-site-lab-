using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Fraud.Collectors.StaticCt;

/// <summary>
/// Static CT kapsama metrikleri (<see cref="Meter"/> adı <see cref="MeterName"/>). Prometheus'a aktarım Hafta 6'da bağlanacak.
/// </summary>
/// <remarks>
/// Tek etiket <c>log</c>: yapılandırmadaki log adı (ör. <c>sycamore2026h2</c>). Alan adı hiçbir etikete girmez (Kural 4).
/// Kapsama oranı = işlenen / (işlenen + atlanan); iki sayaçtan hesaplanır.
/// </remarks>
public sealed class StaticCtMetrics : IDisposable
{
    public const string MeterName = "Fraud.Collectors.StaticCt";

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly ConcurrentDictionary<string, long> _backlog = new(StringComparer.Ordinal);
    private readonly Counter<long> _processed;
    private readonly Counter<long> _skipped;
    private readonly Counter<long> _candidates;
    private readonly Counter<long> _failures;
    private readonly Histogram<double> _lag;

    public StaticCtMetrics(IMeterFactory? meterFactory = null)
    {
        _ownsMeter = meterFactory is null;
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _processed = _meter.CreateCounter<long>("fraud.ct.entries.processed", "{entry}", "Sertifikası okunup filtreden geçirilen kayıt");
        _skipped = _meter.CreateCounter<long>("fraud.ct.entries.skipped", "{entry}", "Okunamayan, indirilemeyen veya doğrulanamayan kayıt");
        _candidates = _meter.CreateCounter<long>("fraud.ct.candidates.stored", "{entry}", "ct_candidates tablosuna ilk kez yazılan kayıt");
        _failures = _meter.CreateCounter<long>("fraud.ct.range.failures", "{range}", "İşlenemeyen aralık (hata türüne göre)");
        _lag = _meter.CreateHistogram<double>("fraud.ct.entry.lag", "s", "Kaydın loga girişi ile işlenmesi arasındaki süre (tile'daki en yeni kayıt)");
        _meter.CreateObservableGauge("fraud.ct.backlog", ObserveBacklog, "{entry}", "Son checkpoint ile sıradaki indeks arasındaki kayıt sayısı");
    }

    public void Processed(string log, long examined, long skipped, long newCandidates)
    {
        var tag = new KeyValuePair<string, object?>("log", log);
        _processed.Add(examined, tag);
        _skipped.Add(skipped, tag);
        _candidates.Add(newCandidates, tag);
    }

    public void Skipped(string log, long entries) => _skipped.Add(entries, new KeyValuePair<string, object?>("log", log));

    public void Failure(string log, Fraud.Domain.Collection.CollectionErrorKind kind) =>
        _failures.Add(1, new KeyValuePair<string, object?>("log", log), new KeyValuePair<string, object?>("error_kind", kind.ToString()));

    public void Lag(string log, TimeSpan lag) => _lag.Record(Math.Max(0, lag.TotalSeconds), new KeyValuePair<string, object?>("log", log));

    public void Backlog(string log, long entries) => _backlog[log] = entries;

    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private IEnumerable<Measurement<long>> ObserveBacklog() =>
        _backlog.Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("log", kv.Key)));
}
