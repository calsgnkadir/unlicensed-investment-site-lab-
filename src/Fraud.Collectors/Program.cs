using Fraud.Collectors.OfficialActions;
using Fraud.Infrastructure;
using Fraud.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Kullanım:
//   dotnet run --project src/Fraud.Collectors -- import-official-actions [--file <yol>]
// Bağlantı metni: ConnectionStrings__FraudDb ortam değişkeni veya user-secrets.
// Çıkış kodu: 0 = temiz, 2 = reddedilen veya çakışan satır var, 1 = hata, 64 = hatalı kullanım.

if (args.Length == 0 || args[0] != "import-official-actions")
{
    await Console.Error.WriteLineAsync("Kullanım: Fraud.Collectors import-official-actions [--file <yol>]").ConfigureAwait(false);
    return 64;
}

// appsettings.json programın yanından okunur; CSV yolu ise çalıştırılan klasöre göre çözülür.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Skip(1).ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddUserSecrets(typeof(OfficialActionImporter).Assembly, optional: true);
builder.Services.AddFraudInfrastructure(builder.Configuration);

var options = new OfficialActionImportOptions();
builder.Configuration.GetSection(OfficialActionImportOptions.SectionName).Bind(options);
var fileArgument = builder.Configuration["file"];
if (!string.IsNullOrWhiteSpace(fileArgument))
{
    options.FilePath = fileArgument;
}

builder.Services.AddSingleton(options);
builder.Services.AddScoped<OfficialActionCsvParser>();
builder.Services.AddScoped<OfficialActionImporter>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Fraud.Collectors");

var path = Path.GetFullPath(options.FilePath);
if (!File.Exists(path))
{
    ImportLog.FileNotFound(logger, options.FilePath);
    return 1;
}

if (builder.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DatabaseMigrator.MigrateAsync(host.Services).ConfigureAwait(false);
}

await using var scope = host.Services.CreateAsyncScope();
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
