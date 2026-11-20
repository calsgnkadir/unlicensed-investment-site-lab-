using System.Globalization;
using System.Net;
using System.Text;
using Fraud.Collectors.Control;
using Fraud.Infrastructure.DomainNames;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Control;

/// <summary>Kontrol listesi kurallarının saf testleri: ayrıştırıcılar (sabit örnek dosyalar), tohum, hafta, kota, DNS günü. Ağ yok.</summary>
public sealed class ControlRuleTests
{
    private static readonly PublicSuffixDomainNormalizer Full = new();
    private static readonly PublicSuffixDomainNormalizer Icann = PublicSuffixDomainNormalizer.IcannOnly();

    internal static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Control", "Fixtures", name);

    [Fact]
    public void Bddk_parser_reads_href_not_the_truncated_text_and_skips_navigation_and_footer()
    {
        var result = BddkBankListParser.Parse(File.ReadAllText(Fixture("bddk-ornek.html")));

        Assert.Equal(
            ["https://www.ornekbanka-a.example/", "http://ornekbanka-b.example", "https://internet.ornekbanka-c.example/tr/",
             "https://www.pozitif-ornek.example/", "https://banka.github.io/"],
            result.Urls);
        Assert.Equal(3, result.SkippedNotAddressText); // Twitter, simge, e-Devlet
        Assert.True(result.SkippedInternal >= 8);       // gezinme ve "Detay"
    }

    [Fact]
    public void Tranco_parser_stops_at_the_rank_limit_and_counts_malformed_lines()
    {
        using var reader = new StreamReader(Fixture("tranco-ornek.csv"));
        var (domains, malformed) = TrancoListParser.Parse(reader, maxRank: 5);

        Assert.Equal(5, domains.Count);
        Assert.Equal("haber-ornek.example", domains[0]);
        Assert.Equal(0, malformed);

        using var all = new StreamReader(Fixture("tranco-ornek.csv"));
        Assert.Equal((20, 1), (TrancoListParser.Parse(all, 1_000_000).Domains.Count, TrancoListParser.Parse(new StreamReader(Fixture("tranco-ornek.csv")), 1_000_000).Malformed));
    }

    [Fact]
    public void Radar_parser_separates_categories_empty_and_errors()
    {
        Assert.Equal(["Business & Economy", "News & Media"], RadarCategoryParser.Parse(File.ReadAllBytes(Fixture("radar-kategori.json"))));
        Assert.Empty(RadarCategoryParser.Parse(File.ReadAllBytes(Fixture("radar-kategorisiz.json")))!);
        Assert.Null(RadarCategoryParser.Parse(Encoding.UTF8.GetBytes("""{"success":false,"errors":[{"code":10000}]}""")));
        Assert.Null(RadarCategoryParser.Parse(Encoding.UTF8.GetBytes("<html>")));
    }

    [Fact]
    public void Tranco_api_parser_accepts_only_short_alphanumeric_list_ids()
    {
        var info = TrancoApiParser.ParseInfo(Encoding.UTF8.GetBytes("""{"list_id":"AB12C","available":true,"created_on":"2026-11-20T00:00:00"}"""));
        Assert.Equal(("AB12C", true), (info!.ListId, info.Available));
        Assert.Null(TrancoApiParser.ParseInfo(Encoding.UTF8.GetBytes("""{"list_id":"../../etc"}""")));
        Assert.False(TrancoApiParser.IsListId("a/b"));
    }

    [Fact]
    public void Seeded_order_is_deterministic_and_depends_on_the_seed()
    {
        var names = Enumerable.Range(0, 200).Select(i => "ad" + i.ToString(CultureInfo.InvariantCulture) + ".example").ToList();

        var a = ControlSeeds.Order(names, "kontrol-g4a-AB12C");
        var b = ControlSeeds.Order(names.AsEnumerable().Reverse(), "kontrol-g4a-AB12C");
        var c = ControlSeeds.Order(names, "kontrol-g4a-XY99Z");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal("kontrol-g3-2026-W47", ControlSeeds.For("G3", "2026-W47"));
    }

