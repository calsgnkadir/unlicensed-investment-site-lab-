using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fraud.Collectors.Wayback;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Fetching;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Integration.Tests.Collectors;

// Aşama 0 arşiv içeriği örneği: metin çıkarma, kişi verisi temizliği, arşiv adresi ve dosya çıktısı.
// Ağ yok: Çekici sahte, alan adları uydurma (.example).
public sealed class ArchiveTextExtractorTests
{
    [Fact]
    public void Removes_script_style_noscript_template_svg_head_and_comments()
    {
        const string html = """
            <html><head><meta charset="utf-8"><title>Başlık</title><style>.x{color:red}</style>
            <script>var gizli = "SCRIPT_ICERIGI";</script></head>
            <body>
            <!-- YORUM_ICERIGI <p>yorumdaki paragraf</p> -->
            <noscript>NOSCRIPT_ICERIGI</noscript>
            <template><p>TEMPLATE_ICERIGI</p></template>
            <svg><style>SVG_STYLE</style><text>SVG_METNI</text></svg>
            <p>Görünür metin</p>
            <SCRIPT type="text/javascript">BUYUK_HARF_SCRIPT</SCRIPT>
            </body></html>
            """;

        var page = ArchiveTextExtractor.Extract(html);

        Assert.Equal("Başlık", page.Title);
        Assert.Equal("Görünür metin", page.Text);
    }

    [Fact]
    public void Decodes_entities_and_strips_tags()
    {
        var page = ArchiveTextExtractor.Extract(
            "<p>Ali &amp; Veli&#39;nin <b>g&uuml;venli</b>&nbsp;yat&#305;r&#x131;m&nbsp;&nbsp;planı &lt;script&gt;</p>");

        // Çözülen &lt;script&gt; artık düz metindir; etiket olarak işlenmez.
        Assert.Equal("Ali & Veli'nin güvenli yatırım planı <script>", page.Text);
    }

    [Fact]
    public void Block_tags_become_lines_and_whitespace_is_collapsed()
    {
        var page = ArchiveTextExtractor.Extract("<div>  Bir\n\n   iki  </div><div></div><br><p>üç</p>");

        Assert.Equal("Bir iki\nüç", page.Text);
    }

    [Fact]
    public void Unclosed_script_hides_the_rest_and_unclosed_comment_too()
    {
        Assert.Equal("önce", ArchiveTextExtractor.Extract("<p>önce</p><script>alert(1) <p>sonra</p>").Text);
        Assert.Equal("önce", ArchiveTextExtractor.Extract("<p>önce</p><!-- <p>sonra</p>").Text);
    }

    [Fact]
    public void Wayback_toolbar_block_is_removed()
    {
        const string html = "<body><!-- BEGIN WAYBACK TOOLBAR INSERT --><div>ARAC_CUBUGU</div><!-- END WAYBACK TOOLBAR INSERT --><p>Sayfa</p></body>";

        Assert.Equal("Sayfa", ArchiveTextExtractor.Extract(html).Text);
    }

    [Fact]
    public void Control_and_bidi_characters_are_removed()
    {
        // ESC ile başlayan terminal kaçış dizisi ve sağdan sola zorlama (U+202E) dosyaya geçmemeli.
        var html = "<p>a" + (char)27 + "[31mb" + (char)0x202E + "c" + (char)0x200B + "d</p>";

        Assert.Equal("a[31mbcd", ArchiveTextExtractor.Extract(html).Text);
    }

    [Fact]
    public void Missing_title_and_empty_input_give_empty_strings()
    {
        Assert.Equal(string.Empty, ArchiveTextExtractor.Extract("<p>x</p>").Title);
        Assert.Equal(new ArchiveText(string.Empty, string.Empty), ArchiveTextExtractor.Extract(null));
    }

    [Fact]
    public void Cap_limits_length_without_splitting_surrogate_pairs()
    {
        var page = ArchiveTextExtractor.Extract("<p>" + new string('a', 5000) + "</p>");
        var capped = ArchiveTextExtractor.Cap(page);
        Assert.Equal(ArchiveTextExtractor.DefaultMaxTextChars, capped.Text.Length);

        var emoji = char.ConvertFromUtf32(0x1F600);
        var cut = ArchiveTextExtractor.Cap("abc" + emoji, 4);
        Assert.Equal("abc", cut);
    }

