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
        var privateRoot = Path.GetFullPath(Path.Combine(Root, "data", "private")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(Root, relative));
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
        var common = Git("rev-parse", "--git-common-dir") is { ExitCode: 0 } r ? r.Output.Trim() : null;
        if (string.IsNullOrEmpty(common))
        {
            throw new InvalidOperationException("git ortak klasörü okunamadı; kilitli test başlamaz.");
        }

        return Path.GetFullPath(Path.Combine(Root, common, "fraud-lab", "locked-run.json"));
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
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(GitTimeout))
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        process.WaitForExit(); // Akışların sonuna kadar okunması için.
        _ = stderr.GetAwaiter().GetResult();
        return (process.ExitCode, stdout.GetAwaiter().GetResult());
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

    /// <summary>Aynı kural; PATH ve çalışma klasörü dışarıdan verilir (test edilebilsin diye).</summary>
    public static string Resolve(string? configured, string pathVariable, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(pathVariable);
        ArgumentNullException.ThrowIfNull(currentDirectory);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathFullyQualified(configured) && File.Exists(configured)
                ? configured
                : throw new InvalidOperationException("Git:Path tam bir yol olmalı ve dosya var olmalı.");
        }

        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
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

        throw new InvalidOperationException("git PATH'te bulunamadı. Git:Path ayarıyla tam yolu verin.");
    }
}
