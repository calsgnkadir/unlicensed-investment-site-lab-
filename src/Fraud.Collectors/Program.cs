using Fraud.Collectors;
using Fraud.Collectors.Collection;
using Fraud.Collectors.OfficialActions;
using Fraud.Collectors.Quality;
using Fraud.Collectors.Http;
using Fraud.Collectors.StaticCt;
using Fraud.Collectors.Wayback;
using Fraud.Domain.Collection;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure;
using Fraud.Infrastructure.Fetching;
using Fraud.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Kullanım:
//   dotnet run --project src/Fraud.Collectors -- import-official-actions [--file <yol>]
//   dotnet run --project src/Fraud.Collectors -- collect-crtsh [--force true]
//   dotnet run --project src/Fraud.Collectors -- collect-wayback [--force true]
//   dotnet run --project src/Fraud.Collectors -- worker
//   dotnet run --project src/Fraud.Collectors -- quality-report
//   dotnet run --project src/Fraud.Collectors -- ct-follow
//   dotnet run --project src/Fraud.Collectors -- migrate
//   dotnet run --project src/Fraud.Collectors -- ct-sample --tiles 3 [--log <ad>] [--max-requests 5] [--verify-root true]
//   dotnet run --project src/Fraud.Collectors -- archive-sample [--count 20] [--seed <metin>] [--out data/private/phase0/arsiv_icerik]
// Bağlantı metni: ConnectionStrings__FraudDb ortam değişkeni veya user-secrets.
// Çıkış kodu: 0 = temiz, 2 = reddedilen/çakışan satır veya hatalı toplama var, 1 = hata, 64 = hatalı kullanım.

const string Usage =
    "Kullanım: Fraud.Collectors <import-official-actions [--file <yol>] | collect-crtsh [--force true] | " +
    "collect-wayback [--force true] | worker | quality-report | ct-follow | migrate | " +
    "ct-sample --tiles N [--log <ad>] [--max-requests M] [--verify-root true] | " +
    "archive-sample [--count N] [--seed <metin>] [--out <klasör>]>";

var command = args.Length > 0 ? args[0] : string.Empty;
if (command is not ("import-official-actions" or "collect-crtsh" or "collect-wayback" or "worker" or "quality-report"
    or "ct-follow" or "ct-sample" or "migrate" or "archive-sample"))
{
    await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
    return 64;
}

if (command == "ct-sample")
{
    // Veritabanı gerekmez: Hiçbir şey yazılmaz, yalnızca sayılar basılır.
    return await CtSampleCommand.RunAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
}

// appsettings.json programın yanından okunur; CSV yolu ise çalıştırılan klasöre göre çözülür.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Skip(1).ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddUserSecrets(typeof(OfficialActionImporter).Assembly, optional: true);
builder.Services.AddFraudInfrastructure(builder.Configuration);
builder.Services.AddCollectors(builder.Configuration);

var importOptions = new OfficialActionImportOptions();
builder.Configuration.GetSection(OfficialActionImportOptions.SectionName).Bind(importOptions);
var fileArgument = builder.Configuration["file"];
if (!string.IsNullOrWhiteSpace(fileArgument))
{
    importOptions.FilePath = fileArgument;
}

builder.Services.AddSingleton(importOptions);
builder.Services.AddScoped<OfficialActionCsvParser>();
builder.Services.AddScoped<OfficialActionImporter>();

if (command == "worker")
{
    builder.Services.AddHostedService<CollectionWorker>();
}
else if (command == "ct-follow")
{
    builder.Services.AddHostedService<CtFollowWorker>();
}
else if (command == "archive-sample")
{
    // Güvenli çekici yalnızca bu komutta kaydedilir. İzinli tek ana bilgisayar web.archive.org (varsayılan liste;
    // yapılandırmadan genişletilmez). Hız sınırı her HTTP isteğine, yönlendirme adımları dahil, uygulanır: 5 sn'de en fazla 1.
    var collectionUserAgent = builder.Configuration["Collection:UserAgent"];
    var fetcherOptions = new SafeFetcherOptions { Timeout = TimeSpan.FromSeconds(60), MaxRedirects = 3 };
    if (!string.IsNullOrWhiteSpace(collectionUserAgent))
    {
        fetcherOptions.UserAgent = collectionUserAgent;
    }

    builder.Services.AddSingleton(sp => new RequestPacer(ArchiveSampleOptions.RequestInterval, sp.GetRequiredService<TimeProvider>()));
    builder.Services.AddSafeFetcher(fetcherOptions,
        (sp, transport) => new PacingHandler(sp.GetRequiredService<RequestPacer>()) { InnerHandler = transport });
}

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Fraud.Collectors");

