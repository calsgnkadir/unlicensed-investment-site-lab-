using Fraud.Collectors.Collection;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Quality;
using Fraud.Collectors.Wayback;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Collectors;

// Gerçek PostgreSQL (Testcontainers) üzerinde toplama akışı. Kaynak istemcileri sahte; ağ yok.
[Trait("Category", "Integration")]
public sealed class CollectionRunnerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static CrtShCertificate Cert(long id, params string[] names) => new(
        id, $"{id:x}", 16418, "C=US, O=Let's Encrypt, CN=R3", names[0], names,
        new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 5, 1, 0, 5, 0, TimeSpan.Zero));

    private static CdxCapture Capture(string timestamp, string url, string digest) => new(
        timestamp, DateTimeOffset.ParseExact(timestamp, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal),
        url, false, 200, "text/html", digest);

    private static async Task<(DomainName A, DomainName B)> SeedAsync(FraudDbContext db)
    {
        var a = new DomainName(new NormalizedDomain("ornek-a.example", "ornek-a.example", null, "example", "ornek-a.example"), Start);
        var b = new DomainName(new NormalizedDomain("ornek-b.example", "ornek-b.example", null, "example", "ornek-b.example"), Start);
        db.Domains.AddRange(a, b);
        foreach (var d in new[] { a, b })
        {
            db.OfficialActions.Add(new OfficialAction(
                d, OfficialActionSource.SpkBulletin, "2026/46", new DateOnly(2026, 8, 14), OfficialActionCategory.Crypto,
                new Uri("https://spk.gov.tr/bulten/2026-46"), d.Host, Start));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return (a, b);
    }

    private static CollectionRunner Runner(FraudDbContext db, ISourceCollector collector, TimeProvider time) =>
        new(db, [collector], new CollectionOptions { MeasurementLocation = "test-lab" }, time, NullLogger<CollectionRunner>.Instance);

    [Fact]
    public async Task Same_crtsh_job_twice_does_not_duplicate_certificates_or_links()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);

        // 1001 iki alan adında da görünüyor; B sorgusu ona yeni bir ad ekliyor.
        var client = new FakeCrtShClient(host => host == a.Host
            ? SourceResult.FromParsed<CrtShCertificate>([Cert(1001, "ornek-a.example"), Cert(1002, "www.ornek-a.example")], 0, 200, 1, false)
            : SourceResult.FromParsed<CrtShCertificate>([Cert(1001, "ornek-b.example")], 0, 200, 1, false));
        var runner = Runner(db, new CrtShCollector(client, new CrtShOptions(), time), time);

        var first = await runner.RunAsync(CollectionSource.CrtSh, force: false, ct);
        time.Advance(TimeSpan.FromMinutes(1));
        var second = await runner.RunAsync(CollectionSource.CrtSh, force: true, ct);

        Assert.Equal(2, first.Succeeded);
        Assert.Equal(2, first.NewItems);
        Assert.Equal(2, second.Succeeded);
        Assert.Equal(0, second.NewItems);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Certificates.CountAsync(ct));
        Assert.Equal(3, await db.CertificateDomains.CountAsync(ct));
        Assert.Equal(4, await db.CollectionLog.CountAsync(ct));

        var shared = await db.Certificates.SingleAsync(c => c.CrtShId == 1001, ct);
        Assert.Equal(["ornek-a.example", "ornek-b.example"], shared.Names);
        Assert.Equal(Start, shared.ObservedAt); // tekrar görülmesi ilk görülme anını değiştirmez
        Assert.Null(shared.IsPrecertificate);

        var logs = await db.CollectionLog.OrderBy(l => l.Id).ToListAsync(ct);
        Assert.All(logs, l =>
        {
            Assert.Equal("test-lab", l.MeasurementLocation);
            Assert.Equal(CollectionOutcome.Success, l.Outcome);
            Assert.NotNull(l.DomainId);
        });
        Assert.All(logs.Skip(2), l => Assert.Equal(0, l.NewItemCount));
    }

    [Fact]
    public async Task Fresh_success_is_skipped_but_errors_are_retried_and_never_look_empty()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);

        var client = new FakeCrtShClient(host => host == a.Host
            ? SourceResult.FromParsed<CrtShCertificate>([Cert(1001, "ornek-a.example")], 0, 200, 1, false)
            : SourceResult.Failed<CrtShCertificate>(CollectionErrorKind.RateLimited, 429, 4));
        var options = new CrtShOptions { CacheTtl = TimeSpan.FromDays(7) };
        var runner = Runner(db, new CrtShCollector(client, options, time), time);

        await runner.RunAsync(CollectionSource.CrtSh, force: false, ct);
        time.Advance(TimeSpan.FromHours(1));
        var second = await runner.RunAsync(CollectionSource.CrtSh, force: false, ct);

        Assert.Equal(1, second.SkippedFresh);
        Assert.Equal(1, client.Calls[a.Host]);
        Assert.Equal(2, client.Calls[b.Host]);

        var bLogs = await db.CollectionLog.Where(l => l.DomainId == b.Id).ToListAsync(ct);
        Assert.Equal(2, bLogs.Count);
        Assert.All(bLogs, l =>
        {
            Assert.Equal(CollectionOutcome.Error, l.Outcome);
            Assert.Equal(CollectionErrorKind.RateLimited, l.ErrorKind);
            Assert.Equal(429, l.HttpStatus);
            Assert.Equal(4, l.Attempts);
            Assert.Equal(0, l.ItemCount);
        });

        // Önbellek süresi dolunca yeniden sorulur.
        time.Advance(TimeSpan.FromDays(8));
        await runner.RunAsync(CollectionSource.CrtSh, force: false, ct);
        Assert.Equal(2, client.Calls[a.Host]);
    }

    [Fact]
    public async Task Same_wayback_rows_twice_are_stored_once_and_partial_error_keeps_rows()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);

        CdxCapture[] rows =
        [
            Capture("20260101000000", "http://ornek-a.example/", "AAAA"),
            Capture("20260201000000", "http://ornek-a.example/", "BBBB"),
        ];
        var client = new FakeWaybackClient(host => host == a.Host
            ? SourceResult.FromParsed<CdxCapture>(rows, 0, 200, 1, false)
            : SourceResult.Failed<CdxCapture>(CollectionErrorKind.ServerError, 503, 3, [Capture("20260301000000", "http://ornek-b.example/", "CCCC")]));
        var runner = Runner(db, new WaybackCollector(client, new WaybackCdxOptions(), time), time);

        await runner.RunAsync(CollectionSource.WaybackCdx, force: false, ct);
        await runner.RunAsync(CollectionSource.WaybackCdx, force: true, ct);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.ArchiveSnapshots.CountAsync(s => s.DomainId == a.Id, ct));
        Assert.Equal(1, await db.ArchiveSnapshots.CountAsync(s => s.DomainId == b.Id, ct));

        var bLog = await db.CollectionLog.Where(l => l.DomainId == b.Id).OrderBy(l => l.Id).FirstAsync(ct);
        Assert.Equal(CollectionOutcome.Error, bLog.Outcome);
        Assert.Equal(1, bLog.ItemCount);
        Assert.Equal(1, bLog.NewItemCount);

        var snapshot = await db.ArchiveSnapshots.Where(s => s.DomainId == a.Id).OrderBy(s => s.CapturedAt).FirstAsync(ct);
        Assert.Equal(Start, snapshot.ObservedAt);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), snapshot.CapturedAt);
    }

    [Fact]
    public async Task Cancellation_is_logged_as_an_error_and_stops_the_run()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        await SeedAsync(db);
        var time = new FakeTimeProvider(Start);
        using var cts = new CancellationTokenSource();

        var client = new FakeCrtShClient(_ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("ulaşılmaz");
        });
        var runner = Runner(db, new CrtShCollector(client, new CrtShOptions(), time), time);

        var summary = await runner.RunAsync(CollectionSource.CrtSh, force: false, cts.Token);

        Assert.True(summary.Cancelled);
        var log = await db.CollectionLog.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CollectionOutcome.Error, log.Outcome);
        Assert.Equal(CollectionErrorKind.Cancelled, log.ErrorKind);
    }

    [Fact]
    public async Task Quality_report_separates_data_empty_errors_and_never_attempted_and_prints_no_names()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);

        var crt = new FakeCrtShClient(host => host == a.Host
            ? SourceResult.FromParsed<CrtShCertificate>([Cert(1001, "ornek-a.example")], 0, 200, 1, false)
            : SourceResult.Failed<CrtShCertificate>(CollectionErrorKind.Timeout, null, 4));
        await Runner(db, new CrtShCollector(crt, new CrtShOptions(), time), time).RunAsync(CollectionSource.CrtSh, false, ct);

        var wayback = new FakeWaybackClient(host => host == a.Host
            ? SourceResult.FromParsed<CdxCapture>([], 0, 200, 1, false)
            : SourceResult.FromParsed<CdxCapture>([Capture("20260101000000", "http://ornek-b.example/", "AAAA")], 0, 200, 1, false));
        await Runner(db, new WaybackCollector(wayback, new WaybackCdxOptions(), time), time).RunAsync(CollectionSource.WaybackCdx, false, ct);

        var report = await new QualityReportBuilder(db, time).BuildAsync(ct);

        Assert.Equal(2, report.TargetDomains);
        var crtQuality = report.Sources.Single(s => s.Source == CollectionSource.CrtSh);
        Assert.Equal((1, 0, 1, 0), (crtQuality.DomainsWithData, crtQuality.DomainsConfirmedEmpty, crtQuality.DomainsOnlyErrors, crtQuality.DomainsNeverAttempted));
        Assert.Equal(0.5, crtQuality.ErrorRate);
        Assert.Equal(1, crtQuality.ErrorsByKind[CollectionErrorKind.Timeout]);
        Assert.Equal(1, crtQuality.DomainsUnknown);

        var waybackQuality = report.Sources.Single(s => s.Source == CollectionSource.WaybackCdx);
        Assert.Equal((1, 1, 0, 0), (waybackQuality.DomainsWithData, waybackQuality.DomainsConfirmedEmpty, waybackQuality.DomainsOnlyErrors, waybackQuality.DomainsNeverAttempted));

        var text = report.ToText();
        Assert.DoesNotContain("ornek", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".example", text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeCrtShClient(Func<string, SourceResult<CrtShCertificate>> respond) : ICrtShClient
    {
        public Dictionary<string, int> Calls { get; } = [];

        public Task<SourceResult<CrtShCertificate>> SearchAsync(string host, CancellationToken cancellationToken)
        {
            Calls[host] = Calls.GetValueOrDefault(host) + 1;
            return Task.FromResult(respond(host));
        }
    }

    private sealed class FakeWaybackClient(Func<string, SourceResult<CdxCapture>> respond) : IWaybackCdxClient
    {
        public Task<SourceResult<CdxCapture>> ListCapturesAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(respond(host));
    }
}
