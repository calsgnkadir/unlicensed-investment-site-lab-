using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fraud.Domain.DomainNames;
using Fraud.Evaluation;
using Fraud.Evaluation.Commitments;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Signals;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;

namespace Fraud.Collectors.Evaluation;

/// <summary>
/// Ölçüm ve taahhüt komutları. Ekrana yalnızca toplu sayılar basılır; alan adı, kayıt veya tuz basılmaz.
/// </summary>
/// <remarks>Gizli yolların hepsi depo köküne bağlıdır (<see cref="RepoContext"/>), çalışma klasörüne değil (E2).</remarks>
public static class EvaluationCommands
{
    /// <summary>Depo köküne göre gizli klasörler.</summary>
    public const string CommitmentsDirectory = "data/private/commitments";
    public const string SubjectKeyFile = "data/private/evaluation/subject_id.key";

    /// <summary>
    /// <c>eval-run --mode Historical|Locked --config &lt;dosya&gt; [--controls &lt;csv&gt;] [--clusters &lt;csv&gt;]
    /// [--commit &lt;sha&gt;] [--allow-rerun true --reason &lt;metin&gt;]</c>;
    /// <c>eval-run --mode Prospective --config &lt;dosya&gt; --commit &lt;sha&gt; --commitment &lt;açık dosya&gt; --opening &lt;açıklama dosyası&gt;</c>
    /// </summary>
    public static Task<int> EvalRunAsync(
        FraudDbContext db, IDomainNameNormalizer normalizer, IReadOnlyDictionary<string, string?> args, TextWriter output,
        CancellationToken cancellationToken, RepoContext? repo = null) =>
        EvalRunCoreAsync(db, normalizer, args, output, repo, testHooks: null, cancellationToken);

    /// <summary>
    /// YALNIZCA testler (InternalsVisibleTo): Kendi TSA kökünü ve kökün commit tarihini veren, temiz derleme denetimini
    /// atlayan kanca. Komut satırından erişilemez; <see cref="EvalRunAsync(FraudDbContext, IDomainNameNormalizer, IReadOnlyDictionary{string, string?}, TextWriter, CancellationToken, RepoContext?)"/>
    /// kancayı her zaman boş verir.
    /// </summary>
    internal sealed record TestHooks(IReadOnlySet<string> PinnedTsaRoots, Func<string, (string Commit, DateTimeOffset Date)?> PinCommit);

    internal static async Task<int> EvalRunCoreAsync(
        FraudDbContext db, IDomainNameNormalizer normalizer, IReadOnlyDictionary<string, string?> args, TextWriter output,
        RepoContext? repo, TestHooks? testHooks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParseMode(Arg(args, "mode"), out var mode) || Arg(args, "config") is not { } configPath)
        {
            await output.WriteLineAsync("Kullanım: eval-run --mode Historical|Locked|Prospective --config <dosya>").ConfigureAwait(false);
            return 64;
        }

        repo ??= RepoContext.Find();
        var configBytes = await ReadLimitedAsync(configPath, EvaluationConfig.MaxFileBytes, cancellationToken).ConfigureAwait(false);
        var config = EvaluationConfig.Parse(Encoding.UTF8.GetString(configBytes));

        // Yapılandırmanın TAMAMI kilitten önce doğrulanır (mentör maddesi 5): hata tek hakkı harcamasın.
        config.ValidateFor(mode);


        // E11 ve E13: Kilitli ve ileriye dönük kip, ana ağaçtan çağrıldıysa (ya da program geçici worktree'nin içinden
        // derlenmemişse) temiz bir geçici worktree'de derlenip orada çalışır. Sonucu yalnızca oradaki "iç" çalıştırma basar.
        // İleriye dönük kipte bu şart: Kabul edilen TSA kökleri, ÇALIŞAN ikiliye gömülü listedir.
        var needsCleanBuild = mode is EvaluationMode.Locked or EvaluationMode.Prospective && testHooks is null;
        if (needsCleanBuild && (!repo.IsLinkedWorktree() || !LockedRunOrchestrator.IsInside(AppContext.BaseDirectory, repo.Root)))
        {
            return await LockedRunOrchestrator.RunAsync(repo, mode, configPath, args, output, cancellationToken).ConfigureAwait(false);
        }

