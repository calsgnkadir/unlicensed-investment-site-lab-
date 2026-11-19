using System.Text.Json;

namespace Fraud.Evaluation.Running;

/// <summary>Kilitli testin bir çalıştırma kaydı. Alan adı yok; yalnızca yapılandırma özeti, commit ve zaman.</summary>
public sealed record LockedRunRecord(DateTimeOffset StartedAt, string ConfigSha256, string Commit, bool IsRerun, string? RerunReason);

/// <summary>
/// Kilitli teste giriş bileti. Yalnızca <see cref="LockedRunGuard.Acquire"/> üretir; <see cref="EvaluationRunner"/>
/// kilitli kipi bu bilet olmadan çalıştırmaz.
/// </summary>
public sealed class LockedRunTicket
{
    internal LockedRunTicket(LockedRunRecord current, LockedRunRecord first)
    {
        Current = current;
        First = first;
    }

    public LockedRunRecord Current { get; }

    /// <summary>İlk (asıl) çalıştırma. Yeniden çalıştırmada rapor ikisini birden yazar.</summary>
    public LockedRunRecord First { get; }

    public bool IsRerun => Current.IsRerun;
}

/// <summary>Kilitli test daha önce çalışmış ve yeniden çalıştırma açıkça istenmemiş.</summary>
public sealed class LockedRunAlreadyUsedException(string message) : InvalidOperationException(message);

/// <summary>
/// "Kilitli test bir kez çalışır" kuralı (Kural 7). Kilit dosyası çalıştırmadan ÖNCE yazılır.
/// </summary>
/// <remarks>
/// <para>
/// Neden önce: Önce çalıştırıp sonra yazsaydık, sonucu görüp beğenmeyen biri programı yazmadan durdurabilir ve
/// "ilk kez" yeniden çalıştırabilirdi. Dosya önceden yazılınca yarıda kalan çalıştırma da "kullanıldı" sayılır.
/// </para>
/// <para>
/// <see cref="FileMode.CreateNew"/>: Dosya zaten varsa işletim sistemi açmayı reddeder. İki süreç aynı anda
/// başlasa bile yalnızca biri "ilk" olur.
/// </para>
/// <para>
/// Yeniden çalıştırma yalnızca <c>allowRerun</c> ve bir gerekçeyle olur; kayıt dosyaya eklenir ve rapor başlığına
/// "YENİDEN ÇALIŞTIRMA" yazılır. Böylece ikinci sonuç asla ilk sonuç gibi sunulamaz.
/// </para>
/// </remarks>
public static class LockedRunGuard
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Tam git commit kimliği: 40 küçük harf onaltılık karakter (SHA-1).</summary>
    public static bool IsFullCommitId(string value) =>
        value is { Length: 40 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    public static LockedRunTicket Acquire(
        string lockFile, string configSha256, string commit, DateTimeOffset now, bool allowRerun = false, string? rerunReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        if (!IsFullCommitId(commit))
        {
            // E1 (b): "son", "HEAD" ya da kısa bir SHA hangi kodla çalışıldığını kanıtlamaz. Komut ayrıca bunun
            // git rev-parse HEAD ile aynı ve çalışma ağacının temiz olduğunu denetler (EvaluationCommands).
            throw new ArgumentException("Commit 40 karakterlik küçük harf onaltılık git kimliği olmalı.", nameof(commit));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockFile))!);

        var first = new LockedRunRecord(now, configSha256, commit, IsRerun: false, RerunReason: null);
        try
        {
            using var stream = new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new List<LockedRunRecord> { first }, Json);
            return new LockedRunTicket(first, first);
        }
        catch (IOException) when (File.Exists(lockFile))
        {
            // Dosya var: kilitli test daha önce kullanıldı.
        }

        var runs = JsonSerializer.Deserialize<List<LockedRunRecord>>(File.ReadAllText(lockFile))
            ?? throw new InvalidOperationException("Kilit dosyası okunamadı.");
        var original = runs[0];
        if (!allowRerun || string.IsNullOrWhiteSpace(rerunReason))
        {
            throw new LockedRunAlreadyUsedException(
                "Kilitli test " + original.StartedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture)
                + " tarihinde (commit " + original.Commit + ") çalıştı. Yeniden çalıştırma için --allow-rerun ve --reason gerekir;"
                + " rapora 'YENİDEN ÇALIŞTIRMA' yazılır.");
        }

        var rerun = new LockedRunRecord(now, configSha256, commit, IsRerun: true, RerunReason: rerunReason);
        runs.Add(rerun);
        File.WriteAllText(lockFile, JsonSerializer.Serialize(runs, Json));
        return new LockedRunTicket(rerun, original);
    }
}
