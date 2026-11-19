using System.Text;
using Fraud.Collectors.Evaluation;
using Fraud.Evaluation.Commitments;
using Fraud.Evaluation.Running;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Evaluation;

/// <summary>
/// E1-E7 düzeltmelerinin getirdiği yeni kod: RepoContext (depo kökü, git), VerifyOpening, VerifyPrevious, küçük hücre.
/// Depoya yazılmaz; geçici klasörler kullanılır.
/// </summary>
public sealed class EvaluationFixRegressionTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("repo-context-");

    public void Dispose() => _dir.Delete(recursive: true);

    private static List<string> Records(int n) =>
        [.. Enumerable.Range(0, n).Select(i => "aday" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".example")];

    [Theory]
    [InlineData("../disari.json")]
    [InlineData("data/private/../../disari.json")]
    [InlineData("data/privatex/a.json")]
    [InlineData("data/private")]
    [InlineData("src/data/private/a.json")]
    public void Private_path_never_leaves_the_root_private_folder(string relative)
    {
        File.WriteAllText(Path.Combine(_dir.FullName, RepoContext.MarkerFile), "");
        var repo = RepoContext.At(_dir.FullName);

        Assert.Throws<InvalidOperationException>(() => repo.PrivatePath(relative));
        Assert.StartsWith(Path.Combine(_dir.FullName, "data", "private") + Path.DirectorySeparatorChar,
            repo.PrivatePath("data/private/commitments/a.opening.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void Absolute_path_cannot_be_smuggled_through_the_relative_argument()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, RepoContext.MarkerFile), "");
        var repo = RepoContext.At(_dir.FullName);
        var outside = Path.Combine(Path.GetTempPath(), "disari", "a.json");

        Assert.Throws<InvalidOperationException>(() => repo.PrivatePath(outside));
    }

    [Fact]
    public void Tampered_opening_never_verifies_against_the_public_commitment()
    {
        var (pub, opening) = CommitmentTool.Create(Records(9), At, null);
        Assert.True(CommitmentTool.VerifyOpening(pub, opening));

        var entries = opening.Entries.ToList();
        OpeningEntry Swap(int i, Func<OpeningEntry, OpeningEntry> f) => f(entries[i]);

        // Kayıt değişti, tuzlar yer değiştirdi, indeks tekrarlandı, kayıt eksik/fazla, özet listesi değişti.
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Entries = [.. entries.Select((e, i) => i == 3 ? e with { Record = "baska.example" } : e)] }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with
        {
            Entries = [.. entries.Select((e, i) => i == 0 ? e with { Salt = entries[1].Salt } : i == 1 ? e with { Salt = entries[0].Salt } : e)],
        }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Entries = [.. entries.Select((e, i) => i == 2 ? e with { Index = 1 } : e)] }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Entries = [.. entries.Skip(1)] }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Entries = [.. entries, Swap(0, e => e with { Index = 9 })] }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Commitments = [.. opening.Commitments.Reverse()] }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Entries = [.. entries.Select((e, i) => i == 4 ? e with { Salt = e.Salt[..^2] } : e)] }));
        Assert.False(CommitmentTool.VerifyOpening(pub, opening with { Entries = [.. entries.Select((e, i) => i == 4 ? e with { Salt = "zz" + e.Salt[2..] } : e)] }));
        Assert.False(CommitmentTool.VerifyOpening(pub with { Count = 10 }, opening));

        // Başka bir listenin açıklaması bu açık taahhüdü doğrulamaz.
        var (_, other) = CommitmentTool.Create(Records(9), At, null);
        Assert.False(CommitmentTool.VerifyOpening(pub, other));
    }

    [Fact]
    public void Previous_link_detects_a_changed_or_fake_previous_file()
    {
        var (first, _) = CommitmentTool.Create(Records(3), At, null);
        var firstBytes = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(first));
        var (second, _) = CommitmentTool.Create(Records(4), At.AddDays(1), firstBytes);

        Assert.True(CommitmentTool.VerifyPrevious(second, firstBytes));

        var changed = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(first with { Count = 30 }));
        Assert.False(CommitmentTool.VerifyPrevious(second, changed));
        Assert.False(CommitmentTool.VerifyPrevious(second, [.. firstBytes, (byte)' ']));
        Assert.False(CommitmentTool.VerifyPrevious(first, firstBytes)); // ilk taahhüdün önceki bağlantısı yok
        Assert.Throws<ArgumentException>(() => CommitmentTool.Create(Records(2), At, Encoding.UTF8.GetBytes("{\"x\":1}")));
        Assert.Null(CommitmentTool.ParsePublic(Encoding.UTF8.GetBytes("not json")));
    }

    [Theory]
    [InlineData(0, "<5")]
    [InlineData(1, "<5")]
    [InlineData(4, "<5")]
    [InlineData(5, "5")]
    public void Small_counts_are_suppressed(int count, string expected)
    {
        Assert.Equal(expected, EvaluationReport.Cell(count));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]     // 39
    [InlineData("0123456789abcdef0123456789abcdef012345678")]   // 41
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")]    // büyük harf
    [InlineData("HEAD")]
    [InlineData("0123456789abcdef0123456789abcdef0123456g")]
    public void Commit_id_must_be_a_full_lowercase_sha1(string value)
    {
        Assert.False(LockedRunGuard.IsFullCommitId(value));
    }

    // ---- Yeniden denetimde açılan bulgular ----

    [Fact]
    public void Repo_root_must_be_the_git_top_level_not_any_marker_file()
    {
        // Bulgu E8: data/private/x/FraudLab.slnx (git yok sayar, çalışma ağacı "temiz" kalır) ya da depo dışındaki bir
        // klasör kök kabul edilir. HEAD ve temizlik denetimleri yine üst depodan geçer; kilit dosyası yeni kökte
        // "ilk kez" oluşur. Kök, git rev-parse --show-toplevel ile aynı olmalı; kilit, worktree'ler arasında ortak git
        // klasörüne (git rev-parse --git-common-dir) bağlanmalı.
        File.WriteAllText(Path.Combine(_dir.FullName, RepoContext.MarkerFile), "");

        Assert.Throws<InvalidOperationException>(() => RepoContext.Find(_dir.FullName));
    }

    [Fact]
    public void Git_is_started_from_a_resolved_trusted_path_with_argument_list()
    {
        // Bulgu E9: new ProcessStartInfo("git", "...") Windows'ta CreateProcess arama sırasıyla çözülür: uygulama klasörü,
        // ÜST SÜRECİN çalışma klasörü, sistem klasörleri, sonra PATH. C:\dev\fraud-lab "Authenticated Users: Modify"
        // devraldığı için başka bir yerel hesap depo köküne git.exe koyup (a) Kadircan adına kod çalıştırabilir,
        // (b) HEAD ve "temiz" cevabını uydurup kilitli testi geçersiz bir durumda başlatabilir. Ayrıca argümanlar tek
        // metin olarak birleştiriliyor (IsTracked yolunu tırnakla sarar). Linux'ta .NET'in çalışma klasörüne bakıp
        // bakmadığı doğrulanmadı (olası).
        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/RepoContext.cs");

        Assert.DoesNotContain("new ProcessStartInfo(\"git\"", code, StringComparison.Ordinal);
        Assert.Contains("ArgumentList", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Prospective_commitment_time_comes_from_a_verified_timestamp()
    {
        // Bulgu E10: RunProspective, alarmların "karardan önce" olduğunu publicCommitment.CreatedAt'e göre ölçüyor. Bu
        // alanı commit-create yazar ve isteyen geçmiş bir tarih yazabilir. Kararlar bilindikten sonra geriye tarihli bir
        // taahhüt "ileriye dönük" sonuç verir. Komut yalnızca "ayrıca doğrulanmalı" diye uyarıyor. Zaman, RFC 3161
        // cevabından (.tsr: imza, messageImprint = SHA-256(açık dosya), genTime) ya da en azından taahhüdün itildiği
        // commit'in tarihinden alınmalı; doğrulanamazsa ileriye dönük sonuç basılmamalı.
        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/EvaluationCommands.cs");

        Assert.Matches(@"Rfc3161TimestampToken|--tsr|""tsr""", code);
    }
}
