using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Http;
using Fraud.Collectors.Quality;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Fraud.Integration.Tests.StaticCt;

// Aynı log için tek süreç kilidi (PostgreSQL advisory lock), canlı akış döngüsü ve kalite raporunun Static CT bölümü.
// Gerçek PostgreSQL (Testcontainers); log sahte, ağ yok.
[Trait("Category", "Integration")]
public sealed class CtFollowLockAndReportTests(PostgresFixture postgres) : IDisposable
{
    private readonly TestCtLog _log = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly StaticCtMetrics _metrics = new();
    private readonly string _tempDir = Directory.CreateTempSubdirectory("ct-heartbeat-").FullName;

    public void Dispose()
    {
        _log.Dispose();
        _logs.Dispose();
        _metrics.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private static StaticCtOptions Fast(int maxAttempts = 2) => new()
    {
        MinRequestInterval = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryAfter = TimeSpan.Zero,
        MaxRetryAttempts = 0,
        AttemptTimeout = TimeSpan.FromSeconds(30),
        MaxConcurrency = 3,
        MaxTilesPerBatch = 32,
        MaxRangeAttempts = maxAttempts,
        PollInterval = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public void Lock_key_is_stable_non_negative_and_differs_between_logs()
    {
        var a = PostgresCtLogLockProvider.LockKey("sycamore2026h2");
        Assert.Equal(a, PostgresCtLogLockProvider.LockKey("sycamore2026h2"));
        Assert.True(a >= 0);
        Assert.NotEqual(a, PostgresCtLogLockProvider.LockKey("sycamore2027h1"));
    }

    [Fact]
    public async Task Lock_is_exclusive_per_log_and_is_released_on_dispose()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var ct = TestContext.Current.CancellationToken;
        var processA = new PostgresCtLogLockProvider(connection);
        var processB = new PostgresCtLogLockProvider(connection);

        var first = await processA.TryAcquireAsync("log-a", ct);
        Assert.NotNull(first);
        Assert.True(await first.IsHeldAsync(ct));

        // İkinci süreç aynı logu alamaz, başka bir logu alabilir.
        Assert.Null(await processB.TryAcquireAsync("log-a", ct));
        var other = await processB.TryAcquireAsync("log-b", ct);
        Assert.NotNull(other);
        await other.DisposeAsync();

        await first.DisposeAsync();
        Assert.False(await first.IsHeldAsync(ct));
        var second = await processB.TryAcquireAsync("log-a", ct);
        Assert.NotNull(second);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task Lock_held_by_a_dropped_connection_is_freed_and_reported_as_lost()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var ct = TestContext.Current.CancellationToken;
        var processA = new PostgresCtLogLockProvider(connection);
        var processB = new PostgresCtLogLockProvider(connection);

        var held = await processA.TryAcquireAsync("log-a", ct);
        Assert.NotNull(held);

        // Süreç A'nın bağlantısı koptu (ör. veritabanı yeniden başladı, ağ kesildi).
        await using (var admin = new NpgsqlConnection(connection))
        {
            await admin.OpenAsync(ct);
            await using var kill = new NpgsqlCommand(
                "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE application_name = @app AND datname = current_database()",
                admin);
            kill.Parameters.AddWithValue("app", PostgresCtLogLockProvider.ApplicationName);
            Assert.Equal(1L, (long)(await kill.ExecuteScalarAsync(ct))!);
        }

        Assert.False(await held.IsHeldAsync(ct));
        var takeover = await processB.TryAcquireAsync("log-a", ct);
        Assert.NotNull(takeover);
        await held.DisposeAsync(); // Kopmuş bağlantıda hata vermez.
        Assert.True(await takeover.IsHeldAsync(ct));
        await takeover.DisposeAsync();
    }

    [Fact]
    public async Task Worker_waits_while_another_process_holds_the_lock_then_takes_over()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var connection = db.Database.GetConnectionString()!;
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries(2 * 256);
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };

        var options = Fast();
        options.HeartbeatFile = Path.Combine(_tempDir, "ct-follow.heartbeat");
        options.Logs.Add(_log.Options("0"));
        var lockProvider = new PostgresCtLogLockProvider(connection);
        await using var services = BuildServices(connection, server, options, lockProvider);

        // Başka bir süreç bu logu izliyor.
        var other = await new PostgresCtLogLockProvider(connection).TryAcquireAsync("testlog", ct);
        Assert.NotNull(other);

        using var worker = new CtFollowWorker(
            services.GetRequiredService<IServiceScopeFactory>(), options, TimeProvider.System,
            services.GetRequiredService<ILogger<CtFollowWorker>>(), lockProvider);
        await worker.StartAsync(ct);
        try
        {
            // Kilit doluyken birkaç bekleme süresi geçer; log'a hiç istek gitmemeli, hiçbir şey yazılmamalı.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            Assert.Empty(server.Paths);
            Assert.False(await db.CtLogStates.AnyAsync(ct));
            Assert.False(File.Exists(options.HeartbeatFile));
            Assert.Single(_logs.Messages, m => m.Contains("başka bir süreç", StringComparison.Ordinal));

            // Diğer süreç durdu: bu süreç kilidi devralır ve ilerler.
            await other.DisposeAsync();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                db.ChangeTracker.Clear();
                if (await db.CtLogStates.AnyAsync(s => s.NextIndex == 512, ct) && File.Exists(options.HeartbeatFile))
                {
                    break;
                }

                await Task.Delay(100, ct);
            }