        // Sinyal kaynakları (sözlük, marka listesi) kilitten ÖNCE okunur: eksik dosya ya da tutmayan özet tek hakkı harcamasın.
        // Kilitli kipte bu satır temiz worktree'deki iç çalıştırmada işler (S1: liste orada G1 taahhüdünden üretilir).
        if (await SignalCommands.LoadResourcesAsync(config.Signal, mode, db, repo, output, cancellationToken).ConfigureAwait(false) is not { } loaded)
        {
            return 2;
        }

        var resources = loaded.Resources;
        var signal = SignalCatalog.Create(config.Signal, resources);

        LockedRunTicket? ticket = null;
        if (mode == EvaluationMode.Locked)
        {
            if (await CheckFrozenStateAsync(repo, configPath, Arg(args, "commit"), output).ConfigureAwait(false) is not { } commit)
            {
                return 64;
            }

            // E8: Kilit git'in ortak klasöründe (worktree'ler aynı dosyayı görür). Eski sürümün depo içindeki kaydı varsa
            // önce yeni konuma taşınır; böylece --allow-rerun ile bile ikinci çalıştırma "ilk" görünmez.
            var lockPath = repo.LockFilePath();

            // Kilit veri yüklenmeden önce alınır: Yarıda kesilen çalıştırma da "kullanıldı" sayılır.
            ticket = LockedRunGuard.AcquireWithLegacy(
                lockPath, repo.PrivatePath(config.LockFile), Convert.ToHexStringLower(SHA256.HashData(configBytes)), commit,
                DateTimeOffset.UtcNow, allowRerun: Arg(args, "allow-rerun") == "true", rerunReason: Arg(args, "reason"),
                brandListSha256: loaded.DomesticBrandsSha256);
            await WriteAnchorInstructionsAsync(lockPath, output, cancellationToken).ConfigureAwait(false);
        }

        var loader = new EvaluationDataLoader(db, normalizer, EvaluationDataLoader.LoadOrCreateKey(repo.PrivatePath(SubjectKeyFile)));
        (IReadOnlySet<string> Ids, DateTimeOffset Time)? committed = null;
        if (mode == EvaluationMode.Prospective)
        {
            // E13: İç kipte de aynı kaynak ve ikili denetimi (HEAD, temiz ağaç, yok sayılan dosya yok, ikilinin +sha'sı).
            if (testHooks is null && await CheckFrozenStateAsync(repo, configPath, Arg(args, "commit"), output).ConfigureAwait(false) is null)
            {
                return 64;
            }

            // E10: Taahhüt anı doğrulanmış RFC 3161 damgasından gelir. Doğrulanamazsa veri bile yüklenmez, sonuç basılmaz.
            committed = await ReadCommittedAsync(
                args, config, repo, testHooks?.PinnedTsaRoots ?? PinnedTsaRoots.Default, testHooks?.PinCommit ?? repo.PinCommit,
                normalizer, loader, output, cancellationToken).ConfigureAwait(false);
            if (committed is null)
            {
                return 2;
            }
        }

        var data = await loader.LoadAsync(
            Arg(args, "clusters"), config.ArchiveKnownDelay, mode == EvaluationMode.Prospective, cancellationToken)
            .ConfigureAwait(false);

        var c = data.Counts;
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Yüklendi: pozitif={c.Positives}, kontrol={c.Controls}, reddedilen kontrol satırı={c.ControlsRejected}, pozitifle çakışan (E01)={c.ControlsOverlappingPositives}, sertifika={c.Certificates}, arşiv={c.Archives}, dns={c.DnsRows}"))
            .ConfigureAwait(false);

        if (config.Signal.Ablation)
        {
            // Yalnızca geçmiş veri testi (ValidateFor bunu zorunlu kılar). Her satır aynı bölme ve eşik kuralıyla çalışır.
            var rows = Ablation.Run(config, data.Subjects, data.Observations, SignalCatalog.Parts(config.Signal, resources));
            await output.WriteAsync(Ablation.ToText(rows)).ConfigureAwait(false);
            return 0;
        }

