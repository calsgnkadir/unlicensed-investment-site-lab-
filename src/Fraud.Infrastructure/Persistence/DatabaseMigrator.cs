using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>Bekleyen migration'ları uygular. Laboratuvar ortamı içindir; ayar ile açılır.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
