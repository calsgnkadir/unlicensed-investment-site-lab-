using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Collectors.Monitoring;
using Fraud.Collectors.Network;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Fraud.Integration.Tests.StaticCt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Monitoring;

// Metrik değerleri gerçek akışlardan sonra doğru mu: sahte CT logu ve sahte DNS istemcisiyle, gerçek PostgreSQL üzerinde.
[Trait("Category", "Integration")]
public sealed class CollectorMetricsTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);

    private readonly TestCtLog _log = new();
    private readonly MetricCapture _capture = new();

    public void Dispose()
    {
        _log.Dispose();
        _capture.Dispose();
    }

    [Fact]
    public async Task Ct_batch_sets_position_lag_progress_and_http_counters()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var metrics = new StaticCtMetrics(_capture.Factory);
        _log.AddEntries((3 * 256) + 40);
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };

        var result = await Follower(db, server, metrics).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Advanced, result.Status);
        _capture.Observe();
        const string log = "log=testlog";
        Assert.Equal(_log.Count, _capture.Last("fraud_ct_checkpoint_size", log));
        Assert.Equal(768, _capture.Last("fraud_ct_next_index", log));
        Assert.Equal(_log.Count - 768, _capture.Last("fraud_ct_lag_entries", log));
        Assert.Equal(768, _capture.Sum("fraud_ct_entries_processed", log));
        Assert.Equal(result.NewCandidates, _capture.Sum("fraud_ct_candidates", log));
        Assert.Equal(3, _capture.Count("fraud_ct_tile_fetch_duration_seconds", log));
        Assert.True(_capture.Sum("fraud_ct_http_body_bytes", log) > 0);
        Assert.NotNull(_capture.Last("fraud_ct_last_success_timestamp_seconds", log));
        Assert.NotNull(_capture.Last("fraud_ct_last_progress_timestamp_seconds", log));
        Assert.NotNull(_capture.Last("fraud_ct_lag_seconds", log));
        Assert.Equal(0, _capture.Sum("fraud_ct_errors"));
    }

    [Fact]
    public async Task Failing_ct_round_counts_the_error_kind_and_leaves_last_success_unset()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var metrics = new StaticCtMetrics(_capture.Factory);
        _log.AddEntries(300);
        using var server = new FakeCtServer(_log)
        {
            PublishedSize = _log.Count,
            Intercept = p => p == "checkpoint" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null,
        };

        var result = await Follower(db, server, metrics).RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Failed, result.Status);
        _capture.Observe();
        Assert.Equal(1, _capture.Sum("fraud_ct_errors", "log=testlog", "error_kind=ServerError"));
        Assert.Null(_capture.Last("fraud_ct_last_success_timestamp_seconds"));
    }

    [Fact]
    public async Task Dns_run_through_the_dispatcher_sets_run_and_last_run_metrics()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db, "ornek-a.example", "ornek-b.example");
        var time = new FakeTimeProvider(Start);
        using var collection = new CollectionMetrics(_capture.Factory, time);
        using var dns = new DnsMetrics(_capture.Factory);

        // ornek-a: UDP ve DoH aynı IP'yi verir. ornek-b: UDP zaman aşımı (2 paket), DoH SERVFAIL.
        var client = new FakeDnsClient((host, transport) => (host, transport) switch
        {
            ("ornek-a.example", _) => Ok("192.0.2.10"),
            ("ornek-b.example", DnsTransport.Udp) => new DnsQueryResult(
                DnsOutcome.Error, DnsErrorKind.Timeout, null, [], null, false, 2, TimeSpan.FromSeconds(10)),
            _ => new DnsQueryResult(DnsOutcome.Error, DnsErrorKind.ServerFailure, 2, [], null, false, 1, TimeSpan.FromMilliseconds(30)),
        });
        var options = new DnsOptions { Location = "test-lab" };
        options.Resolvers.Add("cloudflare");
        options.QueryTypes.Add(DnsRecordType.A);
        options.Transports.Add(DnsTransport.Udp);
        options.Transports.Add(DnsTransport.Doh);

        var runner = new CollectionRunner(
            db, [new DnsCollector(client, options, time, dns, collection)], new CollectionOptions { MeasurementLocation = "test-lab" },
            time, NullLogger<CollectionRunner>.Instance, collection);
        var ipRunner = new IpCollectionRunner(db, [], new CollectionOptions(), new IpTargetFilter(_ => true), time,
            NullLogger<IpCollectionRunner>.Instance, collection);
        var dispatcher = new CollectionDispatcher(runner, ipRunner, NetworkSourceLock.Unlocked, NullLogger<CollectionDispatcher>.Instance,
            collection, dns, new DnsCompareReportBuilder(db), options);

        var summary = await dispatcher.RunAsync(CollectionSource.Dns, force: false, ct);

        // ornek-a cevap aldı; ornek-b'nin iki sorgusu da hata (zaman aşımı ve SERVFAIL), bilinen cevap yok.
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        _capture.Observe();

        // Sorgular: 4 (2 alan adı x 2 yol). Sonuç ve hata türü etikette.
        Assert.Equal(4, _capture.Sum("fraud_dns_queries"));
        Assert.Equal(1, _capture.Sum("fraud_dns_queries", "transport=Udp", "outcome=Error", "error_kind=Timeout"));
        Assert.Equal(1, _capture.Sum("fraud_dns_queries", "transport=Doh", "outcome=Error", "error_kind=ServerFailure"));
        Assert.Equal(4, _capture.Count("fraud_dns_query_duration_seconds"));

        // Son tur: SERVFAIL ayrı sınıf, zaman aşımı "error". Uyarı yalnızca "error" oranına bakar.
        Assert.Equal(1, _capture.Last("fraud_dns_last_run_queries", "transport=Udp", "result=answer"));
        Assert.Equal(1, _capture.Last("fraud_dns_last_run_queries", "transport=Udp", "result=error"));
        Assert.Equal(1, _capture.Last("fraud_dns_last_run_queries", "transport=Doh", "result=answer"));
        Assert.Equal(1, _capture.Last("fraud_dns_last_run_queries", "transport=Doh", "result=servfail"));
        Assert.Null(_capture.Last("fraud_dns_last_run_queries", "transport=Doh", "result=error"));

        // Tekrar deneme: zaman aşımı 2 paket = 1 tekrar.
        Assert.Equal(1, _capture.Sum("fraud_collection_retries", "source=Dns"));

        // UDP-DoH karşılaştırması: yalnızca ornek-a'nın iki cevabı da var ve aynı.
        Assert.Equal(1, _capture.Last("fraud_dns_last_run_udp_doh_compared_domains", "resolver=cloudflare"));
        Assert.Equal(0, _capture.Last("fraud_dns_last_run_udp_doh_mismatch_domains", "resolver=cloudflare"));

        // Tur: hata var ama en az bir hedef cevap aldı -> Partial; son başarı zamanı güncellenir.
        Assert.Equal(1, _capture.Sum("fraud_collection_runs", "source=Dns", "outcome=Partial"));
        Assert.Equal(1, _capture.Sum("fraud_collection_errors", "source=Dns"));
        Assert.Equal(Start.ToUnixTimeSeconds(), _capture.Last("fraud_collection_last_success_timestamp_seconds", "source=Dns"));
        Assert.Equal(Start.ToUnixTimeSeconds(), _capture.Last("fraud_collection_last_run_timestamp_seconds", "source=Dns"));
    }

    [Fact]
    public void Run_outcome_classification()
    {
        static CollectionRunSummary S(int succeeded, int empty, int failed, bool cancelled = false, bool locked = false) =>
            new(CollectionSource.Dns, 10, 0, succeeded, empty, failed, 0, cancelled) { LockedOut = locked };

        Assert.Equal("Completed", CollectionMetrics.Classify(S(0, 0, 0)));   // hepsi önbellekte taze
        Assert.Equal("Completed", CollectionMetrics.Classify(S(3, 1, 0)));
        Assert.Equal("Partial", CollectionMetrics.Classify(S(1, 0, 2)));
        Assert.Equal("Failed", CollectionMetrics.Classify(S(0, 0, 3)));
        // Hiç hedef yok (official_actions boş): başarı değil, son başarı zamanı ilerlemez (THREAT_MODEL M4a).
        Assert.Equal("NoTargets", CollectionMetrics.Classify(new CollectionRunSummary(CollectionSource.Dns, 0, 0, 0, 0, 0, 0, false)));
        Assert.Equal("Cancelled", CollectionMetrics.Classify(S(3, 0, 0, cancelled: true)));
        Assert.Equal("LockedOut", CollectionMetrics.Classify(S(0, 0, 0, locked: true)));
    }

    [Fact]
    public void Failed_and_locked_out_runs_do_not_move_last_success()
    {
        var time = new FakeTimeProvider(Start);
        using var metrics = new CollectionMetrics(_capture.Factory, time);

        metrics.RunStarted(CollectionSource.Dns);
        metrics.RunFinished(new CollectionRunSummary(CollectionSource.Dns, 2, 0, 0, 0, 2, 0, false));
        time.Advance(TimeSpan.FromHours(1));
        metrics.RunFinished(new CollectionRunSummary(CollectionSource.Dns, 0, 0, 0, 0, 0, 0, false) { LockedOut = true });
        _capture.Observe();

        Assert.Null(_capture.Last("fraud_collection_last_success_timestamp_seconds", "source=Dns"));
        Assert.Equal(Start.ToUnixTimeSeconds(), _capture.Last("fraud_collection_last_run_timestamp_seconds", "source=Dns"));
        Assert.Equal(1, _capture.Sum("fraud_collection_runs", "source=Dns", "outcome=Failed"));
        Assert.Equal(1, _capture.Sum("fraud_collection_runs", "source=Dns", "outcome=LockedOut"));
    }

    [Fact]
    public void Cancelled_dns_run_keeps_the_previous_last_run_numbers()
    {
        using var dns = new DnsMetrics(_capture.Factory);
        var timeout = new DnsQueryResult(DnsOutcome.Error, DnsErrorKind.Timeout, null, [], null, false, 1, TimeSpan.FromSeconds(5));
        var cancelled = timeout with { ErrorKind = DnsErrorKind.Cancelled };

        dns.RunStarted();
        dns.Query("google", DnsTransport.Udp, timeout, blockMarker: false);
        dns.RunFinished(cancelled: false);
        dns.RunStarted();
        dns.Query("google", DnsTransport.Udp, Ok("192.0.2.1"), blockMarker: false);
        dns.Query("google", DnsTransport.Udp, cancelled, blockMarker: false);
        dns.RunFinished(cancelled: true);
        _capture.Observe();

        Assert.Equal(1, _capture.Last("fraud_dns_last_run_queries", "transport=Udp", "result=error"));
        Assert.Null(_capture.Last("fraud_dns_last_run_queries", "transport=Udp", "result=answer"));
        Assert.Null(DnsMetrics.Classify(cancelled));
    }

    [Fact]
    public async Task Freshness_monitor_reports_newest_row_age_per_table_without_reading_names()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db, "ornek-a.example");
        var time = new FakeTimeProvider(Start.AddMinutes(10));
        var services = new ServiceCollection()
            .AddScoped(_ => PostgresFixture.CreateContext(db.Database.GetConnectionString()!))
            .BuildServiceProvider();
        await using var _ = services;
        using var monitor = new DataFreshnessMonitor(
            services.GetRequiredService<IServiceScopeFactory>(), new FreshnessOptions { Enabled = true }, time,
            NullLogger<DataFreshnessMonitor>.Instance, _capture.Factory);

        await monitor.RefreshAsync(ct);
        _capture.Observe();

        Assert.Equal(600, _capture.Last("fraud_data_newest_row_age_seconds", "table=official_actions"));
        Assert.Null(_capture.Last("fraud_data_newest_row_age_seconds", "table=ct_candidates")); // boş tablo: seri yok
        Assert.Equal(0, _capture.Sum("fraud_data_freshness_query_errors"));
        Assert.NotNull(_capture.Last("fraud_data_freshness_last_check_timestamp_seconds"));
    }

    private StaticCtFollower Follower(FraudDbContext db, FakeCtServer server, StaticCtMetrics metrics)
    {
        var options = new StaticCtOptions
        {
            MinRequestInterval = TimeSpan.Zero,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryAfter = TimeSpan.Zero,
            MaxRetryAttempts = 0,
            AttemptTimeout = TimeSpan.FromSeconds(30),
            MaxConcurrency = 3,
            MaxTilesPerBatch = 32,
            MaxRangeAttempts = 2,
        };
        var log = _log.Options("0");
        var key = CtLogKey.Create(log.Origin, log.PublicKey);
        var http = new HttpClient(server, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new StaticCtClient(
            new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System,
                metrics.HttpObserver(log.Name)),
            log, key, options.MaxResponseBytes);
        return new StaticCtFollower(
            log, 1, client, new EfCtStateStore(db), new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions())),
            options, new CollectionOptions { MeasurementLocation = "test-lab" }, metrics, TimeProvider.System,
            NullLogger<StaticCtFollower>.Instance);
    }

    private static async Task SeedAsync(FraudDbContext db, params string[] hosts)
    {
        foreach (var host in hosts)
        {
            var d = new DomainName(new NormalizedDomain(host, host, null, "example", host), Start);
            db.Domains.Add(d);
            db.OfficialActions.Add(new OfficialAction(
                d, OfficialActionSource.SpkBulletin, "2026/46", new DateOnly(2026, 8, 14), OfficialActionCategory.Crypto,
                new Uri("https://spk.gov.tr/bulten/2026-46"), host, Start));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
    }

    private static DnsQueryResult Ok(params string[] ips) => new(DnsOutcome.Success, DnsErrorKind.None, 0,
        ips.Select(ip => new DnsAnswer("A", ip, 300)).ToList(), null, false, 1, TimeSpan.FromMilliseconds(20));

    private sealed class FakeDnsClient(Func<string, DnsTransport, DnsQueryResult> respond) : IDnsQueryClient
    {
        public Task<DnsQueryResult> QueryAsync(
            DnsResolver resolver, DnsTransport transport, string name, DnsRecordType type, CancellationToken cancellationToken) =>
            Task.FromResult(respond(name, transport));
    }
}
