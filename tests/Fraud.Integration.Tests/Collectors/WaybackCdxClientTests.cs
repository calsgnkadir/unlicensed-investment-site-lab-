using System.Net;
using Fraud.Collectors.Wayback;
using Fraud.Domain.Collection;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Collectors;

// Uydurma CDX yanıtlarıyla ayrıştırma ve sayfalama testleri. Ağ yok.
public sealed class WaybackCdxClientTests
{
    private const string Host = "ornek-yatirim.example";

    [Fact]
    public void Query_uses_domain_match_json_limited_fields_and_resume_key()
    {
        var client = TestClients.Wayback(new FakeHttpHandler(), TestClients.FastWaybackOptions(pageSize: 500));

        var first = client.BuildQueryUri(Host, null).AbsoluteUri;
        var next = client.BuildQueryUri(Host, "example,ornek-yatirim)/ 20260101000000").AbsoluteUri;

        Assert.Equal(
            "https://web.archive.org/cdx/search/cdx?url=ornek-yatirim.example&matchType=domain&output=json" +
            "&fl=timestamp,original,statuscode,mimetype,digest&limit=500&showResumeKey=true",
            first);
        Assert.EndsWith("&resumeKey=example%2Cornek-yatirim%29%2F%2020260101000000", next, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parses_rows_by_header_and_cleans_urls()
    {
        // Sütun sırası fl= ile aynı olmak zorunda değil; başlıktan okunur.
        var handler = new FakeHttpHandler().EnqueueText("""
            [["original","timestamp","statuscode","mimetype","digest"],
             ["http://ornek-yatirim.example/","20260514093000","200","text/html","AAAABBBBCCCCDDDDEEEEFFFFGGGGHHHH"],
             ["https://panel.ornek-yatirim.example/giris?email=kisi@ornek.example&tel=5550000000#x","20260601120000","302","text/html","IIIIJJJJKKKKLLLLMMMMNNNNOOOOPPPP"],
             ["http://ornek-yatirim.example/profil/kisi@ornek.example/foto","20260602120000","-","warc/revisit","-"],
             ["http://ornek-yatirim.example/x","2026-06-02","200","text/html","QQQQ"],
             ["eksik","satir"]]
            """);

        var result = await TestClients.Wayback(handler).ListCapturesAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Success, result.Outcome);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.SkippedItems); // bozuk zaman damgası + eksik sütun
        Assert.False(result.Truncated);

        var first = result.Items[0];
        Assert.Equal("20260514093000", first.Timestamp);
        Assert.Equal(new DateTimeOffset(2026, 5, 14, 9, 30, 0, TimeSpan.Zero), first.CapturedAt);
        Assert.Equal("http://ornek-yatirim.example/", first.OriginalUrl);
        Assert.Equal(200, first.StatusCode);
        Assert.Equal("text/html", first.MimeType);
        Assert.False(first.QueryRemoved);

        var withQuery = result.Items[1];
        Assert.Equal("https://panel.ornek-yatirim.example/giris", withQuery.OriginalUrl);
        Assert.True(withQuery.QueryRemoved);

        var revisit = result.Items[2];
        Assert.Equal("http://ornek-yatirim.example/profil/[redacted]/foto", revisit.OriginalUrl);
        Assert.Null(revisit.StatusCode);
        Assert.Null(revisit.Digest);

        Assert.All(result.Items, s => Assert.DoesNotContain('@', s.OriginalUrl));
        Assert.All(result.Items, s => Assert.Equal(64, s.CaptureKey.Length));
    }

    [Fact]
    public async Task Empty_array_is_an_empty_result()
    {
        var handler = new FakeHttpHandler().EnqueueText("[]");

        var result = await TestClients.Wayback(handler).ListCapturesAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Empty, result.Outcome);
        Assert.Equal(200, result.HttpStatus);
    }

    [Fact]
    public async Task Follows_resume_key_until_the_last_page()
    {
        var handler = new FakeHttpHandler()
            .EnqueueText("""
                [["timestamp","original","statuscode","mimetype","digest"],
                 ["20260101000000","http://ornek-yatirim.example/","200","text/html","AAAA"],
                 [],
                 ["sayfa-2-anahtari"]]
                """)
            .EnqueueText("""
                [["timestamp","original","statuscode","mimetype","digest"],
                 ["20260201000000","http://ornek-yatirim.example/","200","text/html","BBBB"]]
                """);

        var result = await TestClients.Wayback(handler).ListCapturesAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.Attempts);
        Assert.False(result.Truncated);
        Assert.Contains("resumeKey=sayfa-2-anahtari", handler.Requests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_at_max_pages_and_marks_result_truncated()
    {
        const string Page = """
            [["timestamp","original","statuscode","mimetype","digest"],
             ["20260101000000","http://ornek-yatirim.example/","200","text/html","AAAA"],
             [],
             ["devam"]]
            """;
        var handler = new FakeHttpHandler().EnqueueText(Page).EnqueueText(Page);

        var result = await TestClients.Wayback(handler, TestClients.FastWaybackOptions(maxPages: 2))
            .ListCapturesAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Success, result.Outcome);
        Assert.True(result.Truncated);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Error_on_a_later_page_keeps_earlier_rows_but_reports_error()
    {
        var handler = new FakeHttpHandler()
            .EnqueueText("""
                [["timestamp","original","statuscode","mimetype","digest"],
                 ["20260101000000","http://ornek-yatirim.example/","200","text/html","AAAA"],
                 [],
                 ["devam"]]
                """)
            .EnqueueStatus(HttpStatusCode.ServiceUnavailable)
            .EnqueueStatus(HttpStatusCode.ServiceUnavailable)
            .EnqueueStatus(HttpStatusCode.ServiceUnavailable);

        var result = await TestClients.Wayback(handler).ListCapturesAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Error, result.Outcome);
        Assert.Equal(CollectionErrorKind.ServerError, result.ErrorKind);
        Assert.Single(result.Items);
        Assert.Equal(4, result.Attempts); // 1. sayfa 1 deneme + 2. sayfa 3 deneme
    }

    [Theory]
    [InlineData("http://ornek.example/a/b?x=1", "http://ornek.example/a/b", true)]
    [InlineData("http://kullanici:parola@ornek.example/", "http://ornek.example/", false)]
    [InlineData("HTTP://ORNEK.EXAMPLE:8080/A", "http://ornek.example:8080/A", false)]
    [InlineData("ornek.example/yol", "http://ornek.example/yol", false)]
    [InlineData("http://ornek.example/u/a%40b.example", "http://ornek.example/u/[redacted]", false)]
    public void Url_sanitizer_drops_query_userinfo_and_personal_segments(string raw, string expected, bool queryRemoved)
    {
        var result = ArchiveUrlSanitizer.Sanitize(raw);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Value.Url);
        Assert.Equal(queryRemoved, result.Value.QueryRemoved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://ornek.example/dosya")]
    [InlineData("javascript:alert(1)")]
    public void Url_sanitizer_rejects_non_web_urls(string raw)
    {
        Assert.Null(ArchiveUrlSanitizer.Sanitize(raw));
    }
}
