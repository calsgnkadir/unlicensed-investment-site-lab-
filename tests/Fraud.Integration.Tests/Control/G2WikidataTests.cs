using System.Text;
using System.Text.Json;
using Fraud.Collectors.Control;
using Fraud.Collectors.Evaluation;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Domain.Control;
using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Control;

/// <summary>
/// G2 Wikidata ile (B seçeneği). Sahte istemci; ağa çıkılmaz. Adlar uydurma (.example / .test).
/// </summary>
[Trait("Category", "Integration")]
public sealed class G2WikidataTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Fikstürdeki bütün adlar: hiçbiri ekrana, manifeste ya da deneme kaydına çıkmamalı (Kural 4).</summary>
    private static readonly string[] Names =
    [
        "finans-gazete", "ekonomi-dergi", "borsa-haber", "pozitif-ornek", "g1-banka", "finans-blog", "gizli-kullanici", "kotu-oge",
        "etiketsiz-tur", "trsiz", "192.0.2.10",
    ];

    private static readonly JsonSerializerOptions EnumJson = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("g2-wikidata-");
    private readonly FakeTimeProvider _time = new(Now);

    public void Dispose() => _root.Delete(recursive: true);

    private static byte[] RealQuery() => File.ReadAllBytes(Path.Combine(ControlRuleTests.RepoRoot(), G2WikidataQuery.RelativePath));

    private ControlContext Context(FraudDbContext db, StringWriter output)
    {
        File.WriteAllText(Path.Combine(_root.FullName, RepoContext.MarkerFile), "");
        return new ControlContext
        {
            Db = db,
            Full = new PublicSuffixDomainNormalizer(),
            IcannOnly = PublicSuffixDomainNormalizer.IcannOnly(),
            Repo = RepoContext.At(_root.FullName),
            Output = output,
            Time = _time,
            PinnedG2Query = RealQuery(),
        };
    }

    private sealed class FakeClient(Func<HttpFetchResult> respond) : IWikidataSparqlClient
    {
        public int Calls { get; private set; }

        public string UserAgent => "FraudLabG2/0.1 (https://github.com/ornek/depo) dotnet/10";

        public Task<HttpFetchResult> QueryAsync(string query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static HttpFetchResult Ok(byte[] body) => new(body, 200, 1, CollectionErrorKind.None);

    private static async Task SeedAsync(FraudDbContext db)
    {
        var ct = TestContext.Current.CancellationToken;
        var positive = new DomainName(new NormalizedDomain("pozitif-ornek.example", "pozitif-ornek.example", null, "example", "pozitif-ornek.example"), Now);
        db.Domains.Add(positive);
        db.OfficialActions.Add(new OfficialAction(positive, OfficialActionSource.SpkBulletin, "2026/12", new DateOnly(2026, 3, 4),
            OfficialActionCategory.Other, new Uri("https://spk.gov.tr/bulten/ornek"), positive.Host, Now));
        var bank = new DomainName(new NormalizedDomain("g1-banka.example", "g1-banka.example", null, "example", "g1-banka.example"), Now);
        db.Domains.Add(bank);
        db.ControlMembers.Add(new ControlMember(bank, ControlGroup.G1, null, Now, null, "kontrol-g1-x", "x", "kontrol-g1"));
        await db.SaveChangesAsync(ct);
    }

    private static void AssertNoNames(string text) =>
        Assert.All(Names, n => Assert.DoesNotContain(n, text, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Fetch_once_then_build_offline_selects_with_eliminations_and_turkey_label()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(db);
        using var output = new StringWriter();
        var ctx = Context(db, output);
        var fixture = File.ReadAllBytes(ControlRuleTests.Fixture("wikidata-g2-ornek.json"));
        var client = new FakeClient(() => Ok(fixture));

        Assert.Equal(0, await G2WikidataCommands.FetchAsync(ctx, client, ct));
        var record = Directory.GetFiles(G2WikidataCommands.AttemptDirectory(ctx), "deneme-*.json").Single();
        var recordText = await File.ReadAllTextAsync(record, ct);
        Assert.Contains("\"Succeeded\"", recordText, StringComparison.Ordinal);
        var raw = Directory.GetFiles(Path.Combine(ctx.ControlDirectory, "src"), "wikidata-g2-*.json").Single();
        Assert.Equal(fixture, await File.ReadAllBytesAsync(raw, ct));

        // Başarılı cevaptan sonra yeni istek atılmaz.
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, await G2WikidataCommands.FetchAsync(ctx, client, ct));
        Assert.Equal(1, client.Calls);

        Assert.Equal(0, await G2WikidataCommands.BuildAsync(ctx, raw, ct));
        var members = await db.ControlMembers.Where(m => m.Group == ControlGroup.G2)
            .Select(m => new { m.Domain.RegistrableDomain, m.TurkeyLinked }).ToListAsync(ct);
        // Elle beklenen: 13 satır, 3 geçersiz satır (öğe adresi, site türü, tr yok); 10 adresten 3'ü E00 (javascript:, IP,
        // kullanıcı bilgisi); kalan 7 ana bilgisayardan: 1 tekrar (www. ve çıplak ad), 1 E01, 1 E02, 1 E06 (github.io) → 3 üye.
        Assert.Equal(3, members.Count);
        Assert.True(members.Single(m => m.RegistrableDomain == "finans-gazete.example").TurkeyLinked); // satırlardan biri TR: VEYA
        Assert.All(members.Where(m => m.RegistrableDomain != "finans-gazete.example"), m => Assert.False(m.TurkeyLinked));

        var build = await db.ControlBuilds.SingleAsync(b => b.Group == ControlGroup.G2, ct);
        using (var counts = JsonDocument.Parse(build.Counts))
        {
            var c = counts.RootElement;
            Assert.Equal(13, c.GetProperty("wikidata_satir").GetInt32());
            Assert.Equal(3, c.GetProperty("wikidata_gecersiz_satir").GetInt32());
            Assert.Equal(3, c.GetProperty(EliminationCodes.E00).GetInt32());
            Assert.Equal(1, c.GetProperty(EliminationCodes.Duplicate).GetInt32());
            Assert.Equal(1, c.GetProperty(EliminationCodes.E01).GetInt32());
            Assert.Equal(1, c.GetProperty(EliminationCodes.E02).GetInt32());
            Assert.Equal(1, c.GetProperty(EliminationCodes.E06).GetInt32());
            Assert.Equal(1, c.GetProperty("secilen_turkiye_etiketli").GetInt32());
        }

        Assert.Contains("Yeterli örnek yok", output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root.FullName, ControlCommands.PublicCommitmentDirectory, "kontrol-g2.json")));
        var manifest = await File.ReadAllTextAsync(Path.Combine(ctx.ControlDirectory, "kontrol-g2.manifest.json"), ct);
        Assert.Contains(G2WikidataQuery.Parse(RealQuery())!.Sha256, manifest, StringComparison.Ordinal);

        // İdempotent: İkinci kurma reddedilir, üye sayısı değişmez.
        Assert.Equal(2, await G2WikidataCommands.BuildAsync(ctx, raw, ct));
        Assert.Equal(3, await db.ControlMembers.CountAsync(m => m.Group == ControlGroup.G2, ct));

        // Kural 4: ekran, deneme kaydı, manifest ve açık taahhüt adsız.
        AssertNoNames(output.ToString());
        AssertNoNames(recordText);
        AssertNoNames(manifest);
        AssertNoNames(await File.ReadAllTextAsync(Path.Combine(_root.FullName, ControlCommands.PublicCommitmentDirectory, "kontrol-g2.json"), ct));
    }

    [Fact]
    public async Task Attempts_are_limited_to_three_spaced_and_failed_ones_count()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var output = new StringWriter();
        var ctx = Context(db, output);
        var client = new FakeClient(() => new HttpFetchResult(null, 503, 1, CollectionErrorKind.ServerError));

        Assert.Equal(2, await G2WikidataCommands.FetchAsync(ctx, client, ct));
        // 60 saniye geçmeden istek atılmaz.
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(75, await G2WikidataCommands.FetchAsync(ctx, client, ct));
        Assert.Equal(1, client.Calls);

        _time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(2, await G2WikidataCommands.FetchAsync(ctx, client, ct));
        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(2, await G2WikidataCommands.FetchAsync(ctx, client, ct));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, await G2WikidataCommands.FetchAsync(ctx, client, ct));

        Assert.Equal(3, client.Calls);
        Assert.Equal(3, Directory.GetFiles(G2WikidataCommands.AttemptDirectory(ctx), "deneme-*.json").Length);
        Assert.Contains("Deneme hakkı bitti", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interrupted_attempt_counts_and_rate_limit_needs_a_longer_wait()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var output = new StringWriter();
        var ctx = Context(db, output);

        // Yarıda kesilmiş deneme: yalnızca "başladı" kaydı var (süreç istekten sonra öldü).
        var rateLimited = new FakeClient(() => new HttpFetchResult(null, 429, 1, CollectionErrorKind.RateLimited));
        Assert.Equal(2, await G2WikidataCommands.FetchAsync(ctx, rateLimited, ct));
        _time.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(75, await G2WikidataCommands.FetchAsync(ctx, rateLimited, ct)); // 429 sonrası 5 dakika
        _time.Advance(TimeSpan.FromMinutes(5));

        var dir = G2WikidataCommands.AttemptDirectory(ctx);
        var started = new G2WikidataCommands.AttemptRecord(2, G2WikidataCommands.AttemptStatus.Started, _time.GetUtcNow(), null, "x", "ua",
            null, null, null, null, null);
        await File.WriteAllTextAsync(Path.Combine(dir, "deneme-2.json"),
            JsonSerializer.Serialize(started, EnumJson), ct);
        _time.Advance(TimeSpan.FromMinutes(2));

        var ok = new FakeClient(() => Ok(File.ReadAllBytes(ControlRuleTests.Fixture("wikidata-g2-ornek.json"))));
        Assert.Equal(0, await G2WikidataCommands.FetchAsync(ctx, ok, ct)); // 3. ve son hak
        Assert.True(File.Exists(Path.Combine(dir, "deneme-3.json")));
    }

    [Fact]
    public async Task Build_refuses_an_input_that_no_successful_attempt_recorded()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        using var output = new StringWriter();
        var ctx = Context(db, output);
        var tampered = Path.Combine(_root.FullName, "elle.json");
        await File.WriteAllBytesAsync(tampered, File.ReadAllBytes(ControlRuleTests.Fixture("wikidata-g2-ornek.json")), ct);

        Assert.Equal(2, await G2WikidataCommands.BuildAsync(ctx, tampered, ct));
        Assert.Equal(0, await db.ControlMembers.CountAsync(m => m.Group == ControlGroup.G2, ct));
        Assert.Contains("SHA-256 eşleşmedi", output.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>Saf kurallar: sorgu dosyası, iletişim adresi, ayrıştırıcıya saldırgan girdi. Veritabanı ve ağ yok.</summary>
public sealed class G2WikidataRuleTests
{
    [Fact]
    public void Frozen_query_file_parses_with_its_seed()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ControlRuleTests.RepoRoot(), G2WikidataQuery.RelativePath));
        var query = G2WikidataQuery.Parse(bytes)!;
        Assert.Equal("kontrol-g2-wikidata-2026-11-21", query.Seed);
        Assert.Equal(64, query.Sha256.Length);
        Assert.DoesNotContain("EDU_CLASS", query.Text, StringComparison.Ordinal); // eğitim kolu çıkarıldı

        Assert.Null(G2WikidataQuery.Parse(Encoding.UTF8.GetBytes("SELECT ?x WHERE {}")));                       // tohum yok
        Assert.Null(G2WikidataQuery.Parse(Encoding.UTF8.GetBytes("#tohum: baska-tohum\nSELECT ?x WHERE {}"))); // biçim dışı
        Assert.Null(G2WikidataQuery.Parse(new byte[G2WikidataQuery.MaxBytes + 1]));
    }

    [Fact]
    public void Contact_url_is_only_a_github_repository_without_email()
    {
        Assert.True(ControlSourcesFile.IsContactUrl("https://github.com/ornek/depo-adi-"));
        Assert.False(ControlSourcesFile.IsContactUrl("mailto:kisi@ornek.example"));
        Assert.False(ControlSourcesFile.IsContactUrl("https://github.com/ornek/depo?kisi@ornek.example"));
        Assert.False(ControlSourcesFile.IsContactUrl("https://github.com.ornek.example/a/b"));
        Assert.False(ControlSourcesFile.IsContactUrl("https://github.com/ornek"));
        Assert.False(ControlSourcesFile.IsContactUrl("https://github.com/a/b\r\nX-Header: 1"));

        // Repodaki gerçek dosya: Tranco aynı kaldı, Wikidata iletişim adresi kurala uyuyor.
        var sources = ControlSourcesFile.Parse(File.ReadAllBytes(Path.Combine(ControlRuleTests.RepoRoot(), ControlSourcesFile.RelativePath)))!;
        Assert.Equal("GQ9JK", sources.TrancoListId);
        Assert.True(ControlSourcesFile.IsContactUrl(sources.WikidataContactUrl));
        Assert.Null(ControlSourcesFile.Parse(Encoding.UTF8.GetBytes(
            """{"tranco":{"listId":"AB12C","date":"2026-11-21"},"wikidata":{"contactUrl":"kisi@ornek.example"}}""")));
    }

    [Theory]
    [InlineData("https://www.ornek.example/", "www.ornek.example")]
    [InlineData("http://ORNEK.example./yol?q=1", "ornek.example")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,merhaba", null)]
    [InlineData("ftp://ornek.example/", null)]
    [InlineData("https://192.0.2.10/", null)]
    [InlineData("https://[2001:db8::1]/", null)]
    [InlineData("https://kisi:sifre@ornek.example/", null)]
    [InlineData("https://ornek.example:8443/", null)]
    [InlineData("//ornek.example/", null)]
    [InlineData("ornek.example", null)]
    public void Host_extraction_rejects_unsafe_urls(string url, string? expected) =>
        Assert.Equal(expected, WikidataSparqlParser.HostOf(url));

    [Fact]
    public void Parser_rejects_huge_deep_or_malformed_responses()
    {
        // Dev cevap: boyut sınırının bir bayt üstü, içeriğe bakılmadan reddedilir.
        Assert.Null(WikidataSparqlParser.Parse(new byte[WikidataSparqlParser.MaxBytes + 1]));

        // Çok satır: sınırın bir üstü.
        var many = new StringBuilder("""{"results":{"bindings":[""");
        for (var i = 0; i <= WikidataSparqlParser.MaxRows; i++)
        {
            many.Append(i == 0 ? "{}" : ",{}");
        }

        Assert.Null(WikidataSparqlParser.Parse(Encoding.UTF8.GetBytes(many.Append("]}}").ToString())));

        // Derin iç içe yapı.
        Assert.Null(WikidataSparqlParser.Parse(Encoding.UTF8.GetBytes(new string('[', 100) + new string(']', 100))));
        Assert.Null(WikidataSparqlParser.Parse(Encoding.UTF8.GetBytes("bu json değil")));
        Assert.Null(WikidataSparqlParser.Parse(Encoding.UTF8.GetBytes("""{"results":{"bindings":"dizi değil"}}""")));

        // Çok uzun adres geçersiz satır sayılır, ayrıştırma sürer.
        var longUrl = "https://" + new string('a', WikidataSparqlParser.MaxUrlLength) + ".example/";
        var json = """{"results":{"bindings":[{"item":{"type":"uri","value":"http://www.wikidata.org/entity/Q1"},"site":{"type":"uri","value":"URL"},"tr":{"type":"literal","value":"false"}}]}}"""
            .Replace("URL", longUrl, StringComparison.Ordinal);
        var parsed = WikidataSparqlParser.Parse(Encoding.UTF8.GetBytes(json))!;
        Assert.Equal((1, 1, 0), (parsed.Rows, parsed.InvalidRows, parsed.Sites.Count));
    }
}
