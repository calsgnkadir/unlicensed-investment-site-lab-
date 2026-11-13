using System.Text;
using Fraud.Collectors.PortAudit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fraud.Security.Tests.ResourceLimits;

/// <summary>
/// port-audit (host ağı) -> durum dosyası -> port-audit-exporter (iç ağ). Exporter dosyaya körü körüne güvenmemeli:
/// bozuk, çok büyük, saldırgan değerli dosya istisnayla sunucuyu durdurmaz, sınırsız etiket üretmez, "temiz" demez.
/// </summary>
public sealed class PortAuditStateAttackTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("port-audit-state-");

    private string StateFile => Path.Combine(_dir.FullName, "state.json");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Hostile_values_are_clamped_and_findings_are_bounded()
    {
        var findings = new StringBuilder();
        for (var i = 0; i < 5000; i++)
        {
            findings.Append(i == 0 ? "" : ",").Append("{\"protocol\":\"tcp\",\"port\":").Append(i).Append(",\"scope\":\"wildcard\"}");
        }

        File.WriteAllText(StateFile, $$"""
            {"completedAt":"2026-11-13T10:00:00Z","tcpListeners":-5,"udpListeners":-1,"malformedLines":-9,"expectedEntries":-2,
             "diskAvailable":-1,"diskTotal":-7,"errors":-3,
             "unexpected":[{{findings}},
               {"protocol":"tcp","port":70000,"scope":"wildcard"},{"protocol":"tcp","port":-1,"scope":"wildcard"},
               {"protocol":"tcp","port":22,"scope":null}],
             "missing":[{"protocol":"udp","port":53,"scope":"loopback"},{"protocol":"udp","port":53}]}
            """);

        var snapshot = PortAuditStateFile.Read(StateFile);

        Assert.NotNull(snapshot);
        Assert.Equal((0, 0, 0, 0, 0L), (snapshot.TcpListeners, snapshot.UdpListeners, snapshot.MalformedLines, snapshot.ExpectedEntries, snapshot.Errors));
        Assert.Null(snapshot.DiskAvailable);
        Assert.Null(snapshot.DiskTotal);
        Assert.Equal(PortAuditSnapshot.MaxFindings, snapshot.Unexpected.Count);
        Assert.All(snapshot.Unexpected, f => Assert.InRange(f.Port, 0, 65535));
        Assert.All(snapshot.Unexpected, f => Assert.NotNull(f.Scope));
        Assert.Equal([new PortAuditFinding(ListenerProtocol.Udp, 53, null)], snapshot.Missing);
    }

    [Theory]
    [InlineData("""{"unexpected":[{"protocol":"sctp","port":1,"scope":"wildcard"}]}""")]   // tanımsız enum
    [InlineData("""{"unexpected":[{"protocol":1,"port":1,"scope":3}]}""")]                // sayısal enum kapalı
    [InlineData("""{"unexpected":"tcp/22"}""")]
    [InlineData("""{"tcpListeners":"lots"}""")]
    [InlineData("not json")]
    [InlineData("""[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]]""")]
    [InlineData("")]
    public void Malformed_state_is_a_read_error_not_a_clean_audit(string json)
    {
        File.WriteAllText(StateFile, json);

        Assert.Throws<InvalidDataException>(() => PortAuditStateFile.Read(StateFile));
    }

    [Fact]
    public void Oversized_state_file_is_refused_before_parsing()
    {
        File.WriteAllText(StateFile, "{\"tcpListeners\":1," + new string(' ', PortAuditStateFile.MaxBytes) + "}");

        Assert.Throws<InvalidDataException>(() => PortAuditStateFile.Read(StateFile));
    }

    [Fact]
    public void Exporter_keeps_serving_and_counts_errors_when_the_file_turns_hostile()
    {
        var options = new PortAuditOptions { StateFile = StateFile };
        using var metrics = new PortAuditMetrics();
        var exporter = new PortAuditExporterWorker(options, metrics, TimeProvider.System, NullLogger<PortAuditExporterWorker>.Instance);

        // Dosya yok: sonuç yok (son başarı zamanı sunulmaz; PortAuditStale'in "absent" kolu çalışır).
        Assert.False(exporter.RefreshOnce());

        File.WriteAllText(StateFile, """{"completedAt":"2026-11-13T10:00:00Z","tcpListeners":3}""");
        Assert.True(exporter.RefreshOnce());

        File.WriteAllText(StateFile, "{ bozuk");
        Assert.False(exporter.RefreshOnce()); // istisna dışarı çıkmaz
    }

    [Fact]
    public void Future_completion_time_cannot_silence_the_staleness_alert()
    {
        // Geleceğe ait CompletedAt, "time() - son_denetim > 300" ifadesini hiç doğru yapmaz; reddedilmeli.
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        File.WriteAllText(StateFile, """{"completedAt":"9999-01-01T00:00:00Z","tcpListeners":1}""");

        Assert.Throws<InvalidDataException>(() => PortAuditStateFile.Read(StateFile, now));

        File.WriteAllText(StateFile, """{"completedAt":"2026-11-13T11:59:30Z","tcpListeners":1}""");
        Assert.Equal(1, PortAuditStateFile.Read(StateFile, now)!.TcpListeners);
    }

    [Fact]
    public void Writer_never_leaves_a_half_written_file()
    {
        var snapshot = new PortAuditSnapshot { CompletedAt = DateTimeOffset.UnixEpoch, TcpListeners = 1 };

        PortAuditStateFile.Write(StateFile, snapshot);

        Assert.False(File.Exists(StateFile + ".tmp"));
        Assert.Equal(1, PortAuditStateFile.Read(StateFile)!.TcpListeners);
    }
}
