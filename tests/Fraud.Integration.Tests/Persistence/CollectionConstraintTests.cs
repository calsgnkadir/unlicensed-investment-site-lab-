using Fraud.Domain.Collection;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Fraud.Integration.Tests.Persistence;

// Uygulama katmanı atlansa bile veritabanı "hata = kayıt yok" karışıklığına ve kişi verisine izin vermez.
[Trait("Category", "Integration")]
public sealed class CollectionConstraintTests(PostgresFixture postgres)
{
    private const string LogInsert =
        "INSERT INTO collection_log (source, domain_id, measurement_location, started_at, finished_at, http_status, attempts, " +
        "outcome, error_kind, item_count, skipped_item_count, new_item_count, truncated) VALUES ";

    [Theory]
    [InlineData("('CrtSh', NULL, 'lab', now(), now(), 503, 4, 'Empty', 'None', 0, 0, 0, false)")] // 503 boş sayılamaz
    [InlineData("('CrtSh', NULL, 'lab', now(), now(), NULL, 1, 'Empty', 'None', 0, 0, 0, false)")] // yanıt yokken boş
    [InlineData("('CrtSh', NULL, 'lab', now(), now(), 200, 1, 'Empty', 'None', 0, 3, 0, false)")] // bozuk satır varken boş
    [InlineData("('CrtSh', NULL, 'lab', now(), now(), 429, 2, 'Error', 'None', 0, 0, 0, false)")] // türsüz hata
    [InlineData("('CrtSh', NULL, 'lab', now(), now(), 200, 1, 'Success', 'Timeout', 5, 0, 0, false)")] // hatalı başarı
    [InlineData("('CrtSh', NULL, 'lab', now(), now(), 200, 1, 'Success', 'None', 0, 0, 0, false)")] // kayıtsız başarı
    [InlineData("('CrtSh', NULL, '  ', now(), now(), 200, 1, 'Empty', 'None', 0, 0, 0, false)")] // ölçüm konumu yok
    [InlineData("('CrtSh', NULL, 'lab', now(), now() - interval '1 hour', 200, 1, 'Empty', 'None', 0, 0, 0, false)")]
    public async Task Database_rejects_inconsistent_collection_log_rows(string values)
    {
        await using var db = await postgres.CreateMigratedContextAsync();

        // SQL yalnızca bu dosyadaki sabit test verisinden oluşur; dışarıdan girdi yok.
#pragma warning disable EF1003
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => db.Database.ExecuteSqlRawAsync(LogInsert + values, TestContext.Current.CancellationToken));
#pragma warning restore EF1003

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_accepts_a_real_empty_result()
    {
        await using var db = await postgres.CreateMigratedContextAsync();

        var rows = await db.Database.ExecuteSqlRawAsync(
            LogInsert + "('CrtSh', NULL, 'lab', now(), now(), 200, 1, 'Empty', 'None', 0, 0, 0, false)",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Database_rejects_email_in_issuer_and_query_string_in_archive_url()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;

        var issuer = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO certificates (crtsh_id, serial_number, issuer_name, names, not_before, not_after, observed_at) " +
            "VALUES (1, '01', 'CN=Ornek CA, emailAddress=kisi@ornek.example', ARRAY[]::text[], now(), now(), now())", ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, issuer.SqlState);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO domains (host, registrable_domain, public_suffix, unicode_host, first_observed_at) " +
            "VALUES ('ornek.example', 'ornek.example', 'example', 'ornek.example', now())", ct);
        var url = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO archive_snapshots (domain_id, capture_key, cdx_timestamp, captured_at, original_url, query_removed, observed_at) " +
            "SELECT id, repeat('a', 64), '20260101000000', now(), 'http://ornek.example/?email=kisi@ornek.example', false, now() FROM domains", ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, url.SqlState);
    }

    [Fact]
    public void Entity_refuses_to_record_an_error_as_empty()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => new CollectionLogEntry(
            CollectionSource.CrtSh, 1, "lab", now, now, 503, 4, CollectionOutcome.Empty, CollectionErrorKind.None, 0, 0, 0, false));
        Assert.Throws<ArgumentException>(() => new CollectionLogEntry(
            CollectionSource.CrtSh, 1, "lab", now, now, 429, 2, CollectionOutcome.Error, CollectionErrorKind.None, 0, 0, 0, false));
        Assert.Throws<ArgumentException>(() => new CollectionLogEntry(
            CollectionSource.CrtSh, 1, " ", now, now, 200, 1, CollectionOutcome.Empty, CollectionErrorKind.None, 0, 0, 0, false));
    }
}
