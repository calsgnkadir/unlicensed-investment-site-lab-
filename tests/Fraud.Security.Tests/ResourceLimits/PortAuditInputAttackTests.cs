using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Fraud.Collectors.PortAudit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fraud.Security.Tests.ResourceLimits;

/// <summary>
/// Port denetiminin girdileri: <c>/proc/net</c> tabloları ve beklenen liste dosyası. Bozuk, kesik, çok büyük veya
/// garip yazımlı girdi istisnayla süreci durdurmamalı, "her şey yolunda" da dememeli. Gerçek /proc okunmaz.
/// </summary>
public sealed class PortAuditInputAttackTests
{
    private const string Header = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode";

    private static string Line(string local, string remote, string state) =>
        $"   0: {local} {remote} {state} 00000000:00000000 00:00000000 00000000     0        0 12345 1 0000000000000000 100 0 0 10 0";

    [Fact]
    public void Random_and_mutated_tables_never_throw_and_count_bad_lines()
    {
        var random = new Random(5_2026_1113);
        var good = Header + "\n" + Line("0100007F:0035", "00000000:0000", "0A") + "\n" + Line("00000000:1F90", "00000000:0000", "0A") + "\n";
        var stopwatch = Stopwatch.StartNew();
        var totalMalformed = 0;

        for (var i = 0; i < 20_000; i++)
        {
            var chars = good.ToCharArray();
            for (var m = random.Next(1, 6); m > 0; m--)
            {
                chars[random.Next(chars.Length)] = (char)random.Next(0, 128);
            }

            foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
            {
                foreach (var protocol in new[] { ListenerProtocol.Tcp, ListenerProtocol.Udp })
                {
                    var listeners = ProcNetParser.Parse(new string(chars), protocol, family, out var bad);
                    totalMalformed += bad;
                    Assert.All(listeners, l => Assert.InRange(l.Port, 0, 65535));
                }
            }
        }

        Assert.True(totalMalformed > 0);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Süre {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("0100007F:0035:00")]   // fazladan alan
    [InlineData("0100007F:035")]       // kısa port
    [InlineData("100007F:0035")]       // kısa adres
    [InlineData("0100007G:0035")]      // onaltılık değil
    [InlineData("+100007F:0035")]
    [InlineData("0100007F:-035")]
    [InlineData("0100007F 0035")]
    [InlineData("")]
    public void Malformed_endpoints_are_rejected_not_guessed(string field)
    {
        Assert.False(ProcNetParser.TryParseEndpoint(field, AddressFamily.InterNetwork, out _, out _));
    }

    [Fact]
    public void Byte_order_and_scope_are_read_correctly()
    {
        var v4 = Header + "\n" + Line("0100007F:D2F0", "00000000:0000", "0A") + "\n" + Line("00000000:0016", "00000000:0000", "0A") + "\n"
                 + Line("01C7D50A:2501", "00000000:0000", "0A") + "\n" + Line("0100007F:1F90", "0100007F:9C40", "01") + "\n";
        var listeners = ProcNetParser.Parse(v4, ListenerProtocol.Tcp, AddressFamily.InterNetwork, out var bad);

        Assert.Equal(0, bad);
        Assert.Equal(
            [("127.0.0.1", 54000, BindScope.Loopback), ("0.0.0.0", 22, BindScope.Wildcard), ("10.213.199.1", 9473, BindScope.Specific)],
            listeners.Select(l => (l.Address.ToString(), l.Port, l.Scope)));

        // ::ffff:127.0.0.1 dinleyicisi IPv4 loopback gibi; :: joker.
        var v6 = Header + "\n" + Line("0000000000000000FFFF00000100007F:0035", "00000000000000000000000000000000:0000", "0A") + "\n"
                 + Line("00000000000000000000000000000000:0050", "00000000000000000000000000000000:0000", "0A") + "\n";
        var six = ProcNetParser.Parse(v6, ListenerProtocol.Tcp, AddressFamily.InterNetworkV6, out _);
        Assert.Equal([BindScope.Loopback, BindScope.Wildcard], six.Select(l => l.Scope));
        Assert.Equal(IPAddress.Parse("::ffff:127.0.0.1"), six[0].Address);
    }

    [Fact]
    public void Wildcard_listener_on_a_loopback_only_port_is_unexpected()
    {
        // Grafana yanlışlıkla 0.0.0.0:53000'de açılırsa "beklenen port" sayılmamalı.
        var expected = ExpectedListeners.Parse(["tcp/53000@loopback", "tcp/9465@local"]);
        var actual = new[]
        {
            new Listener(ListenerProtocol.Tcp, IPAddress.Any, 53000),
            new Listener(ListenerProtocol.Tcp, IPAddress.IPv6Any, 9465),
            new Listener(ListenerProtocol.Udp, IPAddress.Loopback, 53000), // protokol farklı
        };

        var result = ExpectedListeners.Compare(actual, expected);

        Assert.Equal(3, result.Unexpected.Count);
        Assert.Equal(2, result.Missing.Count);
    }

    [Theory]
    [InlineData("tcp/0")]
    [InlineData("tcp/65536")]
    [InlineData("tcp/10-5")]
    [InlineData("tcp/1-65536")]
    [InlineData("tcp/22@lan")]
    [InlineData("sctp/22")]
    [InlineData("tcp/２２")]          // tam genişlikte rakam
    [InlineData("tcp/22@loopback@local")]
    [InlineData("tcp/-1")]
    [InlineData("tcp/22​")]
    public void Expected_list_rejects_odd_entries(string entry)
    {
        Assert.Null(ExpectedListeners.ParseEntry(entry));
        Assert.Throws<FormatException>(() => ExpectedListeners.Parse([entry]));
    }

    [Fact]
    public void Oversized_or_unreadable_table_fails_the_audit_without_reporting_clean()
    {
        var dir = Directory.CreateTempSubdirectory("port-audit-");
        try
        {
            var expectedFile = Path.Combine(dir.FullName, "expected");
            File.WriteAllText(expectedFile, "tcp/22\n");
            var big = new StringBuilder(Header).Append('\n');
            while (big.Length < 10_000)
            {
                big.Append(Line("00000000:0016", "00000000:0000", "0A")).Append('\n');
            }

            File.WriteAllText(Path.Combine(dir.FullName, "tcp"), big.ToString());
            File.WriteAllText(Path.Combine(dir.FullName, "udp"), Header + "\n");
            var options = new PortAuditOptions { ProcNetDirectory = dir.FullName, ExpectedFile = expectedFile, MaxFileBytes = 4096, DiskPath = null };
            using var metrics = new PortAuditMetrics();
            var worker = new PortAuditWorker(new PortAuditor(options), options, metrics, TimeProvider.System, NullLogger<PortAuditWorker>.Instance);

            // Boyut sınırı aşıldı: sonuç yok (null), istisna dışarı çıkmaz; son başarı zamanı güncellenmez.
            Assert.Null(worker.RunOnce());

            // Tablo dosyası yoksa (zorunlu tcp) da aynı.
            File.Delete(Path.Combine(dir.FullName, "tcp"));
            Assert.Null(worker.RunOnce());

            // Beklenen liste bozuksa da.
            File.WriteAllText(Path.Combine(dir.FullName, "tcp"), Header + "\n");
            File.WriteAllText(expectedFile, "tcp/22 udp/abc\n");
            Assert.Null(worker.RunOnce());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
