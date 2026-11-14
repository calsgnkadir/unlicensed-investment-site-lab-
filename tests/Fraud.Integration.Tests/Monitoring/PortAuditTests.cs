using System.Net;
using System.Net.Sockets;
using Fraud.Collectors.PortAudit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fraud.Integration.Tests.Monitoring;

// Kendi makinemizin port denetimi: /proc/net örnek dosyaları, beklenen liste ve fark. Ağ yok, gerçek /proc okunmaz.
public sealed class PortAuditTests : IDisposable
{
    // Adres makine bayt sırasında (little-endian), port ağ sırasında yazılır.
    // 3500007F = 127.0.0.53, 0100007F = 127.0.0.1, 01F01FAC = 172.31.240.1, FEFFFF0A = 10.255.255.254.
    private const string Tcp = """
          sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
           0: 3500007F:0035 00000000:0000 0A 00000000:00000000 00:00000000 00000000   991        0 1001 1 0000000000000000 100 0 0 10 5
           1: 0100007F:CF08 00000000:0000 0A 00000000:00000000 00:00000000 00000000   472        0 1002 1 0000000000000000 100 0 0 10 0
           2: 0100007F:CF08 0100007F:9C40 01 00000000:00000000 00:00000000 00000000   472        0 1003 1 0000000000000000 20 4 30 10 -1
           3: 00000000:1FA3 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1004 1 0000000000000000 100 0 0 10 0
           4: 01F01FAC:24F9 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1654        0 1005 1 0000000000000000 100 0 0 10 0
           5: bozuk satır
           6: 0100007F:1F90 00000000:0000 06 00000000:00000000 03:00000F9A 00000000     0        0 0 3 0000000000000000
        """;

    private const string Tcp6 = """
          sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
           0: 00000000000000000000000001000000:1F90 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 2001 1 0000000000000000 100 0 0 10 0
           1: 00000000000000000000000000000000:0016 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 2002 1 0000000000000000 100 0 0 10 0
           2: 0000000000000000FFFF00000100007F:CF08 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000   472        0 2003 1 0000000000000000 100 0 0 10 0
        """;

    private const string Udp = """
           sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode ref pointer drops
          100: FEFFFF0A:0035 00000000:0000 07 00000000:00000000 00:00000000 00000000     0        0 3001 2 0000000000000000 0
          101: 0100007F:A1B2 08080808:0035 01 00000000:00000000 00:00000000 00000000  1000        0 3002 2 0000000000000000 0
          102: 00000000:0044 00000000:0000 07 00000000:00000000 00:00000000 00000000     0        0 3003 2 0000000000000000 0
        """;

    private const string Udp6 = """
           sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode ref pointer drops
            0: 00000000000000000000000001000000:0143 00000000000000000000000000000000:0000 07 00000000:00000000 00:00000000 00000000   999        0 4001 2 0000000000000000 0
        """;

