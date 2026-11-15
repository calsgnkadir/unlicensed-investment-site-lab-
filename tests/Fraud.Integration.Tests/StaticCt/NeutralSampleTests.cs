using System.Globalization;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Http;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.CertificateTransparency;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fraud.Integration.Tests.StaticCt;

/// <summary>
/// Tarafsız CT örneklemi (KONTROL_GRUBU_YONTEMI.md 4.5): determinizm, oran, kelimeden bağımsızlık, tekillik.
/// Alan adları uydurmadır (<c>.example</c>); ağ yok.
/// </summary>
public sealed class NeutralSampleTests
{
    // Testlere özel sabit tuz (gerçek tuz .env içindedir ve hiçbir yerde yazılmaz).
    private const string TestSalt = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    private static readonly PublicSuffixDomainNormalizer Normalizer = new();

    private static NeutralSampler Sampler(double rate = 0.01, string salt = TestSalt) =>
        new(new NeutralSampleOptions { Salt = salt, Rate = rate }, Normalizer);

    private static string Name(int i) => "alan" + i.ToString(CultureInfo.InvariantCulture) + ".example";

    [Fact]
    public void Same_domain_always_gets_the_same_decision_and_salt_changes_the_sample()
    {
        var first = Sampler();
        var second = Sampler();
        var otherSalt = Sampler(salt: "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100");
        var names = Enumerable.Range(0, 5000).Select(Name).ToList();

        var a = names.Where(n => first.Select([n]).Count == 1).ToList();
        var b = names.Where(n => second.Select([n]).Count == 1).ToList();
        var c = names.Where(n => otherSalt.Select([n]).Count == 1).ToList();

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Selected_share_is_close_to_the_rate()
    {
        // 100.000 ad, oran %1: beklenen 1.000, standart sapma ~31,5. Tuz sabit olduğu için sonuç her çalıştırmada aynı;
        // ±100 (yaklaşık ±3 sapma) sınırı kuralın oranı bozmadığını gösterir.
        var sampler = Sampler(0.01);
        var selected = Enumerable.Range(0, 100_000).Count(i => sampler.Select([Name(i)]).Count == 1);

        Assert.InRange(selected, 900, 1100);
    }

    [Fact]
    public void Threshold_is_rate_times_two_to_the_64()
    {
        Assert.Equal(184467440737095520UL, NeutralSampler.ThresholdFor(0.01));
        Assert.Equal(9223372036854775808UL, NeutralSampler.ThresholdFor(0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => NeutralSampler.ThresholdFor(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NeutralSampler.ThresholdFor(1));
    }

    [Fact]
    public void Raising_the_rate_keeps_every_previously_selected_domain()
    {
        // Eşik büyüdükçe seçilenler kümesi yalnızca genişler (karar 3: oran dondurmadan önce bir kez ayarlanabilir).
        var low = Sampler(0.01);
        var high = Sampler(0.02);
        var names = Enumerable.Range(0, 20_000).Select(Name).ToList();

        var lowSet = names.Where(n => low.Select([n]).Count == 1).ToHashSet();
        var highSet = names.Where(n => high.Select([n]).Count == 1).ToHashSet();

        Assert.Subset(highSet, lowSet);
    }

    [Fact]
    public void Decision_does_not_depend_on_keywords_in_the_name()
    {
        // Kelime içeren ve içermeyen adlarda seçilme payı aynı orana yakın olmalı: Kural ada bakmaz, yalnızca özete bakar.
        var sampler = Sampler(0.01);
        var withTerm = Enumerable.Range(0, 50_000).Count(i => sampler.Select(["yatirim" + i.ToString(CultureInfo.InvariantCulture) + ".example"]).Count == 1);
        var withoutTerm = Enumerable.Range(0, 50_000).Count(i => sampler.Select(["sade" + i.ToString(CultureInfo.InvariantCulture) + ".example"]).Count == 1);

        // Beklenen 500, sapma ~22.
        Assert.InRange(withTerm, 430, 570);
        Assert.InRange(withoutTerm, 430, 570);
    }

    [Fact]
    public void Tile_processor_samples_independently_of_the_keyword_filter()
    {
        using var log = new TestCtLog();
        log.AddEntries(256);
        var tile = log.DataTile(0);
        var sampler = Sampler(0.999999);

        var nothing = new TileProcessor(new MatchNothingFilter(), sampler).Process(tile, 0);
        var keyword = new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions()), sampler).Process(tile, 0);
        var noSampler = new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions())).Process(tile, 0);

        Assert.Empty(nothing.Candidates);
        Assert.Equal(256, nothing.NeutralSamples.Count);
        Assert.Equal(nothing.NeutralSamples, keyword.NeutralSamples);
        // Örnekleyici aday akışını değiştirmez.
        Assert.Equal(noSampler.Candidates.Count, keyword.Candidates.Count);
        Assert.Empty(noSampler.NeutralSamples);
    }

    [Fact]
    public void Subdomains_and_wildcards_reduce_to_one_registrable_domain()
    {
        var sampler = Sampler(0.999999);

        var selected = sampler.Select(["*.ornek-site.example", "panel.ornek-site.example", "ornek-site.example", "baska.com.tr"]);

        Assert.Equal(["baska.com.tr", "ornek-site.example"], selected);
    }

    [Fact]
    public void Follow_validation_requires_a_salt_and_never_echoes_it()
    {
        Assert.Throws<InvalidOperationException>(() => new NeutralSampleOptions { Salt = "" }.ValidateForFollow());
        new NeutralSampleOptions { Enabled = false }.ValidateForFollow();

        const string BadSalt = "gizli-tuz-zz";
        var ex = Assert.Throws<InvalidOperationException>(() => new NeutralSampleOptions { Salt = BadSalt }.ValidateForFollow());
        Assert.DoesNotContain(BadSalt, ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new NeutralSampleOptions { Salt = "00ff" }.ValidateForFollow());
        Assert.Throws<InvalidOperationException>(() => new NeutralSampleOptions { Salt = TestSalt, Rate = 1 }.ValidateForFollow());
    }

    private sealed class MatchNothingFilter : ICandidateFilter
    {
        public IReadOnlyList<string> Match(IReadOnlyList<string> dnsNames) => [];
    }
}

