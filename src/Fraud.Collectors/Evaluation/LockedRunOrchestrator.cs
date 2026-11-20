using System.Diagnostics;
using System.Security.Cryptography;
using Fraud.Evaluation;

namespace Fraud.Collectors.Evaluation;

/// <summary>
/// Kilitli testi HEAD'den açılmış temiz, geçici bir worktree'de derleyip orada çalıştırır (E11).
/// </summary>
/// <remarks>
/// <para>
/// Neden: "Temiz ağaç + ikilinin commit'i" çalışan kodu kanıtlamıyordu. (a) Git'in yok saydığı bir klasördeki .cs
/// dosyası (ör. src/Fraud.Evaluation/out/Ek.cs) derlenir ama <c>git status</c>'ta görünmez. (b) İkilideki "+sha"
/// derleme sırasında yazılır; değiştirilmiş bir ağaçtan <c>-p:SourceRevisionId=&lt;HEAD&gt;</c> ile derlenen ikili HEAD'i
/// söyler. Temiz bir worktree'de yalnızca HEAD'deki izlenen dosyalar vardır; derleme orada yapılınca ikili HEAD'in ta
/// kendisidir.
/// </para>
/// <para>
/// Neden ayrı bir komut değil de <c>eval-run --mode Locked</c>: Kullanıcının hatırlaması gereken tek komut kalsın ve
/// "kirli ağaçtan kilitli test" yolu hiç olmasın. Komut ana ağaçtan çağrılınca bu sınıf devreye girer; geçici
/// worktree'deki derlenmiş program aynı komutu "iç" kipte çalıştırır ve kilidi o alır.
/// </para>
/// <para>Adımlar:</para>
/// <list type="number">
/// <item>Ana ağaçta: commit = HEAD, ağaç temiz, src/ ve tests/ altında yok sayılan dosya yok, yapılandırma izleniyor.</item>
/// <item><c>git -c core.fsmonitor=false worktree add --detach &lt;geçici&gt; HEAD</c>.</item>
/// <item>Geçici klasörde <c>dotnet build src/Fraud.Collectors -c Release</c>.</item>
/// <item>Derlenen programla aynı komut, geçici klasörde. İç kip kilidi alır, veriyi yükler, raporu basar.</item>
/// <item>Ne olursa olsun geçici worktree silinir.</item>
/// </list>
/// </remarks>
public static class LockedRunOrchestrator
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromHours(6);

    /// <param name="mode">Kilitli ya da ileriye dönük kip (E13: ileriye dönük kip de yalnızca temiz derlemeyle çalışır).</param>
    public static async Task<int> RunAsync(
        RepoContext repo, EvaluationMode mode, string configPath, IReadOnlyDictionary<string, string?> args, TextWriter output,
        CancellationToken cancellationToken)
    {
        if (mode is not (EvaluationMode.Locked or EvaluationMode.Prospective))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Yalnızca kilitli ve ileriye dönük kip temiz worktree'de çalışır.");
        }

        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        var commit = args.TryGetValue("commit", out var c) ? c : null;
        if (await EvaluationCommands.CheckSourceStateAsync(repo, configPath, commit, output).ConfigureAwait(false) is null)
        {
            return 64;
        }

        var worktree = Path.Combine(Path.GetTempPath(), "fraud-lab-locked-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)));
        if (!repo.AddDetachedWorktree(worktree))
        {
            await output.WriteLineAsync("Geçici worktree açılamadı; " + mode + " çalışmadı.").ConfigureAwait(false);
            return 2;
        }

        try
        {
            var dotnet = GitExecutable.ResolveTool("dotnet", Environment.GetEnvironmentVariable("Dotnet__Path"));
            await output.WriteLineAsync("Temiz worktree'de derleniyor (" + worktree + ").").ConfigureAwait(false);
            var build = new ProcessStartInfo(dotnet) { WorkingDirectory = worktree };
            // -nodeReuse:false ve --disable-build-servers: Derleme sunucusu süreçleri geçici klasörü açık tutmasın; yoksa
            // Windows'ta klasör silinemez.
            foreach (var a in new[]
            {
                "build", Path.Combine("src", "Fraud.Collectors", "Fraud.Collectors.csproj"), "-c", "Release", "--nologo",
                "-nodeReuse:false", "--disable-build-servers",
            })
            {
                build.ArgumentList.Add(a);
            }

            if (ProcessRunner.Run(build, BuildTimeout, output) is not { ExitCode: 0 })
            {
                await output.WriteLineAsync("Temiz worktree'de derleme başarısız; " + mode + " çalışmadı (kilit alınmadı, sonuç basılmadı).")
                    .ConfigureAwait(false);
                return 2;
            }

            var program = Path.Combine(worktree, "src", "Fraud.Collectors", "bin", "Release", "net10.0", "Fraud.Collectors.dll");
            var run = new ProcessStartInfo(dotnet) { WorkingDirectory = worktree };
            run.ArgumentList.Add(program);
            run.ArgumentList.Add("eval-run");
            foreach (var (key, value) in InnerArguments(repo, mode, worktree, configPath, args))
            {
                run.ArgumentList.Add("--" + key);
                run.ArgumentList.Add(value);
            }

            var result = ProcessRunner.Run(run, RunTimeout, output);
            return result?.ExitCode ?? 2;
        }
        finally
        {
            await RemoveQuietlyAsync(repo, worktree, output).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Geçici worktree'yi siler. Silme hatası komutu istisnayla bitirmez: Sonuç zaten basılmıştır; silinemeyen klasör
    /// yalnızca uyarı olarak yazılır (ör. Windows'ta bir süreç dosyayı hâlâ açık tutuyorsa).
    /// </summary>
    private static async Task RemoveQuietlyAsync(RepoContext repo, string worktree, TextWriter output)
    {
        try
        {
            repo.RemoveWorktree(worktree);
            if (Directory.Exists(worktree))
            {
                Directory.Delete(worktree, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync("UYARI: Geçici worktree silinemedi, elle silin: " + worktree + " (git worktree prune).").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// İç çalıştırmanın argümanları. Yapılandırma geçici worktree'deki AYNI izlenen dosyadır (HEAD'deki hâli). Gizli CSV'ler
    /// ana deponun data/private klasöründe kaldığı için tam yolla verilir.
    /// </summary>
    public static IEnumerable<(string Key, string Value)> InnerArguments(
        RepoContext repo, EvaluationMode mode, string worktree, string configPath, IReadOnlyDictionary<string, string?> args)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(args);
        yield return ("mode", mode.ToString());
        yield return ("config", Path.Combine(worktree, Path.GetRelativePath(repo.Root, Path.GetFullPath(configPath))));
        foreach (var key in new[] { "commit", "allow-rerun", "reason" })
        {
            if (args.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                yield return (key, value);
            }
        }

        // Gizli ya da depo dışındaki dosyalar tam yolla (ileriye dönük kipte taahhüt dosyaları da).
        foreach (var key in new[] { "clusters", "commitment", "opening", "tsq", "tsr" })
        {
            if (args.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                yield return (key, Path.GetFullPath(value));
            }
        }
    }

    /// <summary>Yol, kök klasörün içinde mi?</summary>
    public static bool IsInside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, comparison);
    }

}
