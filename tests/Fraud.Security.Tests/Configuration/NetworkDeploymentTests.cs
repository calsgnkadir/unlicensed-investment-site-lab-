using System.Text.RegularExpressions;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Configuration;

/// <summary>
/// Linux kurulumundaki <c>network</c> servisi ve ağ toplayıcısının hız ve gizlilik sözleşmesi (dosya içeriği denetimi).
/// </summary>
public sealed partial class NetworkDeploymentTests
{
    private const string Compose = "deploy/linux/docker-compose.ct.yml";

    private static string Block(string text, string service)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        Assert.True(start >= 0, service + " servisi bulunamadı");
        var end = Array.FindIndex(lines, start + 1, l => l.Length > 0 && !l.StartsWith("   ", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    [Fact]
    public void Network_service_inherits_hardening_and_publishes_no_port()
    {
        var compose = RepoFiles.Read(Compose).Replace("\r\n", "\n", StringComparison.Ordinal);
        var network = Block(compose, "network");
        var anchor = compose[compose.IndexOf("x-collectors:", StringComparison.Ordinal)..compose.IndexOf("services:", StringComparison.Ordinal)];

        Assert.Contains("<<: *collectors", network, StringComparison.Ordinal);
        Assert.Contains("read_only: true", anchor, StringComparison.Ordinal);
        Assert.Matches(@"cap_drop:\s*\n\s*-\s*ALL", anchor);
        Assert.Contains("no-new-privileges:true", anchor, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s+ports:", compose);
        Assert.DoesNotContain("network_mode", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("privileged", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("cap_add", compose, StringComparison.Ordinal);
        Assert.Contains("mem_limit:", network, StringComparison.Ordinal);
        Assert.Contains("pids_limit:", network, StringComparison.Ordinal);
        Assert.Contains("Dns__Location:", network, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^  backend:\s*\n\s*internal: true", compose);
    }

    [Fact]
    public void Network_service_does_not_override_pacing_below_one_per_second()
    {
        var network = Block(RepoFiles.Read(Compose), "network");

        Assert.DoesNotContain("MinQueryInterval", network, StringComparison.Ordinal);
        Assert.DoesNotContain("MinRequestInterval", network, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolvers", network, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_script_reads_no_domain_names_answers_or_ips()
    {
        var sql = string.Join('\n', RepoFiles.ReadLines("deploy/linux/status.sh").Where(l => !l.TrimStart().StartsWith('#')));

        Assert.DoesNotMatch(@"\banswers\b", sql);
        Assert.DoesNotMatch(@"\bhost\b", sql);
        Assert.DoesNotMatch(@"\bprefix\b", sql);
        Assert.DoesNotMatch(@"(?i)select[^;]*\bip\b", sql);
        Assert.DoesNotMatch(@"\bcpes\b|\btags\b", sql);
    }

    [Fact]
    public void Checked_in_dns_settings_keep_the_politeness_floor()
    {
        var settings = RepoFiles.Read("src/Fraud.Collectors/appsettings.json");

        Assert.Contains("\"MinQueryInterval\": \"00:00:01\"", settings, StringComparison.Ordinal);
        Assert.Matches(@"""Resolvers"":\s*\[\s*""cloudflare"",\s*""google""\s*\]", settings);
        Assert.DoesNotMatch(@"(?i)""Location""\s*:\s*""[^""]*(kadir|ev-|home)", settings);
    }

    [Fact]
    public void Network_collection_is_guarded_against_a_second_concurrent_process()
    {
        // Bulgu N8: Hız sınırlayıcı süreç içidir. README, servis çalışırken "docker compose run --rm network
        // collect-dns" komutunu öneriyor; iki süreç aynı çözücülere saniyede 2 paket gönderir ve henüz ölçülmemiş
        // alan adlarını aynı anda sorar. CtLogLock gibi bir PostgreSQL advisory lock (kaynak başına) yok.
        var files = RunnerFiles
            .Concat(Directory.GetFiles(Path.Combine(RepoFiles.Root, "src/Fraud.Collectors/Dns"), "*.cs")
                .Select(f => Path.GetRelativePath(RepoFiles.Root, f)));

        Assert.Contains(files, f => AdvisoryLock().IsMatch(RepoFiles.Read(f)));
    }

    [Fact]
    public async Task Network_source_lock_can_be_rechecked_during_a_run()
    {
        // Bulgu N9: Kilit oturum düzeyindedir; kilit bağlantısı koparsa (veritabanı yeniden başlar, ağ kesilir)
        // PostgreSQL kilidi bırakır. DNS turu ~1 saat sürer ve kilidi hiç yeniden denetlemez; bu arada başlayan elle
        // "collect-dns" kilidi alır ve iki süreç yine saniyede 2 paket gönderir (N8 geri açılır). CtLogLock bunun için
        // IsHeldAsync sunar ve tur başında denetler.
        var handle = await Fraud.Collectors.Network.NetworkSourceLock.Unlocked.TryAcquireAsync(
            Fraud.Domain.Collection.CollectionSource.Dns, TestContext.Current.CancellationToken);
        Assert.NotNull(handle);
        await using (handle)
        {
            var isHeld = handle.GetType().GetMethod("IsHeldAsync");
            Assert.NotNull(isHeld);
            Assert.Equal(typeof(Task<bool>), isHeld.ReturnType);
        }

        // Ve çalıştırıcılar bunu alan adı / IP başına (veya en az birkaç dakikada bir) çağırmalı.
        Assert.Contains(RunnerFiles, f => RepoFiles.Read(f).Contains("IsHeldAsync", StringComparison.Ordinal));
    }

    private static readonly string[] RunnerFiles =
        ["src/Fraud.Collectors/Collection/CollectionRunner.cs", "src/Fraud.Collectors/Network/IpCollectionRunner.cs"];

    [GeneratedRegex("pg_try_advisory_lock|pg_advisory_lock|SourceLock|CollectionLock")]
    private static partial Regex AdvisoryLock();
}
