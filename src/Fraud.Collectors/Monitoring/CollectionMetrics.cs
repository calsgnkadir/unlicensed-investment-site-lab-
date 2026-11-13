using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Observability;

namespace Fraud.Collectors.Monitoring;

/// <summary>
/// Kaynak başına toplama metrikleri (crt.sh, Wayback, DNS, RIPEstat, InternetDB). Etiketler yalnızca kaynak adı,
/// sonuç ve hata türüdür (sabit enum); alan adı ve IP hiçbir etikete girmez.
/// </summary>
/// <remarks>
/// Tur sonucu (<c>outcome</c> etiketi, <c>fraud_collection_runs_total</c>):
/// <list type="bullet">
/// <item><c>Completed</c>: Tur sonuna kadar gitti ve hiç hata yok (bütün hedefler önbellekte taze olsa da).</item>
/// <item><c>NoTargets</c>: Sorulacak hiç hedef yoktu (ör. official_actions boş, son günlerde DNS cevabı yok). Başarı
/// sayılmaz: Aksi halde hedef tablosu boşalınca her tur "başarılı" görünür ve tazelik uyarısı hiç çıkmazdı (M4).</item>
/// <item><c>Partial</c>: Hata var ama en az bir hedef cevap aldı.</item>
/// <item><c>Failed</c>: Hata var ve hiçbir hedef cevap almadı.</item>
/// <item><c>Cancelled</c>: Süreç durdu ya da süreçler arası kilit kaybedildi.</item>
/// <item><c>LockedOut</c>: Kaynağı başka bir süreç topluyordu; hiç sorgu atılmadı.</item>
/// <item><c>Crashed</c>: Tur beklenmeyen istisnayla bitti.</item>
/// </list>
/// "Son başarılı tur" zamanı Completed ve Partial turlarda güncellenir: En az bir cevap alındıysa veri tazelendi demektir.
/// Failed ve NoTargets tur başarı sayılmaz; böylece "DNS 26 saattir başarılı değil" uyarısı, hep hata alan turlarda da çalışır.
/// </remarks>
public sealed class CollectionMetrics : IDisposable
{
    public const string MeterName = "Fraud.Collectors.Collection";

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<CollectionSource, SourceGauges> _gauges = new();
    private readonly Counter<long> _runs;
    private readonly Counter<long> _targets;
    private readonly Counter<long> _newItems;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _retries;

    public CollectionMetrics(IMeterFactory? meterFactory = null, TimeProvider? timeProvider = null)
    {
        _ownsMeter = meterFactory is null;
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _timeProvider = timeProvider ?? TimeProvider.System;

        _runs = _meter.CreateCounter<long>("fraud_collection_runs", null, "Biten toplama turu (sonuca göre)");
        _targets = _meter.CreateCounter<long>("fraud_collection_targets", null,
            "Turda işlenen hedef (alan adı veya IP) sonuca göre; SkippedFresh = önbellekte taze, sorulmadı");
        _newItems = _meter.CreateCounter<long>("fraud_collection_new_items", null, "İlk kez yazılan kayıt");
        _errors = _meter.CreateCounter<long>("fraud_collection_errors", null, "Hata alan hedef (hata türüne göre)");
        _retries = _meter.CreateCounter<long>("fraud_collection_retries", null,
            "Tekrar deneme: HTTP kaynaklarında ilk istekten sonraki istekler, DNS'te ilk paketten sonraki paketler");

        _meter.CreateObservableGauge("fraud_collection_last_run_timestamp_seconds", () => Observe(g => g.LastRun), "s",
            "Son turun bittiği an (Unix saniye), sonuçtan bağımsız");
        _meter.CreateObservableGauge("fraud_collection_last_success_timestamp_seconds", () => Observe(g => g.LastSuccess), "s",
            "Son Completed veya Partial turun bittiği an (Unix saniye)");
        _meter.CreateObservableGauge("fraud_collection_last_run_duration_seconds", ObserveDuration, "s",
            "Son turun süresi");
    }

