using System.Diagnostics.CodeAnalysis;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Http;
using Microsoft.Extensions.Configuration;

namespace Fraud.Collectors.StaticCt;

/// <summary>
/// <c>ct-sample --tiles N</c>: Gerçek log üzerinde sınırlı deneme. Veritabanı kullanmaz, hiçbir şey yazmaz,
/// yalnızca sayıları basar. İstek sayısı HTTP katmanında sert bir bütçeyle sınırlanır (varsayılan 5) ve
/// tekrar deneme kapalıdır; bütçe dolunca istek ağa çıkmaz.
/// </summary>
public static class CtSampleCommand
{
    public const int DefaultBudget = 5;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Komut satırı ucu: Hata türü yazılıp 1 ile çıkılır; istisna mesajı basılmaz.")]
    public static async Task<int> RunAsync(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var options = new StaticCtOptions();
        configuration.GetSection(StaticCtOptions.SectionName).Bind(options);
        var collection = new CollectionOptions();
        configuration.GetSection(CollectionOptions.SectionName).Bind(collection);
        var filterOptions = new CandidateFilterOptions();
        configuration.GetSection(CandidateFilterOptions.SectionName).Bind(filterOptions);

        var tiles = configuration.GetValue("tiles", 0);
        var budget = configuration.GetValue("max-requests", DefaultBudget);
        var verifyRoot = configuration.GetValue("verify-root", false);
        var logName = configuration["log"];
        if (tiles is < 1 or > CtSampleRunner.MaxTiles || budget < tiles + 1)
        {
            await Console.Error.WriteLineAsync($"--tiles 1-{CtSampleRunner.MaxTiles} arası olmalı ve --max-requests en az tiles + 1 olmalı.")
                .ConfigureAwait(false);
            return 64;
        }

        try
        {
            options.Validate();
            collection.Validate();
        }
        catch (InvalidOperationException)
        {
            await Console.Error.WriteLineAsync("StaticCt veya Collection ayarları geçersiz.").ConfigureAwait(false);
            return 64;
        }

        var log = options.Logs.FirstOrDefault(l => logName is null ? l.Enabled : l.Name == logName);
        if (log is null)
        {
            await Console.Error.WriteLineAsync("StaticCt:Logs içinde uygun log yok.").ConfigureAwait(false);
            return 64;
        }

        // Örnekte tekrar deneme yok (her deneme bütçeden düşer) ve istekler arası en az 1 sn.
        var sampleOptions = new StaticCtOptions
        {
            MinRequestInterval = options.MinRequestInterval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : options.MinRequestInterval,
            MaxResponseBytes = options.MaxResponseBytes,
            AttemptTimeout = options.AttemptTimeout,
            MaxRetryAttempts = 0,
        };

        using var budgetHandler = new RequestBudgetHandler(budget)
        {
            InnerHandler = CollectorServiceCollectionExtensions.CreateStaticCtHandler(1),
        };
        using var http = new HttpClient(budgetHandler, disposeHandler: false);
        CollectorServiceCollectionExtensions.ConfigureStaticCtClient(http, collection.UserAgent);
        using var pacer = new RequestPacer(sampleOptions.MinRequestInterval, TimeProvider.System);
        using var key = CtLogKey.Create(log.Origin, log.PublicKey);
        var client = new StaticCtClient(new PoliteHttpClient(http, pacer, sampleOptions, TimeProvider.System), log, key, sampleOptions.MaxResponseBytes);
        var runner = new CtSampleRunner(client, new TileProcessor(new KeywordCandidateFilter(filterOptions)), TimeProvider.System);

        try
        {
            var report = await runner.RunAsync(tiles, verifyRoot, budget, () => budgetHandler.Sent, CancellationToken.None).ConfigureAwait(false);
            await Console.Out.WriteAsync(report.ToText()).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Mesajlar sabit metin ve hata türüdür; adres veya veri içermez.
            var detail = ex is InvalidOperationException or RequestBudgetExceededException ? ex.Message : ex.GetType().Name;
            await Console.Error.WriteLineAsync("ct-sample başarısız: " + detail + " (gönderilen istek: " + budgetHandler.Sent + ")").ConfigureAwait(false);
            return 1;
        }
    }
}
