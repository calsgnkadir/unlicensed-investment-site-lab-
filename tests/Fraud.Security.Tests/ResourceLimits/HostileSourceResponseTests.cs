using System.Diagnostics;
using System.Text;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Collectors.Wayback;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;
using Fraud.Security.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Security.Tests.ResourceLimits;

/// <summary>
/// crt.sh ve Wayback CDX yanıtları güvenilmez veridir. Bozuk, devasa veya kurnazca hazırlanmış yanıt
/// istisna fırlatmamalı; "geçersiz yanıt" olarak kaydedilmeli ve sonraki alan adlarının toplanmasını durdurmamalı.
/// </summary>
public sealed class HostileSourceResponseTests
{
    private static CdxPage Cdx(string json) => CdxResponseParser.Parse(Encoding.UTF8.GetBytes(json));

    private static CrtShParseResult CrtSh(string json) => CrtShResponseParser.Parse(Encoding.UTF8.GetBytes(json));

    private static WaybackCdxOptions FastWayback() => new()
    {
        MinRequestInterval = TimeSpan.Zero,
        RetryBaseDelay = TimeSpan.Zero,
        MaxRetryAfter = TimeSpan.Zero,
        MaxRetryAttempts = 0,
        AttemptTimeout = TimeSpan.FromSeconds(10),
        PageSize = 3,
        MaxPages = 10,
    };

    private static WaybackCdxClient Wayback(StubHttpHandler handler, WaybackCdxOptions options) =>
        new(new PoliteHttpClient(new HttpClient(handler), new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System), options);

    private static CrtShClient CrtShClientFor(StubHttpHandler handler)
    {
        var options = new CrtShOptions
        {
            MinRequestInterval = TimeSpan.Zero, RetryBaseDelay = TimeSpan.Zero, MaxRetryAfter = TimeSpan.Zero,
            MaxRetryAttempts = 0, AttemptTimeout = TimeSpan.FromSeconds(10),
        };
        return new CrtShClient(new PoliteHttpClient(new HttpClient(handler), new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System));
    }