            db.ChangeTracker.Clear();
            Assert.Equal(512, (await db.CtLogStates.SingleAsync(ct)).NextIndex);
            Assert.True(File.Exists(options.HeartbeatFile));
            Assert.DoesNotContain("ornek", await File.ReadAllTextAsync(options.HeartbeatFile, ct), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Durunca kilit bırakılır.
        var after = await new PostgresCtLogLockProvider(connection).TryAcquireAsync("testlog", ct);
        Assert.NotNull(after);
        await after.DisposeAsync();
    }

    [Fact]
    public async Task Quality_report_shows_static_ct_coverage_as_numbers_only()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        _log.AddEntries((4 * 256) + 9);
        var broken = _log.DataTile(2)[..^10];
        using var server = new FakeCtServer(_log)
        {
            PublishedSize = _log.Count,
            Intercept = p => p == TilePath.DataTile(2)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(broken) }
                : null,
        };

        // Tile 0-1 işlenir, tile 2 iki kez okunamaz ve atlanır, tile 3 işlenir. Sondaki 9 kayıt kısmi tile'da kalır.
        for (var i = 0; i < 4; i++)
        {
            await Follower(db, server, Fast(maxAttempts: 2)).RunOnceAsync(ct);
        }

        db.ChangeTracker.Clear();
        var report = await new QualityReportBuilder(db, TimeProvider.System).BuildAsync(ct);

        var q = Assert.Single(report.StaticCtLogs!);
        Assert.Equal("testlog", q.LogName);
        Assert.Equal((1024L, 1033L, 9L), (q.NextIndex, q.LatestCheckpointSize, q.Backlog));
        Assert.Equal((768L, 256L), (q.EntriesProcessed, q.EntriesSkipped));
        Assert.Equal(0.75, q.Coverage);
        Assert.Equal(await db.CtCandidates.LongCountAsync(ct), q.CandidateRows);
        Assert.True(q.CandidateRows > 0);
        Assert.Equal((768L, 256L), (q.ProcessedLast24Hours, q.SkippedLast24Hours));
        Assert.Equal(await db.CollectionLog.CountAsync(ct), q.LogRows);
        Assert.Equal(2, q.ErrorRows);
        Assert.Equal(2, q.ErrorsByKind[CollectionErrorKind.InvalidResponse]);
        Assert.True(q.CheckpointAge >= TimeSpan.Zero);

        var text = report.ToText();
        Assert.Contains("[StaticCt]", text, StringComparison.Ordinal);
        Assert.Contains("75.0000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ornek", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xn--", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test-ct.example", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quality_report_without_ct_state_says_so()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var report = await new QualityReportBuilder(db, TimeProvider.System).BuildAsync(TestContext.Current.CancellationToken);
        Assert.Empty(report.StaticCtLogs!);
        Assert.Contains("ct_log_state boş", report.ToText(), StringComparison.Ordinal);
    }

    private StaticCtFollower Follower(FraudDbContext db, FakeCtServer server, StaticCtOptions options)
    {
        var log = _log.Options("0");
        return new StaticCtFollower(
            log, 1, new FakeClientFactory(server, options).Create(log), new EfCtStateStore(db),
            new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions())),
            options, new CollectionOptions { MeasurementLocation = "test-lab" }, _metrics, TimeProvider.System,
            _logs.CreateLogger("StaticCt"));
    }

    private ServiceProvider BuildServices(string connection, FakeCtServer server, StaticCtOptions options, ICtLogLockProvider lockProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(_logs));
        services.AddDbContext<FraudDbContext>(o => o.UseNpgsql(connection));
        services.AddSingleton(options);
        services.AddSingleton(new CollectionOptions { MeasurementLocation = "test-lab" });
        services.AddSingleton<ICandidateFilter>(new KeywordCandidateFilter(new CandidateFilterOptions()));
        services.AddSingleton(_metrics);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICtStateStore, EfCtStateStore>();
        services.AddSingleton<IStaticCtClientFactory>(new FakeClientFactory(server, options));
        services.AddSingleton(lockProvider);
        return services.BuildServiceProvider();
    }

    /// <summary>Gerçek <see cref="StaticCtClient"/>; HTTP katmanı sahte sunucuya gider.</summary>
    private sealed class FakeClientFactory(FakeCtServer server, StaticCtOptions options) : IStaticCtClientFactory
    {
        public IStaticCtClient Create(CtLogOptions log)
        {
            var http = new HttpClient(server, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            return new StaticCtClient(
                new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System),
                log, CtLogKey.Create(log.Origin, log.PublicKey), options.MaxResponseBytes);
        }
    }
}
