using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Evaluation;
using Fraud.Collectors.Http;
using Fraud.Domain.Control;
using Fraud.Domain.DomainNames;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using Fraud.Domain.Signals;

namespace Fraud.Collectors.Control;

/// <summary>Kontrol listesi komutlarının bağımlılıkları. Testler sahte kaynaklarla kurar; ağ yok.</summary>
public sealed class ControlContext
{
    public required FraudDbContext Db { get; init; }

    /// <summary>Tam Public Suffix List (özel bölüm dahil).</summary>
    public required IDomainNameNormalizer Full { get; init; }

    /// <summary>Yalnızca ICANN bölümü (E06).</summary>
    public required IDomainNameNormalizer IcannOnly { get; init; }

    public required RepoContext Repo { get; init; }

    public required TextWriter Output { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>crt.sh'ten ilk sertifika (G3, G4b). Üretimde <see cref="ControlCommands.CrtShFirstCertificate"/>.</summary>
    public Func<string, CancellationToken, Task<FirstCertificateLookup>>? FirstCertificate { get; init; }

    public IRadarClient? Radar { get; init; }

    public ITrancoClient? Tranco { get; init; }

    /// <summary>
    /// Sabitlenmiş kaynaklar (C1). Boşsa docs/evaluation/control_sources.json git denetimleriyle okunur. Yalnızca testler verir.
    /// </summary>
    public ControlSources? PinnedSources { get; init; }

    /// <summary>Donmuş G2 sorgu dosyasının baytları. Boşsa docs/evaluation/g2_wikidata_query.rq git denetimleriyle okunur. Yalnızca testler verir.</summary>
    public byte[]? PinnedG2Query { get; init; }

    /// <summary>Gizli çıktı klasörü: depo kökünde data/private/control.</summary>
    public string ControlDirectory => Path.GetDirectoryName(Repo.PrivatePath("data/private/control/.liste"))!;
}

/// <summary>
/// Kontrol listesi kurma komutları (KONTROL_LISTESI_KURULUM.md 4). Ekrana YALNIZCA sayılar yazılır (Kural 4): alan adı,
/// kurum adı, token yok. Listeler data/private/control altına ve control_members tablosuna gider.
/// </summary>
/// <remarks>
/// <para>Ortak kurallar:</para>
/// <list type="bullet">
/// <item>Tohum gruba ve haftaya (ya da liste kimliğine) göre sabit kuralla üretilir; komut satırından verilemez.</item>
/// <item>Sıra <c>SHA-256(tohum|kayıtlı_alan_adı)</c> artan; ilk k alınır.</item>
/// <item>Elemeler: E00 normalleştirilemedi, E06 PSL özel bölüm, E01 pozitif, E02 başka grupta (önceki kurmalar), E10/E20 kaynak.</item>
/// <item>İdempotent: (grup, hafta) ikinci kez kurulamaz (control_builds tekil indeksi).</item>
/// <item>Seçilen her liste <c>commit-create</c> ile taahhüt edilir; açık dosya docs/evaluation/commitments altına gider.</item>
/// </list>
/// </remarks>
public static class ControlCommands
{
    /// <summary>G2 kategori kümesi (Değişiklik kaydı 2026-11-20, madde 6; kurulum belgesi 2.3).</summary>
    public static readonly IReadOnlySet<string> G2Categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Economy & Finance", "Personal Finance", "News & Media", "Educational Institutions",
    };

    public const string RadarTokenFile = "deploy/linux/secrets/cloudflare_radar_token";
    public const string PublicCommitmentDirectory = "docs/evaluation/commitments";
    public const int WeeklyQuota = 60;
    public const int G2Size = 300;
    public const int G4aSize = 300;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    // ---- G1 ----

