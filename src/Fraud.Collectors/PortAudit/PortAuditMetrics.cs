using System.Diagnostics.Metrics;
using Fraud.Infrastructure.Observability;

namespace Fraud.Collectors.PortAudit;

/// <summary>
/// Port denetimi metrikleri. Etiketler: protokol, port numarası, bağlanma kapsamı (loopback/specific/wildcard).
/// Port numarası etiket olabilir çünkü kendi makinemizdir; adres, süreç adı ve kullanıcı etikete girmez.
/// </summary>
/// <remarks>
/// Bütün göstergeler tek bir <see cref="PortAuditSnapshot"/>'tan okunur. port-audit süreci özeti denetimden kurar
/// (<see cref="Completed"/>), port-audit-exporter süreci dosyadan alır (<see cref="Apply"/>); iki yol aynı çıktıyı verir.
/// </remarks>
public sealed class PortAuditMetrics : IDisposable
{
    public const string MeterName = "Fraud.Collectors.PortAudit";

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly Counter<long> _stateReadErrors;
    private readonly Lock _gate = new();
    private PortAuditSnapshot? _snapshot;

    public PortAuditMetrics(IMeterFactory? meterFactory = null)
    {
        _ownsMeter = meterFactory is null;
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _stateReadErrors = _meter.CreateCounter<long>("port_audit_state_read_errors", null,
            "Okunamayan durum dosyası (port-audit-exporter)");

        _meter.CreateObservableCounter("port_audit_errors", () => Value(s => s.Errors, requireCompleted: false), null,
            "Tamamlanamayan denetim (dosya okunamadı veya liste geçersiz)");
        _meter.CreateObservableGauge("port_audit_unexpected_listeners", () => Value(s => s.Unexpected.Count), null,
            "Beklenen listede olmayan dinleyici sayısı (protokol, port ve kapsam bir kez sayılır)");
        _meter.CreateObservableGauge("port_audit_expected_missing", () => Value(s => s.Missing.Count), null,
            "Beklenen listede olup dinlemeyen tek portlu kayıt sayısı");
        _meter.CreateObservableGauge("port_audit_listeners", ObserveListeners, null, "Dinleyen soket sayısı (protokole göre)");
        _meter.CreateObservableGauge("port_audit_unexpected_listener", ObserveUnexpected, null,
            "Beklenmeyen dinleyici (değer her zaman 1; protokol, port ve kapsam etikette)");
        _meter.CreateObservableGauge("port_audit_expected_listener_missing", ObserveMissing, null,
            "Dinlemeyen beklenen kayıt (değer her zaman 1)");
        _meter.CreateObservableGauge("port_audit_malformed_lines", () => Value(s => s.MalformedLines), null,
            "Son denetimde ayrıştırılamayan soket tablosu satırı");
        _meter.CreateObservableGauge("port_audit_expected_entries", () => Value(s => s.ExpectedEntries), null,
            "Beklenen listedeki kayıt sayısı");
        _meter.CreateObservableGauge("port_audit_last_run_timestamp_seconds", ObserveLastRun, "s",
            "Son başarılı denetimin zamanı (Unix saniye). Okuma hatasında güncellenmez.");
        _meter.CreateObservableGauge("fraud_disk_available_bytes", () => Disk(s => s.DiskAvailable), "By",
            "Docker veri diskinde yetkisiz kullanıcının kullanabileceği boş alan");
        _meter.CreateObservableGauge("fraud_disk_size_bytes", () => Disk(s => s.DiskTotal), "By", "Docker veri diskinin boyutu");
    }

    /// <summary>Şu anki özet (durum dosyasına yazılmak için).</summary>
    public PortAuditSnapshot? Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Completed(PortAuditResult result, int expectedEntries, DateTimeOffset at)
    {
        lock (_gate)
        {
            _snapshot = PortAuditSnapshot.From(result, expectedEntries, at, _snapshot);
        }
    }

    public void Failed()
    {
        lock (_gate)
        {
            _snapshot = (_snapshot ?? new PortAuditSnapshot()) with { Errors = (_snapshot?.Errors ?? 0) + 1 };
        }
    }

    /// <summary>Disk ölçümü (denetimden bağımsız; ölçülemezse eski değer silinir).</summary>
    public void Disk(long? available, long? total)
    {
        lock (_gate)
        {
            var ok = available is not null && total is not null;
            _snapshot = (_snapshot ?? new PortAuditSnapshot()) with
            {
                DiskAvailable = ok ? available : null,
                DiskTotal = ok ? total : null,
            };
        }
    }

    /// <summary>Dosyadan okunan özeti uygular (port-audit-exporter).</summary>
    public void Apply(PortAuditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var clean = snapshot.Sanitized();
        lock (_gate)
        {
            _snapshot = clean;
        }
    }

    public void StateReadFailed() => _stateReadErrors.Add(1);

    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private IEnumerable<Measurement<long>> Value(Func<PortAuditSnapshot, long> select, bool requireCompleted = true)
    {
        if (Snapshot is { } s && (!requireCompleted || s.CompletedAt is not null))
        {
            yield return new Measurement<long>(select(s));
        }
    }

    private IEnumerable<Measurement<long>> Disk(Func<PortAuditSnapshot, long?> select)
    {
        if (Snapshot is { } s && select(s) is { } v)
        {
            yield return new Measurement<long>(v);
        }
    }

    private IEnumerable<Measurement<int>> ObserveListeners()
    {
        if (Snapshot is not { CompletedAt: not null } s)
        {
            yield break;
        }

        yield return new Measurement<int>(s.TcpListeners, ProtocolTag(ListenerProtocol.Tcp));
        yield return new Measurement<int>(s.UdpListeners, ProtocolTag(ListenerProtocol.Udp));
    }

    private IEnumerable<Measurement<int>> ObserveUnexpected()
    {
        if (Snapshot is not { CompletedAt: not null } s)
        {
            yield break;
        }

        foreach (var u in s.Unexpected)
        {
            yield return new Measurement<int>(1, ProtocolTag(u.Protocol), MetricLabels.Port(u.Port), ScopeTag(u.Scope ?? BindScope.Wildcard));
        }
    }

    private IEnumerable<Measurement<int>> ObserveMissing()
    {
        if (Snapshot is not { CompletedAt: not null } s)
        {
            yield break;
        }

        foreach (var m in s.Missing)
        {
            yield return new Measurement<int>(1, ProtocolTag(m.Protocol), MetricLabels.Port(m.Port));
        }
    }

    private IEnumerable<Measurement<double>> ObserveLastRun()
    {
        if (Snapshot is { CompletedAt: { } t })
        {
            yield return new Measurement<double>(t.ToUnixTimeMilliseconds() / 1000.0);
        }
    }

    private static KeyValuePair<string, object?> ProtocolTag(ListenerProtocol protocol) =>
        MetricLabels.Tag("protocol", protocol == ListenerProtocol.Tcp ? "tcp" : "udp");

    private static KeyValuePair<string, object?> ScopeTag(BindScope scope) => MetricLabels.Tag("bind", scope switch
    {
        BindScope.Loopback => "loopback",
        BindScope.Specific => "specific",
        _ => "wildcard",
    });
}
