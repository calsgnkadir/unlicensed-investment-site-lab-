using System.Diagnostics;
using System.Text.RegularExpressions;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Configuration;

/// <summary>
/// Yapılandırma denetimi: compose portları, konteyner yetkileri, parola yönetimi, CI yetkileri,
/// derleme bağlamı ve gizli verinin depoya girmemesi. Dosyalar metin olarak okunur; hiçbir konteyner çalıştırılmaz.
/// </summary>
public sealed partial class DeploymentConfigTests
{
    [Fact]
    public void Every_published_compose_port_is_bound_to_loopback()
    {
        var lines = RepoFiles.ReadLines("docker-compose.yml");
        var published = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "ports:")
            {
                continue;
            }

            var indent = lines[i].Length - lines[i].TrimStart().Length;
            for (var j = i + 1; j < lines.Length && (lines[j].Length - lines[j].TrimStart().Length > indent || lines[j].Trim().Length == 0); j++)
            {
                var entry = lines[j].Trim();
                if (entry.StartsWith('-'))
                {
                    published.Add(entry.TrimStart('-', ' ').Trim('"', '\''));
                }
            }
        }

        Assert.NotEmpty(published);
        Assert.All(published, p => Assert.StartsWith("127.0.0.1:", p, StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_has_no_privileged_or_host_level_escape_hatches()
    {
        var compose = RepoFiles.Read("docker-compose.yml");

        Assert.DoesNotContain("privileged: true", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("network_mode: host", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("pid: host", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("docker.sock", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("cap_add", compose, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("collectors")]
    public void Application_containers_are_hardened(string service)
    {
        var block = ServiceBlock(service);

        Assert.Contains("read_only: true", block, StringComparison.Ordinal);
        Assert.Matches(@"cap_drop:\s*\n\s*-\s*ALL", block);
        Assert.Contains("no-new-privileges:true", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Collectors_do_not_start_by_default()
    {
        // Dış kaynaklara istek atan servis yalnızca açıkça istenince çalışır.
        Assert.Contains("profiles: [\"collectors\"]", ServiceBlock("collectors"), StringComparison.Ordinal);
    }

    [Fact]
    public void Database_password_is_required_from_env_and_never_hard_coded()
    {
        var compose = RepoFiles.Read("docker-compose.yml");

        Assert.Contains("${POSTGRES_PASSWORD:?", compose, StringComparison.Ordinal);
        foreach (Match m in PasswordValue().Matches(compose))
        {
            Assert.StartsWith("${", m.Groups["v"].Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("src/Fraud.Api/appsettings.json")]
    [InlineData("src/Fraud.Collectors/appsettings.json")]
    [InlineData("src/Fraud.Api/Properties/launchSettings.json")]
    public void Checked_in_settings_contain_no_credentials(string path)
    {
        var text = RepoFiles.Read(path);

        Assert.DoesNotMatch("(?i)password\\s*=", text);
        Assert.DoesNotMatch("(?i)\"ConnectionStrings\"", text);
    }

    [Fact]
    public void Api_host_filtering_is_restricted_to_loopback_names()
    {
        Assert.Contains("\"AllowedHosts\": \"localhost;127.0.0.1\"", RepoFiles.Read("src/Fraud.Api/appsettings.json"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/Fraud.Api/Dockerfile")]
    [InlineData("src/Fraud.Collectors/Dockerfile")]
    public void Dockerfiles_run_as_non_root_with_pinned_base_images_and_no_secrets(string path)
    {
        var text = RepoFiles.Read(path);

        Assert.Contains("USER $APP_UID", text, StringComparison.Ordinal);
        Assert.DoesNotMatch("(?im)^\\s*(ENV|ARG)\\s+\\S*(PASSWORD|SECRET|TOKEN|KEY)", text);
        Assert.DoesNotMatch("(?im)^FROM\\s+\\S+:latest", text);
        Assert.DoesNotMatch("(?im)^COPY\\s+\\.\\s", text); // bütün bağlamı kopyalamaz
        Assert.DoesNotContain("data/", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Docker_build_context_is_an_allow_list_that_excludes_data_and_env()
    {
        var entries = RepoFiles.ReadLines(".dockerignore")
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        Assert.Equal("*", entries[0]);
        Assert.DoesNotContain(entries, e => e.StartsWith("!data", StringComparison.Ordinal) || e.StartsWith("!.env", StringComparison.Ordinal));
    }

    [Fact]
    public void Git_ignores_private_data_and_env_files()
    {
        var entries = RepoFiles.ReadLines(".gitignore").Select(l => l.Trim()).ToList();

        Assert.Contains("data/private/", entries);
        Assert.Contains(".env", entries);
    }

    [Fact]
    public void Ci_has_read_only_token_and_actions_pinned_to_commits()
    {
        var ci = RepoFiles.Read(".github/workflows/ci.yml");

        Assert.Matches(@"permissions:\s*\n\s*contents:\s*read", ci);
        Assert.DoesNotContain("pull_request_target", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("write-all", ci, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", ci, StringComparison.Ordinal);

        var uses = UsesLine().Matches(ci).Select(m => m.Groups["ref"].Value).ToList();
        Assert.NotEmpty(uses);
        Assert.All(uses, r => Assert.Matches("^[0-9a-f]{40}$", r));
    }

    [Fact]
    public void Ci_does_not_interpolate_untrusted_event_fields_into_shell()
    {
        // ${{ github.event.pull_request.title }} gibi alanlar run: içinde komut enjeksiyonuna yol açar.
        var ci = RepoFiles.Read(".github/workflows/ci.yml");

        Assert.DoesNotMatch(@"\$\{\{\s*github\.event\.", ci);
        Assert.DoesNotMatch(@"\$\{\{\s*github\.head_ref", ci);
    }

    [Fact]
    public void Private_data_is_not_tracked_by_git()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo("git", "ls-files -- data/private .env")
            {
                WorkingDirectory = RepoFiles.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var git = Process.Start(psi)!;
            output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0)
            {
                Assert.Skip("git ls-files çalışmadı; depo dışında çalışılıyor olabilir.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Skip("git bulunamadı.");
            return;
        }

        Assert.Equal(string.Empty, output.Trim());
    }

    private static string ServiceBlock(string service)
    {
        var compose = RepoFiles.Read("docker-compose.yml").Replace("\r\n", "\n", StringComparison.Ordinal);
        var match = Regex.Match(compose, $@"(?m)^  {Regex.Escape(service)}:\n(?<body>(?:^(?:    .*|\s*)\n)+)");
        Assert.True(match.Success, service + " servisi bulunamadı");
        return match.Groups["body"].Value;
    }

    [GeneratedRegex(@"(?i)(POSTGRES_PASSWORD:|Password=)\s*(?<v>[^;""\s]+)")]
    private static partial Regex PasswordValue();

    [GeneratedRegex(@"uses:\s*[\w./-]+@(?<ref>\S+)")]
    private static partial Regex UsesLine();
}
