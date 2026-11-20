using System.Diagnostics;
using System.Reflection;

namespace Fraud.Collectors.Evaluation;

/// <summary>
/// Depo kökü ve git durumu. Ölçüm ve taahhüt komutlarının gizli yolları buraya bağlanır (E1, E2, E8, E9).
/// </summary>
/// <remarks>
/// <para>
/// Neden: "data/private/..." gibi göreli bir yol ÇALIŞMA KLASÖRÜNE göre çözülür. Komut ör. src/Fraud.Collectors içinden
/// çalıştırılsaydı açıklama dosyası git'in yok saymadığı bir yere yazılırdı (E2).
/// </para>
/// <para>
/// Kök (E8): Yukarı doğru bulunan ilk FraudLab.slnx yetmez; git'in yok saydığı bir klasöre (ör. data/private/x/) konan
/// sahte bir işaret dosyası yeni bir "kök" ve yeni bir kilit dosyası açardı. Bu yüzden bulunan klasör
/// <c>git rev-parse --show-toplevel</c> çıktısıyla AYNI olmalı; değilse komut durur.
/// </para>
/// </remarks>
public sealed class RepoContext
{
    public const string MarkerFile = "FraudLab.slnx";

    /// <summary>git çağrısının en uzun süresi. Aşılırsa süreç öldürülür ve sonuç "başarısız" sayılır.</summary>
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    private readonly string? _git;

    private RepoContext(string root, string? git)
    {
        Root = root;
        _git = git;
    }

    public string Root { get; }