    /// <summary>
    /// <c>control-fetch-g1</c>: BDDK banka listesi sayfasını TEK istekle indirir ve data/private/control/src altına yazar.
    /// Yalnızca www.bddk.org.tr'ye bağlanılır (bağlantı anında izin listesi), yönlendirme izlenmez, boyut sınırlı.
    /// </summary>
    public static async Task<int> FetchG1Async(ControlContext ctx, Func<CancellationToken, Task<HttpFetchResult>> fetch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(fetch);
        var result = await fetch(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Body is null)
        {
            await ctx.Output.WriteLineAsync("BDDK sayfası indirilemedi: " + result.ErrorKind + " (HTTP " + (result.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-") + ").")
                .ConfigureAwait(false);
            return 2;
        }

        var path = Path.Combine(ctx.ControlDirectory, "src", "bddk-77-" + ctx.Time.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".html");
        await WriteNewAsync(path, result.Body, cancellationToken).ConfigureAwait(false);
        await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"BDDK sayfası kaydedildi: {result.Body.Length} bayt, SHA-256 {Sha(result.Body)}. Sonraki adım: control-build-g1 --input <dosya>."))
            .ConfigureAwait(false);
        return 0;
    }

    /// <summary><c>control-build-g1 --input &lt;html&gt;</c>: Ağ yok. Sayfadaki bütün banka adresleri (örnekleme yok, sayım).</summary>
    public static async Task<int> BuildG1Async(ControlContext ctx, string inputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (await RefuseIfBuiltAsync(ctx, ControlGroup.G1, ControlBuild.OneShot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var parsed = BddkBankListParser.Parse(Encoding.UTF8.GetString(bytes));
        var eligibility = await EligibilityAsync(ctx, cancellationToken).ConfigureAwait(false);
        var selected = new List<SelectedMember>();
        foreach (var url in parsed.Urls)
        {
            if (eligibility.Check(url) is { } name)
            {
                selected.Add(new SelectedMember(name, null));
            }
        }

        var counts = Merge(eligibility.Counts, new()
        {
            ["bagland_toplam"] = parsed.Anchors,
            ["ic_baglanti"] = parsed.SkippedInternal,
            ["adres_yazisi_degil"] = parsed.SkippedNotAddressText,
            ["web_adresi"] = parsed.Urls.Count,
        });
        var listId = Sha(bytes)[..16];
        return await FinishAsync(ctx, ControlGroup.G1, ControlBuild.OneShot, "kontrol-g1-" + listId, listId, selected, counts, cancellationToken)
            .ConfigureAwait(false);
    }

    // ---- G2 ----

    /// <summary>
    /// KULLANIMDAN KALKTI (Değişiklik kaydı 2026-11-21): Radar önkoşulu geçmedi, G2 Wikidata ile kurulur
    /// (<see cref="G2WikidataCommands"/>). Kod ve testleri kayıt için duruyor; komut satırı bu yola artık gelmez.
    /// <c>control-build-g2</c>: Liste kimliği yalnızca sabitlenmiş dosyadan (C1). Önce %80 önkoşul denemesi (ilk 1.000 sıradan
    /// tohumlu 100), geçerse ilk 100.000 sıradan tohumlu sırayla Radar'a sorup kategori kümesine uyan ilk 300.
    /// </summary>
    /// <remarks>
    /// Deneme kaydı TEK dosyadır (g2-onkosul.json) ve listeye bağlı değildir: Başka bir listeyle yeniden denenemez (C1).
    /// Kayıt ilk deneme sorgusundan ÖNCE "başladı" olarak yazılır (C2): Yarıda kalan ya da hatalı biten deneme de
    /// "kullanıldı" sayılır, kilitli testteki gibi. Token ya da izin hatası denemeyi yakmasın diye deneme dışı tek bir ön
    /// sorgu yapılır (<see cref="PreflightDomain"/>); o başarısızsa hiçbir kayıt yazılmaz.
    /// </remarks>
    public static async Task<int> BuildG2Async(ControlContext ctx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Radar is null)
        {
            await ctx.Output.WriteLineAsync("Radar token'ı yok: " + RadarTokenFile + " dosyasını oluşturun (izin 600). Komut durdu.").ConfigureAwait(false);
            return 64;
        }

        if (await PinnedListIdAsync(ctx, cancellationToken).ConfigureAwait(false) is not { } listId
            || await RefuseIfBuiltAsync(ctx, ControlGroup.G2, ControlBuild.OneShot, cancellationToken).ConfigureAwait(false)
            || await TrancoAsync(ctx, listId, cancellationToken).ConfigureAwait(false) is not { } tranco)
        {
            return 2;
        }

        // 1) Önkoşul denemesi (kurulum belgesi 2.4).
        var trialFile = Path.Combine(ctx.ControlDirectory, "g2-onkosul.json");
        if (File.Exists(trialFile))
        {
            var previous = JsonSerializer.Deserialize<G2TrialRecord>(await File.ReadAllTextAsync(trialFile, cancellationToken).ConfigureAwait(false), Json);
            if (previous is not { Status: TrialStatus.Passed })
            {
                await ctx.Output.WriteLineAsync("Önkoşul denemesi daha önce kullanıldı (durum: " + (previous?.Status.ToString() ?? "okunamadı")
                    + "). Karar 6: B seçeneğine (Wikidata) geçilir.").ConfigureAwait(false);
                return 2;
            }
        }
        else
        {
            if ((await ctx.Radar.CategoriesAsync(PreflightDomain, cancellationToken).ConfigureAwait(false)).IsError)
            {
                await ctx.Output.WriteLineAsync("Radar ön sorgusu başarısız (token, izin ya da ağ). Deneme başlamadı, kayıt yazılmadı.").ConfigureAwait(false);
                return 2;
            }

            // Kayıt ilk sorgudan ÖNCE: Ctrl+C ya da hata denemeyi silmenin yolu olmasın (C2).
            await WriteNewAsync(trialFile, JsonSerializer.SerializeToUtf8Bytes(new G2TrialRecord(TrialStatus.Started, listId, ctx.Time.GetUtcNow(), null), Json),
                cancellationToken).ConfigureAwait(false);
            var trial = await G2TrialAsync(ctx, tranco.Top(1000), "kontrol-g2-onkosul-" + listId, cancellationToken).ConfigureAwait(false);
            var status = trial.Errors > 0 ? TrialStatus.Errored : trial.Passed ? TrialStatus.Passed : TrialStatus.Failed;
            await File.WriteAllBytesAsync(trialFile, JsonSerializer.SerializeToUtf8Bytes(new G2TrialRecord(status, listId, ctx.Time.GetUtcNow(), trial), Json),
                cancellationToken).ConfigureAwait(false);

            // Sonuç yalnızca deneme bitince basılır.
            await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"Önkoşul denemesi: sorulan={trial.Asked}, kategorili={trial.WithCategories}, kategorisiz={trial.Empty}, hata={trial.Errors}, durum={status}."))
                .ConfigureAwait(false);
            if (status != TrialStatus.Passed)
            {
                await ctx.Output.WriteLineAsync("Önkoşul geçmedi ya da hatalı bitti; deneme kullanıldı. Karar 6: B seçeneğine geçilir.").ConfigureAwait(false);
                return 2;
            }
        }

        // 2) Seçim.
        var seed = ControlSeeds.For("g2", listId);
        var eligibility = await EligibilityAsync(ctx, cancellationToken).ConfigureAwait(false);
        var candidates = ControlSeeds.Order(tranco.Top(100_000).Select(eligibility.Check).OfType<string>(), seed);
        var selected = new List<SelectedMember>();
        int outside = 0, empty = 0, errors = 0;
        foreach (var name in candidates)
        {
            if (selected.Count == G2Size)
            {
                break;
            }

            var lookup = await ctx.Radar.CategoriesAsync(name, cancellationToken).ConfigureAwait(false);
            if (lookup.IsError)
            {
                errors++;
                if (errors >= 20 && selected.Count == 0)
                {
                    await ctx.Output.WriteLineAsync("Radar art arda hata veriyor (token ya da izin). Hiçbir şey yazılmadı.").ConfigureAwait(false);
                    return 2;
                }

                continue;
            }

            if (lookup.Categories!.Count == 0)
            {
                empty++;
            }
            else if (lookup.Categories.Any(G2Categories.Contains))
            {
                selected.Add(new SelectedMember(name, null));
            }
            else
            {
                outside++;
            }
        }

        var counts = Merge(eligibility.Counts, new()
        {
            [EliminationCodes.E20] = errors, ["kategori_yok"] = empty, ["kategori_kume_disi"] = outside, ["tranco_bozuk_satir"] = tranco.Malformed,
        });
        if (selected.Count < 100)
        {
            await ctx.Output.WriteLineAsync("Not: 100'den az üye; raporda 'yeterli örnek yok' yazılır, oran verilmez.").ConfigureAwait(false);
        }

        return await FinishAsync(ctx, ControlGroup.G2, ControlBuild.OneShot, seed, listId, selected, counts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deneme ön sorgusunun alan adı: herkese açık, kontrol listesinde olmayan bir ad (token ve izin denetimi).</summary>
    public const string PreflightDomain = "cloudflare.com";

    /// <summary>Bir önkoşul denemesinin durumu. "Başladı" kalan kayıt yarıda kesilmiş bir denemedir ve kullanılmış sayılır.</summary>
    public enum TrialStatus
    {
        Started = 1,
        Passed = 2,
        Failed = 3,
        Errored = 4,
    }

    /// <summary>G2 deneme kaydı (tek dosya). Alan adı yok.</summary>
    public sealed record G2TrialRecord(TrialStatus Status, string ListId, DateTimeOffset At, G2Trial? Result);

    /// <summary>G2 önkoşul denemesinin sayıları. Alan adı yok.</summary>
    public sealed record G2Trial(int Asked, int WithCategories, int Empty, int Errors)
    {
        public bool Passed => Errors == 0 && WithCategories >= 80;
    }

    private static async Task<G2Trial> G2TrialAsync(ControlContext ctx, IReadOnlyList<string> top1000, string seed, CancellationToken cancellationToken)
    {
        var names = ControlSeeds.Order(top1000.Select(d => ctx.Full.Normalize(d).Value?.RegistrableDomain).OfType<string>(), seed).Take(100);
        int asked = 0, with = 0, empty = 0, errors = 0;
        foreach (var name in names)
        {
            asked++;
            var lookup = await ctx.Radar!.CategoriesAsync(name, cancellationToken).ConfigureAwait(false);
            if (lookup.IsError)
            {
                errors++;
            }
            else if (lookup.Categories!.Count > 0)
            {
                with++;
            }
            else
            {
                empty++;
            }
        }

        return new G2Trial(asked, with, empty, errors);
    }

    // ---- G4a ----

    /// <summary><c>control-build-g4a</c>: Sabitlenmiş listenin tamamından, elemelerden sonra tohumlu ilk 300.</summary>
    public static async Task<int> BuildG4aAsync(ControlContext ctx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (await PinnedListIdAsync(ctx, cancellationToken).ConfigureAwait(false) is not { } listId
            || await RefuseIfBuiltAsync(ctx, ControlGroup.G4a, ControlBuild.OneShot, cancellationToken).ConfigureAwait(false)
            || await TrancoAsync(ctx, listId, cancellationToken).ConfigureAwait(false) is not { } tranco)
        {
            return 2;
        }

        var seed = ControlSeeds.For("g4a", listId);
        var eligibility = await EligibilityAsync(ctx, cancellationToken).ConfigureAwait(false);
        var selected = ControlSeeds.Order(tranco.Domains.Select(eligibility.Check).OfType<string>(), seed)
            .Take(G4aSize)
            .Select(n => new SelectedMember(n, null))
            .ToList();
        var counts = Merge(eligibility.Counts, new() { ["tranco_bozuk_satir"] = tranco.Malformed });
        return await FinishAsync(ctx, ControlGroup.G4a, ControlBuild.OneShot, seed, listId, selected, counts, cancellationToken).ConfigureAwait(false);
    }

    // ---- G3 ve G4b (haftalık) ----

    /// <summary>
    /// <c>control-build-g3 --week yyyy-Www</c> ve <c>control-build-g4b --week yyyy-Www</c>. Hafta, <c>first_logged_at</c>'e göre
    /// (UTC, ISO). Hafta bitmeden kurulmaz.
    /// </summary>
    /// <remarks>
    /// <para>G3: Çerçeve N (ct_neutral_sample) ve Çerçeve K (ct_candidates) ayrı ayrı; her birinde donmuş sözlükle eşleşen,
    /// "yeni" (ilk sertifika CT'de ilk görülmeden en çok 28 gün önce) adlardan tohumlu sırayla en çok 60.</para>
    /// <para>G4b: Çerçeve N; yaş sınıfı kotası 60 × (%23 / %51 / %26), en büyük kalan yöntemiyle 14 / 31 / 15.</para>
    /// <para>Yaş için aday başına bir kez crt.sh (15 sn aralık). Hata "yeni değil" sayılmaz, ayrı sayılır (E20). Kota dolunca
    /// sorgu durur. <paramref name="maxQueries"/> süreyi sınırlar; dolarsa sayı rapora yazılır, hafta yeniden kurulamaz.</para>
    /// </remarks>
    public static async Task<int> BuildWeeklyAsync(
        ControlContext ctx, ControlGroup group, string week, int maxQueries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (group is not (ControlGroup.G3 or ControlGroup.G4b) || !IsoWeek.TryParse(week, out _, out _))
        {
            await ctx.Output.WriteLineAsync("Kullanım: control-build-g3|control-build-g4b --week yyyy-Www").ConfigureAwait(false);
            return 64;
        }

        var (start, end) = IsoWeek.Range(week);
        if (end > ctx.Time.GetUtcNow())
        {
            await ctx.Output.WriteLineAsync("Hafta henüz bitmedi; seçim hafta bittikten sonra bir kez yapılır.").ConfigureAwait(false);
            return 2;
        }

        if (ctx.FirstCertificate is null)
        {
            throw new InvalidOperationException("crt.sh istemcisi yok.");
        }

        if (await RefuseIfBuiltAsync(ctx, group, week, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        FinanceLexicon? dictionary = null;
        if (group == ControlGroup.G3)
        {
            dictionary = FinanceLexicon.Load(await File.ReadAllBytesAsync(Path.Combine(ctx.Repo.Root, FinanceLexicon.RelativePath), cancellationToken)
                .ConfigureAwait(false));
        }

        var store = new ControlStore(ctx.Db, ctx.Full);
        var positives = await store.PositiveRegistrablesAsync(cancellationToken).ConfigureAwait(false);
        var members = await store.ExistingMembersAsync(cancellationToken).ConfigureAwait(false);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var selected = new List<SelectedMember>();
        var queries = 0;
        var frames = group == ControlGroup.G3 ? new[] { SamplingFrame.N, SamplingFrame.K } : [SamplingFrame.N];
        foreach (var frame in frames)
        {
            var pool = frame == SamplingFrame.N
                ? await NeutralPoolAsync(ctx.Db, start, end, cancellationToken).ConfigureAwait(false)
                : await CandidatePoolAsync(ctx.Db, ctx.Full, start, end, cancellationToken).ConfigureAwait(false);
            counts["havuz_" + frame] = pool.Count;

            var seed = ControlSeeds.For(group == ControlGroup.G3 ? "g3" + frame.ToString().ToLowerInvariant() : "g4b", week);
            var firstLogged = pool.ToDictionary(p => p.Name, p => p.FirstLoggedAt, StringComparer.Ordinal);
            var subsets = new Dictionary<string, string>(StringComparer.Ordinal);
            // Çerçeve başına ayrı süzgeç: Önceki çerçevede SEÇİLEN ad bu çerçevede E02 ile elenir; seçilmeyen ad elenmez.
            var taken = members.Concat(selected.Select(m => m.RegistrableDomain)).ToHashSet(StringComparer.Ordinal);
            var eligibility = new ControlEligibility(ctx.Full, ctx.IcannOnly, positives, taken);
            var eligible = new List<string>();
            foreach (var p in pool)
            {
                if (eligibility.Check(p.Name) is not { } name)
                {
                    continue;
                }

                if (dictionary is not null)
                {
                    if (dictionary.Match(name) is not { } match)
                    {
                        counts["sozlukte_yok_" + frame] = counts.GetValueOrDefault("sozlukte_yok_" + frame) + 1;
                        continue;
                    }

                    subsets[name] = match.Subset;
                }

                eligible.Add(name);
            }

            // G3: yalnızca "yeni" (≤ 28 gün), en çok 60. G4b: yaş sınıfı kotası.
            var quotas = group == ControlGroup.G3 ? new[] { WeeklyQuota, 0, 0 } : QuotaRounding.LargestRemainder(WeeklyQuota, AgeClasses.PositiveShares);
            var filled = new int[3];
            foreach (var name in ControlSeeds.Order(eligible, seed))
            {
                if (filled.SequenceEqual(quotas))
                {
                    break;
                }

                if (queries >= maxQueries)
                {
                    counts["sorgu_siniri_doldu"] = 1;
                    break;
                }

                queries++;
                var lookup = await ctx.FirstCertificate(name, cancellationToken).ConfigureAwait(false);
                if (lookup.Failed)
                {
                    counts[EliminationCodes.E20] = counts.GetValueOrDefault(EliminationCodes.E20) + 1;
                    continue;
                }

                if (lookup.FirstNotBefore is not { } firstCert)
                {
                    counts["crtsh_kaydi_yok"] = counts.GetValueOrDefault("crtsh_kaydi_yok") + 1;
                    continue;
                }

                var age = (int)AgeClasses.Of(firstLogged[name], firstCert);
                if (filled[age] >= quotas[age])
                {
                    var reason = group == ControlGroup.G3 ? "yeni_degil" : "kota_dolu_" + (AgeClass)age;
                    counts[reason] = counts.GetValueOrDefault(reason) + 1;
                    continue;
                }

                filled[age]++;
                selected.Add(new SelectedMember(name, frame, firstLogged[name], firstCert, subsets.GetValueOrDefault(name)));
            }

            counts["secilen_" + frame] = filled.Sum();
            foreach (var (code, count) in eligibility.Counts)
            {
                counts[code + "_" + frame] = count;
            }
        }

        counts["crtsh_sorgu"] = queries;
        var buildSeed = ControlSeeds.For(group == ControlGroup.G3 ? "g3" : "g4b", week);
        return await FinishAsync(ctx, group, week, buildSeed, null, selected, counts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Çerçeve N havuzu: hafta içinde ilk loglanan tarafsız örnek adları.</summary>
    public static async Task<List<(string Name, DateTimeOffset FirstLoggedAt)>> NeutralPoolAsync(
        FraudDbContext db, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.CtNeutralSamples
            .Where(s => s.FirstLoggedAt >= start && s.FirstLoggedAt < end)
            .Select(s => new { s.RegistrableDomain, s.FirstLoggedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => (r.RegistrableDomain, r.FirstLoggedAt))];
    }

    /// <summary>
    /// Çerçeve K havuzu: ct_candidates kayıtlarının SAN adları kayıtlı alan adına indirgenir. Ad, İLK kez bu hafta
    /// loglandıysa havuzdadır (daha önceki bir kayıtta görülen ad bu haftaya girmez).
    /// </summary>
    public static async Task<List<(string Name, DateTimeOffset FirstLoggedAt)>> CandidatePoolAsync(
        FraudDbContext db, IDomainNameNormalizer normalizer, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(normalizer);
        var rows = await db.CtCandidates
            .Where(c => c.LoggedAt < end)
            .Select(c => new { c.LoggedAt, c.SanNames })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var first = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var san in row.SanNames)
            {
                var host = san.StartsWith("*.", StringComparison.Ordinal) ? san[2..] : san;
                if (normalizer.Normalize(host).Value?.RegistrableDomain is { } name
                    && (!first.TryGetValue(name, out var seen) || row.LoggedAt < seen))
                {
                    first[name] = row.LoggedAt;
                }
            }
        }

        return [.. first.Where(kv => kv.Value >= start).Select(kv => (kv.Key, kv.Value))];
    }

    /// <summary>Üretimdeki ilk sertifika sorgusu: crt.sh <c>%.ad</c>, en küçük not_before. Hata ayrı döner.</summary>
    public static Func<string, CancellationToken, Task<FirstCertificateLookup>> CrtShFirstCertificate(ICrtShClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return async (name, token) =>
        {
            var result = await client.SearchAsync(name, token).ConfigureAwait(false);
            return result.Outcome switch
            {
                Fraud.Domain.Collection.CollectionOutcome.Success => new FirstCertificateLookup(result.Items.Min(c => c.NotBefore), false),
                Fraud.Domain.Collection.CollectionOutcome.Empty => new FirstCertificateLookup(null, false),
                _ => new FirstCertificateLookup(null, true),
            };
        };
    }

    // ---- ortak ----

    internal static async Task<bool> RefuseIfBuiltAsync(ControlContext ctx, ControlGroup group, string weekKey, CancellationToken cancellationToken)
    {
        if (!await new ControlStore(ctx.Db, ctx.Full).IsBuiltAsync(group, weekKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await ctx.Output.WriteLineAsync(group + " (" + weekKey + ") daha önce kuruldu; ikinci kez kurulmaz.").ConfigureAwait(false);
        return true;
    }

    internal static async Task<ControlEligibility> EligibilityAsync(ControlContext ctx, CancellationToken cancellationToken)
    {
        var store = new ControlStore(ctx.Db, ctx.Full);
        return new ControlEligibility(
            ctx.Full, ctx.IcannOnly,
            await store.PositiveRegistrablesAsync(cancellationToken).ConfigureAwait(false),
            await store.ExistingMembersAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Sabitlenmiş Tranco liste kimliği (C1): test verdiyse o, değilse git denetimli dosya.</summary>
    private static async Task<string?> PinnedListIdAsync(ControlContext ctx, CancellationToken cancellationToken)
    {
        var sources = ctx.PinnedSources ?? await ControlSourcesFile.LoadAsync(ctx.Repo, ctx.Output, cancellationToken).ConfigureAwait(false);
        if (sources is not null)
        {
            await ctx.Output.WriteLineAsync("Sabitlenmiş Tranco listesi: " + sources.TrancoListId + " (" + sources.TrancoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ").")
                .ConfigureAwait(false);
        }

        return sources?.TrancoListId;
    }

    /// <summary>Önbellekli Tranco listesi: data/private/control/src/tranco-&lt;kimlik&gt;.csv yoksa bir kez indirilir.</summary>
    private static async Task<TrancoList?> TrancoAsync(ControlContext ctx, string listId, CancellationToken cancellationToken)
    {
        if (!TrancoApiParser.IsListId(listId))
        {
            await ctx.Output.WriteLineAsync("Sabitlenmiş Tranco liste kimliği geçerli değil.").ConfigureAwait(false);
            return null;
        }

        var path = Path.Combine(ctx.ControlDirectory, "src", "tranco-" + listId + ".csv");
        if (!File.Exists(path))
        {
            if (ctx.Tranco is null || await ctx.Tranco.ListInfoAsync(listId, cancellationToken).ConfigureAwait(false) is not { Available: true }
                || await ctx.Tranco.DownloadAsync(listId, cancellationToken).ConfigureAwait(false) is not { } body)
            {
                await ctx.Output.WriteLineAsync("Tranco listesi alınamadı (kimlik, ağ ya da liste hazır değil).").ConfigureAwait(false);
                return null;
            }

            await WriteNewAsync(path, body, cancellationToken).ConfigureAwait(false);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = new StringReader(Encoding.UTF8.GetString(bytes));
        var (domains, malformed) = TrancoListParser.Parse(reader, 1_000_000);
        await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Tranco {listId}: satır={domains.Count}, bozuk={malformed}, SHA-256 {Sha(bytes)}.")).ConfigureAwait(false);
        return new TrancoList(domains, malformed);
    }

    private sealed record TrancoList(IReadOnlyList<string> Domains, int Malformed)
    {
        public IReadOnlyList<string> Top(int n) => [.. Domains.Take(n)];
    }

    /// <summary>
    /// Seçimi bitirir: liste dosyası ve taahhüt (seçim boş değilse), sonra üyeler ve kurma kaydı tek işlemde. Ekrana sayılar.
    /// </summary>
    /// <param name="extra">Manifeste eklenecek alan adsız bilgiler (ör. sorgu ve cevap SHA-256'sı).</param>
    internal static async Task<int> FinishAsync(
        ControlContext ctx, ControlGroup group, string weekKey, string seed, string? listId, List<SelectedMember> selected,
        IReadOnlyDictionary<string, int> counts, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? extra = null)
    {
        var name = "kontrol-" + group.ToString().ToLowerInvariant() + (weekKey == ControlBuild.OneShot ? string.Empty : "-" + weekKey.ToLowerInvariant());
        string? commitmentId = null;
        if (selected.Count > 0)
        {
            var records = Path.Combine(ctx.ControlDirectory, name + ".txt");
            await WriteNewAsync(records, Encoding.UTF8.GetBytes(string.Join('\n', selected.Select(s => s.RegistrableDomain)) + "\n"), cancellationToken)
                .ConfigureAwait(false);
            var publicOut = Path.Combine(ctx.Repo.Root, PublicCommitmentDirectory, name + ".json");
            var code = await EvaluationCommands.CommitCreateAsync(
                EvaluationCommands.ParseArgs(["--records", records, "--name", name, "--public-out", publicOut]), ctx.Output, cancellationToken, ctx.Repo)
                .ConfigureAwait(false);
            if (code != 0)
            {
                await ctx.Output.WriteLineAsync("Taahhüt oluşturulamadı; üyeler kaydedilmedi.").ConfigureAwait(false);
                return 2;
            }

            commitmentId = name;
        }

        await new ControlStore(ctx.Db, ctx.Full)
            .SaveAsync(group, weekKey, seed, listId, commitmentId, selected, counts, ctx.Time.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        var manifest = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["group"] = group.ToString(), ["week"] = weekKey, ["seed"] = seed, ["source_list_id"] = listId, ["commitment"] = commitmentId,
            ["selected"] = selected.Count, ["counts"] = counts, ["built_at"] = ctx.Time.GetUtcNow(),
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            manifest[key] = value;
        }

        await WriteNewAsync(Path.Combine(ctx.ControlDirectory, name + ".manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Json), cancellationToken)
            .ConfigureAwait(false);

        await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{group} {weekKey}: seçilen={selected.Count}.")).ConfigureAwait(false);
        foreach (var (code, count) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"  {code}={count}")).ConfigureAwait(false);
        }

        return 0;
    }

    internal static Dictionary<string, int> Merge(IReadOnlyDictionary<string, int> a, Dictionary<string, int> b)
    {
        var result = new Dictionary<string, int>(a, StringComparer.Ordinal);
        foreach (var (k, v) in b)
        {
            result[k] = result.GetValueOrDefault(k) + v;
        }

        return result;
    }

    internal static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Var olan dosyanın üzerine yazmaz (CreateNew).</summary>
    internal static async Task WriteNewAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }
}
