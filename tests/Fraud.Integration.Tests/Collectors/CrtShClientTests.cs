using System.Net;
using System.Text;
using Fraud.Collectors.CrtSh;
using Fraud.Domain.Collection;
using Fraud.Integration.Tests.Infrastructure;

namespace Fraud.Integration.Tests.Collectors;

// Uydurma crt.sh yanıtlarıyla ayrıştırma, hata ve tekrar deneme testleri. Ağ yok.
public sealed class CrtShClientTests
{
    private const string Host = "ornek-yatirim.example";

    // Aynı sertifika (id 1001) iki satırda farklı adlarla geliyor; biri S/MIME e-postası taşıyor.
    // Issuer adında emailAddress ve tırnak içinde virgül var. Bir satırın tarihi bozuk.
    private const string SampleJson = """
        [
          {"issuer_ca_id":16418,"issuer_name":"C=US, O=Let's Encrypt, CN=R3","common_name":"ornek-yatirim.example",
           "name_value":"ornek-yatirim.example\nwww.ornek-yatirim.example","id":1001,
           "entry_timestamp":"2026-05-02T10:11:12.345","not_before":"2026-05-02T09:11:12","not_after":"2026-07-31T09:11:11",
           "serial_number":"04AB01","result_count":3},
          {"issuer_ca_id":16418,"issuer_name":"C=US, O=Let's Encrypt, CN=R3","common_name":"ornek-yatirim.example",
           "name_value":"panel.ornek-yatirim.example\nkisi.adi@ornek-yatirim.example","id":1001,
           "entry_timestamp":"2026-05-02T10:11:12.345","not_before":"2026-05-02T09:11:12","not_after":"2026-07-31T09:11:11",
           "serial_number":"04AB01","result_count":3},
          {"issuer_ca_id":9,"issuer_name":"C=TR, O=\"Ornek, A.S.\", CN=Ornek CA, emailAddress=ca-yonetici@ornek.example, L=Istanbul",
           "common_name":"Ad Soyad","name_value":"*.ornek-yatirim.example","id":1002,
           "entry_timestamp":null,"not_before":"2026-06-01T00:00:00","not_after":"2027-06-01T00:00:00",
           "serial_number":"00FF","result_count":1},
          {"issuer_ca_id":9,"issuer_name":"C=TR","name_value":"x.ornek-yatirim.example","id":1003,
           "not_before":"bozuk-tarih","not_after":"2027-06-01T00:00:00","serial_number":"01"}
        ]
        """;

    [Fact]
    public void Query_uses_wildcard_subdomains_json_and_deduplicate()
    {
        var uri = CrtShClient.BuildQueryUri(Host);

        Assert.Equal("https://crt.sh/?q=%25.ornek-yatirim.example&output=json&deduplicate=Y", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ornek yatirim.example")]
    [InlineData("ornek.example&output=html")]
    [InlineData("*.ornek.example")]
    public void Query_rejects_values_that_are_not_normalized_domains(string host)
    {
        Assert.Throws<ArgumentException>(() => CrtShClient.BuildQueryUri(host));
    }

    [Fact]
    public async Task Parses_merges_duplicates_and_removes_personal_data()
    {
        var handler = new FakeHttpHandler().EnqueueText(SampleJson);

        var result = await TestClients.CrtSh(handler).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Success, result.Outcome);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(1, result.SkippedItems); // bozuk tarihli satır
        Assert.Equal([1001L, 1002L], result.Items.Select(c => c.CrtShId));

        var first = result.Items[0];
        Assert.Equal(["ornek-yatirim.example", "panel.ornek-yatirim.example", "www.ornek-yatirim.example"], first.Names);
        Assert.Equal("04ab01", first.SerialNumber);
        Assert.Equal(16418, first.IssuerCaId);
        Assert.Equal("C=US, O=Let's Encrypt, CN=R3", first.IssuerName);
        Assert.Equal(new DateTimeOffset(2026, 5, 2, 9, 11, 12, TimeSpan.Zero), first.NotBefore);
        Assert.Equal(new DateTimeOffset(2026, 5, 2, 10, 11, 12, 345, TimeSpan.Zero), first.LoggedAt);

        var second = result.Items[1];
        Assert.Equal("C=TR, O=\"Ornek, A.S.\", CN=Ornek CA", second.IssuerName);
        Assert.Null(second.CommonName); // "Ad Soyad" bir DNS adı değil
        Assert.Null(second.LoggedAt);
        Assert.Equal(["*.ornek-yatirim.example"], second.Names);

        Assert.All(result.Items, c =>
        {
            Assert.DoesNotContain('@', c.IssuerName);
            Assert.All(c.Names, n => Assert.DoesNotContain('@', n));
        });
    }

    [Fact]
    public async Task Empty_array_is_the_only_way_to_get_an_empty_result()
    {
        var handler = new FakeHttpHandler().EnqueueText("[]");

        var result = await TestClients.CrtSh(handler).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Empty, result.Outcome);
        Assert.Equal(CollectionErrorKind.None, result.ErrorKind);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("<html><body>Sorry, something went wrong</body></html>", "text/html")]
    [InlineData("{\"error\":\"timeout\"}", "application/json")]
    [InlineData("", "application/json")]
    public async Task Html_or_unexpected_body_is_an_error_not_an_empty_result(string body, string mediaType)
    {
        var handler = new FakeHttpHandler().EnqueueText(body, mediaType);

        var result = await TestClients.CrtSh(handler).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Error, result.Outcome);
        Assert.Equal(CollectionErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Equal(200, result.HttpStatus);
    }

