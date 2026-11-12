using Fraud.Collectors.OfficialActions;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Integration.Tests.OfficialActions;

// Uydurma CSV ile gerçek PostgreSQL üzerinde yükleme testleri.
[Trait("Category", "Integration")]
public sealed class OfficialActionImporterTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset FirstRunAt = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly string SampleCsv = $"""
        {OfficialActionCsvParserTests.Header}
        ornek-yatirim.example,2026/46,2026-08-14,kripto,2026-05-02,7,var,https://spk.gov.tr/bulten/2026-46
        https://www.ORNEK-YATIRIM.example/giris,2026/46,2026-08-14,kripto,,,,https://spk.gov.tr/bulten/2026-46
        ornek-yatirim.example,2026/47,2026-08-21,kripto,,,,https://spk.gov.tr/bulten/2026-47
        panel.ornek-kaldirac.com.tr,2026/47,2026-08-21,kaldıraçlı,,,,https://spk.gov.tr/bulten/2026-47
        örnek-yatırım.com,2026/47,2026-08-21,diğer,,,,https://spk.gov.tr/bulten/2026-47
        eksik-bulten.example,,2026-08-21,kripto,,,,https://spk.gov.tr/bulten/2026-47
        eksik-kaynak.example,2026/47,2026-08-21,kripto,,,,
        """;

    private static async Task<OfficialActionImportReport> ImportAsync(FraudDbContext db, string csv, DateTimeOffset now)
    {
        var parser = new OfficialActionCsvParser(new PublicSuffixDomainNormalizer(), new OfficialActionImportOptions());
        var importer = new OfficialActionImporter(db, parser, new FixedTimeProvider(now));
        using var reader = new StringReader(csv);
        return await importer.ImportAsync(reader, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Valid_rows_are_stored_and_invalid_rows_are_reported()
    {
        await using var db = await postgres.CreateMigratedContextAsync();

        var report = await ImportAsync(db, SampleCsv, FirstRunAt);

        Assert.Equal(5, report.ValidRows);
        Assert.Equal(4, report.Inserted);
        Assert.Equal(1, report.AlreadyPresent); // aynı bültende "www." ve URL biçimiyle tekrar yazılmış satır
        Assert.Equal(3, report.NewDomains);
        Assert.Empty(report.Conflicts);
        Assert.Equal(
            [(7, RowRejectionReason.MissingBulletinNumber), (8, RowRejectionReason.MissingSourceUrl)],
            report.Rejections.Select(r => (r.RowNumber, r.Reason)));
        Assert.False(report.IsClean);

        db.ChangeTracker.Clear();
        var actions = await db.OfficialActions.Include(a => a.Domain)
            .OrderBy(a => a.BulletinNumber).ThenBy(a => a.Domain.Host)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, actions.Count);
        Assert.All(actions, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.BulletinNumber));
            Assert.StartsWith("https://spk.gov.tr/", a.SourceUrl, StringComparison.Ordinal);
            Assert.Equal(FirstRunAt, a.ObservedAt);
            Assert.Equal(OfficialActionSource.SpkBulletin, a.Source);
        });

        var first = actions[0];
        Assert.Equal("2026/46", first.BulletinNumber);
        Assert.Equal("ornek-yatirim.example", first.Domain.Host);
        Assert.Equal(new DateOnly(2026, 8, 14), first.DecisionDate);
        Assert.Equal("ornek-yatirim.example", first.ListedAs);

        var subdomain = actions.Single(a => a.Domain.Host == "panel.ornek-kaldirac.com.tr");
        Assert.Equal("ornek-kaldirac.com.tr", subdomain.Domain.RegistrableDomain);
        Assert.Equal(OfficialActionCategory.Leveraged, subdomain.Category);

        var idn = actions.Single(a => a.Domain.Host.StartsWith("xn--", StringComparison.Ordinal));
        Assert.Equal("örnek-yatırım.com", idn.Domain.UnicodeHost);
    }

    [Fact]
    public async Task Importing_the_same_file_twice_does_not_duplicate_anything()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;

        await ImportAsync(db, SampleCsv, FirstRunAt);
        db.ChangeTracker.Clear();
        var second = await ImportAsync(db, SampleCsv, SecondRunAt);

        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.NewDomains);
        Assert.Equal(5, second.AlreadyPresent);
        Assert.Empty(second.Conflicts);

        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Domains.CountAsync(ct));
        Assert.Equal(4, await db.OfficialActions.CountAsync(ct));

        // İlk görülme zamanı ikinci çalıştırmada değişmez.
        Assert.All(await db.OfficialActions.ToListAsync(ct), a => Assert.Equal(FirstRunAt, a.ObservedAt));
        Assert.All(await db.Domains.ToListAsync(ct), d => Assert.Equal(FirstRunAt, d.FirstObservedAt));
    }

    [Fact]
    public async Task Same_key_with_different_content_is_a_conflict_and_is_not_overwritten()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;

        await ImportAsync(db, $"""
            {OfficialActionCsvParserTests.Header}
            ornek.com,2026/5,2026-02-12,kripto,,,,https://spk.gov.tr/bulten/2026-5
            """, FirstRunAt);
        db.ChangeTracker.Clear();

        var report = await ImportAsync(db, $"""
            {OfficialActionCsvParserTests.Header}
            ornek.com,2026/5,2026-02-13,diğer,,,,https://spk.gov.tr/bulten/2026-5
            yeni-ornek.com,2026/5,2026-02-12,kripto,,,,https://spk.gov.tr/bulten/2026-5
            """, SecondRunAt);

        Assert.Equal(1, report.Inserted);
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal(2, conflict.RowNumber);
        Assert.Contains("karar_tarihi", conflict.Detail, StringComparison.Ordinal);
        Assert.Contains("kategori", conflict.Detail, StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        var stored = await db.OfficialActions.Include(a => a.Domain).SingleAsync(a => a.Domain.Host == "ornek.com", ct);
        Assert.Equal(new DateOnly(2026, 2, 12), stored.DecisionDate);
        Assert.Equal(OfficialActionCategory.Crypto, stored.Category);
    }

    [Fact]
    public async Task Same_domain_in_different_bulletins_gives_separate_actions_and_one_domain()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;

        await ImportAsync(db, $"""
            {OfficialActionCsvParserTests.Header}
            ornek.com,2026/5,2026-02-12,kripto,,,,https://spk.gov.tr/bulten/2026-5
            WWW.ORNEK.COM,2026/9,2026-03-12,kripto,,,,https://spk.gov.tr/bulten/2026-9
            """, FirstRunAt);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Domains.CountAsync(ct));
        Assert.Equal(2, await db.OfficialActions.CountAsync(ct));
    }

    [Fact]
    public async Task Database_unique_index_blocks_duplicates_even_without_the_importer()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;

        await ImportAsync(db, $"""
            {OfficialActionCsvParserTests.Header}
            ornek.com,2026/5,2026-02-12,kripto,,,,https://spk.gov.tr/bulten/2026-5
            """, FirstRunAt);

        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO official_actions (domain_id, source, bulletin_number, decision_date, category, source_url, listed_as, observed_at) " +
            "SELECT domain_id, source, bulletin_number, decision_date, category, source_url, listed_as, now() FROM official_actions", ct));
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, ex.SqlState);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
