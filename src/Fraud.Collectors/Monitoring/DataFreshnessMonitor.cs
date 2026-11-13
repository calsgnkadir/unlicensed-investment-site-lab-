using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Fraud.Infrastructure.Observability;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.Monitoring;

/// <summary>Tazeliği ölçülen tablolar (log mesajında serbest metin yerine).</summary>
public enum FreshnessTable
{
    OfficialActions = 1,
    CollectionLog = 2,
    CtCandidates = 3,
    CtLogState = 4,
    DnsSnapshots = 5,
    IpAsnSnapshots = 6,
    PortSnapshots = 7,
}

/// <summary><c>Metrics:Freshness</c> bölümü. Yalnızca bir süreçte açılır (Linux kurulumunda <c>ct-follow</c>).</summary>
public sealed class FreshnessOptions
{
    public const string SectionName = "Metrics:Freshness";

    public bool Enabled { get; set; }

    /// <summary>İki veritabanı okuması arasındaki süre.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Veri tazeliği: Her tablonun en yeni satırının yaşı (<c>fraud_data_newest_row_age_seconds{table}</c>).
/// Veritabanı dakikada bir okunur; yaş, Prometheus her okuduğunda "şimdi - en yeni satır zamanı" olarak hesaplanır.
/// </summary>
/// <remarks>
/// <para>
/// Sorgular ucuz olacak şekilde seçildi: Büyük tablolarda (<c>ct_candidates</c>, <c>collection_log</c>,
/// <c>dns_snapshots</c>) zaman sütununda indeks yok; <c>max(observed_at)</c> bütün tabloyu tarardı. Bunun yerine birincil
/// anahtarın en büyüğü (en son eklenen satır) okunur: <c>ORDER BY id DESC LIMIT 1</c> indeksten tek satır getirir.
/// Kimlik sırayla verildiği için en son eklenen satır, en yeni satırla aynıdır (DNS'te sonradan güncellenen hata satırı
/// küçük bir sapma yaratabilir). Küçük tablolarda ve indeksli sütunlarda <c>max()</c> kullanılır.
/// </para>
/// <para>Yalnızca zaman okunur; hiçbir ad, IP veya cevap okunmaz. Tablo adları sabit listedir (etiket izin listesinde).</para>
/// </remarks>
public sealed partial class DataFreshnessMonitor : BackgroundService
{
    public const string MeterName = "Fraud.Collectors.Freshness";

    /// <summary>Tablo, metrik etiketi (tablo adı) ve sabit sorgusu. Sorgu metni koddadır; kullanıcı girdisi yoktur.</summary>
    public static readonly IReadOnlyList<(FreshnessTable Table, string Label, string Sql)> Queries =
    [
        (FreshnessTable.OfficialActions, "official_actions", """SELECT max(observed_at) AS "Value" FROM official_actions"""),
        (FreshnessTable.CollectionLog, "collection_log", """SELECT finished_at AS "Value" FROM collection_log ORDER BY id DESC LIMIT 1"""),
        (FreshnessTable.CtCandidates, "ct_candidates", """SELECT observed_at AS "Value" FROM ct_candidates ORDER BY id DESC LIMIT 1"""),
        (FreshnessTable.CtLogState, "ct_log_state", """SELECT max(updated_at) AS "Value" FROM ct_log_state"""),
        (FreshnessTable.DnsSnapshots, "dns_snapshots", """SELECT queried_at AS "Value" FROM dns_snapshots ORDER BY id DESC LIMIT 1"""),
        (FreshnessTable.IpAsnSnapshots, "ip_asn_snapshots", """SELECT max(queried_at) AS "Value" FROM ip_asn_snapshots"""),
        (FreshnessTable.PortSnapshots, "port_snapshots", """SELECT max(queried_at) AS "Value" FROM port_snapshots"""),
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FreshnessOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataFreshnessMonitor> _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _newest = new(StringComparer.Ordinal);
    private readonly Counter<long> _errors;
    private DateTimeOffset? _lastCheck;

    public DataFreshnessMonitor(
        IServiceScopeFactory scopeFactory,
        FreshnessOptions options,
        TimeProvider timeProvider,
        ILogger<DataFreshnessMonitor> logger,
        IMeterFactory? meterFactory = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _ownsMeter = meterFactory is null;
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _errors = _meter.CreateCounter<long>("fraud_data_freshness_query_errors", null, "Okunamayan tazelik sorgusu");
        _meter.CreateObservableGauge("fraud_data_newest_row_age_seconds", ObserveAge, "s",
            "Tablodaki en yeni satırın yaşı (şimdi - satırın gözlem zamanı). Tablo boşsa seri yok.");
        _meter.CreateObservableGauge("fraud_data_freshness_last_check_timestamp_seconds", ObserveLastCheck, "s",
            "Tazelik sorgularının son çalıştığı an (Unix saniye)");
    }

    /// <summary>Bütün tabloları bir kez okur. Bir tablonun hatası diğerlerini durdurmaz.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "İzleme sorgusu hatası toplayıcıyı durdurmamalı; yalnızca tablo adı (sabit) ve istisna türü loglanır.")]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(10));
        foreach (var (table, label, sql) in Queries)
        {
            try
            {
                var value = await db.Database.SqlQueryRaw<DateTimeOffset?>(sql).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (value is { } v)
                {
                    _newest[label] = v;
                }
                else
                {
                    _newest.TryRemove(label, out _);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Eski değer kalır; yaşı büyümeye devam eder (bilinmeyen tazelik "taze" gösterilmez).
                _errors.Add(1, MetricLabels.Tag("table", label));
                FreshnessLog.QueryFailed(_logger, table, ex.HResult);
            }
        }

        _lastCheck = _timeProvider.GetUtcNow();
    }

    public override void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }

        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(_options.Interval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private IEnumerable<Measurement<double>> ObserveAge()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (table, newest) in _newest)
        {
            yield return new Measurement<double>(Math.Max(0, (now - newest).TotalSeconds), MetricLabels.Tag("table", table));
        }
    }

    private IEnumerable<Measurement<double>> ObserveLastCheck()
    {
        if (_lastCheck is { } t)
        {
            yield return new Measurement<double>(t.ToUnixTimeMilliseconds() / 1000.0);
        }
    }

    private static partial class FreshnessLog
    {
        // Serbest metin yok (LogSurfaceTests): tablo enum, hata yalnızca HResult sayısı.
        [LoggerMessage(EventId = 900, Level = LogLevel.Warning, Message = "Tazelik sorgusu okunamadı: tablo {Table}, HResult {HResult}")]
        public static partial void QueryFailed(ILogger logger, FreshnessTable table, int hresult);
    }
}