    [Fact]
    public void Large_hostile_input_finishes()
    {
        // Kapanmayan çok sayıda etiket geri izleme patlaması yaratmamalı (NonBacktracking).
        var html = string.Concat(Enumerable.Repeat("<script <a <!-", 50_000));

        var page = ArchiveTextExtractor.Extract(html);

        Assert.NotNull(page.Text);
    }
}

public sealed class PersonalDataRedactorTests
{
    [Theory]
    [InlineData("İletişim: destek.ekibi@ornek-yatirim.example adresine yazın", "İletişim: [E-POSTA] adresine yazın")]
    [InlineData("Ad.Soyad+not@posta.example.com.tr", "[E-POSTA]")]
    public void Emails_are_redacted(string input, string expected) =>
        Assert.Equal(expected, PersonalDataRedactor.Redact(input));

    [Theory]
    [InlineData("Ara: 0532 123 45 67 hemen")]
    [InlineData("Ara: +90 532 123 45 67 hemen")]
    [InlineData("Ara: +90 (532) 123-45-67 hemen")]
    [InlineData("Ara: 0 (532) 123 45 67 hemen")]
    [InlineData("Ara: 05321234567 hemen")]
    [InlineData("Ara: +905321234567 hemen")]
    [InlineData("Ara: 0532.123.45.67 hemen")]
    [InlineData("Ara: 532 123 4567 hemen")]
    [InlineData("Ara: 0090 532 123 45 67 hemen")]
    [InlineData("Ara: 0212 555 12 34 hemen")]
    public void Turkish_phone_formats_are_redacted(string input)
    {
        var result = PersonalDataRedactor.Redact(input);

        Assert.Equal("Ara: [TELEFON] hemen", result);
    }

    [Theory]
    [InlineData("IBAN: TR33 0006 1005 1978 6457 8413 26 adına", "IBAN: [IBAN] adına")]
    [InlineData("IBAN: TR330006100519786457841326", "IBAN: [IBAN]")]
    [InlineData("IBAN: DE89 3704 0044 0532 0130 00", "IBAN: [IBAN]")]
    public void Ibans_are_redacted(string input, string expected) =>
        Assert.Equal(expected, PersonalDataRedactor.Redact(input));

    [Theory]
    [InlineData("Kanal: https://t.me/ornek_kanal katıl", "Kanal: [MESAJ_BAGLANTISI] katıl")]
    [InlineData("Kanal: t.me/+AbCdEf katıl", "Kanal: [MESAJ_BAGLANTISI] katıl")]
    [InlineData("Yaz: https://wa.me/905321234567 şimdi", "Yaz: [MESAJ_BAGLANTISI] şimdi")]
    [InlineData("Yaz: https://api.whatsapp.com/send?phone=905321234567 şimdi", "Yaz: [MESAJ_BAGLANTISI] şimdi")]
    [InlineData("Yaz: whatsapp://send?phone=905321234567 şimdi", "Yaz: [MESAJ_BAGLANTISI] şimdi")]
    public void Messaging_links_are_redacted(string input, string expected) =>
        Assert.Equal(expected, PersonalDataRedactor.Redact(input));

    [Theory]
    [InlineData("2026 yılında %12 getiri")]
    [InlineData("Fiyat: 1.250 TL, kampanya 49.990,00 TL")]
    [InlineData("Kampanya 01.01.2026 - 31.12.2026 arası")]
    [InlineData("SPK lisans no 123456")]
    [InlineData("WhatsApp destek hattımız açıktır")]
    public void Ordinary_numbers_and_words_are_kept(string input) =>
        Assert.Equal(input, PersonalDataRedactor.Redact(input));

    [Fact]
    public void Links_and_the_sample_domain_are_replaced_but_other_names_stay()
    {
        var result = PersonalDataRedactor.RedactLinksAndDomain(
            "Ornek-Yatirim.example ve panel.ornek-yatirim.example, bkz. https://baska.example/a?b=c ve www.baska.example. SPK lisanslıyız.",
            "ornek-yatirim.example");

        Assert.Equal("[ALAN_ADI] ve [ALAN_ADI], bkz. [BAGLANTI] ve [BAGLANTI] SPK lisanslıyız.", result);
    }
}

public sealed class ArchivePageUrlTests
{
    private const string Domain = "ornek-yatirim.example";

