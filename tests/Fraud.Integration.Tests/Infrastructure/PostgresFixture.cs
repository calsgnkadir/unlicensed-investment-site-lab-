using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Fraud.Integration.Tests.Infrastructure.PostgresFixture))]

namespace Fraud.Integration.Tests.Infrastructure;

/// <summary>
/// Bütün test çalıştırması için tek bir PostgreSQL konteyneri açar. Her test kendi boş veritabanını
/// ister; böylece testler birbirinin verisini görmez ve paralel çalışabilir.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // docker-compose.yml ile aynı sürüm. Biri değişirse diğeri de değişmeli.
    public const string Image = "postgres:17.9-alpine3.23";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Yeni, boş bir veritabanı oluşturur ve bağlantı metnini döner. Migration uygulanmaz.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Yeni bir veritabanı oluşturur, migration'ları uygular ve bağlamı döner.</summary>
    public async Task<FraudDbContext> CreateMigratedContextAsync()
    {
        var context = CreateContext(await CreateEmptyDatabaseAsync());
        await context.Database.MigrateAsync();
        return context;
    }

    public static FraudDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<FraudDbContext>().UseNpgsql(connectionString).Options);
}
