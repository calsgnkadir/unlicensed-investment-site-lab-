using System.Net;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Dns;
using Fraud.Collectors.Http;
using Fraud.Collectors.Network;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Dns;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Fraud.Integration.Tests.Network;

/// <summary>Hafta 4 güvenlik denetimi sonrası sağlamlık düzeltmeleri (THREAT_MODEL 5.1). Ağ yok.</summary>
[Trait("Category", "Integration")]
public sealed class NetworkHardeningTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 6, 0, 0, TimeSpan.Zero);

    private static DnsQueryClient Client(params IDnsTransport[] transports) =>
        new(transports, new RequestPacer(TimeSpan.Zero, TimeProvider.System), TimeProvider.System, maxRetries: 1);

    [Fact]
    public async Task Truncated_udp_packet_cut_in_the_middle_goes_to_tcp_instead_of_malformed()
    {
        // TC bitli ve ortasından kesilmiş paket: Ayrıştırılamaz ama sunucu "TCP ile sor" demiştir.
        var udp = new DnsQueryClientTests.FakeTransport(DnsTransport.Udp)
            .ThenPacket(q => Responses.A(q, flags: Responses.QrRdRa | 0x0200, addresses: [[203, 0, 113, 66]])[..20]);
        var tcp = new DnsQueryClientTests.FakeTransport(DnsTransport.Tcp)
            .ThenPacket(q => Responses.A(q, addresses: [[192, 0, 2, 1]]));

        var result = await Client(udp, tcp).QueryAsync(DnsResolverCatalog.Cloudflare, DnsTransport.Udp, "ornek.example",
            DnsRecordType.A, TestContext.Current.CancellationToken);

        Assert.Equal((DnsOutcome.Success, true, 2), (result.Outcome, result.Truncated, result.Attempts));
        Assert.Equal(["192.0.2.1"], result.Answers.Select(a => a.Value));
    }

    [Fact]
    public async Task Cut_packet_without_tc_bit_is_still_malformed()
    {
        var udp = new DnsQueryClientTests.FakeTransport(DnsTransport.Udp)
            .ThenPacket(q => Responses.A(q, addresses: [[203, 0, 113, 66]])[..20]);

        var result = await Client(udp).QueryAsync(DnsResolverCatalog.Cloudflare, DnsTransport.Udp, "ornek.example",
            DnsRecordType.A, TestContext.Current.CancellationToken);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.Malformed, false), (result.Outcome, result.ErrorKind, result.Truncated));
    }

    [Fact]
    public async Task Answer_section_with_only_unrelated_records_is_an_error_not_empty()
    {
        var tcp = new DnsQueryClientTests.FakeTransport(DnsTransport.Tcp).ThenPacket(q =>
        {
            var w = new PacketWriter().Header(q.Id, Responses.QrRdRa, 1, 1, 0, 0).Labels(q.Name).U16(1).U16(1);
            w.Labels("baska.example").RecordMeta(DnsRecordType.A, 60).AData(203, 0, 113, 66);
            return w.ToArray();
        });

        var result = await Client(tcp).QueryAsync(DnsResolverCatalog.Cloudflare, DnsTransport.Tcp, "ornek.example",
            DnsRecordType.A, TestContext.Current.CancellationToken);

        Assert.Equal((DnsOutcome.Error, DnsErrorKind.UnrelatedAnswer, 0), (result.Outcome, result.ErrorKind, result.Rcode));
        Assert.Empty(result.Answers);
    }

    [Fact]
    public void Cname_loop_and_chain_longer_than_sixteen_are_malformed()
    {
        var query = DnsQueryBuilder.Build(1, "ornek.example", DnsRecordType.A);
        DnsResourceRecord Cname(string from, string to) => new(from, (int)DnsRecordType.CNAME, 1, 60, to, null);

        Assert.Throws<DnsFormatException>(() => DnsQueryClient.SelectAnswers(query,
            [Cname("ornek.example", "b.example"), Cname("b.example", "ornek.example")]));

        var chain = new List<DnsResourceRecord>();
        var current = "ornek.example";
        for (var i = 0; i < DnsQueryClient.MaxCnameChain; i++)
        {
            chain.Add(Cname(current, $"h{i}.example"));
            current = $"h{i}.example";
        }

        Assert.Equal(16, DnsQueryClient.SelectAnswers(query, chain).Count); // tam 16 halka: kabul
        chain.Add(Cname(current, "son.example"));
        Assert.Throws<DnsFormatException>(() => DnsQueryClient.SelectAnswers(query, chain)); // 17: ret

        Assert.Throws<DnsFormatException>(() => DnsQueryClient.SelectAnswers(query,
            [Cname("ornek.example", "a.example"), Cname("ornek.example", "b.example")])); // RFC 2181 Bölüm 10.1
    }

    [Fact]
    public void Ipv4_mapped_block_page_ip_sets_the_marker_and_is_not_sent_to_ip_sources()
    {
        var options = new DnsOptions { Location = "test-lab" };
        var result = new DnsQueryResult(DnsOutcome.Success, DnsErrorKind.None, 0,
            [new DnsAnswer("AAAA", "::ffff:195.175.254.2", 60)], null, false, 1, TimeSpan.Zero);

        var snapshot = DnsCollector.ToSnapshot(1, Start, "test-lab", DnsResolverCatalog.Cloudflare, DnsTransport.Doh,
            DnsRecordType.AAAA, result, options.EffectiveBlockPageIps);

        Assert.True(snapshot.BlockMarker);
        Assert.Equal(IPAddress.Parse("192.0.2.1"), NetworkAddress.Canonical(IPAddress.Parse("::ffff:192.0.2.1")));
    }

    private static async Task<DomainName> SeedAsync(FraudDbContext db)
    {
        var d = new DomainName(new NormalizedDomain("ornek-a.example", "ornek-a.example", null, "example", "ornek-a.example"), Start);
        db.Domains.Add(d);
        db.OfficialActions.Add(new OfficialAction(d, OfficialActionSource.SpkBulletin, "2026/46", new DateOnly(2026, 8, 14),
            OfficialActionCategory.Crypto, new Uri("https://spk.gov.tr/bulten/2026-46"), d.Host, Start));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return d;
    }

    private sealed class ThrowingOnDohClient : IDnsQueryClient
    {
        public int Calls { get; private set; }

        public Task<DnsQueryResult> QueryAsync(
            DnsResolver resolver, DnsTransport transport, string name, DnsRecordType type, CancellationToken cancellationToken)
        {
            Calls++;
            if (transport == DnsTransport.Doh)
            {
                throw new InvalidDataException("bozuk gzip (sahte)");
            }

            return Task.FromResult(new DnsQueryResult(DnsOutcome.Success, DnsErrorKind.None, 0,
                [new DnsAnswer("A", "192.0.2.1", 60)], null, false, 1, TimeSpan.Zero));
        }
    }

    private static DnsOptions OneResolverA()
    {
        var options = new DnsOptions { Location = "test-lab" };
        options.Resolvers.Add("cloudflare");
        options.QueryTypes.Add(DnsRecordType.A);
        return options;
    }

    [Fact]
    public async Task Unexpected_exception_in_one_query_is_written_as_an_error_row_and_other_rows_are_kept()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var domain = await SeedAsync(db);
        var time = new FakeTimeProvider(Start);
        var client = new ThrowingOnDohClient();
        var runner = new CollectionRunner(db, [new DnsCollector(client, OneResolverA(), time)],
            new CollectionOptions { MeasurementLocation = "test-lab" }, time, NullLogger<CollectionRunner>.Instance);

        var summary = await runner.RunAsync(CollectionSource.Dns, force: false, ct);

        Assert.Equal(1, summary.Succeeded);
        db.ChangeTracker.Clear();
        var rows = await db.DnsSnapshots.ToListAsync(ct);
        Assert.Equal(3, rows.Count);
        var error = Assert.Single(rows, r => r.Outcome == DnsOutcome.Error);
        Assert.Equal((DnsTransport.Doh, DnsErrorKind.Unexpected), (error.Transport, error.ErrorKind));
        var log = await db.CollectionLog.SingleAsync(ct);
        Assert.Equal((domain.Id, 2, 1), (log.DomainId!.Value, log.ItemCount, log.SkippedItemCount));
    }

    private sealed class ExplodingIpSource : IIpSourceCollector
    {
        public CollectionSource Source => CollectionSource.RipeStat;

        public TimeSpan CacheTtl => TimeSpan.FromDays(7);

        public TimeSpan DnsLookback => TimeSpan.FromDays(3);

        public Task<HashSet<IPAddress>> RecentlyAnsweredAsync(FraudDbContext db, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(new HashSet<IPAddress>());

        public Task<IpCollectResult> CollectAsync(FraudDbContext db, IPAddress ip, string location, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("veritabanı yazma hatası (sahte)");
    }

    [Fact]
    public async Task Ip_run_writes_its_collection_log_row_even_when_every_ip_fails_unexpectedly()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var domain = await SeedAsync(db);
        db.DnsSnapshots.Add(new DnsSnapshot(domain.Id, Start, "test-lab", "cloudflare", DnsTransport.Doh, "A", 0,
            DnsOutcome.Success, DnsErrorKind.None, [new DnsAnswer("A", "192.0.2.10", 60)], null, false, 1, 1, false));
        await db.SaveChangesAsync(ct);
        var time = new FakeTimeProvider(Start.AddHours(1));
        var runner = new IpCollectionRunner(db, [new ExplodingIpSource()], new CollectionOptions { MeasurementLocation = "test-lab" },
            new IpTargetFilter(_ => true), time, NullLogger<IpCollectionRunner>.Instance);

        var summary = await runner.RunAsync(CollectionSource.RipeStat, false, ct);

        Assert.Equal(1, summary.Failed);
        db.ChangeTracker.Clear();
        var log = await db.CollectionLog.SingleAsync(ct);
        Assert.Equal((CollectionOutcome.Error, CollectionErrorKind.Unexpected, 1), (log.Outcome, log.ErrorKind, log.SkippedItemCount));
    }

    [Fact]
    public async Task Second_process_cannot_take_the_same_network_source_and_dispatcher_sends_nothing()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db);
        var connectionString = db.Database.GetConnectionString()!;
        var first = new NetworkSourceLock(connectionString);
        var second = new NetworkSourceLock(connectionString);

        var held = await first.TryAcquireAsync(CollectionSource.Dns, ct);
        Assert.NotNull(held);
        Assert.Null(await second.TryAcquireAsync(CollectionSource.Dns, ct));
        var other = await second.TryAcquireAsync(CollectionSource.RipeStat, ct); // başka kaynak etkilenmez
        Assert.NotNull(other);
        await other.DisposeAsync();

        var time = new FakeTimeProvider(Start);
        var client = new ThrowingOnDohClient();
        var dispatcher = new CollectionDispatcher(
            new CollectionRunner(db, [new DnsCollector(client, OneResolverA(), time)],
                new CollectionOptions { MeasurementLocation = "test-lab" }, time, NullLogger<CollectionRunner>.Instance),
            new IpCollectionRunner(db, [], new CollectionOptions(), IpTargetFilter.PublicOnly, time, NullLogger<IpCollectionRunner>.Instance),
            second,
            NullLogger<CollectionDispatcher>.Instance);

        var locked = await dispatcher.RunAsync(CollectionSource.Dns, force: false, ct);
        Assert.True(locked.LockedOut);
        Assert.Equal(0, client.Calls);

        await held.DisposeAsync(); // bırakılınca ikinci süreç alabilir
        var run = await dispatcher.RunAsync(CollectionSource.Dns, force: false, ct);
        Assert.False(run.LockedOut);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>Kilit oturumunu sunucu tarafında öldürür: veritabanı yeniden başlaması veya ağ kopması gibi.</summary>
    private static async Task KillLockSessionsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = @app AND datname = current_database()",
            connection);
        command.Parameters.AddWithValue("app", NetworkSourceLock.ApplicationName);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class KillLockOnFirstDomainClient(string connectionString) : IDnsQueryClient
    {
        public List<string> Hosts { get; } = [];

        public async Task<DnsQueryResult> QueryAsync(
            DnsResolver resolver, DnsTransport transport, string name, DnsRecordType type, CancellationToken cancellationToken)
        {
            Hosts.Add(name);
            if (Hosts.Count == 1)
            {
                await KillLockSessionsAsync(connectionString);
            }

            return new DnsQueryResult(DnsOutcome.Success, DnsErrorKind.None, 0,
                [new DnsAnswer("A", "192.0.2.1", 60)], null, false, 1, TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task Run_stops_when_the_lock_connection_is_lost_midway()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var first = await SeedAsync(db);
        var secondDomain = new DomainName(new NormalizedDomain("ornek-b.example", "ornek-b.example", null, "example", "ornek-b.example"), Start);
        db.Domains.Add(secondDomain);
        db.OfficialActions.Add(new OfficialAction(secondDomain, OfficialActionSource.SpkBulletin, "2026/46", new DateOnly(2026, 8, 14),
            OfficialActionCategory.Crypto, new Uri("https://spk.gov.tr/bulten/2026-46"), secondDomain.Host, Start));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var connectionString = db.Database.GetConnectionString()!;

        await using var handle = await new NetworkSourceLock(connectionString).TryAcquireAsync(CollectionSource.Dns, ct);
        Assert.NotNull(handle);
        Assert.True(await handle.IsHeldAsync(ct));

        var time = new FakeTimeProvider(Start);
        var client = new KillLockOnFirstDomainClient(connectionString);
        var runner = new CollectionRunner(db, [new DnsCollector(client, OneResolverA(), time)],
            new CollectionOptions { MeasurementLocation = "test-lab" }, time, NullLogger<CollectionRunner>.Instance);

        var summary = await runner.RunAsync(CollectionSource.Dns, force: false, handle, ct);

        // İlk alan adı tamamlandı (3 sorgu, satırları ve log satırı yazıldı); ikincisine hiç sorgu gitmedi.
        Assert.True(summary.Cancelled);
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(3, client.Hosts.Count);
        Assert.All(client.Hosts, h => Assert.Equal(first.Host, h));
        Assert.False(await handle.IsHeldAsync(ct)); // kopuk bağlantı: false, istisna yok
        db.ChangeTracker.Clear();
        Assert.Equal(first.Id, (await db.CollectionLog.SingleAsync(ct)).DomainId);
        Assert.Equal(0, await db.DnsSnapshots.CountAsync(s => s.DomainId == secondDomain.Id, ct));

        // Kilit sunucu tarafında bırakıldığı için başka bir süreç artık alabilir.
        await using var other = await new NetworkSourceLock(connectionString).TryAcquireAsync(CollectionSource.Dns, ct);
        Assert.NotNull(other);
    }

    private sealed class KillLockIpSource(string connectionString) : IIpSourceCollector
    {
        public int Calls { get; private set; }

        public CollectionSource Source => CollectionSource.RipeStat;

        public TimeSpan CacheTtl => TimeSpan.FromDays(7);

        public TimeSpan DnsLookback => TimeSpan.FromDays(3);

        public Task<HashSet<IPAddress>> RecentlyAnsweredAsync(FraudDbContext db, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(new HashSet<IPAddress>());

        public async Task<IpCollectResult> CollectAsync(FraudDbContext db, IPAddress ip, string location, CancellationToken cancellationToken)
        {
            Calls++;
            await KillLockSessionsAsync(connectionString);
            return new IpCollectResult(CollectionOutcome.Empty, CollectionErrorKind.None, 1, false);
        }
    }

    [Fact]
    public async Task Ip_run_stops_when_the_lock_is_lost_and_still_writes_its_log_row()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var domain = await SeedAsync(db);
        foreach (var (type, ip) in new[] { ("A", "192.0.2.10"), ("AAAA", "2001:db8::10") })
        {
            db.DnsSnapshots.Add(new DnsSnapshot(domain.Id, Start, "test-lab", "cloudflare", DnsTransport.Doh, type, 0,
                DnsOutcome.Success, DnsErrorKind.None, [new DnsAnswer(type, ip, 60)], null, false, 1, 1, false));
        }

        await db.SaveChangesAsync(ct);
        var connectionString = db.Database.GetConnectionString()!;
        await using var handle = await new NetworkSourceLock(connectionString).TryAcquireAsync(CollectionSource.RipeStat, ct);
        Assert.NotNull(handle);
        var source = new KillLockIpSource(connectionString);
        var runner = new IpCollectionRunner(db, [source], new CollectionOptions { MeasurementLocation = "test-lab" },
            new IpTargetFilter(_ => true), new FakeTimeProvider(Start.AddHours(1)), NullLogger<IpCollectionRunner>.Instance);

        var summary = await runner.RunAsync(CollectionSource.RipeStat, false, handle, ct);

        Assert.True(summary.Cancelled);
        Assert.Equal(1, source.Calls); // ikinci IP'ye gidilmedi
        db.ChangeTracker.Clear();
        var log = await db.CollectionLog.SingleAsync(ct);
        Assert.Equal((CollectionOutcome.Error, CollectionErrorKind.Cancelled, 1), (log.Outcome, log.ErrorKind, log.ItemCount));
    }
}
