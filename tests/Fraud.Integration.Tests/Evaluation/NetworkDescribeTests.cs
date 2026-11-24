using System.Globalization;
using System.Net;
using Fraud.Collectors.Evaluation;
using Fraud.Collectors.RipeStat;
using Fraud.Domain.Collection;
using Fraud.Domain.Control;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Fraud.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Integration.Tests.Evaluation;

/// <summary>
/// <c>net-describe</c> komutu gerçek PostgreSQL üzerinde (Testcontainers), uydurma veriyle. Ağa hiç istek atılmaz: Komut yalnızca
/// tablolardaki satırları okur. Adlar .example, IP'ler belgeleme aralığından (RFC 5737).
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetworkDescribeTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    // Pozitiflerin karar günü (2026/30) ve ondan önceki ölçüm günü.
    private static readonly DateOnly DecisionDay = new(2026, 8, 1);
    private static readonly DateTimeOffset Before = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fraud-net-it-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static DomainName Domain(string host) => new(new NormalizedDomain(host, host, null, "example", host), Now);

    private static string Two(int i) => i.ToString("00", CultureInfo.InvariantCulture);

    private static DnsSnapshot Doh(long domainId, DateTimeOffset at, string type, params DnsAnswer[] answers) =>
        new(domainId, at, "test-lab", "cloudflare", DnsTransport.Doh, type, 0, DnsOutcome.Success, DnsErrorKind.None, answers, null, false, 10, 1, false);

    [Fact]
    public async Task Command_prints_only_aggregates_and_respects_as_of_and_small_cells()
    {
        await using var db = await postgres.CreateMigratedContextAsync();
        var ct = TestContext.Current.CancellationToken;
        var url = new Uri("https://bulten.example/2026-30");

        // 12 pozitif. 11'inin DNS kaydı karardan önce, 1'ininki karar gününden SONRA (görünmemeli).
        // Karardan önce: 6'sı AS64500 (CDN dışı), 5'i AS13335 (CDN). İkisi aynı sertifikada.
        var positives = Enumerable.Range(1, 12).Select(i => Domain("poz-" + Two(i) + ".example")).ToList();
        db.Domains.AddRange(positives);
        foreach (var p in positives)
        {
            db.OfficialActions.Add(new OfficialAction(p, OfficialActionSource.SpkBulletin, "2026/30", DecisionDay, OfficialActionCategory.Other, url, p.Host, Now));
        }

        // 10 kontrol (G4a), hepsi AS64502. Kontrol grubunun ASN'i hiçbir koşulda yazılmamalı.
        var controls = Enumerable.Range(1, 10).Select(i => Domain("kontrol-" + Two(i) + ".example")).ToList();
        db.Domains.AddRange(controls);
        await db.SaveChangesAsync(ct);
        foreach (var c in controls)
        {
            db.ControlMembers.Add(new ControlMember(c, ControlGroup.G4a, null, Now, null, "kontrol-g4a-it", "X1", "kontrol-g4a",
                firstCertificateAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        for (var i = 0; i < positives.Count; i++)
        {
            var at = i == 11 ? new DateTimeOffset(DecisionDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(12) : Before;
            var ip = i < 6 ? "198.51.100.1" : "192.0.2.1";
            db.DnsSnapshots.Add(Doh(positives[i].Id, at, "A", new DnsAnswer("A", ip, 300)));
            db.DnsSnapshots.Add(Doh(positives[i].Id, at, "NS", new DnsAnswer("NS", "ns1.saglayici-" + (i % 6) + ".example", 300)));
        }

        foreach (var c in controls)
        {
            // Kontrol referansı (ilk sertifika kuralıyla) 2 Eylül; DNS bundan önce.
            db.DnsSnapshots.Add(Doh(c.Id, new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero), "A", new DnsAnswer("A", "203.0.113.1", 300)));
        }

        var ripeAt = Before.AddHours(1);
        db.IpAsnSnapshots.Add(new IpAsnSnapshot(IPAddress.Parse("198.51.100.1"), ripeAt, "test-lab", CollectionOutcome.Success, CollectionErrorKind.None, 200, [64500], "198.51.100.0/24", null));
        // Kayıttaki işaret boş, ama ASN bugünkü CDN listesinde: Yükleyici yine CDN saymalı.
        db.IpAsnSnapshots.Add(new IpAsnSnapshot(IPAddress.Parse("192.0.2.1"), ripeAt, "test-lab", CollectionOutcome.Success, CollectionErrorKind.None, 200, [13335], "192.0.2.0/24", null));
        db.IpAsnSnapshots.Add(new IpAsnSnapshot(IPAddress.Parse("203.0.113.1"), ripeAt, "test-lab", CollectionOutcome.Success, CollectionErrorKind.None, 200, [64502], "203.0.113.0/24", null));
        // Ertesi günkü hata satırı: "kayıt yok" sayılmaz, yalnızca başlıkta sayılır.
        db.IpAsnSnapshots.Add(new IpAsnSnapshot(IPAddress.Parse("198.51.100.1"), ripeAt.AddDays(1), "test-lab", CollectionOutcome.Error, CollectionErrorKind.Timeout, null, [], null, null));
        db.PortSnapshots.Add(new PortSnapshot(IPAddress.Parse("198.51.100.1"), ripeAt, "test-lab", CollectionOutcome.Success, CollectionErrorKind.None, 200, [22, 443], [], []));
        await db.SaveChangesAsync(ct);

        // Ortak sertifika: poz-01 ve poz-02, karardan önce.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO certificates (crtsh_id, serial_number, issuer_ca_id, issuer_name, common_name, names, not_before, not_after, logged_at, is_precertificate, observed_at)
            VALUES (1, '0a', NULL, 'C=US, O=Test CA', NULL, ARRAY['poz-01.example','poz-02.example'], {Before.AddDays(-30)}, {Before.AddDays(60)}, NULL, NULL, {Now})
            """, ct);
        var certId = await db.Certificates.Select(c => c.Id).SingleAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO certificate_domains (certificate_id, domain_id, observed_at) VALUES ({certId}, {positives[0].Id}, {Now})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO certificate_domains (certificate_id, domain_id, observed_at) VALUES ({certId}, {positives[1].Id}, {Now})", ct);
        db.ChangeTracker.Clear();

        var repo = TempRepo();
        var config = Path.Combine(_dir, "eval.json");
        await File.WriteAllTextAsync(config, """{ "controlReference": "FirstCertificate", "bootstrapIterations": 100 }""", ct);
        using var output = new StringWriter();

        var exit = await NetworkCommands.DescribeAsync(
            db, new PublicSuffixDomainNormalizer(), new RipeStatOptions().CdnAsns, EvaluationCommands.ParseArgs(["--config", config]),
            output, repo, _ => true, ct, Now);
        var text = output.ToString();

        Assert.Equal(0, exit);
        // Görünüm kuralı: Karar günü ölçülen pozitif tabana girmez (12 değil 11).
        Assert.Contains("Grup Positive: birim=12, taban (DNS kaydı bilinen)=11", text, StringComparison.Ordinal);
        Assert.Contains("Grup ControlRandom / None: birim=10, taban (DNS kaydı bilinen)=10", text, StringComparison.Ordinal);
        // Ortak ASN: 6/11 ve 5/11 (ikisinin de tümleyeni en az 5). Kontrolün ASN'i (64502) hiç yazılmaz.
        Assert.Contains("AS64500 6/11", text, StringComparison.Ordinal);
        Assert.Contains("AS13335 5/11", text, StringComparison.Ordinal);
        Assert.DoesNotContain("64502", text, StringComparison.Ordinal);
        // CDN listesi kayıttaki boş işarete rağmen uygulandı: 5 pozitif yalnız CDN arkasında.
        Assert.Contains("Yalnız CDN/bulut arkasında (ASN'si bilinen bütün IP'leri): 5/11", text, StringComparison.Ordinal);
        Assert.Contains("RIPEstat=1", text, StringComparison.Ordinal);
        // Küme: İki pozitif ortak sertifikayla bağlı; küçük hücre olduğu için sayı yazılmaz.
        Assert.Contains("Ortak sertifikayla bağlanan pozitif: <5/12", text, StringComparison.Ordinal);
        Assert.Contains("bölme değişmedi", text, StringComparison.Ordinal);
        // Hiçbir ad, IP ya da NS sağlayıcısı basılmaz.
        Assert.DoesNotContain(".example", text, StringComparison.Ordinal);
        Assert.DoesNotContain("saglayici", text, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100", text, StringComparison.Ordinal);
        Assert.DoesNotContain("192.0.2.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113", text, StringComparison.Ordinal);
        // Kimlik anahtarı yalnızca data/private altında.
        Assert.True(File.Exists(Path.Combine(repo.Root, "data", "private", "evaluation", "subject_id.key")));
    }

    [Fact]
    public async Task Missing_config_is_a_usage_error_and_touches_nothing()
    {
        using var output = new StringWriter();
        var exit = await NetworkCommands.DescribeAsync(
            null!, new PublicSuffixDomainNormalizer(), new RipeStatOptions().CdnAsns, EvaluationCommands.ParseArgs([]),
            output, TempRepo(), _ => true, TestContext.Current.CancellationToken);

        Assert.Equal(64, exit);
        Assert.False(Directory.Exists(Path.Combine(_dir, "data")));
    }

    private Fraud.Collectors.Evaluation.RepoContext TempRepo()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, Fraud.Collectors.Evaluation.RepoContext.MarkerFile), "<Solution />");
        return Fraud.Collectors.Evaluation.RepoContext.At(_dir);
    }
}
