using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Dns;
using Fraud.Collectors.InternetDb;
using Fraud.Collectors.Network;
using Fraud.Collectors.Quality;
using Fraud.Collectors.RipeStat;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Fraud.Integration.Tests.Network;

// Gerçek PostgreSQL (Testcontainers) üzerinde DNS, RIPEstat ve InternetDB akışı. İstemciler sahte; ağ yok.
[Trait("Category", "Integration")]
public sealed class NetworkPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);

    /// <summary>Testte belgeleme aralıklarına izin veren süzgeç (üretimde yalnızca herkese açık IP).</summary>
    private static readonly IpTargetFilter AllowAll = new(_ => true);

    private static async Task<(DomainName A, DomainName B, DomainName NotListed)> SeedAsync(FraudDbContext db)
    {
        DomainName D(string host) => new(new NormalizedDomain(host, host, null, "example", host), Start);
        var a = D("ornek-a.example");
        var b = D("ornek-b.example");
        var notListed = D("liste-disi.example"); // official_actions'ta yok: ağ kaynaklarına hiç gitmemeli
        db.Domains.AddRange(a, b, notListed);
        foreach (var d in new[] { a, b })
        {
            db.OfficialActions.Add(new OfficialAction(
                d, OfficialActionSource.SpkBulletin, "2026/46", new DateOnly(2026, 8, 14), OfficialActionCategory.Crypto,
                new Uri("https://spk.gov.tr/bulten/2026-46"), d.Host, Start));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return (a, b, notListed);
    }

    private sealed class FakeDnsClient(Func<string, DnsTransport, DnsQueryResult> respond) : IDnsQueryClient
    {
        public List<(string Host, DnsTransport Transport)> Calls { get; } = [];

        public Task<DnsQueryResult> QueryAsync(
            DnsResolver resolver, DnsTransport transport, string name, DnsRecordType type, CancellationToken cancellationToken)
        {
            Calls.Add((name, transport));
            return Task.FromResult(respond(name, transport));
        }
    }

    private static DnsQueryResult Ok(params string[] ips) => new(DnsOutcome.Success, DnsErrorKind.None, 0,
        ips.Select(ip => new DnsAnswer("A", ip, 300)).ToList(), null, false, 1, TimeSpan.FromMilliseconds(20));

    private static readonly DnsQueryResult Timeout =
        new(DnsOutcome.Error, DnsErrorKind.Timeout, null, [], null, false, 2, TimeSpan.FromSeconds(10));

    private static DnsOptions Options()
    {
        var options = new DnsOptions { Location = "test-lab" };
        options.Resolvers.Add("cloudflare");
        options.QueryTypes.Add(DnsRecordType.A);
        return options;
    }

    private static CollectionRunner DnsRunner(FraudDbContext db, IDnsQueryClient client, TimeProvider time) =>
        new(db, [new DnsCollector(client, Options(), time)], new CollectionOptions { MeasurementLocation = "test-lab" },
            time, NullLogger<CollectionRunner>.Instance);

    [Fact]
    public async Task Dns_run_stores_one_row_per_question_and_rerunning_never_duplicates()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, b, _) = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);
        var tcpFails = true;
        var client = new FakeDnsClient((host, transport) =>
            host == b.Host && transport == DnsTransport.Tcp && tcpFails ? Timeout : Ok("192.0.2.1"));

        var first = await DnsRunner(db, client, time).RunAsync(CollectionSource.Dns, force: false, ct);

        Assert.Equal(2, first.Succeeded);
        Assert.Equal(6, client.Calls.Count); // 2 alan adı x 1 çözücü x 3 yol x 1 tür
        Assert.DoesNotContain(client.Calls, c => c.Host == "liste-disi.example");
        db.ChangeTracker.Clear();
        var rows = await db.DnsSnapshots.OrderBy(s => s.Id).ToListAsync(ct);
        Assert.Equal(6, rows.Count);
        var error = Assert.Single(rows, r => r.Outcome == DnsOutcome.Error);
        Assert.Equal((DnsErrorKind.Timeout, DnsTransport.Tcp), (error.ErrorKind, error.Transport));
        Assert.Empty(error.Answers);
        Assert.All(rows.Where(r => r.Outcome == DnsOutcome.Success),
            r => Assert.Equal([new DnsAnswer("A", "192.0.2.1", 300)], r.Answers));
        Assert.All(rows, r => Assert.Equal(("test-lab", "cloudflare", "A"), (r.Location, r.Resolver, r.QueryType)));

        var bLog = await db.CollectionLog.SingleAsync(l => l.DomainId == b.Id, ct);
        Assert.Equal((CollectionOutcome.Success, 2, 1, 4), (bLog.Outcome, bLog.ItemCount, bLog.SkippedItemCount, bLog.Attempts));
        Assert.Null(bLog.HttpStatus);

        // Aynı gün önbellekle: hiç sorgu yok.
        time.Advance(TimeSpan.FromHours(2));
        var second = await DnsRunner(db, client, time).RunAsync(CollectionSource.Dns, force: false, ct);
        Assert.Equal(2, second.SkippedFresh);
        Assert.Equal(6, client.Calls.Count);

        // Aynı gün zorla: Satır sayısı değişmez; yalnızca hatalı satır başarılıyla değişir.
        tcpFails = false;
        var forced = await DnsRunner(db, client, time).RunAsync(CollectionSource.Dns, force: true, ct);
        db.ChangeTracker.Clear();
        Assert.Equal(1, forced.NewItems);
        Assert.Equal(6, await db.DnsSnapshots.CountAsync(ct));
        Assert.Equal(0, await db.DnsSnapshots.CountAsync(s => s.Outcome == DnsOutcome.Error, ct));

        // Ertesi gün yeni ölçüm: yeni satırlar.
        time.Advance(TimeSpan.FromDays(1));
        await DnsRunner(db, client, time).RunAsync(CollectionSource.Dns, force: false, ct);
        Assert.Equal(12, await db.DnsSnapshots.CountAsync(ct));
    }

    [Fact]
    public async Task Database_refuses_dns_error_rows_that_carry_answers_or_success_without_answers()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, _, _) = await SeedAsync(db);

        Task Insert(string outcome, string errorKind, int? rcode, string answers, bool block) => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dns_snapshots (domain_id, queried_at, measured_on, location, resolver, transport, query_type, rcode,
                                       outcome, error_kind, answers, truncated, latency_ms, attempts, block_marker)
            VALUES ({a.Id}, now(), current_date, 'test-lab', 'cloudflare', 'Udp', 'A', {rcode}, {outcome}, {errorKind},
                    CAST({answers} AS jsonb), false, 1, 1, {block})
            """, ct);

        const string oneAnswer = """[{"type":"A","value":"192.0.2.1","ttl":1}]""";
        foreach (var bad in new Func<Task>[]
                 {
                     () => Insert("Error", "Timeout", null, oneAnswer, false),
                     () => Insert("Error", "None", null, "[]", false),
                     () => Insert("Success", "None", 0, "[]", false),
                     () => Insert("Empty", "None", null, "[]", false), // RCODE bilinmeden "boş" denemez
                     () => Insert("NxDomain", "None", 0, "[]", false),
                     () => Insert("Empty", "None", 0, "[]", true),
                 })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(bad);
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        }
    }

    private static async Task<FraudDbContext> SeedSnapshotsForIpSourcesAsync(FraudDbContext db, DomainName a, DomainName notListed)
    {
        DnsSnapshot S(long domain, DnsTransport transport, string ip, bool block = false, string type = "A", string resolver = "cloudflare") => new(
            domain, Start, "test-lab", resolver, transport, type, 0, DnsOutcome.Success, DnsErrorKind.None,
            [new DnsAnswer(type == "AAAA" ? "AAAA" : "A", ip, 60)], null, false, 5, 1, block);

        db.DnsSnapshots.AddRange(
            S(a.Id, DnsTransport.Doh, "192.0.2.10"),
            S(a.Id, DnsTransport.Doh, "2001:db8::10", type: "AAAA"),
            S(a.Id, DnsTransport.Udp, "198.51.100.7"),            // düz UDP: alınmaz (yolda değişmiş olabilir)
            S(a.Id, DnsTransport.Doh, "195.175.254.2", block: true, resolver: "google"), // engel işareti: alınmaz
            S(notListed.Id, DnsTransport.Doh, "203.0.113.5"));      // resmi listede yok: alınmaz
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return db;
    }

    private sealed class FakeRipe(Func<IPAddress, RipeStatLookup> respond) : IRipeStatClient
    {
        public List<IPAddress> Calls { get; } = [];

        public Task<RipeStatLookup> LookupAsync(IPAddress ip, CancellationToken cancellationToken)
        {
            Calls.Add(ip);
            return Task.FromResult(respond(ip));
        }
    }

    private sealed class FakeInternetDb(Func<IPAddress, InternetDbLookup> respond) : IInternetDbClient
    {
        public List<IPAddress> Calls { get; } = [];

        public Task<InternetDbLookup> LookupAsync(IPAddress ip, CancellationToken cancellationToken)
        {
            Calls.Add(ip);
            return Task.FromResult(respond(ip));
        }
    }

    private static IpCollectionRunner IpRunner(FraudDbContext db, IIpSourceCollector collector, TimeProvider time) =>
        new(db, [collector], new CollectionOptions { MeasurementLocation = "test-lab" }, AllowAll, time,
            NullLogger<IpCollectionRunner>.Instance);

    [Fact]
    public async Task Ripestat_gets_only_doh_non_blocked_ips_of_listed_domains_and_skips_them_for_seven_days()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, _, notListed) = await SeedAsync(db);
        await SeedSnapshotsForIpSourcesAsync(db, a, notListed);
        var time = new FakeTimeProvider(Start.AddHours(1));
        var ripe = new FakeRipe(ip => new RipeStatLookup(CollectionOutcome.Success, CollectionErrorKind.None,
            new RipeStatNetworkInfo([13335], ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "192.0.2.0/24" : "2001:db8::/32"),
            200, 1));
        var collector = new RipeStatCollector(ripe, new RipeStatOptions(), time);

        var first = await IpRunner(db, collector, time).RunAsync(CollectionSource.RipeStat, false, ct);

        Assert.Equal([IPAddress.Parse("192.0.2.10"), IPAddress.Parse("2001:db8::10")], ripe.Calls);
        Assert.Equal((2, 2), (first.Succeeded, first.NewItems));
        db.ChangeTracker.Clear();
        var rows = await db.IpAsnSnapshots.OrderBy(r => r.Id).ToListAsync(ct);
        Assert.All(rows, r => Assert.Equal(("cloudflare", "test-lab"), (r.CdnOrCloud, r.Location)));
        Assert.Equal([13335L], rows[0].Asns);
        var log = await db.CollectionLog.SingleAsync(l => l.Source == CollectionSource.RipeStat, ct);
        Assert.Null(log.DomainId);
        Assert.Equal((CollectionOutcome.Success, 2, 2), (log.Outcome, log.ItemCount, log.Attempts));

        // 2 gün sonra: DNS kaydı hâlâ geriye bakış penceresinde (3 gün), ama 7 gün dolmadı: sorgu yok, log satırı da yok.
        time.Advance(TimeSpan.FromDays(2));
        var second = await IpRunner(db, collector, time).RunAsync(CollectionSource.RipeStat, false, ct);
        Assert.Equal(2, second.SkippedFresh);
        Assert.Equal(2, ripe.Calls.Count);
        Assert.Equal(1, await db.CollectionLog.CountAsync(l => l.Source == CollectionSource.RipeStat, ct));
    }

    [Fact]
    public async Task Internetdb_records_empty_for_no_information_and_retries_errors_on_the_next_run()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, _, notListed) = await SeedAsync(db);
        await SeedSnapshotsForIpSourcesAsync(db, a, notListed);
        var time = new FakeTimeProvider(Start.AddHours(1));
        var failing = true;
        var internetDb = new FakeInternetDb(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? new InternetDbLookup(CollectionOutcome.Empty, CollectionErrorKind.None, null, 404, 1)
            : failing
                ? new InternetDbLookup(CollectionOutcome.Error, CollectionErrorKind.ServerError, null, 503, 4)
                : new InternetDbLookup(CollectionOutcome.Success, CollectionErrorKind.None,
                    new InternetDbHost([443], ["cdn"], [], 0), 200, 1));
        var collector = new InternetDbCollector(internetDb, new InternetDbOptions(), time);

        var first = await IpRunner(db, collector, time).RunAsync(CollectionSource.InternetDb, false, ct);
        Assert.Equal((0, 1, 1), (first.Succeeded, first.Empty, first.Failed));

        failing = false;
        time.Advance(TimeSpan.FromHours(1));
        var second = await IpRunner(db, collector, time).RunAsync(CollectionSource.InternetDb, false, ct);
        Assert.Equal((1, 1), (second.Succeeded, second.SkippedFresh)); // boş sonuç taze sayılır, hata yeniden sorulur

        db.ChangeTracker.Clear();
        var rows = await db.PortSnapshots.OrderBy(r => r.Ip).ToListAsync(ct);
        Assert.Equal(2, rows.Count); // hatalı satır aynı gün başarılıyla değişti
        Assert.Contains(rows, r => r.Outcome == CollectionOutcome.Empty && r.Ports.Length == 0 && r.HttpStatus == 404);
        Assert.Contains(rows, r => r.Outcome == CollectionOutcome.Success && r.Ports.SequenceEqual([443]) && r.Tags.SequenceEqual(["cdn"]));
    }

    [Fact]
    public async Task Dns_compare_and_quality_report_print_only_numbers()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var (a, b, _) = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);

        // A için UDP farklı IP veriyor (araya girme belirtisi); B için hepsi aynı. TCP B için NXDOMAIN.
        var client = new FakeDnsClient((host, transport) => (host, transport) switch
        {
            ("ornek-a.example", DnsTransport.Udp) => Ok("195.175.254.2"),
            ("ornek-b.example", DnsTransport.Tcp) => new DnsQueryResult(DnsOutcome.NxDomain, DnsErrorKind.None, 3, [], 300, false, 1, TimeSpan.Zero),
            _ => Ok("192.0.2.1"),
        });
        await DnsRunner(db, client, time).RunAsync(CollectionSource.Dns, false, ct);

        var compare = await new DnsCompareReportBuilder(db).BuildAsync("test-lab", null, ct);
        var resolver = Assert.Single(compare.Resolvers);
        Assert.Equal((2, 1), (resolver.DomainsComparedUdp, resolver.DomainsDifferingUdp));
        Assert.Equal((2, 1), (resolver.DomainsComparedTcp, resolver.DomainsDifferingTcp));
        var udp = compare.Transports.Single(t => t.Transport == DnsTransport.Udp);
        Assert.Equal(1, udp.BlockMarkers);
        Assert.Equal(0.5, compare.Transports.Single(t => t.Transport == DnsTransport.Tcp).NxDomainRate);

        var report = await new QualityReportBuilder(db, time).BuildAsync(ct);
        var dns = report.Sources.Single(s => s.Source == CollectionSource.Dns);
        Assert.Equal((2, 0), (dns.DomainsWithData, dns.DomainsNeverAttempted));
        Assert.Equal(2, report.IpSources!.Count);

        foreach (var text in new[] { compare.ToText(), report.ToText() })
        {
            Assert.DoesNotContain("ornek", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".example", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("192.0.2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("195.175", text, StringComparison.Ordinal);
        }
    }
}