    /// <summary>
    /// Çalışma klasöründen yukarı doğru FraudLab.slnx arar ve bulunan klasörün git kökü olduğunu doğrular.
    /// </summary>
    /// <param name="gitPath">git'in tam yolu (ayar: <c>Git:Path</c>). Boşsa <see cref="GitExecutable.Resolve"/> PATH'ten bulur.</param>
    /// <exception cref="InvalidOperationException">Kök bulunamadı ya da git kökü değil.</exception>
    public static RepoContext Find(string? start = null, string? gitPath = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, MarkerFile)))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Depo kökü (" + MarkerFile + ") bulunamadı. Komutu depo içinden çalıştırın.");
        }

        var git = GitExecutable.Resolve(gitPath ?? Environment.GetEnvironmentVariable("Git__Path"));
        var candidate = new RepoContext(dir.FullName, git);
        var topLevel = candidate.Git("rev-parse", "--show-toplevel") is { ExitCode: 0 } r ? r.Output.Trim() : null;
        if (topLevel is null || !SamePath(topLevel, dir.FullName))
        {
            throw new InvalidOperationException(MarkerFile + " bulunan klasör git deposunun kökü değil (git rev-parse --show-toplevel). Komut durdu.");
        }

        return candidate;
    }

    /// <summary>YALNIZCA testler için: kökü git denetimi yapmadan verir. git çağrıları bu örnekte hep "başarısız" döner.</summary>
    public static RepoContext At(string root) =>
        File.Exists(Path.Combine(root, MarkerFile)) ? new RepoContext(Path.GetFullPath(root), git: null) : throw new InvalidOperationException("Kök değil.");

    /// <summary>
    /// Depo köküne göre gizli yol. Sonuç her zaman <c>&lt;kök&gt;/data/private/</c> altındadır; değilse istisna.
    /// </summary>
    public string PrivatePath(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        // Geçici worktree'de de ana deponun data/private klasörü kullanılır (kimlik anahtarı aynı kalsın).
        var baseDir = PrivateRootBase;
        var privateRoot = Path.GetFullPath(Path.Combine(baseDir, "data", "private")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(baseDir, relative));
        if (!full.StartsWith(privateRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Gizli dosya data/private dışına yazılamaz.");
        }

        return full;
    }

    /// <summary>
    /// Kilitli test kaydının yolu: <c>&lt;git ortak klasörü&gt;/fraud-lab/locked-run.json</c> (E8). <c>git worktree add</c> ile
    /// açılan ikinci çalışma ağacı aynı ortak klasörü kullanır, yani yeni bir "ilk" kilit açamaz. Başka bir klasöre
    /// klonlamaya karşı koruma yoktur; onun için dış çapa gerekir (kilit özetinin commit'i ve zaman damgası).
    /// </summary>
    public string LockFilePath()
    {
        var common = GitCommonDir() ?? throw new InvalidOperationException("git ortak klasörü okunamadı; kilitli test başlamaz.");
        return Path.Combine(common, "fraud-lab", "locked-run.json");
    }

    /// <summary><c>git rev-parse HEAD</c>.</summary>
    public string? HeadCommit() => Git("rev-parse", "HEAD") is { ExitCode: 0 } r ? r.Output.Trim() : null;

    /// <summary>
    /// Çalışma ağacı temiz mi? İki denetim (E9): <c>git status --porcelain</c> boş olmalı ve <c>git ls-files -v</c> içinde
    /// "değişmedi say" (küçük harf, ör. <c>h</c>) ya da "çalışma ağacını atla" (<c>S</c>) işaretli dosya olmamalı.
    /// Bu işaretler değiştirilmiş bir dosyayı <c>git status</c>'tan gizler.
    /// </summary>
    public bool IsClean()
    {
        if (Git("status", "--porcelain") is not { ExitCode: 0 } status || status.Output.Trim().Length > 0)
        {
            return false;
        }

        if (Git("ls-files", "-v") is not { ExitCode: 0 } files)
        {
            return false;
        }

        return !files.Output.Split('\n').Any(line => line.Length > 0 && (char.IsLower(line[0]) || line[0] == 'S'));
    }

    /// <summary>Dosya git'te izleniyor mu? Kilitli testte yapılandırma dosyası dondurulmuş commit'in parçası olmalı.</summary>
    public bool IsTracked(string path) =>
        Git("ls-files", "--error-unmatch", "--", Path.GetRelativePath(Root, Path.GetFullPath(path)).Replace('\\', '/')) is { ExitCode: 0 };

    /// <summary>
    /// Çalışan programın derlendiği commit (SDK'nın <c>AssemblyInformationalVersion</c> içine yazdığı "+sha" kısmı).
    /// Bilinmiyorsa <c>null</c>.
    /// </summary>
    public static string? BuildCommit()
    {
        var version = typeof(RepoContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return plus >= 0 ? version![(plus + 1)..] : null;
    }

    private static bool SamePath(string a, string b)
    {
        static string Normalize(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Normalize(a), Normalize(b), comparison);
    }

    /// <summary>
    /// git'i çözülmüş tam yoldan, argümanları tek tek (<see cref="ProcessStartInfo.ArgumentList"/>) vererek çalıştırır.
    /// </summary>
    /// <remarks>
    /// <para>Argümanlar tek metin olarak birleştirilmez: Bir yol boşluk ya da tırnak içerse bile ayrı argüman kalır.</para>
    /// <para>stdout ve stderr ikisi de okunur. Biri okunmazsa işletim sistemi tamponu dolar, git yazmayı bekler ve süreç
    /// kilitlenir. Süre aşılırsa süreç öldürülür ve sonuç başarısız sayılır; bitmemiş sürecin çıkış koduna güvenilmez.</para>
    /// </remarks>
    private (int ExitCode, string Output)? Git(params string[] arguments)
    {
        if (_git is null)
        {
            return null;
        }

        var psi = new ProcessStartInfo(_git)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // E11 (c): .git/config içindeki core.fsmonitor kancası git status'a "değişiklik yok" dedirtebilir ve kancanın kodunu
        // çalıştırır. Her çağrıda kapatılır.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.fsmonitor=false");
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return ProcessRunner.Run(psi, GitTimeout);
    }

    /// <summary>
    /// Ana çalışma ağacının kökü: git ortak klasörünün (<c>.../.git</c>) üst klasörü. Geçici worktree içinden çalışınca da
    /// gizli dosyalar (kimlik anahtarı, eski kilit) ana depodaki data/private'tan okunur. git yoksa <see cref="Root"/>.
    /// </summary>
    public string PrivateRootBase
    {
        get
        {
            var common = GitCommonDir();
            return common is not null && string.Equals(Path.GetFileName(common), ".git", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(common)!
                : Root;
        }
    }

    /// <summary>Bu klasör <c>git worktree add</c> ile açılmış ikincil bir çalışma ağacı mı (git-dir ≠ git-common-dir)?</summary>
    public bool IsLinkedWorktree()
    {
        var gitDir = Git("rev-parse", "--absolute-git-dir") is { ExitCode: 0 } r ? r.Output.Trim() : null;
        var common = GitCommonDir();
        return gitDir is not null && common is not null && !SamePath(gitDir, common);
    }

    /// <summary>
    /// src/ ve tests/ altında git'in YOK SAYDIĞI dosyalar (bin/ ve obj/ derleme çıktıları hariç). E11 (a): SDK'nın varsayılan
    /// derleme kalıbı yok sayılan klasördeki (ör. out/) .cs dosyasını da derler; git status bunu göstermez.
    /// </summary>
    public IReadOnlyList<string>? IgnoredSourcePaths() =>
        Git("status", "--ignored", "--porcelain", "--untracked-files=all") is { ExitCode: 0 } r ? FindIgnoredSourcePaths(r.Output) : null;

    /// <summary>Saf ayrıştırıcı: <c>git status --ignored --porcelain</c> çıktısında "!! " satırlarından src/ ve tests/ altındakiler.</summary>
    public static IReadOnlyList<string> FindIgnoredSourcePaths(string porcelain)
    {
        ArgumentNullException.ThrowIfNull(porcelain);
        return [.. porcelain.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("!! ", StringComparison.Ordinal))
            .Select(l => l[3..].Trim('"'))
            .Where(p => p.StartsWith("src/", StringComparison.Ordinal) || p.StartsWith("tests/", StringComparison.Ordinal))
            .Where(p => !p.Split('/').Any(segment => segment is "bin" or "obj"))];
    }

    /// <summary>Sabitlenmiş TSA kök listesinin depodaki yolu.</summary>
    public const string TsaRootsFile = "src/Fraud.Collectors/Evaluation/tsa_roots.txt";

    /// <summary>
    /// Bir TSA kök parmak izinin tsa_roots.txt'e İLK eklendiği commit ve tarihi (E13):
    /// <c>git log --format="%H %cI" -S&lt;parmak_izi&gt; -- tsa_roots.txt</c>. git log yeniden eskiye sıralar; son satır en eski
    /// commit'tir. Bulunamazsa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Sınır: Commit tarihi commit'i yapanın beyanıdır (<c>GIT_COMMITTER_DATE</c> ile değiştirilebilir). Bu denetim
    /// "kök sonuçtan sonra eklenmiş" hatasını yakalar; kasıtlı geriye tarihlemeye karşı tek koruma commit'in uzak depoya
    /// zamanında itilmesi ya da damgalanmasıdır.
    /// </remarks>
    public (string Commit, DateTimeOffset Date)? PinCommit(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (fingerprint.Length != 64 || !fingerprint.All(char.IsAsciiHexDigitLower))
        {
            return null;
        }

        if (Git("log", "--format=%H %cI", "-S" + fingerprint, "--", TsaRootsFile) is not { ExitCode: 0 } r)
        {
            return null;
        }

        var oldest = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        var parts = oldest?.Split(' ');
        return parts is { Length: 2 } && DateTimeOffset.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date)
            ? (parts[0], date)
            : null;
    }

    /// <summary>HEAD'den ayrık (detached) geçici bir worktree açar.</summary>
    public bool AddDetachedWorktree(string path) => Git("worktree", "add", "--detach", path, "HEAD") is { ExitCode: 0 };

    /// <summary>Geçici worktree'yi siler (dosyalar ve git kaydı).</summary>
    public void RemoveWorktree(string path)
    {
        _ = Git("worktree", "remove", "--force", path);
        _ = Git("worktree", "prune");
    }

    private string? GitCommonDir()
    {
        var common = Git("rev-parse", "--git-common-dir") is { ExitCode: 0 } r ? r.Output.Trim() : null;
        return string.IsNullOrEmpty(common) ? null : Path.GetFullPath(Path.Combine(Root, common));
    }
}