    [Theory]
    [InlineData("http://ornek-yatirim.example/", "https://web.archive.org/web/20260514093000id_/http://ornek-yatirim.example/")]
    [InlineData("https://www.ornek-yatirim.example/hakkimizda", "https://web.archive.org/web/20260514093000id_/https://www.ornek-yatirim.example/hakkimizda")]
    [InlineData("http://ornek-yatirim.example", "https://web.archive.org/web/20260514093000id_/http://ornek-yatirim.example/")]
    [InlineData("http://ornek-yatirim.example:8080/a", "https://web.archive.org/web/20260514093000id_/http://ornek-yatirim.example:8080/a")]
    [InlineData("http://ornek-yatirim.example/a?email=x#y", "https://web.archive.org/web/20260514093000id_/http://ornek-yatirim.example/a")]
    public void Builds_raw_archive_url(string original, string expected)
    {
        Assert.True(ArchivePageUrl.TryBuild("20260514093000", original, Domain, out var url, out var rejection));

        Assert.Equal(ArchiveUrlRejection.None, rejection);
        Assert.Equal(expected, url!.AbsoluteUri);
        Assert.Equal("web.archive.org", url.Host);
    }

    [Theory]
    [InlineData("http://baska.example/", ArchiveUrlRejection.HostNotUnderDomain)]
    [InlineData("http://kotu-ornek-yatirim.example/", ArchiveUrlRejection.HostNotUnderDomain)]
    [InlineData("http://ornek-yatirim.example.kotu.example/", ArchiveUrlRejection.HostNotUnderDomain)]
    [InlineData("http://127.0.0.1/", ArchiveUrlRejection.HostNotUnderDomain)]
    [InlineData("ftp://ornek-yatirim.example/", ArchiveUrlRejection.SchemeNotAllowed)]
    [InlineData("javascript:alert(1)", ArchiveUrlRejection.SchemeNotAllowed)]
    [InlineData("file:///etc/passwd", ArchiveUrlRejection.SchemeNotAllowed)]
    [InlineData("http://kisi@ornek-yatirim.example/", ArchiveUrlRejection.UserInfoNotAllowed)]
    [InlineData("goreli/yol", ArchiveUrlRejection.InvalidUrl)]
    [InlineData("", ArchiveUrlRejection.InvalidUrl)]
    public void Rejects_unsafe_original_urls(string original, ArchiveUrlRejection expected)
    {
        Assert.False(ArchivePageUrl.TryBuild("20260514093000", original, Domain, out var url, out var rejection));

        Assert.Null(url);
        Assert.Equal(expected, rejection);
    }

    [Theory]
    [InlineData("2026051409300")]
    [InlineData("20261399000000")]
    [InlineData("2026051409300a")]
    [InlineData(null)]
    public void Rejects_bad_timestamps(string? timestamp)
    {
        Assert.False(ArchivePageUrl.TryBuild(timestamp, "http://ornek-yatirim.example/", Domain, out _, out var rejection));
        Assert.Equal(ArchiveUrlRejection.InvalidTimestamp, rejection);
    }

    [Fact]
    public void Rejects_single_label_domain() =>
        Assert.Equal(ArchiveUrlRejection.InvalidDomain,
            ArchivePageUrl.TryBuild("20260514093000", "http://ornek.example/", "example", out _, out var r) ? ArchiveUrlRejection.None : r);

    [Fact]
    public void Parses_final_url_after_redirect()
    {
        var final = new Uri("https://web.archive.org/web/20260512000000id_/http://www.ornek-yatirim.example/");

        Assert.True(ArchivePageUrl.TryParse(final, Domain, out var timestamp, out _));
        Assert.Equal("20260512000000", timestamp);

        Assert.False(ArchivePageUrl.TryParse(new Uri("https://web.archive.org/web/20260512000000/http://ornek-yatirim.example/"), Domain, out _, out _));
        Assert.False(ArchivePageUrl.TryParse(new Uri("https://web.archive.org/web/20260512000000id_/http://baska.example/"), Domain, out _, out _));
        Assert.False(ArchivePageUrl.TryParse(new Uri("https://ornek-yatirim.example/web/20260512000000id_/http://ornek-yatirim.example/"), Domain, out _, out _));
    }

    [Fact]
    public void Homepage_detection()
    {
        Assert.True(ArchivePageUrl.IsHomepage("http://ornek-yatirim.example"));
        Assert.True(ArchivePageUrl.IsHomepage("https://www.ornek-yatirim.example/"));
        Assert.False(ArchivePageUrl.IsHomepage("http://ornek-yatirim.example/giris"));
    }
}

public sealed class ArchiveSampleRunnerTests
{
    private const string Domain = "ornek-yatirim.example";
    private static readonly DateOnly Decided = new(2026, 8, 14);

