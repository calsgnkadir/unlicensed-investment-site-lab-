using System.Text.RegularExpressions;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Configuration;

/// <summary>
/// Hafta 5 izleme kurulumu (Prometheus, Grafana, port denetimi): açığa çıkma, sertleştirme, dışarıya istek, sızıntı.
/// Yalnızca dosya içeriği okunur; Docker çalıştırılmaz.
/// </summary>
public sealed partial class MonitoringDeploymentTests
{
    private const string MonitoringCompose = "deploy/linux/docker-compose.monitoring.yml";

    private static string Read(string path) => RepoFiles.Read(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>İki boşlukla girintili servis bloğu (bir sonraki servis veya üst düzey anahtara kadar).</summary>
    private static string Block(string text, string service)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, l => l == $"  {service}:");
        Assert.True(start >= 0, service + " servisi bulunamadı");
        var end = Array.FindIndex(lines, start + 1, l => l.Length > 0 && !l.StartsWith("   ", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]
            .Where(l => !l.TrimStart().StartsWith('#'))
            .Select(l => l.Contains(" #", StringComparison.Ordinal) ? l[..l.IndexOf(" #", StringComparison.Ordinal)] : l));
    }

    private static IEnumerable<string> ServiceNames(string text)
    {
        var services = text[text.IndexOf("\nservices:\n", StringComparison.Ordinal)..];
        var stop = Regex.Match(services[1..], @"\n[a-z]").Index;
        return ServiceName().Matches(services[..(stop > 0 ? stop + 1 : services.Length)]).Select(m => m.Groups[1].Value);
    }

    [Theory]
    [InlineData(MonitoringCompose)]
    [InlineData("docker-compose.yml")]
    [InlineData("deploy/linux/docker-compose.ct.yml")]
    public void Every_published_port_is_bound_to_loopback(string path)
    {
        var text = Read(path);
        var lines = text.Split('\n');
        var seen = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "ports:")
            {
                continue;
            }

            for (var j = i + 1; j < lines.Length
                 && (lines[j].TrimStart().StartsWith("- ", StringComparison.Ordinal) || lines[j].TrimStart().StartsWith('#')); j++)
            {
                if (lines[j].TrimStart().StartsWith('#'))
                {
                    continue;
                }

                seen++;
                var entry = lines[j].TrimStart()[2..].Split('#')[0].Trim().Trim('"', '\'');
                Assert.StartsWith("127.0.0.1:", entry, StringComparison.Ordinal);
            }
        }

        Assert.True(path == "deploy/linux/docker-compose.ct.yml" ? seen == 0 : seen > 0, $"{path}: {seen} yayımlanan port");

        // Uzun sözdizimi (published/host_ip) kullanılmıyor; kullanılırsa bu test genişletilmeli.
        Assert.DoesNotContain("published:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("host_ip", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_grafana_publishes_a_port_and_only_port_audit_uses_the_host_network()
    {
        var text = Read(MonitoringCompose);

        foreach (var service in ServiceNames(text))
        {
            var block = Block(text, service);
            Assert.Equal(service == "grafana", block.Contains("\n    ports:", StringComparison.Ordinal));
            Assert.Equal(service == "port-audit", block.Contains("network_mode: host", StringComparison.Ordinal));
        }

        Assert.Single(Regex.Matches(text, @"(?m)^\s+network_mode:"));
        foreach (var path in new[] { "docker-compose.yml", "deploy/linux/docker-compose.ct.yml" })
        {
            Assert.DoesNotContain("network_mode", Read(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_monitoring_service_is_hardened_and_none_overrides_it()
    {
        var text = Read(MonitoringCompose);
        var anchor = text[text.IndexOf("x-hardening:", StringComparison.Ordinal)..text.IndexOf("\nservices:", StringComparison.Ordinal)];

        Assert.Contains("read_only: true", anchor, StringComparison.Ordinal);
        Assert.Matches(@"cap_drop:\s*\n\s*-\s*ALL", anchor);
        Assert.Contains("no-new-privileges:true", anchor, StringComparison.Ordinal);

        foreach (var service in ServiceNames(text))
        {
            var block = Block(text, service);
            Assert.Contains("<<: *hardening", block, StringComparison.Ordinal);
            Assert.DoesNotContain("read_only: false", block, StringComparison.Ordinal);
            Assert.DoesNotContain("cap_add", block, StringComparison.Ordinal);
            Assert.DoesNotContain("privileged", block, StringComparison.Ordinal);
            Assert.DoesNotContain("security_opt", block, StringComparison.Ordinal); // anchor'daki listeyi ezerdi
            Assert.DoesNotContain("pid:", block, StringComparison.Ordinal);
            Assert.DoesNotContain("docker.sock", block, StringComparison.Ordinal);
            Assert.Contains("mem_limit:", block, StringComparison.Ordinal);
            Assert.Contains("pids_limit:", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Port_audit_gets_no_secrets_no_host_mounts_and_hands_results_to_the_exporter_through_a_volume()
    {
        var text = Read(MonitoringCompose);
        var audit = Block(text, "port-audit");
        var exporter = Block(text, "port-audit-exporter");

        Assert.DoesNotContain("ConnectionStrings", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", audit, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"-\s*/(proc|sys|var|etc|run|home|root)\b[^\n]*:", audit); // host yolu bağlanmıyor
        Assert.Matches(@"\./port-audit\.expected:[^\n]*:ro", audit);
        Assert.Matches(@"-\s*port_audit_state:/var/lib/port-audit\s*(\n|$)", audit);

        // Exporter: host ağında değil, yalnızca iç metrik ağında; durum birimini salt okur; sır ve /proc yok.
        Assert.DoesNotContain("network_mode", exporter, StringComparison.Ordinal);
        Assert.Matches(@"networks:\s*\n\s*-\s*metrics\s*(\n|$)", exporter);
        Assert.Matches(@"-\s*port_audit_state:/var/lib/port-audit:ro", exporter);
        Assert.DoesNotContain("port-audit.expected", exporter, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", exporter, StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", exporter, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MonitoringCompose)]
    [InlineData("docker-compose.yml")]
    public void Grafana_has_no_anonymous_access_no_default_password_and_no_outbound_calls(string path)
    {
        var grafana = Block(Read(path), "grafana");

        foreach (var setting in new[]
                 {
                     "GF_AUTH_ANONYMOUS_ENABLED: \"false\"", "GF_USERS_ALLOW_SIGN_UP: \"false\"",
                     "GF_ANALYTICS_REPORTING_ENABLED: \"false\"", "GF_ANALYTICS_CHECK_FOR_UPDATES: \"false\"",
                     "GF_ANALYTICS_CHECK_FOR_PLUGIN_UPDATES: \"false\"", "GF_NEWS_NEWS_FEED_ENABLED: \"false\"",
                     "GF_SECURITY_DISABLE_GRAVATAR: \"true\"", "GF_SNAPSHOTS_EXTERNAL_ENABLED: \"false\"",
                     "GF_PLUGINS_PLUGIN_ADMIN_ENABLED: \"false\"", "GF_PLUGINS_PREINSTALL_DISABLED: \"true\"",
                 })
        {
            Assert.Contains(setting, grafana, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("GF_INSTALL_PLUGINS", grafana, StringComparison.Ordinal);
        Assert.DoesNotContain("GF_PLUGINS_PREINSTALL:", grafana, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"GF_SECURITY_ADMIN_PASSWORD(__FILE)?:\s*""?admin", grafana);

        if (path == MonitoringCompose)
        {
            // Linux: parola dosyadan (ortam değişkeninde değil); dosya salt okunur bağlanır; giriş betiği dosya yoksa
            // veya boşsa Grafana'yı açmaz (varsayılan admin/admin hiçbir zaman kullanılmaz).
            Assert.Contains("GF_SECURITY_ADMIN_PASSWORD__FILE: /run/secrets/grafana_admin_password", grafana, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"GF_SECURITY_ADMIN_PASSWORD:", grafana);
            Assert.Matches(@"-\s*\./secrets/grafana_admin_password:/run/secrets/grafana_admin_password:ro", grafana);
            Assert.Contains("[ -f \"$$GF_SECURITY_ADMIN_PASSWORD__FILE\" ] && [ -s \"$$GF_SECURITY_ADMIN_PASSWORD__FILE\" ] ||", grafana, StringComparison.Ordinal);
            Assert.Contains("exit 1; }; exec /run.sh", grafana, StringComparison.Ordinal);

            // Parola dosyası depoya girmez; klasör yalnızca sahibine açık.
            Assert.Contains("deploy/linux/secrets/", Read(".gitignore"), StringComparison.Ordinal);
            Assert.Matches(@"chmod 700 ""\$SECRETS_DIR""", Read("deploy/linux/install.sh"));
        }
        else
        {
            // Geliştirme: ortam değişkeni; boşsa giriş betiği çıkar.
            Assert.Contains("[ -n \"$$GF_SECURITY_ADMIN_PASSWORD\" ] ||", grafana, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(MonitoringCompose)]
    [InlineData("docker-compose.yml")]
    public void Prometheus_and_grafana_images_are_pinned_by_digest_and_prometheus_admin_api_is_off(string path)
    {
        var text = Read(path);

        foreach (Match m in Regex.Matches(text, @"image:\s*(?<img>(prom/prometheus|grafana/grafana)\S*)"))
        {
            Assert.Matches(@"^[a-z/]+:v?[0-9.]+@sha256:[0-9a-f]{64}$", m.Groups["img"].Value);
        }

        Assert.DoesNotContain("--web.enable-admin-api", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--web.enable-lifecycle", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--web.enable-remote-write-receiver", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Monitoring_services_in_the_dev_compose_start_only_with_their_profile()
    {
        var text = Read("docker-compose.yml");

        foreach (var service in new[] { "prometheus", "grafana" })
        {
            Assert.Contains("profiles: [\"monitoring\"]", Block(text, service), StringComparison.Ordinal);
            Assert.DoesNotContain("\n    ports:\n      - \"0.0.0.0", Block(text, service), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ports:", Block(text, "prometheus"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("deploy/linux/monitoring/prometheus/prometheus.yml")]
    [InlineData("deploy/dev/prometheus.yml")]
    public void Prometheus_scrapes_only_internal_targets_and_ships_nothing_out(string path)
    {
        var text = Read(path);
        var targets = Regex.Matches(text, @"targets:\s*\[([^\]]*)\]").SelectMany(m => m.Groups[1].Value.Split(','))
            .Select(t => t.Trim().Trim('"', '\'')).ToList();

        Assert.NotEmpty(targets);
        // Yalnızca compose servis adları ve Prometheus'un kendisi. Host adresi (ör. bir köprü ağ geçidi) hedef olamaz:
        // Hedef host'ta dinleyen bir port demektir (M1).
        Assert.All(targets, t => Assert.Matches(@"^((ct-follow|network|port-audit-exporter|api|collectors):9464|127\.0\.0\.1:9090)$", t));
        foreach (var forbidden in new[] { "remote_write", "remote_read", "alertmanagers", "basic_auth", "password", "file_sd", "http_sd", "dns_sd" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rules_dashboard_and_provisioning_carry_no_domain_ip_url_or_html()
    {
        var files = new[]
        {
            "deploy/linux/monitoring/prometheus/rules/fraud-lab.yml",
            "deploy/linux/monitoring/grafana/dashboards/fraud-lab.json",
        };

        foreach (var path in files)
        {
            var text = Read(path);
            Assert.DoesNotMatch(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", text);                    // IPv4
            Assert.DoesNotMatch(@"(?i)https?://", text);                                            // dış bağlantı
            Assert.DoesNotMatch(@"(?i)\b[a-z0-9-]+\.(com|net|org|tr|io|example)\b", text);           // alan adı biçimi
            Assert.DoesNotMatch(@"(?i)<\s*(script|iframe|img|a\s)", text);
            Assert.DoesNotContain("label_values(", text, StringComparison.Ordinal);                // serbest değer listesi yok
        }

        // Panel yalnızca sabit uid'li Prometheus veri kaynağını kullanır; harici panel bağlantısı yok.
        var dashboard = Read("deploy/linux/monitoring/grafana/dashboards/fraud-lab.json");
        Assert.DoesNotContain("\"type\": \"link\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("\"type\": \"text\",\n      \"options\": {\n        \"mode\": \"html\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"""mode"":\s*""html""", dashboard);
    }

    [Theory]
    [InlineData("deploy/linux/install.sh")]
    [InlineData("deploy/linux/status.sh")]
    [InlineData("deploy/linux/backup.sh")]
    public void Scripts_never_print_or_log_the_grafana_or_database_password(string path)
    {
        var lines = RepoFiles.ReadLines(path).Where(l => !l.TrimStart().StartsWith('#')).ToList();

        Assert.DoesNotContain(lines, l => PrintsSecret().IsMatch(l));
        Assert.DoesNotContain(lines, l => l.Contains("set -x", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => Regex.IsMatch(l, @"\bcat\s+[""']?\$?\{?ENV_FILE|cat\s+\.env\b"));
    }

    [Fact]
    public void Ci_promtool_runs_offline_from_the_pinned_image()
    {
        var ci = Read(".github/workflows/ci.yml").Replace("\\\n", " ", StringComparison.Ordinal);
        var runs = Regex.Matches(ci, @"docker run [^\n]*").Select(m => m.Value).Where(r => r.Contains("promtool", StringComparison.Ordinal)
            || r.Contains("PROMETHEUS_IMAGE", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(runs);
        Assert.All(runs, r => Assert.Contains("--network none", r, StringComparison.Ordinal));
        Assert.Matches(@"PROMETHEUS_IMAGE:\s*prom/prometheus:v[0-9.]+@sha256:[0-9a-f]{64}", ci);
    }

    // ---- Hafta 5 bulguları (M1-M4 kapatıldı; testler gerilemeyi yakalar) ----

    [Fact]
    public void No_metrics_port_listens_on_the_host_and_port_audit_opens_no_socket()
    {
        // M1: port-audit host ağında; orada açılan her dinleyici host'un (ve zayıf host modeliyle başka köprülerin /
        // LAN'ın) erişebileceği bir porttur. Bu yüzden host ağında hiçbir metrik portu olmamalı.
        var compose = Read(MonitoringCompose);
        var audit = Block(compose, "port-audit");

        // 1) port-audit'te metrik ayarı yok; uygulama varsayılanı kapalı (Metrics:Enabled false).
        Assert.DoesNotContain("Metrics__", audit, StringComparison.Ordinal);
        Assert.Matches(@"""Metrics"":\s*\{\s*""Enabled"":\s*false", Read("src/Fraud.Collectors/appsettings.json"));

        // 2) Eski köprü ve 9465 hiçbir yerde yok; beklenen port listesi host'ta 9464/9465 beklemiyor.
        foreach (var path in new[] { MonitoringCompose, "deploy/linux/docker-compose.ct.yml", "deploy/linux/monitoring/prometheus/prometheus.yml" })
        {
            var text = Read(path);
            Assert.DoesNotContain("9465", text, StringComparison.Ordinal);
            Assert.DoesNotContain("hostmetrics", text, StringComparison.Ordinal);
            Assert.DoesNotContain("10.213.199", text, StringComparison.Ordinal);
        }

        var expected = RepoFiles.ReadLines("deploy/linux/port-audit.expected").Select(l => l.Split('#')[0]).ToList();
        Assert.DoesNotContain(expected, l => Regex.IsMatch(l, @"\b946[45]\b"));

        // 3) Host ağındaki tek servis port-audit ve hiçbir servis host'ta port yayımlamıyor (Grafana yalnızca 127.0.0.1).
        Assert.Single(Regex.Matches(compose, @"(?m)^\s+network_mode:"));

        // 4) Denetim kodu soket açmaz: PortAudit klasöründe ağ API'si yok (dinleyici, istemci, ad çözümü).
        var dir = Path.Combine(RepoFiles.Root, "src", "Fraud.Collectors", "PortAudit");
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var code = File.ReadAllText(file);
            Assert.DoesNotMatch(@"\b(TcpListener|HttpListener|UdpClient|TcpClient|HttpClient)\b|new\s+Socket\b|\bDns\.Get|\bConnectAsync\b|\.Bind\(", code);
            Assert.DoesNotContain("AddFraudMetrics", code, StringComparison.Ordinal);
        }

        // 5) Program'daki port-audit yolu metrikleri yalnızca ayar açıksa kurar (AddFraudMetrics kapalıyken hiçbir şey
        //    eklemez); exporter dalı ayrı komuttur.
        var program = Read("src/Fraud.Collectors/Program.cs");
        Assert.Contains("command is \"port-audit\" or \"port-audit-exporter\"", program, StringComparison.Ordinal);
        Assert.Matches(@"if \(!options\.Enabled\)\s*\{\s*return services;", Read("src/Fraud.Infrastructure/Observability/MetricsSetup.cs"));
    }

    [Fact]
    public void Published_docker_ports_are_audited_independently_of_proc_net()
    {
        // Bulgu M2: Yanlış negatif. "userland-proxy": false (veya bazı nftables kurulumları) ile yayımlanan port yalnızca
        // DNAT kuralıdır, dinleyen soket yoktur; 0.0.0.0:5432 gibi yanlışlıkla yayımlanan bir port UnexpectedOpenPort
        // üretmez. status.sh host'ta docker'a erişebildiği için yayımlanan portları ayrıca listeleyip 127.0.0.1 dışını
        // işaretleyebilir (veya install.sh userland-proxy'nin açık olduğunu doğrulayabilir).
        var status = Read("deploy/linux/status.sh");
        var install = Read("deploy/linux/install.sh");

        Assert.True(
            Regex.IsMatch(status, @"docker[^\n]*(\{\{\s*\.Ports\s*\}\}|Publishers|docker port)")
            || install.Contains("userland-proxy", StringComparison.Ordinal),
            "Yayımlanan portlar /proc/net'ten bağımsız denetlenmiyor");
    }

    [Fact]
    public void Alerting_has_a_watchdog_that_something_outside_prometheus_checks()
    {
        // Bulgu M3: Alertmanager yok; uyarılar yalnızca biri Grafana'yı veya status.sh'ı açarsa görülür. Prometheus
        // çökerse kurallar hiç değerlendirilmez, panel "veri yok" der. Her zaman ateşlenen bir Watchdog kuralı ve onu
        // Prometheus dışından denetleyen bir şey (host'ta systemd zamanlayıcısı ile status.sh --check, ya da yalnızca
        // "yaşıyorum" sinyali alan bir ölü adam servisi) gerekir.
        var rules = Read("deploy/linux/monitoring/prometheus/rules/fraud-lab.yml");

        Assert.Matches(@"alert:\s*Watchdog\s*\n\s*expr:\s*vector\(1\)", rules);
        Assert.Contains("--check", Read("deploy/linux/status.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void Data_freshness_has_an_alert_rule()
    {
        // M4: Toplama "başarılı" görünse de veri gelmeyebilir. Tazelik göstergesi uyarıda da kullanılmalı.
        var rules = Read("deploy/linux/monitoring/prometheus/rules/fraud-lab.yml");

        Assert.Contains("fraud_data_newest_row_age_seconds", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void Ct_stall_rule_sees_a_log_that_never_succeeded_after_restart()
    {
        // M4(c): İki log açıkken, hiç başarılı tur bitirmeyen logun son başarı serisi yoktur; başlangıç serisi ile yakalanır.
        var rules = Read("deploy/linux/monitoring/prometheus/rules/fraud-lab.yml");

        Assert.Matches(@"fraud_ct_follower_started_timestamp_seconds[^\n]*\)\s*>\s*900\)\s*\n\s*unless on \(log\)", rules);
    }

    [Fact]
    public void Check_mode_prints_only_alert_names_ports_and_service_names()
    {
        // --check çıktısı Windows bildirimine gider. Adres (bind_addr), alan adı veya Prometheus etiket değeri basmamalı.
        var status = Read("deploy/linux/status.sh");
        var published = status[status.IndexOf("unexpected_published_ports() {", StringComparison.Ordinal)..];
        published = published[..published.IndexOf("\n}\n", StringComparison.Ordinal)];

        Assert.Contains("printf '%s/%s(%s)\\n' \"$bind_port\" \"$proto\" \"$scope\"", published, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"printf[^\n]*\$bind_addr", published);
        Assert.DoesNotMatch(@"echo[^\n]*\$(entry|bind_part|bind_addr)", published);

        // Uyarı sorgusu yalnızca alertname'e göre gruplar; diğer etiketler (log, port) özetlenmez, alan adı hiç yoktur.
        Assert.Contains("count by (alertname) (ALERTS{alertstate=\"firing\"})", status, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_check_script_has_no_injection_or_xml_escape_holes()
    {
        var script = Read("deploy/windows/fraud-lab-check.ps1");

        // Dağıtım adı katı kalıpla; komut sabit; çağrı doğrudan wsl.exe (kabuk/Invoke-Expression yok).
        Assert.Contains("[ValidatePattern('^[A-Za-z0-9._-]{1,64}$')]", script, StringComparison.Ordinal);
        Assert.Matches(@"\$CheckCommand = '~/fraud-lab/deploy/linux/status\.sh --check'", script);
        Assert.DoesNotMatch(@"(?i)Invoke-Expression|\biex\b|Start-Process|cmd(\.exe)? /c|-EncodedCommand", script);
        Assert.Contains("Join-Path $env:SystemRoot 'System32\\wsl.exe'", script, StringComparison.Ordinal);
        Assert.Contains("$psi.UseShellExecute = $false", script, StringComparison.Ordinal);

        // Toast XML'ine giren her metin kontrol karakterlerinden arınır, kısaltılır ve XML kaçışından geçer.
        Assert.Contains("[System.Security.SecurityElement]::Escape($clean)", script, StringComparison.Ordinal);
        Assert.Matches(@"'<text>' \+ \(Get-CleanText \$Title \d+\) \+ '</text>'", script);
        Assert.Matches(@"'<text>' \+ \(Get-CleanText \$Body \d+\) \+ '</text>'", script);
        Assert.Single(Regex.Matches(script, @"\$xml\.LoadXml\("));

        // Zamanlanmış görev yönetici yetkisiyle kurulmaz.
        Assert.DoesNotMatch(@"(?i)/RL\s+HIGHEST|-RunLevel\s+Highest", Read("deploy/linux/README.md"));
    }

    // ---- Açık bulgular (yeniden denetimde) ----

    [Fact]
    public void Data_stale_rule_fires_when_the_freshness_series_disappears()
    {
        // Bulgu M6: DataFreshnessMonitor, tablo boşsa (sorgu NULL döner) göstergeyi kaldırır; ct-follow içindeki izleyici
        // durursa da seri kaybolur. "fraud_data_newest_row_age_seconds{table=...} > eşik" boş döner ve DataStale
        // hiç ateşlenmez. Ör. dns_snapshots yanlışlıkla boşaltılırsa veya boş bir yedek geri yüklenirse sessiz kalır.
        var rules = Read("deploy/linux/monitoring/prometheus/rules/fraud-lab.yml");
        var dataStale = rules[rules.IndexOf("alert: DataStale", StringComparison.Ordinal)..];
        dataStale = dataStale[..dataStale.IndexOf("annotations:", StringComparison.Ordinal)];

        Assert.Contains("absent", dataStale, StringComparison.Ordinal);
    }

    [Fact]
    public void Scheduled_check_runs_a_script_other_local_users_cannot_modify()
    {
        // Bulgu M5: C:\ altında açılan klasörler varsayılan olarak "Authenticated Users: Modify" devralır (icacls ile
        // doğrulandı: C:\dev\fraud-lab\deploy\windows\fraud-lab-check.ps1 -> NT AUTHORITY\Authenticated Users:(I)(M)).
        // README'deki görev bu dosyayı -ExecutionPolicy Bypass ile 15 dakikada bir Kadircan'ın hesabıyla çalıştırır.
        // Makinedeki başka bir yerel hesap betiği değiştirip o kullanıcı adına kod çalıştırabilir. Ayrıca depoya giren her
        // değişiklik (ör. git pull) gözden geçirilmeden zamanlanmış olarak çalışır.
        // Düzeltme: install-check-task.ps1 betiği kullanıcıya özel bir kopyadan çalıştırır.
        var readme = Read("deploy/linux/README.md");
        foreach (Match task in Regex.Matches(readme, @"schtasks /Create[^\n]*"))
        {
            Assert.Matches(@"(?i)-File %LOCALAPPDATA%\\fraud-lab\\fraud-lab-check\.ps1", task.Value);
            Assert.DoesNotMatch(@"(?i)/RL\s+HIGHEST", task.Value);
        }

        Assert.DoesNotMatch(@"(?i)-File\s+C:\\dev\\fraud-lab\\deploy\\windows\\fraud-lab-check\.ps1[^\n]*(schtasks|Register-ScheduledTask)", readme);

        var installer = Read("deploy/windows/install-check-task.ps1");

        // Hedef kullanıcının profilinde; kaynak yalnızca betiğin yanındaki dosya.
        Assert.Contains("$TargetDir = Join-Path $env:LOCALAPPDATA 'fraud-lab'", installer, StringComparison.Ordinal);
        Assert.Contains("$Source = Join-Path $PSScriptRoot 'fraud-lab-check.ps1'", installer, StringComparison.Ordinal);

        // İzinler: miras kaldırılır, yalnızca kullanıcı + SYSTEM + Administrators (SID ile), sonra geri okunup doğrulanır.
        Assert.Contains("/inheritance:r /grant:r", installer, StringComparison.Ordinal);
        Assert.Contains("$SystemSid = 'S-1-5-18'", installer, StringComparison.Ordinal);
        Assert.Contains("$AdministratorsSid = 'S-1-5-32-544'", installer, StringComparison.Ordinal);
        Assert.Contains("AreAccessRulesProtected", installer, StringComparison.Ordinal);
        Assert.Contains("$AllowedSids -notcontains $sid", installer, StringComparison.Ordinal);

        // Sıra (TOCTOU yok): klasör önce özel yapılır ve doğrulanır, kopya ondan SONRA ve içine yazılır; sonra kopya
        // da daraltılıp doğrulanır ve özeti kaynakla karşılaştırılır.
        int At(string text) => installer.IndexOf(text, StringComparison.Ordinal);
        Assert.True(At("Set-PrivateAcl $TargetDir $true") < At("Assert-PrivateAcl $TargetDir"));
        Assert.True(At("Assert-PrivateAcl $TargetDir") < At("Copy-Item -LiteralPath $Source -Destination $Target"));
        Assert.True(At("Copy-Item -LiteralPath $Source -Destination $Target") < At("Assert-PrivateAcl $Target\n"));
        Assert.True(At("Assert-PrivateAcl $Target\n") < At("Register-ScheduledTask"));
        Assert.Contains("if ($sourceHash -ne $targetHash)", installer, StringComparison.Ordinal);

        // Görev: kopyadan, tırnaklı yol, sınırlı yetki, sabit powershell.exe yolu.
        Assert.Contains("-File \"' + $Target + '\"'", installer, StringComparison.Ordinal);
        Assert.Contains("-RunLevel Limited", installer, StringComparison.Ordinal);
        Assert.Contains("Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe'", installer, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)Invoke-Expression|\biex\b|-RunLevel\s+Highest", installer);
    }

    [GeneratedRegex(@"(?m)^  ([a-z][a-z0-9-]*):\s*$")]
    private static partial Regex ServiceName();

    [GeneratedRegex(@"(echo|printf|info|warn|log)[^\n]*\$\{?(GRAFANA_ADMIN_PASSWORD|POSTGRES_PASSWORD|password|grafana_password)\b")]
    private static partial Regex PrintsSecret();
}