/// <summary>Dış süreç çalıştırıcı: stdout ve stderr ikisi de okunur, süre aşılırsa süreç ağacı öldürülür.</summary>
public static class ProcessRunner
{
    /// <summary>
    /// stdout ve stderr ikisi de okunur. Biri okunmazsa işletim sistemi tamponu dolar ve süreç kilitlenir. Süre aşılırsa
    /// süreç öldürülür ve sonuç <c>null</c> olur; bitmemiş sürecin çıkış koduna güvenilmez.
    /// </summary>
    /// <param name="echo">Verilirse her çıktı satırı buraya da yazılır (uzun derleme ve çalıştırma için).</param>
    public static (int ExitCode, string Output)? Run(ProcessStartInfo psi, TimeSpan timeout, TextWriter? echo = null)
    {
        ArgumentNullException.ThrowIfNull(psi);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stdout)
            {
                stdout.Append(e.Data).Append('\n');
            }

            echo?.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                echo?.WriteLine(e.Data);
            }
        };

        if (!process.Start())
        {
            return null;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        process.WaitForExit(); // Akışların sonuna kadar okunması için.
        lock (stdout)
        {
            return (process.ExitCode, stdout.ToString());
        }
    }
}

/// <summary>
/// git ikilisinin güvenilir yolu (E9). Yalnızca adıyla ("git") başlatılsaydı Windows önce ÇALIŞMA KLASÖRÜNE bakardı;
/// depo klasörüne başka bir hesabın koyduğu git.exe çalışır, HEAD ve "temiz" cevabını uydurabilirdi.
/// </summary>
public static class GitExecutable
{
    /// <summary>
    /// <list type="number">
    /// <item>Ayarla verilen yol (<c>Git:Path</c>): tam yol olmalı ve dosya var olmalı.</item>
    /// <item>Yoksa PATH girdileri sırayla gezilir. Göreli girdiler, çalışma klasörü ve onun altındaki klasörler atlanır.</item>
    /// </list>
    /// </summary>
    /// <exception cref="InvalidOperationException">git bulunamadı ya da ayardaki yol geçersiz.</exception>
    public static string Resolve(string? configured) =>
        Resolve(configured, Environment.GetEnvironmentVariable("PATH") ?? string.Empty, Directory.GetCurrentDirectory());

