using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fraud.Integration.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_apply_to_an_empty_database_and_match_the_model()
    {
        await using var db = PostgresFixture.CreateContext(await postgres.CreateEmptyDatabaseAsync());

        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        // Model değişip migration eklenmeyi unutulursa bu test kırılır.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Migrating_twice_is_harmless()
    {
        await using var db = await postgres.CreateMigratedContextAsync();

        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Database_rejects_official_action_without_bulletin_number_or_with_bad_url()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;

        // Uygulama katmanı atlansa bile veritabanı kuralları geçersiz kaydı durdurur.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO domains (host, registrable_domain, public_suffix, unicode_host, first_observed_at) " +
            "VALUES ('ornek.example', 'ornek.example', 'example', 'ornek.example', now())", ct);

        var blankBulletin = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO official_actions (domain_id, source, bulletin_number, decision_date, category, source_url, listed_as, observed_at) " +
            "SELECT id, 'SpkBulletin', '  ', '2026-01-08', 'Crypto', 'https://spk.gov.tr/b/1', 'ornek.example', now() FROM domains", ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, blankBulletin.SqlState);

        var badUrl = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO official_actions (domain_id, source, bulletin_number, decision_date, category, source_url, listed_as, observed_at) " +
            "SELECT id, 'SpkBulletin', '2026/1', '2026-01-08', 'Crypto', 'spk.gov.tr/b/1', 'ornek.example', now() FROM domains", ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badUrl.SqlState);
    }
}
