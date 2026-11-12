using Fraud.Infrastructure;
using Fraud.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

// Konteyner sağlık denetimi: Çalışma imajında curl yok; aynı program kendi hazır olma adresini sorar.
if (args.Contains("--health-probe"))
{
    return await HealthProbe.RunAsync().ConfigureAwait(false);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFraudInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FraudDbContext>("database", tags: ["ready"]);

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DatabaseMigrator.MigrateAsync(app.Services).ConfigureAwait(false);
}

// Canlılık: süreç ayakta mı. Hazır olma: veritabanına ulaşılabiliyor mu.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Yalnızca toplu sayılar döner; tek tek alan adı listelenmez.
app.MapGet("/api/official-actions/summary", async (FraudDbContext db, CancellationToken ct) =>
{
    var domainCount = await db.Domains.CountAsync(ct).ConfigureAwait(false);
    var actionCount = await db.OfficialActions.CountAsync(ct).ConfigureAwait(false);
    var bulletinCount = await db.OfficialActions.Select(a => a.BulletinNumber).Distinct().CountAsync(ct).ConfigureAwait(false);
    return Results.Ok(new { domains = domainCount, officialActions = actionCount, bulletins = bulletinCount });
});

await app.RunAsync().ConfigureAwait(false);
return 0;

internal static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0] ?? "8080";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/health/ready")).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
