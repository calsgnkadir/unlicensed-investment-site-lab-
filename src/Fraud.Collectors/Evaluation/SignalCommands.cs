using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fraud.Domain.Control;
using Fraud.Domain.Signals;
using Fraud.Evaluation;
using Fraud.Evaluation.Commitments;
using Fraud.Infrastructure.Persistence;
using Fraud.Signals;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Evaluation;

/// <summary>
/// Sinyal kaynaklarını diskten okur ve marka listesini üretir. Ekrana yalnızca sayı ve özet basılır, ad basılmaz (Kural 4).
/// </summary>
public static class SignalCommands
{
    /// <summary>Kaynaklar ve (marka sinyali kullanıldıysa) yerli listenin kurallı SHA-256'sı.</summary>
    public sealed record LoadedResources(SignalResources Resources, string? DomesticBrandsSha256);

    /// <summary>G1 taahhüdünün adı (ControlCommands.FinishAsync'in verdiği ad).</summary>
    public const string G1CommitmentName = "kontrol-g1";

    /// <summary>
    /// Seçilen sinyallerin gerektirdiği kaynakları okur. Sorun varsa nedeni yazar ve <c>null</c> döner (kilitten ÖNCE çağrılır,
    /// eksik dosya kilitli testin tek hakkını harcamasın).
    /// </summary>
    /// <remarks>
    /// S1: Geçmiş veri testinde yerli marka listesi <c>data/private/signals/brands.txt</c>'den okunur. Kilitli ve ileriye dönük
    /// kipte dosya HİÇ okunmaz: Liste, G1 taahhüdünün açıklamasından (doğrulanmış) yeniden üretilir ve kurallı özeti yapılandırmadaki
    /// <see cref="SignalConfig.DomesticBrandsSha256"/> ile karşılaştırılır. Böylece dondurma ile kilitli çalıştırma arasında listeye
    /// marka eklenemez: Taahhüt değişirse açık dosya değişir; liste değişirse özet tutmaz.
    /// </remarks>
    public static async Task<LoadedResources?> LoadResourcesAsync(
        SignalConfig config, EvaluationMode mode, FraudDbContext? db, RepoContext repo, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(output);

        FinanceLexicon? lexicon = null;
        if (SignalCatalog.NeedsLexicon(config))
        {
            var path = Path.Combine(repo.Root, FinanceLexicon.RelativePath);
            try
            {
                // Load donmuş SHA-256'yı denetler: sözlük değiştiyse sinyal çalışmaz.
                lexicon = FinanceLexicon.Load(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                await output.WriteLineAsync("Finans sözlüğü okunamadı ya da donmuş sürümle aynı değil; sinyal çalışmadı.").ConfigureAwait(false);
                return null;
            }
        }

        if (!SignalCatalog.NeedsBrands(config))
        {
            return new LoadedResources(new SignalResources(lexicon, null), null);
        }

        IReadOnlyList<string>? labels;
        if (mode == EvaluationMode.Historical)
        {
            var path = repo.PrivatePath(BrandList.DomesticRelativePath);
            if (!File.Exists(path))
            {
                await output.WriteLineAsync("Yerli marka listesi yok. Önce: signals-brands (G1 üyelerinden üretir).").ConfigureAwait(false);
                return null;
            }

            labels = [.. BrandList.ReadLines(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)))];
        }
        else
        {
            labels = await CommittedG1LabelsAsync(db ?? throw new ArgumentNullException(nameof(db)), repo, output, cancellationToken)
                .ConfigureAwait(false);
            if (labels is null)
            {
                return null;
            }
        }

        var sha = CanonicalSha256(labels);
        if (mode != EvaluationMode.Historical && !string.Equals(sha, config.DomesticBrandsSha256, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Yerli marka listesinin özeti dondurulmuş değerle aynı değil (signal.domesticBrandsSha256). Çalışma kilitten önce durdu.")
                .ConfigureAwait(false);
            return null;
        }

        var brands = BrandList.Create(labels);
        // Özet, listenin hangi sürümüyle çalışıldığını kayda geçirmek içindir. Ad basılmaz.
        var origin = mode == EvaluationMode.Historical ? "dosyadan" : "G1 taahhüdünden";
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Marka listesi: yerli={brands.DomesticCount}, yabancı={brands.ForeignCount}, yerli liste SHA-256={sha} ({origin})"))
            .ConfigureAwait(false);
        return new LoadedResources(new SignalResources(lexicon, brands), sha);
    }