    // ---------- crt.sh ----------

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"[]\"")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("[")]
    [InlineData("")]
    public void CrtSh_non_array_or_broken_body_is_invalid_not_empty(string json)
    {
        Assert.False(CrtSh(json).IsValid);
    }

    [Fact]
    public void CrtSh_deep_nesting_is_rejected_by_depth_limit()
    {
        var json = new string('[', 100_000) + new string(']', 100_000);

        Assert.False(CrtSh(json).IsValid);
    }

    [Theory]
    [InlineData("""[{"id":"12","serial_number":"ab","not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00"}]""")]
    [InlineData("""[{"id":1e400,"serial_number":"ab","not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00"}]""")]
    [InlineData("""[{"id":-5,"serial_number":"ab","not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00"}]""")]
    [InlineData("""[{"id":7,"serial_number":"ab","not_before":"2026-05-01T00:00:00","not_after":"2026-03-01T00:00:00"}]""")]
    [InlineData("""[{"id":7,"serial_number":["x"],"not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00"}]""")]
    [InlineData("""[{"id":7,"serial_number":"ab","not_before":"yarın","not_after":"2026-03-01T00:00:00"}]""")]
    [InlineData("""[42, "x", null, []]""")]
    public void CrtSh_wrong_types_and_values_are_skipped(string json)
    {
        var result = CrtSh(json);

        Assert.True(result.IsValid);
        Assert.Empty(result.Certificates);
        Assert.True(result.Skipped > 0);
    }

    [Fact]
    public void CrtSh_serial_longer_than_column_is_skipped()
    {
        var serial = new string('a', 129);
        var result = CrtSh($$"""[{"id":7,"serial_number":"{{serial}}","not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00"}]""");

        Assert.Empty(result.Certificates);
    }

    [Fact]
    public void CrtSh_many_rows_for_one_id_are_merged_in_reasonable_time()
    {
        // Aynı kimlik için 5.000 satır, her birinde farklı ad. Birleştirme karesel büyümemeli.
        var sb = new StringBuilder("[");
        for (var i = 0; i < 5000; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(System.Globalization.CultureInfo.InvariantCulture,
                $$"""{"id":7,"serial_number":"ab","not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00","name_value":"a{{i}}.ornek.example"}""");
        }

        sb.Append(']');
        var watch = Stopwatch.StartNew();

        var result = CrtSh(sb.ToString());

        watch.Stop();
        Assert.Single(result.Certificates);
        Assert.Equal(5000, result.Certificates[0].Names.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Birleştirme {watch.Elapsed.TotalSeconds:F1} sn sürdü.");
    }

    [Fact]
    public void CrtSh_invalid_utf8_inside_a_string_is_invalid_not_thrown()
    {
        // Geçerli JSON yapısı, ama issuer_name içinde geçersiz UTF-8 baytı (0xFF).
        var head = Encoding.UTF8.GetBytes("""[{"id":7,"serial_number":"ab","not_before":"2026-01-01T00:00:00","not_after":"2026-03-01T00:00:00","issuer_name":"CN=""");
        var tail = Encoding.UTF8.GetBytes("\"}]");
        byte[] json = [.. head, 0xFF, 0xFE, .. tail];

        var exception = Record.Exception(() => CrtShResponseParser.Parse(json));

        Assert.Null(exception);
    }

    // ---------- Wayback CDX ----------

    [Theory]
    [InlineData("{}")]
    [InlineData("[[]]")]
    [InlineData("[[1,2,3]]")]
    [InlineData("""[["statuscode","mimetype"]]""")]
    [InlineData("""[[["timestamp"]]]""")]
    public void Cdx_header_without_required_columns_is_invalid(string json)
    {
        Assert.False(Cdx(json).IsValid);
    }

    [Fact]
    public void Cdx_deep_nesting_is_rejected_by_depth_limit()
    {
        Assert.False(Cdx(new string('[', 10_000) + new string(']', 10_000)).IsValid);
    }

    [Fact]
    public void Cdx_duplicate_header_column_is_invalid_not_thrown()
    {
        const string json = """[["timestamp","original","timestamp"],["20260101000000","http://ornek.example/","x"]]""";

        var exception = Record.Exception(() => Cdx(json));

        Assert.Null(exception);
    }

    [Fact]
    public void Cdx_invalid_utf8_inside_a_row_is_invalid_not_thrown()
    {
        var head = Encoding.UTF8.GetBytes("""[["timestamp","original"],["20260101000000","http://ornek.example/""");
        var tail = Encoding.UTF8.GetBytes("\"]]");
        byte[] json = [.. head, 0xC3, 0x28, .. tail];

        var exception = Record.Exception(() => CdxResponseParser.Parse(json));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("""[["timestamp","original"],["20261399999999","http://ornek.example/"]]""")]
    [InlineData("""[["timestamp","original"],["2026010100000","http://ornek.example/"]]""")]
    [InlineData("""[["timestamp","original"],["20260101000000\n","http://ornek.example/"]]""")]
    [InlineData("""[["timestamp","original"],["20260101000000","javascript:alert(1)"]]""")]
    [InlineData("""[["timestamp","original"],["20260101000000","file:///etc/passwd"]]""")]
    [InlineData("""[["timestamp","original"],["20260101000000"]]""")]
    public void Cdx_bad_rows_are_skipped_and_counted(string json)
    {
        var page = Cdx(json);

        Assert.True(page.IsValid);
        Assert.Empty(page.Captures);
        Assert.Equal(1, page.Skipped);
    }

    [Fact]
    public async Task Cdx_resume_key_that_never_ends_stops_at_the_page_limit()
    {
        var handler = new StubHttpHandler();
        for (var i = 0; i < 10; i++)
        {
            handler.EnqueueText("""[["timestamp","original"],["20260101000000","http://ornek.example/"],[],["ayni-anahtar"]]""");
        }

        var result = await Wayback(handler, FastWayback()).ListCapturesAsync("ornek.example", TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Equal(10, handler.Requests.Count);
    }

    [Fact]
    public async Task Cdx_huge_resume_key_is_rejected_not_thrown()
    {
        // .NET Uri yaklaşık 65 bin karakterden uzun adresi reddeder. Sınırsız resumeKey bir sonraki isteği kurarken patlar.
        var key = new string('k', 70_000);
        var handler = new StubHttpHandler()
            .EnqueueText($$"""[["timestamp","original"],["20260101000000","http://ornek.example/"],[],["{{key}}"]]""")
            .EnqueueText("[]");

        var exception = await Record.ExceptionAsync(
            () => Wayback(handler, FastWayback()).ListCapturesAsync("ornek.example", TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    // ---------- Etki zinciri: hata collection_log'a yazılmadan tur durur ----------

    [Fact]
    public async Task Malformed_cdx_page_does_not_escape_the_collector_as_an_exception()
    {
        // FetchAsync istisnası, veritabanına dokunulmadan önce fırlar; bu yüzden gerçek bir veritabanı gerekmez.
        // Bağlantı metni hiçbir zaman açılmaz.
        var handler = new StubHttpHandler().EnqueueText("""[["timestamp","original","timestamp"],["20260101000000","http://ornek.example/","x"]]""");
        var options = FastWayback();
        var collector = new WaybackCollector(Wayback(handler, options), options, TimeProvider.System);
        await using var db = new FraudDbContext(new DbContextOptionsBuilder<FraudDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=9;Database=yok;Username=yok;Password=yok;Timeout=1").Options);

        var exception = await Record.ExceptionAsync(() => collector.CollectAsync(
            db, new Fraud.Collectors.Collection.CollectionTarget(1, "ornek.example"), "test", TestContext.Current.CancellationToken));

        // Beklenen güvenli davranış: istisna yerine Error/InvalidResponse ile log satırı.
        // Veritabanı yok, bu yüzden ArgumentException dışındaki bir hata (bağlantı) kabul edilir.
        Assert.False(exception is ArgumentException, "Bozuk CDX başlığı ArgumentException olarak toplayıcıdan kaçtı.");
    }

    [Fact]
    public async Task CrtSh_client_maps_html_error_page_to_invalid_response()
    {
        var handler = new StubHttpHandler().EnqueueText("<html>Too many requests</html>", "text/html");

        var result = await CrtShClientFor(handler).SearchAsync("ornek.example", TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.InvalidResponse, result.ErrorKind);
    }
}