    private const string Expected = """
        # Örnek beklenen liste
        tcp/53000@loopback          # Grafana (docker-proxy)
        tcp/9465@local, udp/53@local
        tcp/53@local
        tcp/32768-60999@loopback
        tcp/8080
        udp/323@loopback            # chrony komut portu
        tcp/5432                    # dinlemiyor: eksik
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fraud-port-audit-" + Guid.NewGuid().ToString("N"));

    public PortAuditTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Tcp_parser_keeps_only_listen_state_and_decodes_little_endian_addresses()
    {
        var listeners = ProcNetParser.Parse(Tcp, ListenerProtocol.Tcp, AddressFamily.InterNetwork, out var malformed);

        Assert.Equal(1, malformed);
        Assert.Equal(
            ["127.0.0.53:53", "127.0.0.1:53000", "0.0.0.0:8099", "172.31.240.1:9465"],
            listeners.Select(l => $"{l.Address}:{l.Port}").ToArray());
        Assert.Equal([BindScope.Loopback, BindScope.Loopback, BindScope.Wildcard, BindScope.Specific], listeners.Select(l => l.Scope).ToArray());
    }

    [Fact]
    public void Tcp6_parser_decodes_word_swapped_ipv6_and_mapped_ipv4()
    {
        var listeners = ProcNetParser.Parse(Tcp6, ListenerProtocol.Tcp, AddressFamily.InterNetworkV6, out var malformed);

        Assert.Equal(0, malformed);
        Assert.Equal(IPAddress.IPv6Loopback, listeners[0].Address);
        Assert.Equal(8080, listeners[0].Port);
        Assert.Equal(IPAddress.IPv6Any, listeners[1].Address);
        Assert.Equal(BindScope.Wildcard, listeners[1].Scope);
        Assert.Equal(IPAddress.Parse("::ffff:127.0.0.1"), listeners[2].Address);
        Assert.Equal(BindScope.Loopback, listeners[2].Scope);
    }

    [Fact]
    public void Udp_parser_keeps_unconnected_sockets_and_drops_connected_clients()
    {
        var v4 = ProcNetParser.Parse(Udp, ListenerProtocol.Udp, AddressFamily.InterNetwork, out _);
        var v6 = ProcNetParser.Parse(Udp6, ListenerProtocol.Udp, AddressFamily.InterNetworkV6, out _);

        // 101 numaralı satır 8.8.8.8:53'e bağlanmış bir istemci soketi: dinleyici değil.
        Assert.Equal(["10.255.255.254:53", "0.0.0.0:68"], v4.Select(l => $"{l.Address}:{l.Port}").ToArray());
        Assert.Equal(BindScope.Specific, v4[0].Scope);
        Assert.Single(v6);
        Assert.Equal(323, v6[0].Port);
        Assert.Equal(BindScope.Loopback, v6[0].Scope);
    }

    [Theory]
    [InlineData("0100007F:0035", "127.0.0.1", 53)]
    [InlineData("00000000:FFFF", "0.0.0.0", 65535)]
    [InlineData("0100007F:35", null, 0)]   // port 4 hane olmalı
    [InlineData("100007F:0035", null, 0)]  // adres 8 hane olmalı
    [InlineData("0100007G:0035", null, 0)] // onaltılık değil
    [InlineData("", null, 0)]
    public void Ipv4_endpoint_parsing_rejects_malformed_fields(string field, string? address, int port)
    {
        var ok = ProcNetParser.TryParseEndpoint(field, AddressFamily.InterNetwork, out var parsed, out var parsedPort);

        Assert.Equal(address is not null, ok);
        if (address is not null)
        {
            Assert.Equal(IPAddress.Parse(address), parsed);
            Assert.Equal(port, parsedPort);
        }
    }

    [Theory]
    [InlineData("tcp/22", ListenerProtocol.Tcp, 22, 22, ExpectedScope.Any)]
    [InlineData("UDP/53@local", ListenerProtocol.Udp, 53, 53, ExpectedScope.Local)]
    [InlineData("tcp/32768-60999@loopback", ListenerProtocol.Tcp, 32768, 60999, ExpectedScope.Loopback)]
    public void Expected_entries_parse(string token, ListenerProtocol protocol, int from, int to, ExpectedScope scope)
    {
        Assert.Equal(new ExpectedListener(protocol, from, to, scope), ExpectedListeners.ParseEntry(token));
    }

    [Theory]
    [InlineData("tcp/0")]
    [InlineData("tcp/70000")]
    [InlineData("tcp/80-79")]
    [InlineData("sctp/80")]
    [InlineData("tcp/80@lan")]
    [InlineData("tcp:80")]
    [InlineData("tcp/80,")]
    public void Invalid_expected_entries_are_rejected(string token)
    {
        Assert.Null(ExpectedListeners.ParseEntry(token));
    }

    [Fact]
    public void Invalid_line_in_expected_file_stops_with_line_number()
    {
        var ex = Assert.Throws<FormatException>(() => ExpectedListeners.Parse(["tcp/22", "# yorum", "tcp/abc"]));
        Assert.Contains("satır 3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_reports_unexpected_listeners_and_missing_expected_entries()
    {
        var listeners = Parse(Tcp, Tcp6, Udp, Udp6);
        var expected = ExpectedListeners.Parse(Expected.Split('\n'));

        var result = ExpectedListeners.Compare(listeners, expected);

        Assert.Equal(
            [
                new UnexpectedListener(ListenerProtocol.Tcp, 22, BindScope.Wildcard),
                new UnexpectedListener(ListenerProtocol.Tcp, 8099, BindScope.Wildcard),
                new UnexpectedListener(ListenerProtocol.Udp, 68, BindScope.Wildcard),
            ],
            result.Unexpected);
        Assert.Equal([new ExpectedListener(ListenerProtocol.Tcp, 5432, 5432, ExpectedScope.Any)], result.Missing);
    }

    [Fact]
    public void Loopback_only_entry_does_not_cover_a_wildcard_bind_of_the_same_port()
    {
        // Grafana yanlışlıkla 0.0.0.0:53000'e açılırsa: hem beklenmeyen (LAN'a açık) hem eksik (loopback yok).
        var listeners = new[] { new Listener(ListenerProtocol.Tcp, IPAddress.Any, 53000) };
        var expected = ExpectedListeners.Parse(["tcp/53000@loopback"]);

        var result = ExpectedListeners.Compare(listeners, expected);

        Assert.Equal([new UnexpectedListener(ListenerProtocol.Tcp, 53000, BindScope.Wildcard)], result.Unexpected);
        Assert.Single(result.Missing);
    }

    [Fact]
    public void Ranges_cover_ports_but_are_never_reported_missing()
    {
        var expected = ExpectedListeners.Parse(["tcp/32768-60999@loopback"]);
        var inRange = ExpectedListeners.Compare([new Listener(ListenerProtocol.Tcp, IPAddress.Loopback, 40000)], expected);
        var none = ExpectedListeners.Compare([], expected);

        Assert.Empty(inRange.Unexpected);
        Assert.Empty(none.Missing);
    }

    [Fact]
    public void Same_port_on_ipv4_and_ipv6_is_reported_once_per_scope()
    {
        var listeners = new[]
        {
            new Listener(ListenerProtocol.Tcp, IPAddress.Any, 8099),
            new Listener(ListenerProtocol.Tcp, IPAddress.IPv6Any, 8099),
        };

        Assert.Single(ExpectedListeners.Compare(listeners, []).Unexpected);
    }

    [Fact]
    public void Worker_reads_proc_files_and_updates_metrics()
    {
        WriteSamples(includeV6: false);
        using var capture = new MetricCapture();
        using var metrics = new PortAuditMetrics(capture.Factory);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        var worker = Worker(metrics, time);

        var result = worker.RunOnce();

        // IPv6 dosyaları yok (IPv6 kapalı makine): hata değil.
        Assert.NotNull(result);
        capture.Observe();
        Assert.Equal(2, capture.Last("port_audit_unexpected_listeners"));
        // IPv6 dosyası yokken tcp/8080 (yalnızca ::1'de vardı), udp/323 (yalnızca ::1'de) ve tcp/5432 eksik.
        Assert.Equal(3, capture.Last("port_audit_expected_missing"));
        Assert.Equal(1, capture.Last("port_audit_unexpected_listener", "protocol=tcp", "port=8099", "bind=wildcard"));
        Assert.Equal(1, capture.Last("port_audit_unexpected_listener", "protocol=udp", "port=68", "bind=wildcard"));
        Assert.Equal(1, capture.Last("port_audit_expected_listener_missing", "protocol=tcp", "port=5432"));
        Assert.Equal(4, capture.Last("port_audit_listeners", "protocol=tcp"));
        Assert.Equal(time.GetUtcNow().ToUnixTimeSeconds(), capture.Last("port_audit_last_run_timestamp_seconds"));
        Assert.Equal(1, capture.Last("port_audit_malformed_lines"));
        Assert.True(capture.Last("fraud_disk_available_bytes") > 0);
        Assert.True(capture.Last("fraud_disk_size_bytes") >= capture.Last("fraud_disk_available_bytes"));
    }

    [Fact]
    public void Unreadable_table_is_an_error_and_never_a_clean_result()
    {
        WriteSamples(includeV6: true);
        File.Delete(Path.Combine(_dir, "tcp"));
        using var capture = new MetricCapture();
        using var metrics = new PortAuditMetrics(capture.Factory);

        var result = Worker(metrics, new FakeTimeProvider()).RunOnce();

        Assert.Null(result);
        capture.Observe();
        Assert.Equal(1, capture.Last("port_audit_errors"));
        Assert.Null(capture.Last("port_audit_last_run_timestamp_seconds")); // PortAuditStale uyarısı çıkar
        Assert.Null(capture.Last("port_audit_unexpected_listeners"));
    }

    [Fact]
    public void Oversized_table_is_an_error_not_a_truncated_read()
    {
        WriteSamples(includeV6: false);
        File.WriteAllText(Path.Combine(_dir, "udp"), Udp + new string(' ', 10_000));
        using var capture = new MetricCapture();
        using var metrics = new PortAuditMetrics(capture.Factory);
        var options = Options();
        options.MaxFileBytes = 4096;

        var worker = new PortAuditWorker(new PortAuditor(options), options, metrics, new FakeTimeProvider(), NullLogger<PortAuditWorker>.Instance);

        Assert.Null(worker.RunOnce());
    }

    [Fact]
    public void Checked_in_expected_list_for_the_linux_deployment_parses()
    {
        var path = Path.Combine(RepoRoot(), "deploy", "linux", "port-audit", "expected");
        var entries = ExpectedListeners.Parse(File.ReadLines(path));

        // Grafana yalnızca loopback'te. port-audit host ağında hiçbir port dinlemez (M1): 9465 listede yok.
        Assert.Contains(entries, e => e is { Protocol: ListenerProtocol.Tcp, FromPort: 53000, Scope: ExpectedScope.Loopback });
        Assert.DoesNotContain(entries, e => e.Protocol == ListenerProtocol.Tcp && e.FromPort <= 9465 && e.ToPort >= 9465);
        Assert.DoesNotContain(entries, e => e.Scope == ExpectedScope.Any && e.IsRange);
    }

    [Fact]
    public void Auditor_writes_a_state_file_that_the_exporter_turns_into_the_same_metrics()
    {
        WriteSamples(includeV6: true);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        var options = Options();
        options.StateFile = Path.Combine(_dir, "state.json");

        using var auditorCapture = new MetricCapture();
        using var auditorMetrics = new PortAuditMetrics(auditorCapture.Factory);
        Assert.NotNull(new PortAuditWorker(new PortAuditor(options), options, auditorMetrics, time, NullLogger<PortAuditWorker>.Instance).RunOnce());

        // Dosyada adres yok: yalnızca protokol, port, kapsam ve sayılar.
        var json = File.ReadAllText(options.StateFile);
        Assert.DoesNotContain("127.0.0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("10.255", json, StringComparison.Ordinal);

        using var exporterCapture = new MetricCapture();
        using var exporterMetrics = new PortAuditMetrics(exporterCapture.Factory);
        var exporter = new PortAuditExporterWorker(options, exporterMetrics, time, NullLogger<PortAuditExporterWorker>.Instance);
        Assert.True(exporter.RefreshOnce());

        auditorCapture.Observe();
        exporterCapture.Observe();
        foreach (var name in new[]
                 {
                     "port_audit_unexpected_listeners", "port_audit_expected_missing", "port_audit_last_run_timestamp_seconds",
                     "port_audit_malformed_lines", "port_audit_expected_entries", "fraud_disk_available_bytes",
                 })
        {
            Assert.Equal(auditorCapture.Last(name), exporterCapture.Last(name));
        }

        Assert.Equal(1, exporterCapture.Last("port_audit_unexpected_listener", "protocol=tcp", "port=22", "bind=wildcard"));
        Assert.Equal(1, exporterCapture.Last("port_audit_expected_listener_missing", "protocol=tcp", "port=5432"));
    }

    [Fact]
    public void Exporter_rejects_or_clamps_a_hostile_state_file()
    {
        var path = Path.Combine(_dir, "state.json");
        var options = new PortAuditOptions { StateFile = path };
        using var capture = new MetricCapture();
        using var metrics = new PortAuditMetrics(capture.Factory);
        // Saat dosyadaki zamandan sonra olmalı; ileri tarihli CompletedAt reddedilir.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 12, 5, 0, TimeSpan.Zero));
        var exporter = new PortAuditExporterWorker(options, metrics, clock, NullLogger<PortAuditExporterWorker>.Instance);

        // Dosya yok: hiçbir değer yok (PortAuditStale çıkar), hata da değil.
        Assert.False(exporter.RefreshOnce());

        // Bozuk JSON ve sayısal enum: okunmaz, hata sayılır.
        File.WriteAllText(path, "{ \"completedAt\": ");
        Assert.False(exporter.RefreshOnce());
        Assert.Equal(1, capture.Sum("port_audit_state_read_errors"));
        File.WriteAllText(path, """{"completedAt":"2026-11-13T12:00:00Z","unexpected":[{"protocol":1,"port":22,"scope":3}]}""");
        Assert.False(exporter.RefreshOnce());
        Assert.Equal(2, capture.Sum("port_audit_state_read_errors"));

        // Çok büyük dosya: okunmaz.
        File.WriteAllText(path, new string(' ', PortAuditStateFile.MaxBytes + 1));
        Assert.False(exporter.RefreshOnce());

        // Sınır dışı değerler atılır veya sıfırlanır; tekrar eden bulgu bir kez sayılır.
        File.WriteAllText(path, """
            {"completedAt":"2026-11-13T12:00:00Z","tcpListeners":-5,"errors":-1,
             "unexpected":[{"protocol":"tcp","port":70000,"scope":"wildcard"},{"protocol":"tcp","port":8099,"scope":"loopback"},
                           {"protocol":"tcp","port":8099,"scope":"loopback"},{"protocol":"udp","port":53}],
             "missing":[{"protocol":"tcp","port":-1}]}
            """);
        Assert.True(exporter.RefreshOnce());
        capture.Observe();

        Assert.Equal(1, capture.Last("port_audit_unexpected_listeners"));
        Assert.Equal(1, capture.Last("port_audit_unexpected_listener", "protocol=tcp", "port=8099", "bind=loopback"));
        Assert.Equal(0, capture.Last("port_audit_expected_missing"));
        Assert.Equal(0, capture.Last("port_audit_listeners", "protocol=tcp"));
        Assert.Equal(3, capture.Sum("port_audit_state_read_errors")); // bozuk JSON, sayısal enum, büyük dosya
    }

    [Fact]
    public void Expected_list_is_reread_every_run_even_when_replaced_by_rename()
    {
        // git pull ve çoğu düzenleyici dosyayı yeni dosya yazıp yeniden adlandırarak değiştirir (yeni inode). Denetim her
        // turda dosyayı yolundan yeniden açar; Linux kurulumunda bu yüzden dosya değil klasör bağlanır.
        WriteSamples(includeV6: false);
        using var capture = new MetricCapture();
        using var metrics = new PortAuditMetrics(capture.Factory);
        var worker = Worker(metrics, new FakeTimeProvider());

        Assert.Contains(worker.RunOnce()!.Unexpected, u => u.Port == 8099);

        var replacement = Path.Combine(_dir, "expected.new");
        File.WriteAllText(replacement, Expected + "\ntcp/8099\nudp/68\n");
        File.Move(replacement, Path.Combine(_dir, "expected"), overwrite: true);

        Assert.Empty(worker.RunOnce()!.Unexpected);
    }

    [Fact]
    public void Exporter_rejects_a_completed_time_in_the_future()
    {
        // Kırpılsaydı ileri tarihli dosya her okumada "taze" görünür ve PortAuditStale hiç çıkmazdı.
        var path = Path.Combine(_dir, "state.json");
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        using var capture = new MetricCapture();
        using var metrics = new PortAuditMetrics(capture.Factory);
        var exporter = new PortAuditExporterWorker(
            new PortAuditOptions { StateFile = path }, metrics, new FakeTimeProvider(now), NullLogger<PortAuditExporterWorker>.Instance);

        File.WriteAllText(path, """{"completedAt":"2026-11-13T13:00:00Z"}""");
        Assert.False(exporter.RefreshOnce());

        File.WriteAllText(path, """{"completedAt":"2026-11-13T12:01:00Z"}"""); // saat farkı payı içinde
        Assert.True(exporter.RefreshOnce());
        capture.Observe();

        Assert.Equal(1, capture.Sum("port_audit_state_read_errors"));
        Assert.Equal(now.AddMinutes(1).ToUnixTimeSeconds(), capture.Last("port_audit_last_run_timestamp_seconds"));
    }

    private static List<Listener> Parse(string tcp, string tcp6, string udp, string udp6) =>
    [
        .. ProcNetParser.Parse(tcp, ListenerProtocol.Tcp, AddressFamily.InterNetwork, out _),
        .. ProcNetParser.Parse(tcp6, ListenerProtocol.Tcp, AddressFamily.InterNetworkV6, out _),
        .. ProcNetParser.Parse(udp, ListenerProtocol.Udp, AddressFamily.InterNetwork, out _),
        .. ProcNetParser.Parse(udp6, ListenerProtocol.Udp, AddressFamily.InterNetworkV6, out _),
    ];

    private PortAuditOptions Options() => new()
    {
        ProcNetDirectory = _dir,
        ExpectedFile = Path.Combine(_dir, "expected"),
        DiskPath = Path.GetPathRoot(_dir),
    };

    private PortAuditWorker Worker(PortAuditMetrics metrics, TimeProvider time)
    {
        var options = Options();
        return new PortAuditWorker(new PortAuditor(options), options, metrics, time, NullLogger<PortAuditWorker>.Instance);
    }

    private void WriteSamples(bool includeV6)
    {
        File.WriteAllText(Path.Combine(_dir, "tcp"), Tcp);
        File.WriteAllText(Path.Combine(_dir, "udp"), Udp);
        if (includeV6)
        {
            File.WriteAllText(Path.Combine(_dir, "tcp6"), Tcp6);
            File.WriteAllText(Path.Combine(_dir, "udp6"), Udp6);
        }

        File.WriteAllText(Path.Combine(_dir, "expected"), Expected);
    }

    /// <summary>Depo kökü: derleme çıktısı başka klasörde olabilir (--artifacts-path), bu yüzden kaynak dosyadan yürünür.</summary>
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FraudLab.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}