    [Fact]
    public void Iso_week_is_based_on_utc_and_monday()
    {
        Assert.Equal("2026-W47", IsoWeek.Of(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero)));
        // Pazar gece yarısından önce (UTC) hâlâ aynı hafta; pazartesi 00:00 UTC yeni hafta.
        Assert.Equal("2026-W47", IsoWeek.Of(new DateTimeOffset(2026, 11, 22, 23, 59, 59, TimeSpan.Zero)));
        Assert.Equal("2026-W48", IsoWeek.Of(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero)));
        var (start, end) = IsoWeek.Range("2026-W47");
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(start.AddDays(7), end);
        Assert.False(IsoWeek.TryParse("2026-47", out _, out _));
        Assert.False(IsoWeek.TryParse("2026-W54", out _, out _));
    }

    [Fact]
    public void Quota_rounding_uses_largest_remainder()
    {
        // 60 × (0,23 / 0,51 / 0,26) = 13,8 / 30,6 / 15,6 → 13 / 30 / 15 + kalan 2: 0,8 ve eşit 0,6'lardan önce gelen.
        Assert.Equal([14, 31, 15], QuotaRounding.LargestRemainder(60, AgeClasses.PositiveShares));
        Assert.Equal(7, QuotaRounding.LargestRemainder(7, AgeClasses.PositiveShares).Sum());
    }

    [Fact]
    public void Age_classes_follow_the_28_and_365_day_boundaries()
    {
        var seen = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(AgeClass.New, AgeClasses.Of(seen, seen.AddDays(-28)));
        Assert.Equal(AgeClass.Middle, AgeClasses.Of(seen, seen.AddDays(-29)));
        Assert.Equal(AgeClass.Middle, AgeClasses.Of(seen, seen.AddDays(-365)));
        Assert.Equal(AgeClass.Old, AgeClasses.Of(seen, seen.AddDays(-366)));
    }

    [Fact]
    public void Dns_days_split_members_about_half_and_half_deterministically()
    {
        var names = Enumerable.Range(0, 10_000).Select(i => "uye" + i.ToString(CultureInfo.InvariantCulture) + ".example").ToList();
        var saturday = new DateOnly(2026, 11, 21);
        var sunday = saturday.AddDays(1);
        IReadOnlyList<DayOfWeek> days = [DayOfWeek.Saturday, DayOfWeek.Sunday];

        var onSaturday = names.Count(n => ControlDnsSchedule.IsDue(n, saturday, days));
        var onSunday = names.Count(n => ControlDnsSchedule.IsDue(n, sunday, days));

        Assert.Equal(names.Count, onSaturday + onSunday);                 // her üye tam bir gün
        Assert.InRange(onSaturday, 4800, 5200);                           // ~yarı yarıya (beklenen sapma ~50)
        Assert.Equal(0, names.Count(n => ControlDnsSchedule.IsDue(n, saturday.AddDays(-1), days))); // cuma hiçbiri
        Assert.Equal(ControlDnsSchedule.Slot("ornek.example"), ControlDnsSchedule.Slot("ornek.example"));
    }

    [Fact]
    public void Eligibility_counts_every_elimination_code()
    {
        var check = new ControlEligibility(Full, Icann,
            new HashSet<string> { "pozitif-ornek.example" }, new HashSet<string> { "zaten-uye.example" });

        Assert.Equal("ornek.example", check.Check("https://www.ornek.example/yol"));
        Assert.Null(check.Check("panel.ornek.example"));       // tekrar (aynı kayıtlı alan adı)
        Assert.Null(check.Check("kisi.github.io"));            // E06: özel bölüm
        Assert.Null(check.Check("pozitif-ornek.example"));     // E01
        Assert.Null(check.Check("zaten-uye.example"));         // E02
        Assert.Null(check.Check("192.0.2.10"));                // E00

        Assert.Equal(1, check.Counts[EliminationCodes.E06]);
        Assert.Equal(1, check.Counts[EliminationCodes.E01]);
        Assert.Equal(1, check.Counts[EliminationCodes.E02]);
        Assert.Equal(1, check.Counts[EliminationCodes.E00]);
        Assert.Equal(1, check.Counts[EliminationCodes.Duplicate]);
    }

    [Fact]
    public void Frozen_lexicon_loads_only_with_the_frozen_hash_and_matches_the_left_label()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "docs", "research", "finans_sozlugu.txt"));
        var lexicon = FinanceLexicon.Load(bytes);
        Assert.Equal(55, lexicon.TermCount);

        var tampered = bytes.Concat(Encoding.UTF8.GetBytes("yeniterim\n")).ToArray();
        Assert.Throws<InvalidOperationException>(() => FinanceLexicon.Load(tampered));

        Assert.Equal(("yatirim", true), (lexicon.Match("yatirimci-ornek.example")!.Term, lexicon.Match("yatirimci-ornek.example")!.Baseline));
        Assert.Equal("wide", lexicon.Match("ornek-finans.example")!.Subset);
        Assert.Equal("yatirim", lexicon.Match(new IdnMapping().GetAscii("yatırım-ornek") + ".example")!.Term); // IDN + harf katlama
        Assert.Null(lexicon.Match("ornek.trade"));                   // TLD aranmaz
        Assert.Null(lexicon.Match("investigate-ornek.example"));     // hariç tutma: "investigat" silinir
        Assert.Equal("kripto", lexicon.Match("kripto-para.example")!.Term);
    }

    [Fact]
    public async Task Radar_token_travels_only_in_the_authorization_header()
    {
        const string Token = "gizli-radar-token-123";
        using var handler = new FakeHttpHandler().EnqueueText(File.ReadAllText(Fixture("radar-kategori.json")));
        using var radar = new RadarClient(Token, "FraudLab/0.1 (passive research)", handler);

        var lookup = await radar.CategoriesAsync("haber-ornek.example", TestContext.Current.CancellationToken);

        Assert.Equal(["Business & Economy", "News & Media"], lookup.Categories);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.cloudflare.com", request.Host);
        Assert.DoesNotContain(Token, request.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer " + Token, Assert.Single(handler.Authorizations));
    }

    [Fact]
    public void Missing_or_malformed_token_file_gives_no_client()
    {
        var dir = Directory.CreateTempSubdirectory("radar-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, Fraud.Collectors.Evaluation.RepoContext.MarkerFile), "");
            var repo = Fraud.Collectors.Evaluation.RepoContext.At(dir.FullName);
            Assert.Null(ControlCommandLine.RadarFromTokenFile(repo, new Dictionary<string, string?>()));

            var file = Path.Combine(dir.FullName, "token");
            File.WriteAllText(file, "iki parca token");
            Assert.Null(ControlCommandLine.RadarFromTokenFile(repo, new Dictionary<string, string?> { ["token-file"] = file }));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Tranco_zip_is_unpacked_and_plain_text_passes_through()
    {
        var plain = Encoding.UTF8.GetBytes("1,a.example\n");
        Assert.Equal(plain, TrancoClient.Unzip(plain));

        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry("top-1m.csv").Open())
        {
            entry.Write(plain);
        }

        Assert.Equal(plain, TrancoClient.Unzip(buffer.ToArray()));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FraudLab.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