    [Fact]
    public async Task Only_malformed_rows_is_an_error_not_an_empty_result()
    {
        var handler = new FakeHttpHandler().EnqueueText("""[{"id":"yazı","not_before":"x"}]""");

        var result = await TestClients.CrtSh(handler).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Error, result.Outcome);
        Assert.Equal(CollectionErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Equal(1, result.SkippedItems);
    }

    [Fact]
    public async Task Rate_limit_then_success_is_retried()
    {
        var handler = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.TooManyRequests, r => r.Headers.TryAddWithoutValidation("Retry-After", "30"))
            .EnqueueStatus(HttpStatusCode.BadGateway)
            .EnqueueText(SampleJson);

        var result = await TestClients.CrtSh(handler).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Success, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent_server_error_is_logged_as_error_after_all_retries()
    {
        var handler = new FakeHttpHandler();
        for (var i = 0; i < 4; i++)
        {
            handler.EnqueueStatus(HttpStatusCode.ServiceUnavailable);
        }

        var result = await TestClients.CrtSh(handler, TestClients.FastCrtShOptions(maxRetries: 3))
            .SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Error, result.Outcome);
        Assert.Equal(CollectionErrorKind.ServerError, result.ErrorKind);
        Assert.Equal(503, result.HttpStatus);
        Assert.Equal(4, result.Attempts); // ilk deneme + 3 tekrar
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Persistent_rate_limit_is_an_error()
    {
        var handler = new FakeHttpHandler()
            .EnqueueStatus(HttpStatusCode.TooManyRequests)
            .EnqueueStatus(HttpStatusCode.TooManyRequests);

        var result = await TestClients.CrtSh(handler, TestClients.FastCrtShOptions(maxRetries: 1))
            .SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.RateLimited, result.ErrorKind);
        Assert.Equal(429, result.HttpStatus);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task Client_error_is_not_retried()
    {
        var handler = new FakeHttpHandler().EnqueueStatus(HttpStatusCode.NotFound);

        var result = await TestClients.CrtSh(handler).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.ClientError, result.ErrorKind);
        Assert.Equal(404, result.HttpStatus);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Network_error_is_retried_and_then_reported()
    {
        var handler = new FakeHttpHandler().EnqueueNetworkError().EnqueueNetworkError();

        var result = await TestClients.CrtSh(handler, TestClients.FastCrtShOptions(maxRetries: 1))
            .SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.NetworkError, result.ErrorKind);
        Assert.Null(result.HttpStatus);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task Attempt_timeout_is_an_error()
    {
        var options = TestClients.FastCrtShOptions(maxRetries: 1);
        options.AttemptTimeout = TimeSpan.FromMilliseconds(200);
        var handler = new FakeHttpHandler().EnqueueHang().EnqueueHang();

        var result = await TestClients.CrtSh(handler, options).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionOutcome.Error, result.Outcome);
        Assert.Equal(CollectionErrorKind.Timeout, result.ErrorKind);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task Response_over_the_size_limit_is_rejected_without_retry()
    {
        var options = TestClients.FastCrtShOptions();
        options.MaxResponseBytes = 1024;
        var big = "[" + string.Join(',', Enumerable.Repeat("{\"id\":1}", 500)) + "]";
        var handler = new FakeHttpHandler().Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // Akış içerik: Content-Length başlığı yok, sınır okunan bayt üzerinden uygulanmalı.
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(big))),
        });

        var result = await TestClients.CrtSh(handler, options).SearchAsync(Host, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.ResponseTooLarge, result.ErrorKind);
        Assert.Equal(1, result.Attempts);
    }
}