// "migrate" ayardan bağımsız olarak şemayı günceller ve çıkar. Linux kurulumunda (deploy/linux) şemayı yalnızca
// bu tek seferlik servis günceller; ct-follow ondan sonra başlar ve kendisi migration yapmaz.
if (command == "migrate" || builder.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DatabaseMigrator.MigrateAsync(host.Services).ConfigureAwait(false);
}

if (command == "migrate")
{
    await Console.Out.WriteLineAsync("Migration tamam.").ConfigureAwait(false);
    return 0;
}

// Ctrl+C ile tek seferlik komutlar da düzgün durur; yarım kalan iş collection_log'a "Cancelled" olarak yazılır.
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

switch (command)
{
    case "worker":
    case "ct-follow":
        await host.RunAsync().ConfigureAwait(false);
        return 0;

    case "collect-crtsh":
    case "collect-wayback":
    {
        var source = command == "collect-crtsh" ? CollectionSource.CrtSh : CollectionSource.WaybackCdx;
        var force = builder.Configuration.GetValue("force", false);
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<CollectionRunner>();
        var summary = await runner.RunAsync(source, force, stop.Token).ConfigureAwait(false);
        return summary.Failed == 0 && !summary.Cancelled ? 0 : 2;
    }

    case "quality-report":
    {
        await using var scope = host.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<QualityReportBuilder>().BuildAsync(stop.Token).ConfigureAwait(false);
        await Console.Out.WriteAsync(report.ToText()).ConfigureAwait(false);
        return 0;
    }

    case "archive-sample":
    {
        var sampleOptions = new ArchiveSampleOptions
        {
            Count = builder.Configuration.GetValue("count", ArchiveSampleOptions.DefaultCount),
            Seed = builder.Configuration["seed"] ?? ArchiveSampleOptions.DefaultSeed,
            OutputDirectory = builder.Configuration["out"] ?? ArchiveSampleOptions.DefaultOutputDirectory,
        };
        await using var scope = host.Services.CreateAsyncScope();
        return await ArchiveSampleCommand.RunAsync(
            scope.ServiceProvider.GetRequiredService<FraudDbContext>(),
            scope.ServiceProvider.GetRequiredService<ISafeFetcher>(),
            sampleOptions,
            stop.Token).ConfigureAwait(false);
    }

    default:
        return await ImportOfficialActionsAsync(host.Services, importOptions, logger).ConfigureAwait(false);
}

static async Task<int> ImportOfficialActionsAsync(IServiceProvider services, OfficialActionImportOptions options, ILogger logger)
{
    var path = Path.GetFullPath(options.FilePath);
    if (!File.Exists(path))
    {
        ImportLog.FileNotFound(logger, options.FilePath);
        return 1;
    }

    await using var scope = services.CreateAsyncScope();
    var importer = scope.ServiceProvider.GetRequiredService<OfficialActionImporter>();

    using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
    var report = await importer.ImportAsync(reader).ConfigureAwait(false);

    // Log yalnızca sayı, satır numarası ve neden içerir; alan adı yazılmaz.
    ImportLog.Finished(logger, report.ValidRows, report.Inserted, report.AlreadyPresent, report.NewDomains, report.Conflicts.Count, report.Rejections.Count);

    foreach (var rejection in report.Rejections)
    {
        ImportLog.Rejected(logger, rejection.RowNumber, rejection.Reason, rejection.Detail);
    }

    foreach (var conflict in report.Conflicts)
    {
        ImportLog.Conflict(logger, conflict.RowNumber, conflict.Detail);
    }

    return report.IsClean ? 0 : 2;
}