        EvaluationReport report;
        if (committed is { } commitment)
        {
            report = EvaluationRunner.RunProspective(config, data.Subjects, data.Observations, commitment.Ids, commitment.Time);
        }
        else
        {
            report = EvaluationRunner.Run(config, mode, data.Subjects, data.Observations, signal, ticket);
        }

        await output.WriteAsync(report.ToText()).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// <c>commit-create --records &lt;dosya&gt; --name &lt;ad&gt; --public-out &lt;dosya&gt; [--previous &lt;açık dosya&gt;]</c>
    /// </summary>
    /// <remarks>
    /// Kayıtlar taahhütten ÖNCE normalleştirilir (mentör maddesi 10): küçük harf, punycode, kayıtlı alan adı. Böylece
    /// sonradan aynı ad farklı yazılsa da (büyük harf, www., Unicode) aynı kayıt bulunur. Normalleştirilemeyen ya da
    /// normalleştirince tekrar eden satır varsa hiçbir şey yazılmaz.
    /// </remarks>
    public static async Task<int> CommitCreateAsync(
        IReadOnlyDictionary<string, string?> args, TextWriter output, CancellationToken cancellationToken, RepoContext? repo = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (Arg(args, "records") is not { } recordsPath || Arg(args, "name") is not { } name || Arg(args, "public-out") is not { } publicOut
            || !IsSafeName(name))
        {
            await output.WriteLineAsync("Kullanım: commit-create --records <dosya> --name <ad: harf, rakam, tire> --public-out <dosya> [--previous <dosya>]")
                .ConfigureAwait(false);
            return 64;
        }

        repo ??= RepoContext.Find();
        var normalizer = new PublicSuffixDomainNormalizer();
        var lines = (await File.ReadAllLinesAsync(recordsPath, cancellationToken).ConfigureAwait(false)).Where(l => l.Trim().Length > 0).ToList();
        var records = lines.Select(l => NormalizeRecord(normalizer, l)).ToList();
        var invalid = records.Count(r => r is null);
        if (invalid > 0 || records.Distinct(StringComparer.Ordinal).Count() != records.Count)
        {
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"Taahhüt yazılmadı: normalleştirilemeyen satır={invalid}; normalleştirince tekrar eden satır var mı={records.Distinct(StringComparer.Ordinal).Count() != records.Count}."))
                .ConfigureAwait(false);
            return 2;
        }

        byte[]? previous = null;
        if (Arg(args, "previous") is { } previousPath)
        {
            previous = await ReadLimitedAsync(previousPath, 64 * 1024, cancellationToken).ConfigureAwait(false);
            if (CommitmentTool.ParsePublic(previous) is null)
            {
                await output.WriteLineAsync("--previous geçerli bir açık taahhüt dosyası değil.").ConfigureAwait(false);
                return 2;
            }
        }

        var openingPath = repo.PrivatePath(CommitmentsDirectory + "/" + name + ".opening.json");
        if (File.Exists(openingPath) || File.Exists(publicOut))
        {
            // E3: Var olan taahhüdün üzerine yazılmaz; eski tuzlar kaybolursa o taahhüt bir daha kanıtlanamaz.
            await output.WriteLineAsync("Bu adla bir taahhüt zaten var; üzerine yazılmaz. Yeni bir --name ve --public-out verin.").ConfigureAwait(false);
            return 2;
        }

        var (publicCommitment, opening) = CommitmentTool.Create(records!, DateTimeOffset.UtcNow, previous);
        await WriteNewFileAsync(openingPath, Encoding.UTF8.GetBytes(CommitmentTool.ToJson(opening)), cancellationToken).ConfigureAwait(false);
        var publicBytes = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(publicCommitment));
        await WriteNewFileAsync(Path.GetFullPath(publicOut), publicBytes, cancellationToken).ConfigureAwait(false);

        // Zaman damgası isteği: Açık dosyanın baytları üzerinden. Ağa bağlanılmaz (README: elle adım).
        var nonce = Math.Max(BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & long.MaxValue, 1);
        await WriteNewFileAsync(Path.GetFullPath(publicOut) + ".tsq", CommitmentTool.TimestampRequest(publicBytes, nonce), cancellationToken)
            .ConfigureAwait(false);

        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Taahhüt: kayıt={publicCommitment.Count}, kök={publicCommitment.Root}. Açık dosya ve .tsq yazıldı; açıklama dosyası {CommitmentsDirectory} altında."))
            .ConfigureAwait(false);
        return 0;
    }

    /// <summary><c>commit-prove --opening &lt;dosya&gt; --index &lt;i&gt;</c>: tek kayıt kanıtını data/private/commitments altına yazar.</summary>
    public static async Task<int> CommitProveAsync(
        IReadOnlyDictionary<string, string?> args, TextWriter output, CancellationToken cancellationToken, RepoContext? repo = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (Arg(args, "opening") is not { } openingPath
            || !int.TryParse(Arg(args, "index"), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            await output.WriteLineAsync("Kullanım: commit-prove --opening <dosya> --index <i>").ConfigureAwait(false);
            return 64;
        }

        repo ??= RepoContext.Find();
        var opening = CommitmentTool.FromJson<CommitmentOpening>(await File.ReadAllTextAsync(openingPath, cancellationToken).ConfigureAwait(false));
        if (index < 0 || index >= opening.Entries.Count)
        {
            await output.WriteLineAsync("İndeks aralık dışında.").ConfigureAwait(false);
            return 64;
        }

        var proof = CommitmentTool.Prove(opening, index);
        var baseName = Path.GetFileName(openingPath).Replace(".opening.json", string.Empty, StringComparison.Ordinal);
        if (!IsSafeName(baseName))
        {
            await output.WriteLineAsync("Açıklama dosyasının adı beklenen biçimde değil.").ConfigureAwait(false);
            return 64;
        }

        var proofPath = repo.PrivatePath(CommitmentsDirectory + "/" + baseName + ".proof-" + index.ToString(CultureInfo.InvariantCulture) + ".json");
        if (File.Exists(proofPath))
        {
            File.Delete(proofPath); // Kanıt açıklama dosyasından yeniden üretilebilir; üzerine yazmak bilgi kaybettirmez.
        }

        await WriteNewFileAsync(proofPath, Encoding.UTF8.GetBytes(CommitmentTool.ToJson(proof)), cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Kanıt yazıldı ({CommitmentsDirectory}): indeks {index} / {proof.TreeSize}, yol uzunluğu {proof.Path.Count}.")).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// <c>commit-verify --public &lt;dosya&gt; --proof &lt;dosya&gt; [--record &lt;ad&gt;] [--previous &lt;önceki açık dosya&gt;]</c>.
    /// Kayıt ekrana basılmaz. <c>--record</c> verilirse aynı normalleştirmeden geçirilip kanıttaki kayıtla karşılaştırılır.
    /// </summary>
    public static async Task<int> CommitVerifyAsync(IReadOnlyDictionary<string, string?> args, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (Arg(args, "public") is not { } publicPath || Arg(args, "proof") is not { } proofPath)
        {
            await output.WriteLineAsync("Kullanım: commit-verify --public <dosya> --proof <dosya> [--record <ad>] [--previous <dosya>]").ConfigureAwait(false);
            return 64;
        }

        var publicCommitment = CommitmentTool.FromJson<PublicCommitment>(await File.ReadAllTextAsync(publicPath, cancellationToken).ConfigureAwait(false));
        var proof = CommitmentTool.FromJson<InclusionProofFile>(await File.ReadAllTextAsync(proofPath, cancellationToken).ConfigureAwait(false));
        var ok = CommitmentTool.Verify(publicCommitment, proof);

        if (ok && Arg(args, "record") is { } record)
        {
            // Mentör maddesi 10: Kanıtı sorgulayan kişi adı farklı yazsa da (büyük harf, www.) aynı normalleştirme uygulanır.
            ok = string.Equals(NormalizeRecord(new PublicSuffixDomainNormalizer(), record), proof.Record, StringComparison.Ordinal);
        }

        if (ok && Arg(args, "previous") is { } previousPath)
        {
            // E5: zincir bağı. Önceki dosya değiştirilmişse özet tutmaz.
            ok = CommitmentTool.VerifyPrevious(publicCommitment, await File.ReadAllBytesAsync(previousPath, cancellationToken).ConfigureAwait(false));
        }

        await output.WriteLineAsync(ok
            ? string.Create(CultureInfo.InvariantCulture, $"Doğrulandı: kayıt ağaçta (indeks {proof.Index} / {publicCommitment.Count}).")
            : "Doğrulanamadı: kanıt, kayıt ya da önceki taahhüt bağı tutmuyor.").ConfigureAwait(false);
        return ok ? 0 : 2;
    }

    /// <summary>"--ad değer" çiftlerini sözlüğe çevirir.</summary>
    public static IReadOnlyDictionary<string, string?> ParseArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                map[args[i][2..]] = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            }
        }

        return map;
    }

    /// <summary>Taahhüt kaydı biçimi: kayıtlı alan adı, küçük harf, ASCII (punycode). Geçersizse <c>null</c>.</summary>
    public static string? NormalizeRecord(IDomainNameNormalizer normalizer, string value)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        return normalizer.Normalize(value).Value?.RegistrableDomain;
    }

    /// <summary>
    /// Kaynak ağacı denetimi (E1, E9, E11). Hepsi geçerse commit kimliğini döner; geçmezse nedeni yazar ve <c>null</c> döner.
    /// Hem ana ağaçta (geçici worktree açılmadan önce) hem geçici worktree'de (kilit alınmadan önce) çalışır.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><c>--commit</c> 40 karakterlik git kimliği ve <c>git rev-parse HEAD</c> ile aynı.</item>
    /// <item>Çalışma ağacı temiz: <c>git status --porcelain</c> boş, <c>ls-files -v</c> içinde h/S işareti yok.</item>
    /// <item>src/ ve tests/ altında git'in yok saydığı dosya yok (bin/ ve obj/ hariç): Yok sayılan .cs de derlenir (E11).</item>
    /// <item>Yapılandırma dosyası depoda izleniyor: Özeti, dondurulmuş commit'teki dosyanın özetidir.</item>
    /// </list>
    /// </remarks>
    public static async Task<string?> CheckSourceStateAsync(RepoContext repo, string configPath, string? commit, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(output);
        string? problem = null;
        if (commit is null || !LockedRunGuard.IsFullCommitId(commit))
        {
            problem = "--commit 40 karakterlik git kimliği olmalı (git rev-parse HEAD).";
        }
        else if (!string.Equals(repo.HeadCommit(), commit, StringComparison.Ordinal))
        {
            problem = "--commit, git rev-parse HEAD ile aynı değil.";
        }
        else if (!repo.IsClean())
        {
            problem = "Çalışma ağacı temiz değil (git status). Kilitli test yalnızca commit edilmiş kodla çalışır.";
        }
        else if (repo.IgnoredSourcePaths() is not { Count: 0 } ignored)
        {
            problem = "src/ ya da tests/ altında git'in yok saydığı dosya var (bin/ ve obj/ dışında). Bu dosyalar derlenebilir; önce silin.";
        }
        else if (!repo.IsTracked(configPath))
        {
            problem = "Yapılandırma dosyası depoda izlenmiyor; dondurulmuş commit'in parçası olmalı.";
        }

        if (problem is null)
        {
            return commit;
        }

        await output.WriteLineAsync("Kilitli test başlamadı (kilit alınmadı): " + problem).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// İç kip denetimi: <see cref="CheckSourceStateAsync"/> ve ek olarak çalışan programın derlendiği commit HEAD ile aynı
    /// olmalı. İkilideki "+sha" ancak bu geçici, temiz worktree'den derlenmişse güvenilir sayılır; o yüzden iç kip yalnızca
    /// geçici worktree'de ve oradan derlenmiş programla çalışır (<see cref="LockedRunOrchestrator"/>).
    /// </summary>
    private static async Task<string?> CheckFrozenStateAsync(RepoContext repo, string configPath, string? commit, TextWriter output)
    {
        if (await CheckSourceStateAsync(repo, configPath, commit, output).ConfigureAwait(false) is not { } checkedCommit)
        {
            return null;
        }

        if (!string.Equals(RepoContext.BuildCommit(), checkedCommit, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Kilitli test başlamadı (kilit alınmadı): Çalışan programın derlendiği commit HEAD ile aynı değil ya da bilinmiyor.")
                .ConfigureAwait(false);
            return null;
        }

        return checkedCommit;
    }

    /// <summary>
    /// İleriye dönük test için taahhüt edilmiş alarm kümesi (mentör maddesi 1) ve DOĞRULANMIŞ taahhüt anı (E10).
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>Taahhüt anı açık dosyadaki <c>CreatedAt</c> DEĞİLDİR: O alan bir beyandır, geriye tarihlenebilir. Zaman, açık
    /// dosyanın SHA-256'sı için alınmış RFC 3161 cevabından (<c>--tsr</c>, varsayılan: açık dosya + ".tsr") okunur ve
    /// <see cref="TimestampVerifier"/> ile imza, özet ve sertifika zinciri (yapılandırmadaki TSA kökü) doğrulanır.</item>
    /// <item>Açıklama dosyasındaki her kaydın taahhüdü tuzundan yeniden hesaplanır, kök açık dosyadaki kökle karşılaştırılır.</item>
    /// </list>
    /// </remarks>
    private static async Task<(IReadOnlySet<string> Ids, DateTimeOffset Time)?> ReadCommittedAsync(
        IReadOnlyDictionary<string, string?> args, EvaluationConfig config, RepoContext repo, IReadOnlySet<string> pinnedTsaRoots,
        Func<string, (string Commit, DateTimeOffset Date)?> pinCommit, IDomainNameNormalizer normalizer, EvaluationDataLoader loader,
        TextWriter output, CancellationToken cancellationToken)
    {
        // E12: Kabul edilen TSA kökleri git'te izlenen tsa_roots.txt'te sabit. Liste boşsa ileriye dönük test hiç çalışmaz.
        if (pinnedTsaRoots.Count == 0)
        {
            await output.WriteLineAsync("İleriye dönük test çalışmadı: Sabitlenmiş TSA kökü yok (src/Fraud.Collectors/Evaluation/tsa_roots.txt boş).")
                .ConfigureAwait(false);
            return null;
        }

        if (Arg(args, "commitment") is not { } publicPath || Arg(args, "opening") is not { } openingPath)
        {
            await output.WriteLineAsync("İleriye dönük test için --commitment <açık dosya> ve --opening <açıklama dosyası> gerekli.").ConfigureAwait(false);
            return null;
        }

        var tsqPath = Arg(args, "tsq") ?? publicPath + ".tsq";
        var tsrPath = Arg(args, "tsr") ?? publicPath + ".tsr";
        var rootPath = Path.IsPathFullyQualified(config.TimestampAuthorityRootFile!)
            ? config.TimestampAuthorityRootFile!
            : Path.Combine(repo.Root, config.TimestampAuthorityRootFile!);
        if (!File.Exists(tsqPath) || !File.Exists(tsrPath) || !File.Exists(rootPath))
        {
            await output.WriteLineAsync("İleriye dönük test çalışmadı: .tsq, .tsr ya da TSA kök sertifikası yok. Zaman damgası doğrulanmadan sonuç basılmaz.")
                .ConfigureAwait(false);
            return null;
        }

        var publicBytes = await File.ReadAllBytesAsync(publicPath, cancellationToken).ConfigureAwait(false);
        var verified = TimestampVerifier.Verify(
            publicBytes,
            await File.ReadAllBytesAsync(tsqPath, cancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(tsrPath, cancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(rootPath, cancellationToken).ConfigureAwait(false),
            pinnedTsaRoots);
        if (verified.Time is not { } time)
        {
            await output.WriteLineAsync("İleriye dönük test çalışmadı: zaman damgası doğrulanamadı (" + verified.Problem + ").").ConfigureAwait(false);
            return null;
        }

        // E13: Kök, damgadan ÖNCE sabitlenmiş olmalı. Sonradan eklenen bir kök (sonucu gördükten sonra seçilmiş) kabul edilmez.
        if (pinCommit(verified.RootFingerprint!) is not { } pin || pin.Date >= time)
        {
            await output.WriteLineAsync("İleriye dönük test çalışmadı: TSA kökünün tsa_roots.txt'e eklendiği commit bulunamadı ya da damga zamanından önce değil.")
                .ConfigureAwait(false);
            return null;
        }

        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"TSA kökü {pin.Commit} commit'inde ({pin.Date:u}) sabitlenmiş; damgadan önce.")).ConfigureAwait(false);

        var publicCommitment = CommitmentTool.ParsePublic(publicBytes);
        var opening = CommitmentTool.FromJson<CommitmentOpening>(await File.ReadAllTextAsync(openingPath, cancellationToken).ConfigureAwait(false));
        if (publicCommitment is null || !CommitmentTool.VerifyOpening(publicCommitment, opening))
        {
            await output.WriteLineAsync("Açıklama dosyası açık taahhütle tutmuyor; ileriye dönük test çalışmadı.").ConfigureAwait(false);
            return null;
        }

        var ids = opening.Entries
            .Select(e => NormalizeRecord(normalizer, e.Record))
            .Where(r => r is not null)
            .Select(r => loader.SubjectIdOf(r!))
            .ToHashSet(StringComparer.Ordinal);
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Taahhüt: kayıt={publicCommitment.Count}, doğrulanmış zaman damgası (genTime)={time:u}, TSA kökü SHA-256={verified.RootFingerprint}."))
            .ConfigureAwait(false);
        return (ids, time);
    }

    /// <summary>
    /// Dış çapa (E8): Kilit dosyası yereldir; depo başka bir klasöre klonlanınca kilit görünmez. Kilit alınır alınmaz
    /// kaydın özeti ve onu commit edip damgalamak için gereken komutlar yazdırılır; damga isteği (.tsq) da yazılır.
    /// </summary>
    private static async Task WriteAnchorInstructionsAsync(string lockPath, TextWriter output, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(lockPath, cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var nonce = Math.Max(BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & long.MaxValue, 1);
        var tsq = lockPath + "." + digest[..12] + ".tsq";
        if (!File.Exists(tsq))
        {
            await WriteNewFileAsync(tsq, CommitmentTool.TimestampRequest(bytes, nonce), cancellationToken).ConfigureAwait(false);
        }

        const string Quote = "\"";
        await output.WriteLineAsync("ZORUNLU ADIM (dış çapa): Kilit kaydının SHA-256 özeti " + digest + ". Şimdi çalıştırın:").ConfigureAwait(false);
        await output.WriteLineAsync("  echo " + Quote + "locked-run " + digest + Quote + " >> docs/LOCKED_RUNS.txt && git add docs/LOCKED_RUNS.txt && git commit -m "
            + Quote + "Kilitli test kaydı" + Quote).ConfigureAwait(false);
        await output.WriteLineAsync("  curl -sS -H " + Quote + "Content-Type: application/timestamp-query" + Quote + " --data-binary @" + Quote + tsq + Quote
            + " https://freetsa.org/tsr -o " + Quote + tsq[..^4] + ".tsr" + Quote).ConfigureAwait(false);
        await output.WriteLineAsync("Bu adım yapılmazsa kilitli testin bir kez çalıştığı dışarıdan kanıtlanamaz (README: Kilitli test).").ConfigureAwait(false);
    }

    /// <summary>
    /// Yeni dosya yazar, var olanın üzerine yazmaz (E3): Önce aynı klasörde geçici bir dosya <c>FileMode.CreateNew</c> ile
    /// yazılır, sonra hedef ada taşınır. Taşıma aynı disk içinde atomiktir; hedef varsa istisna verir.
    /// </summary>
    private static async Task WriteNewFileAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)) + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }

    /// <summary>Boyut sınırlı okuma (E7): Dosya sınırdan büyükse okunmaz.</summary>
    private static async Task<byte[]> ReadLimitedAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > maxBytes)
        {
            throw new InvalidOperationException("Dosya " + maxBytes + " bayttan büyük.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryParseMode(string? text, out EvaluationMode mode)
    {
        mode = default;
        return text is "Historical" or "Locked" or "Prospective" && Enum.TryParse(text, out mode);
    }

    private static string? Arg(IReadOnlyDictionary<string, string?> args, string name) =>
        args.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool IsSafeName(string name) => name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