/// <summary>Örneklem tablosu: alan adı başına tek satır, tekrar işlemede sayaç şişmez.</summary>
[Trait("Category", "Integration")]
public sealed class NeutralSampleStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static CtNeutralSample Sample(string domain, string log, long index, DateTimeOffset observedAt) =>
        new(domain, log, index, observedAt.AddMinutes(-5), observedAt.AddHours(-1), observedAt);

    [Fact]
    public async Task One_row_per_domain_first_sighting_kept_and_replays_not_counted()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var store = new EfCtStateStore(db);

        var firstBatch = new[]
        {
            Sample("ornek-a.example", "log1", 10, T0),
            Sample("ornek-a.example", "log1", 11, T0),
            Sample("ornek-b.example", "log1", 12, T0),
        };
        var added = await store.CommitAsync(null, false, [], firstBatch, _ => [], ct);
        Assert.Equal(2, added);

        // Aynı kayıtlar ikinci kez gelir (ör. aynı aralık yeniden işlendi): yeni satır yok, sayaç artmaz.
        var replay = await store.CommitAsync(null, false, [], firstBatch, _ => [], ct);
        Assert.Equal(0, replay);

        // Sonraki bir kayıt ve başka bir log: sayaç artar, ilk görülme değişmez.
        var later = new[] { Sample("ornek-a.example", "log1", 300, T0.AddHours(1)), Sample("ornek-a.example", "log2", 5, T0.AddHours(2)) };
        Assert.Equal(0, await store.CommitAsync(null, false, [], later, _ => [], ct));

        db.ChangeTracker.Clear();
        var rows = await db.CtNeutralSamples.OrderBy(r => r.RegistrableDomain).ToListAsync(ct);
        Assert.Equal(2, rows.Count);
        var a = rows[0];
        Assert.Equal(4, a.SeenCount);
        Assert.Equal(T0, a.FirstSeenAt);
        Assert.Equal(("log1", 10L), (a.FirstLogName, a.FirstEntryIndex));
        Assert.Equal(("log2", 5L), (a.LastLogName, a.LastEntryIndex));
        Assert.Equal(T0.AddMinutes(-5), a.FirstLoggedAt);
        Assert.Equal(1, rows[1].SeenCount);
    }

    [Fact]
    public async Task Follower_writes_the_sample_in_the_same_transaction_and_counts_it()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var log = new TestCtLog();
        log.AddEntries(2 * 256);
        using var server = new FakeCtServer(log) { PublishedSize = log.Count };
        using var metrics = new StaticCtMetrics();
        var sampler = new NeutralSampler(
            new NeutralSampleOptions { Salt = "00112233445566778899aabbccddeeff", Rate = 0.999999 }, new PublicSuffixDomainNormalizer());

        var options = new StaticCtOptions
        {
            MinRequestInterval = TimeSpan.Zero, RetryBaseDelay = TimeSpan.Zero, MaxRetryAfter = TimeSpan.Zero,
            MaxRetryAttempts = 0, MaxConcurrency = 2, MaxTilesPerBatch = 32, MaxRangeAttempts = 2,
        };
        var logOptions = log.Options("0");
        var key = CtLogKey.Create(logOptions.Origin, logOptions.PublicKey);
        using var http = new HttpClient(server, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new StaticCtClient(
            new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System),
            logOptions, key, options.MaxResponseBytes);
        var follower = new StaticCtFollower(
            logOptions, 1, client, new EfCtStateStore(db),
            new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions()), sampler),
            options, new CollectionOptions { MeasurementLocation = "test-lab" }, metrics, TimeProvider.System, NullLogger.Instance);

        var result = await follower.RunOnceAsync(ct);

        Assert.Equal(CtBatchStatus.Advanced, result.Status);
        // Aday akışı değişmedi.
        Assert.Equal(log.KeywordCount(0, 512), result.NewCandidates);

        db.ChangeTracker.Clear();
        var rows = await db.CtNeutralSamples.ToListAsync(ct);
        // Test logundaki her kayıt tam bir kayıtlı alan adı taşır ve bu adlar dört adlık sabit bir kümeden gelir.
        // Oran ~1 olduğu için 512 kaydın hepsi sayılır, ama satır sayısı farklı ad sayısı kadardır (tekillik).
        string[] known = [TestCtLog.KeywordName, TestCtLog.IdnKeywordName, TestCtLog.PlainName, "baska-" + TestCtLog.PlainName];
        Assert.Equal(512, rows.Sum(r => r.SeenCount));
        Assert.All(rows, r => Assert.Contains(r.RegistrableDomain, known));
        Assert.InRange(rows.Count, 2, 4);
        Assert.Equal(rows.Count, rows.Select(r => r.RegistrableDomain).Distinct().Count());
        Assert.All(rows, r => Assert.True(r.FirstSeenAt > r.FirstLoggedAt));
    }
}