    /// <summary>Aynı kural, başka bir ikili için (ör. kilitli testte <c>dotnet</c>).</summary>
    public static string ResolveTool(string baseName, string? configured) =>
        Resolve(configured, Environment.GetEnvironmentVariable("PATH") ?? string.Empty, Directory.GetCurrentDirectory(), baseName);

    /// <summary>git için kural; PATH ve çalışma klasörü dışarıdan verilir (test edilebilsin diye).</summary>
    public static string Resolve(string? configured, string pathVariable, string currentDirectory) =>
        Resolve(configured, pathVariable, currentDirectory, "git");

    /// <summary>Genel kural: verilen ad için PATH'te güvenilir yol.</summary>
    public static string Resolve(string? configured, string pathVariable, string currentDirectory, string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentNullException.ThrowIfNull(pathVariable);
        ArgumentNullException.ThrowIfNull(currentDirectory);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathFullyQualified(configured) && File.Exists(configured)
                ? configured
                : throw new InvalidOperationException("Git:Path tam bir yol olmalı ve dosya var olmalı.");
        }

        var name = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;
        var current = Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var entry in pathVariable.Split(Path.PathSeparator))
        {
            var dir = entry.Trim().Trim('"');
            if (dir.Length == 0 || !Path.IsPathFullyQualified(dir))
            {
                continue;
            }

            var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(current, comparison))
            {
                continue; // çalışma klasörü ya da altındaki bir klasör
            }

            var candidate = Path.Combine(full, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(baseName + " PATH'te bulunamadı. Tam yolu ayarla verin (ör. Git:Path).");
    }
}