    /// <summary>
    /// Kurallı özet: soldaki etiketler kırpılmış, tekrarsız, sıralı, her biri bir satır ve sonu yeni satır. Yorum ve boş satır
    /// sayılmaz; böylece dosya başlığı değişse de özet aynı kalır, tek bir etiket değişirse değişir.
    /// </summary>
    public static string CanonicalSha256(IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var text = string.Concat(labels.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(l => l + "\n"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// G1 taahhüdünden etiketler: açık dosya (repoda, dondurma commit'inde) ve açıklama dosyası (data/private) birlikte doğrulanır;
    /// kayıtlar control_members'daki taahhütlü G1 üyeleriyle birebir aynı olmalı. Sorun varsa nedeni yazar ve <c>null</c> döner.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> CommittedG1LabelsAsync(
        FraudDbContext db, RepoContext repo, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(output);
        var publicPath = Path.Combine(repo.Root, "docs", "evaluation", "commitments", G1CommitmentName + ".json");
        var openingPath = repo.PrivatePath(EvaluationCommands.CommitmentsDirectory + "/" + G1CommitmentName + ".opening.json");
        if (!File.Exists(publicPath) || !File.Exists(openingPath))
        {
            await output.WriteLineAsync("G1 taahhüdü (açık dosya ya da açıklama) bulunamadı; marka listesi üretilemedi.").ConfigureAwait(false);
            return null;
        }

        var publicCommitment = CommitmentTool.ParsePublic(await File.ReadAllBytesAsync(publicPath, cancellationToken).ConfigureAwait(false));
        CommitmentOpening? opening;
        try
        {
            opening = CommitmentTool.FromJson<CommitmentOpening>(await File.ReadAllTextAsync(openingPath, cancellationToken).ConfigureAwait(false));
        }
        catch (System.Text.Json.JsonException)
        {
            opening = null;
        }

        if (publicCommitment is null || opening is null || !CommitmentTool.VerifyOpening(publicCommitment, opening))
        {
            await output.WriteLineAsync("G1 taahhüdü doğrulanamadı; marka listesi üretilemedi.").ConfigureAwait(false);
            return null;
        }

        var committed = opening.Entries.Select(e => e.Record).ToHashSet(StringComparer.Ordinal);
        var members = (await db.ControlMembers
            .Where(m => m.Group == ControlGroup.G1 && m.CommitmentId == G1CommitmentName)
            .Select(m => m.Domain.RegistrableDomain)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        if (!committed.SetEquals(members))
        {
            await output.WriteLineAsync("G1 taahhüdündeki kayıtlar control_members'daki G1 üyeleriyle aynı değil; marka listesi üretilemedi.")
                .ConfigureAwait(false);
            return null;
        }

        return [.. committed.Select(LeftLabel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string LeftLabel(string registrable) => registrable.Split('.')[0];

    /// <summary>
    /// <c>signals-brands</c>: control_members tablosundaki G1 üyelerinin (BDDK bankaları) soldaki etiketlerini
    /// <c>data/private/signals/brands.txt</c> dosyasına yazar. Aynı veriyle iki kez çalışınca aynı dosyayı üretir (sıralı).
    /// </summary>
    public static async Task<int> BuildBrandsAsync(FraudDbContext db, RepoContext repo, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(output);

        var names = await db.ControlMembers
            .Where(m => m.Group == ControlGroup.G1)
            .Select(m => m.Domain.RegistrableDomain)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var labels = names.Select(LeftLabel).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (labels.Count == 0)
        {
            await output.WriteLineAsync("G1 üyesi yok; önce control-build-g1. Dosya yazılmadı.").ConfigureAwait(false);
            return 2;
        }

        var text = new StringBuilder()
            .AppendLine("# Yerli marka belirteçleri: control_members G1 (BDDK bankaları) soldaki etiketleri. GİZLİ, commit edilmez.")
            .AppendLine("# Üreten komut: signals-brands. Elle düzenlemeyin; G1 yeniden kurulursa komutu tekrar çalıştırın.");
        foreach (var label in labels)
        {
            text.AppendLine(label);
        }

        var path = repo.PrivatePath(BrandList.DomesticRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Geçici dosyaya yazıp yerine taşı: Yarıda kesilen yazım yarım bir liste bırakmasın.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, text.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);

        var kept = BrandList.Create(labels).DomesticCount;
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Marka listesi yazıldı: G1 üyesi={names.Count}, etiket={labels.Count}, {BrandList.MinTokenLength}+ karakterli belirteç={kept}, kurallı SHA-256={CanonicalSha256(labels)}. Dosya: {BrandList.DomesticRelativePath}"))
            .ConfigureAwait(false);
        return 0;
    }
}