    [Fact]
    public void Selector_prefers_homepage_then_latest_and_ignores_post_decision()
    {
        var captures = new[]
        {
            new CaptureCandidate("20260801000000", At(2026, 8, 1), "http://ornek-yatirim.example/giris"),
            new CaptureCandidate("20260601000000", At(2026, 6, 1), "http://ornek-yatirim.example/"),
            new CaptureCandidate("20260701000000", At(2026, 7, 1), "https://www.ornek-yatirim.example/"),
            new CaptureCandidate("20260814000000", At(2026, 8, 14), "http://ornek-yatirim.example/"),
        };

        Assert.Equal("20260701000000", ArchiveSampleSelector.Pick(captures, Decided)!.CdxTimestamp);
        Assert.Equal("20260801000000", ArchiveSampleSelector.Pick(captures.Take(1), Decided)!.CdxTimestamp);
        Assert.Null(ArchiveSampleSelector.Pick(captures.Skip(3), Decided));
    }

    [Fact]
    public async Task Writes_samples_without_domain_and_records_errors_as_errors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fraudlab-test-" + Guid.NewGuid().ToString("N"), "private", "arsiv");
        try
        {
            const string html = """
                <html><head><title>Ornek Yatirim | ornek-yatirim.example</title></head>
                <body><p>Garantili kazanç! Bize yazın: destek@ornek-yatirim.example, 0532 123 45 67</p>
                <p>Detay: https://ornek-yatirim.example/kayit ve https://t.me/kanal</p></body></html>
                """;
            var fetcher = new QueueFetcher()
                .Add(new SafeFetchResult(SafeFetchStatus.Success, SafeFetchBlockReason.None,
                    new Uri("https://web.archive.org/web/20260601000000id_/http://ornek-yatirim.example/"), 200, "text/html", html, html.Length, 0))
                .Add(SafeFetchResult.Failed(SafeFetchStatus.Timeout, null, null, 0))
                .Add(new SafeFetchResult(SafeFetchStatus.Success, SafeFetchBlockReason.None,
                    new Uri("https://web.archive.org/web/20260901000000id_/http://ornek-yatirim.example/"), 200, "text/html", html, html.Length, 1));

            var items = new[]
            {
                Item(1, "http://ornek-yatirim.example/"),
                Item(2, "http://ornek-yatirim.example/"),
                Item(3, "http://ornek-yatirim.example/"),
                Item(4, "http://baska.example/"),
            };

            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "17.txt"), "eski", TestContext.Current.CancellationToken);

            var results = await new ArchiveSampleRunner(fetcher).RunAsync(items, dir, TestContext.Current.CancellationToken);

            Assert.Equal(["Basarili", "Hata:Timeout", "Hata:KararSonrasiKayit", "Hata:AdresReddedildi(HostNotUnderDomain)"],
                results.Select(r => r.Outcome));
            // Reddedilen adres için istek gönderilmedi; giden istekler yalnızca web.archive.org'a.
            Assert.Equal(3, fetcher.Requests.Count);
            Assert.All(fetcher.Requests, u => Assert.Equal("web.archive.org", u.Host));
            Assert.Equal("https://web.archive.org/web/20260601000000id_/http://ornek-yatirim.example/", fetcher.Requests[0].AbsoluteUri);

            Assert.False(File.Exists(Path.Combine(dir, "17.txt")));
            var first = await File.ReadAllTextAsync(Path.Combine(dir, "01.txt"), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("ornek-yatirim", first, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http", first, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0532", first, StringComparison.Ordinal);
            Assert.DoesNotContain("destek@", first, StringComparison.Ordinal);
            Assert.Contains("ornek: 01\n", first, StringComparison.Ordinal);
            Assert.Contains("bulten: 2026/46\n", first, StringComparison.Ordinal);
            Assert.Contains("arsiv_tarihi: 2026-06-01\n", first, StringComparison.Ordinal);
            Assert.Contains("karardan_once_gun: 74\n", first, StringComparison.Ordinal);
            Assert.Contains("ilk_sertifikadan_karara_gun: 104\n", first, StringComparison.Ordinal);
            Assert.Contains("ana_sayfa: evet\n", first, StringComparison.Ordinal);
            Assert.Contains("sonuc: Basarili\n", first, StringComparison.Ordinal);
            Assert.Contains("Garantili kazanç! Bize yazın: [E-POSTA], [TELEFON]", first, StringComparison.Ordinal);
            Assert.Contains("[MESAJ_BAGLANTISI]", first, StringComparison.Ordinal);
            Assert.Contains("--- BASLIK ---\nOrnek Yatirim | [ALAN_ADI]\n", first, StringComparison.Ordinal);

            // Hata metin olarak "boş sayfa" yazılmaz: sonuç hata türünü taşır, metin bölümü boştur.
            var second = await File.ReadAllTextAsync(Path.Combine(dir, "02.txt"), TestContext.Current.CancellationToken);
            Assert.Contains("sonuc: Hata:Timeout\n", second, StringComparison.Ordinal);
            Assert.DoesNotContain("ornek-yatirim", second, StringComparison.OrdinalIgnoreCase);

            var fourth = await File.ReadAllTextAsync(Path.Combine(dir, "04.txt"), TestContext.Current.CancellationToken);
            Assert.DoesNotContain("baska", fourth, StringComparison.Ordinal);

            var csv = await File.ReadAllLinesAsync(Path.Combine(dir, ArchiveSampleRunner.SelectionFileName), TestContext.Current.CancellationToken);
            Assert.Equal("ornek,domain,bulletin,cdx_timestamp,original_url,outcome", csv[0]);
            Assert.Equal("\"01\",\"ornek-yatirim.example\",\"2026/46\",\"20260601000000\",\"http://ornek-yatirim.example/\",\"Basarili\"", csv[1]);
            Assert.Equal(5, csv.Length);

            var summary = ArchiveSampleCommand.Summarize("tohum", 30, results);
            Assert.DoesNotContain("ornek-yatirim", summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("baska", summary, StringComparison.Ordinal);
            Assert.Contains("Hata:Timeout: 1", summary, StringComparison.Ordinal);
        }
        finally
        {
            var root = Directory.GetParent(Directory.GetParent(dir)!.FullName)!.FullName;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ArchiveSampleItem Item(int number, string original) =>
        new(number, number, Domain, "2026/46", "Website", Decided, new DateOnly(2026, 5, 2),
            new CaptureCandidate("20260601000000", At(2026, 6, 1), original));

    private static DateTimeOffset At(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    private sealed class QueueFetcher : ISafeFetcher
    {
        private readonly Queue<SafeFetchResult> _results = new();

        public List<Uri> Requests { get; } = [];

        public QueueFetcher Add(SafeFetchResult result)
        {
            _results.Enqueue(result);
            return this;
        }

        public Task<SafeFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default)
        {
            Requests.Add(url);
            return Task.FromResult(_results.Dequeue());
        }
    }
}

// Seçim sorgusu gerçek PostgreSQL üzerinde (Testcontainers). Ağ yok.
[Trait("Category", "Integration")]
public sealed class ArchiveSampleSelectionTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Observed = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Selects_domains_with_recent_first_cert_and_pre_decision_html_capture()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await postgres.CreateMigratedContextAsync();

        var decided = new DateOnly(2026, 8, 14);
        var recent = await AddDomainAsync(db, "yakin.example", decided, "2026-05-02");          // 104 gün: uygun
        var boundary = await AddDomainAsync(db, "sinir.example", decided, "2025-08-14");        // tam 365 gün: uygun
        var tooOld = await AddDomainAsync(db, "eski.example", decided, "2025-08-13");           // 366 gün: dışarıda
        var after = await AddDomainAsync(db, "sonra.example", decided, "2026-08-14");           // 0 gün: dışarıda
        var postOnly = await AddDomainAsync(db, "gec.example", decided, "2026-05-02");          // yalnızca karar sonrası kayıt
        var redirectOnly = await AddDomainAsync(db, "yonlendirme.example", decided, "2026-05-02"); // yalnızca 302
        var twoBulletins = await AddDomainAsync(db, "iki.example", decided, "2026-05-02");

        // İkinci bülten daha önce: karar tarihi en erken olan, bülten numarası en küçük olan alınır.
        await AddActionAsync(db, twoBulletins, "2026/40", new DateOnly(2026, 7, 1));

        await AddCaptureAsync(db, recent, "20260601000000", "http://yakin.example/giris", 200, "text/html");
        await AddCaptureAsync(db, recent, "20260501000000", "http://www.yakin.example/", 200, "text/html");
        await AddCaptureAsync(db, recent, "20260813235959", "http://yakin.example/", 200, "text/plain");
        await AddCaptureAsync(db, boundary, "20260101000000", "http://sinir.example/a", 200, "text/html");
        await AddCaptureAsync(db, tooOld, "20260101000000", "http://eski.example/", 200, "text/html");
        await AddCaptureAsync(db, after, "20260101000000", "http://sonra.example/", 200, "text/html");
        await AddCaptureAsync(db, postOnly, "20260814000000", "http://gec.example/", 200, "text/html");
        await AddCaptureAsync(db, redirectOnly, "20260601000000", "http://yonlendirme.example/", 302, "text/html");
        await AddCaptureAsync(db, twoBulletins, "20260701000000", "http://iki.example/", 200, "text/html"); // 1 Temmuz kararı günü: dışarıda
        await AddCaptureAsync(db, twoBulletins, "20260630000000", "http://iki.example/", 200, "text/html");

        const string seed = "test-tohum";
        var (eligible, items) = await ArchiveSampleCommand.SelectAsync(db, seed, 20, ct);

        Assert.Equal(3, eligible);
        var expectedOrder = new[] { recent, boundary, twoBulletins }
            .OrderBy(id => Md5Hex(id + ":" + seed), StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedOrder, items.Select(i => i.DomainId));
        Assert.Equal([1, 2, 3], items.Select(i => i.Number));

        var r = items.Single(i => i.DomainId == recent);
        Assert.Equal("20260501000000", r.Capture!.CdxTimestamp); // ana sayfa, giriş sayfasından eski olsa da
        Assert.Equal(new DateOnly(2026, 5, 2), r.FirstCert);

        var two = items.Single(i => i.DomainId == twoBulletins);
        Assert.Equal(new DateOnly(2026, 7, 1), two.Decided);
        Assert.Equal("2026/40", two.Bulletin);
        Assert.Equal("Crypto", two.Category);
        Assert.Equal("20260630000000", two.Capture!.CdxTimestamp);

        var (_, limited) = await ArchiveSampleCommand.SelectAsync(db, seed, 2, ct);
        Assert.Equal(expectedOrder.Take(2), limited.Select(i => i.DomainId));
    }

    // md5 burada güvenlik için değil, PostgreSQL'deki md5() sıralamasını C#'ta yeniden üretmek için kullanılır.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "Sıralama anahtarı; gizlilik veya bütünlük sağlamıyor.")]
    private static string Md5Hex(string value) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<long> AddDomainAsync(FraudDbContext db, string host, DateOnly decided, string firstCert)
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = new DomainName(
            new NormalizedDomain(host, host, null, "example", host), Observed);
        db.Domains.Add(domain);
        db.OfficialActions.Add(new OfficialAction(
            domain, OfficialActionSource.SpkBulletin, "2026/46", decided,
            OfficialActionCategory.Crypto, new Uri("https://spk.gov.tr/bulten/2026-46"), host, Observed));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var notBefore = DateTimeOffset.Parse(firstCert + "T10:00:00Z", CultureInfo.InvariantCulture);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            with c as (
              insert into certificates (crtsh_id, serial_number, issuer_name, names, not_before, not_after, observed_at)
              values ({domain.Id + 1000}, {"s" + domain.Id}, {"C=US, O=Test CA"}, {new[] { host }}, {notBefore}, {notBefore.AddDays(90)}, {Observed})
              returning id)
            insert into certificate_domains (certificate_id, domain_id, observed_at) select id, {domain.Id}, {Observed} from c
            """, ct);
        return domain.Id;
    }

    private static async Task AddActionAsync(FraudDbContext db, long domainId, string bulletin, DateOnly decided)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into official_actions (domain_id, source, bulletin_number, decision_date, category, source_url, listed_as, observed_at)
            values ({domainId}, 'SpkBulletin', {bulletin}, {decided}, 'Crypto', 'https://spk.gov.tr/bulten/x', 'x', {Observed})
            """, TestContext.Current.CancellationToken);
    }

    private static async Task AddCaptureAsync(
        FraudDbContext db, long domainId, string timestamp, string url, int status, string mime)
    {
        var capturedAt = DateTimeOffset.ParseExact(timestamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(timestamp + url + status + mime)));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into archive_snapshots (domain_id, capture_key, cdx_timestamp, captured_at, original_url, query_removed, status_code, mime_type, observed_at)
            values ({domainId}, {key}, {timestamp}, {capturedAt}, {url}, false, {status}, {mime}, {Observed})
            """, TestContext.Current.CancellationToken);
    }
}