    /// <summary>Tur başladı (süre ölçümü için).</summary>
    public void RunStarted(CollectionSource source)
    {
        var g = _gauges.GetOrAdd(source, _ => new SourceGauges());
        lock (g)
        {
            g.Started = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>Tur bitti; özetten tur sonucu, hedef sayıları ve zamanlar yazılır.</summary>
    public void RunFinished(CollectionRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var outcome = Classify(summary);
        RecordRun(summary.Source, outcome);
        if (outcome is "LockedOut")
        {
            return;
        }

        var source = SourceTag(summary.Source);
        Add(_targets, summary.Succeeded, source, MetricLabels.Tag("outcome", nameof(CollectionOutcome.Success)));
        Add(_targets, summary.Empty, source, MetricLabels.Tag("outcome", nameof(CollectionOutcome.Empty)));
        Add(_targets, summary.Failed, source, MetricLabels.Tag("outcome", nameof(CollectionOutcome.Error)));
        Add(_targets, summary.SkippedFresh, source, MetricLabels.Tag("outcome", "SkippedFresh"));
        Add(_newItems, summary.NewItems, source);
    }

    /// <summary>Tur istisnayla bitti.</summary>
    public void RunCrashed(CollectionSource source) => RecordRun(source, "Crashed");

    /// <summary>Bir hedef hata aldı.</summary>
    public void TargetFailed(CollectionSource source, CollectionErrorKind kind) =>
        _errors.Add(1, SourceTag(source), MetricLabels.Tag("error_kind", kind));

    /// <summary>Tekrar deneme (HTTP isteği veya DNS paketi).</summary>
    public void Retried(CollectionSource source, int count)
    {
        if (count > 0)
        {
            _retries.Add(count, SourceTag(source));
        }
    }

    /// <summary>Bu kaynağın HTTP istemcisi için gözlemci (tekrar denemeleri sayar).</summary>
    public IHttpFetchObserver HttpObserver(CollectionSource source) => new SourceHttpObserver(this, source);

    /// <summary>Tur sonucu sınıfı. Kural sınıf açıklamasında.</summary>
    public static string Classify(CollectionRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.LockedOut)
        {
            return "LockedOut";
        }

        if (summary.Cancelled)
        {
            return "Cancelled";
        }

        if (summary.Targets == 0)
        {
            return "NoTargets";
        }

        if (summary.Failed == 0)
        {
            return "Completed";
        }

        return summary.Succeeded + summary.Empty > 0 ? "Partial" : "Failed";
    }

    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private void RecordRun(CollectionSource source, string outcome)
    {
        _runs.Add(1, SourceTag(source), MetricLabels.Tag("outcome", outcome));
        if (outcome is "LockedOut")
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var g = _gauges.GetOrAdd(source, _ => new SourceGauges());
        lock (g)
        {
            g.LastRun = now;
            g.LastDuration = g.Started is { } started ? now - started : null;
            if (outcome is "Completed" or "Partial")
            {
                g.LastSuccess = now;
            }
        }
    }

    private static void Add(Counter<long> counter, long value, params ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (value > 0)
        {
            counter.Add(value, tags);
        }
    }

    private static KeyValuePair<string, object?> SourceTag(CollectionSource source) => MetricLabels.Tag("source", source);

    private IEnumerable<Measurement<double>> Observe(Func<SourceGauges, DateTimeOffset?> select)
    {
        foreach (var (source, g) in _gauges)
        {
            DateTimeOffset? value;
            lock (g)
            {
                value = select(g);
            }

            if (value is { } v)
            {
                yield return new Measurement<double>(v.ToUnixTimeMilliseconds() / 1000.0, SourceTag(source));
            }
        }
    }

    private IEnumerable<Measurement<double>> ObserveDuration()
    {
        foreach (var (source, g) in _gauges)
        {
            TimeSpan? value;
            lock (g)
            {
                value = g.LastDuration;
            }

            if (value is { } v)
            {
                yield return new Measurement<double>(Math.Max(0, v.TotalSeconds), SourceTag(source));
            }
        }
    }

    private sealed class SourceGauges
    {
        public DateTimeOffset? Started { get; set; }

        public DateTimeOffset? LastRun { get; set; }

        public DateTimeOffset? LastSuccess { get; set; }

        public TimeSpan? LastDuration { get; set; }
    }

    private sealed class SourceHttpObserver(CollectionMetrics owner, CollectionSource source) : IHttpFetchObserver
    {
        public void Fetched(HttpFetchResult result, TimeSpan elapsed) => owner.Retried(source, result.Attempts - 1);
    }
}
