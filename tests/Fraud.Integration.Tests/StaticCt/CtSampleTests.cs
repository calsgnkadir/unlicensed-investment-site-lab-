using Fraud.Collectors.Http;
using Fraud.Collectors.StaticCt;

namespace Fraud.Integration.Tests.StaticCt;

/// <summary><c>ct-sample</c>: istek bütçesi sert sınırdır ve çıktı yalnızca sayı içerir. Ağ yok.</summary>
public sealed class CtSampleTests : IDisposable
{
    private readonly TestCtLog _log = new TestCtLog().AddEntries((4 * 256) + 20);

    public void Dispose() => _log.Dispose();

    private (CtSampleRunner Runner, RequestBudgetHandler Budget, CtLogKey Key) Build(FakeCtServer server, int budget)
    {
        var options = new StaticCtOptions { MinRequestInterval = TimeSpan.Zero, RetryBaseDelay = TimeSpan.Zero, MaxRetryAttempts = 0 };
        var handler = new RequestBudgetHandler(budget) { InnerHandler = server };
        var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var log = _log.Options();
        var key = CtLogKey.Create(log.Origin, log.PublicKey);
        var client = new StaticCtClient(
            new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System),
            log, key, options.MaxResponseBytes);
        return (new CtSampleRunner(client, new TileProcessor(new KeywordCandidateFilter(new CandidateFilterOptions())), TimeProvider.System), handler, key);
    }

    [Fact]
    public async Task Three_tiles_fit_in_five_requests_and_the_report_has_only_numbers()
    {
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };
        var (runner, budget, key) = Build(server, 5);
        using (key)
        using (budget)
        {
            var report = await runner.RunAsync(3, verifyRoot: true, 5, () => budget.Sent, TestContext.Current.CancellationToken);

            Assert.Equal(5, budget.Sent);
            Assert.Equal(5, server.Paths.Count);
            Assert.Equal(768, report.Entries);
            Assert.Equal(3, report.Level1Matched);
            Assert.Null(report.RootVerified); // tam doğrulama bütçeye sığmadı, istek atılmadı
            Assert.Equal(_log.KeywordCount(256, 1024), report.CandidateCertificates);

            var text = report.ToText();
            Assert.DoesNotContain("ornek", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("xn--", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task With_a_larger_budget_the_root_is_verified_against_the_signed_checkpoint()
    {
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };
        var (runner, budget, key) = Build(server, 10);
        using (key)
        using (budget)
        {
            var report = await runner.RunAsync(2, verifyRoot: true, 10, () => budget.Sent, TestContext.Current.CancellationToken);

            Assert.True(report.RootVerified);
            Assert.True(budget.Sent <= 10);
        }
    }

    [Fact]
    public async Task Budget_handler_stops_requests_before_they_leave()
    {
        using var server = new FakeCtServer(_log) { PublishedSize = _log.Count };
        var (runner, budget, key) = Build(server, 2);
        using (key)
        using (budget)
        {
            await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(
                () => runner.RunAsync(3, verifyRoot: false, 2, () => budget.Sent, TestContext.Current.CancellationToken));
            Assert.Empty(server.Paths);
        }
    }
}
