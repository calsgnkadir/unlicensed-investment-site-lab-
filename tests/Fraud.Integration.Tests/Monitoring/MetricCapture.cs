using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Integration.Tests.Monitoring;

/// <summary>
/// Testin kendi <see cref="IMeterFactory"/>'si ve yalnızca o fabrikanın meter'larını dinleyen bir dinleyici.
/// Paralel çalışan başka testlerin aynı adlı meter'ları karışmaz (filtre meter adına değil, fabrikaya göre).
/// </summary>
internal sealed class MetricCapture : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, double> _sums = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> _last = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    public MetricCapture()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        Factory = _services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, Factory))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        _listener.Start();
    }

    public IMeterFactory Factory { get; }

    /// <summary>Gözlemlenen göstergeleri şimdi okut (Prometheus okuması gibi).</summary>
    public void Observe()
    {
        _last.Clear();
        _listener.RecordObservableInstruments();
    }

    /// <summary>Sayaç toplamı (etiket filtresiyle). Hiç ölçüm yoksa 0.</summary>
    public double Sum(string instrument, params string[] tags) =>
        _sums.Where(kv => Matches(kv.Key, instrument, tags)).Sum(kv => kv.Value);

    /// <summary>Histogram ölçüm sayısı.</summary>
    public int Count(string instrument, params string[] tags) =>
        _counts.Where(kv => Matches(kv.Key, instrument, tags)).Sum(kv => kv.Value);

    /// <summary>Göstergenin son değeri (<see cref="Observe"/> sonrası). Yoksa <c>null</c>.</summary>
    public double? Last(string instrument, params string[] tags)
    {
        var hits = _last.Where(kv => Matches(kv.Key, instrument, tags)).ToList();
        return hits.Count == 0 ? null : hits.Sum(kv => kv.Value);
    }

    /// <summary>Görülen bütün etiketler ("anahtar=değer").</summary>
    public IReadOnlyCollection<string> AllTags() =>
        _sums.Keys.Concat(_last.Keys).SelectMany(k => k.Split('|').Skip(1)).Distinct().ToList();

    public void Dispose()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var parts = new List<string>(tags.Length);
        foreach (var t in tags)
        {
            parts.Add(t.Key + "=" + Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        parts.Sort(StringComparer.Ordinal);
        var key = instrument.Name + "|" + string.Join('|', parts);
        if (instrument.GetType().Name.StartsWith("Observable", StringComparison.Ordinal))
        {
            _last[key] = value;
        }
        else
        {
            _sums.AddOrUpdate(key, value, (_, old) => old + value);
            _counts.AddOrUpdate(key, 1, (_, old) => old + 1);
        }
    }

    private static bool Matches(string key, string instrument, string[] tags)
    {
        var parts = key.Split('|');
        return parts[0] == instrument && tags.All(t => parts.Skip(1).Contains(t, StringComparer.Ordinal));
    }
}
