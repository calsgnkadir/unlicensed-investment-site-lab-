using System.Globalization;
using System.Text;
using Fraud.Domain.Signals;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Network;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Splitting;

namespace Fraud.Evaluation.Clustering;

/// <summary>Pozitif küme analizinin sonucu. Küme ve birim kimliği dışarıya yazılmaz; yalnızca sayılar.</summary>
/// <param name="ClusterOf">Birim → küme kimliği (kümedeki en küçük birim kimliği). Yalnızca bellekte ve testlerde.</param>
/// <param name="Sizes">Her kümenin pozitif sayısı.</param>
/// <param name="MultiBulletinClusters">En az iki farklı bültenden pozitif içeren küme sayısı.</param>
/// <param name="LinkedBy">Kenar türüne göre en az bir kenarı olan pozitif sayısı (yalnızca kullanılan kenarlar).</param>
/// <param name="HeldBackByCdn">Yalnızca CDN/bulut üzerinden bağlanacak olan, başka hiçbir ağ kenarı olmayan pozitif sayısı.</param>
/// <param name="MovedIfUsedInSplit">Bu kümeler bölmeye verilseydi bölmesi değişecek pozitif sayısı. Bölme DEĞİŞTİRİLMEDİ.</param>
public sealed record ClusterAnalysis(
    int Positives,
    IReadOnlyDictionary<string, string> ClusterOf,
    IReadOnlyList<int> Sizes,
    int MultiBulletinClusters,
    IReadOnlyDictionary<ClusterEdgeKind, int> LinkedBy,
    int HeldBackByCdn,
    int MovedIfUsedInSplit);

/// <summary>
/// Basit küme analizi (ROADMAP Hafta 8): SPK pozitiflerini ortak sertifika, ad gövdesi ve CDN dışı ortak ağ ile bağlar.
/// </summary>
/// <remarks>
/// <para><b>Kenar kuralları</b> (KONTROL_GRUBU_YONTEMI.md 7.5):</para>
/// <list type="number">
/// <item>Ortak sertifika: Aynı veriliş kimliği (yayıncı + seri, <c>c-…</c>). Ön sertifika ile son sertifika aynı kimliği taşır.</item>
/// <item>Ad gövdesi: Kayıtlı adın soldaki etiketi, Türkçe harfler katlanmış, rakamlar ve uçtaki tireler atılmış. En az
/// <see cref="MinStemLength"/> karakter. Benzerlik puanı DEĞİL, birebir eşitlik.</item>
/// <item>Ortak ağ: Aynı IP ya da aynı /24 (IPv6 /48). İki tarafın IP'si de görünüm anında ASN'i bilinen ve CDN/bulut DIŞI
/// olmalı. CDN/bulut IP'si hiçbir zaman kullanılan bir kenar üretmez; ASN'i bilinmeyen IP hiç kenar üretmez.</item>
/// </list>
/// <para><b>Görünüm:</b> Her pozitif yalnızca karar gününden önce bilineni görür: sertifika <c>KnownAt</c> (sinyallerle aynı
/// kural), ağ kaydı sorgu anı (<see cref="NetworkIndex"/>). Ad zaman taşımaz.</para>
/// <para><b>Belirlenimcilik:</b> Birimler kimliğe göre sıralanır; aynı anahtarı paylaşanlar bu sırayla zincir halinde bağlanır.
/// Bağlantılı bileşen <see cref="ClusterBuilder"/> ile bulunur (bölmedeki birleşim-bul yapısı).</para>
/// </remarks>
public static class PositiveClusters
{
    /// <summary>
    /// En kısa ad gövdesi. Marka listesindeki "5 karakterden kısa marka alınmaz" kuralıyla aynı sayı (DECISIONS 2026-11-21).
    /// Neden: Rakamları atınca "fx", "abc" gibi kısa gövdeler ilgisiz pek çok adı bağlardı; rakamdan ibaret adlar boş gövde verirdi.
    /// </summary>
    public const int MinStemLength = 5;

    /// <summary>Ad gövdesi ya da çok kısaysa <c>null</c>. Örnek: <c>borsa-24.example</c> → <c>borsa</c>, <c>yatırım7.example</c> → <c>yatirim</c>.</summary>
    public static string? NameStem(string registrableDomain)
    {
        ArgumentNullException.ThrowIfNull(registrableDomain);
        // Fold sinyallerin kullandığı katlama: punycode → Unicode, Türkçe harfler → ASCII, küçük harf.
        var folded = FinanceLexicon.Fold(registrableDomain.Split('.')[0]);
        if (folded is null)
        {
            return null;
        }

        var stem = new string([.. folded.Where(c => !char.IsAsciiDigit(c))]).Trim('-');
        return stem.Length >= MinStemLength ? stem : null;
    }

    /// <summary>
    /// Bütün kenarlar. CDN/bulut üzerinden gelen ağ kenarları da üretilir ama <see cref="ClusterEdge.ViaCdnOrCloud"/> ile
    /// işaretlenir: <see cref="ClusterBuilder"/> onları KULLANMAZ. Yalnızca "CDN olmasaydı kaç bağ olurdu" sayısı için tutulur.
    /// </summary>
    public static IReadOnlyList<ClusterEdge> Edges(
        IReadOnlyList<Subject> positives, IReadOnlyList<Observation> observations, Func<Subject, NetworkProfile> profileOf)
    {
        ArgumentNullException.ThrowIfNull(positives);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(profileOf);
        var byId = observations.GroupBy(o => o.SubjectId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var keys = new List<(string Key, ClusterEdgeKind Kind, bool ViaCdn, string SubjectId)>();
        foreach (var s in positives.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            // Sinyallerle aynı görünüm: karar gününden önce bilinmeyen sertifika burada hiç yok.
            var view = AsOfView.Create(s, s.ReferenceTime!.Value.AddTicks(-1), byId.GetValueOrDefault(s.Id) ?? []);
            foreach (var certificate in view.Observations.Where(o => o.Kind == ObservationKinds.Certificate && o.Value is not null).Select(o => o.Value!).Distinct())
            {
                keys.Add(("cert|" + certificate, ClusterEdgeKind.SharedCertificate, false, s.Id));
            }

            if (view.Observations.FirstOrDefault(o => o.Kind == ObservationKinds.Name)?.Value is { } name && NameStem(name) is { } stem)
            {
                keys.Add(("stem|" + stem, ClusterEdgeKind.NameStem, false, s.Id));
            }

            // ASN'i bilinmeyen IP (CdnOrCloud null) hiç anahtar üretmez: CDN olup olmadığını bilmiyorsak bağ kurmayız.
            foreach (var ip in profileOf(s).Ips.Where(ip => ip.CdnOrCloud is not null))
            {
                var via = ip.CdnOrCloud!.Value;
                // "cdn" eki anahtarı ayırır: Bir taraf IP'yi CDN, öteki CDN dışı görürse (farklı günün RIPEstat kaydı) bağ kurulmaz.
                var suffix = via ? "|cdn" : string.Empty;
                keys.Add(("ip|" + ip.Ip + suffix, ClusterEdgeKind.SharedNetwork, via, s.Id));
                keys.Add(("block|" + ip.Block + suffix, ClusterEdgeKind.SharedNetwork, via, s.Id));
            }
        }

        var edges = new List<ClusterEdge>();
        foreach (var group in keys.GroupBy(k => k.Key, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = group.Select(k => k.SubjectId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var first = group.First();
            // Zincir: a–b, b–c, ... Bileşen için yeterli; bütün çiftleri yazmak gereksiz büyürdü.
            for (var i = 1; i < members.Count; i++)
            {
                edges.Add(new ClusterEdge(members[i - 1], members[i], first.Kind, first.ViaCdn));
            }
        }

        return edges;
    }

    public static ClusterAnalysis Analyze(
        IReadOnlyList<Subject> positives, IReadOnlyList<Observation> observations, Func<Subject, NetworkProfile> profileOf, EvaluationConfig config)
    {
        ArgumentNullException.ThrowIfNull(positives);
        ArgumentNullException.ThrowIfNull(config);
        if (positives.Any(s => !s.IsPositive))
        {
            throw new ArgumentException("Küme analizi yalnızca pozitiflerle çalışır.", nameof(positives));
        }

        var edges = Edges(positives, observations, profileOf);
        var clusterOf = ClusterBuilder.Build(positives.Select(s => s.Id), edges);
        var sizes = clusterOf.Values.GroupBy(c => c, StringComparer.Ordinal).Select(g => g.Count()).OrderDescending().ToList();

        var bulletinOf = positives.ToDictionary(s => s.Id, s => s.Bulletin!.Value, StringComparer.Ordinal);
        var multiBulletin = clusterOf.GroupBy(kv => kv.Value, StringComparer.Ordinal)
            .Count(g => g.Select(kv => bulletinOf[kv.Key]).Distinct().Count() > 1);

        var used = edges.Where(e => !e.ViaCdnOrCloud).ToList();
        var linkedBy = Enum.GetValues<ClusterEdgeKind>().ToDictionary(
            kind => kind,
            kind => used.Where(e => e.Kind == kind).SelectMany(e => new[] { e.A, e.B }).Distinct(StringComparer.Ordinal).Count());
        var networkLinked = used.Where(e => e.Kind == ClusterEdgeKind.SharedNetwork).SelectMany(e => new[] { e.A, e.B }).ToHashSet(StringComparer.Ordinal);
        var heldBack = edges.Where(e => e.ViaCdnOrCloud).SelectMany(e => new[] { e.A, e.B }).Distinct(StringComparer.Ordinal)
            .Count(id => !networkLinked.Contains(id));

        return new ClusterAnalysis(positives.Count, clusterOf, sizes, multiBulletin, linkedBy, heldBack, MovedIfUsedInSplit(positives, clusterOf, config));
    }

    /// <summary>
    /// "Bu kümeler bölmeye verilseydi ne değişirdi?" Bölme iki kez hesaplanır: bugünkü küme kimlikleriyle ve yeni kümelerle.
    /// Bölmesi farklı çıkan pozitif sayılır. Bölmenin kendisi DEĞİŞTİRİLMEZ (ayar/test ataması donmuş kabul edilir); bu yalnızca
    /// Kadircan'ın karar vermesi için bir sayıdır.
    /// </summary>
    /// <remarks>Kontroller burada yok: Kontrollerin bölmesi pozitiflerin bütünlük taşımasından önce hesaplanan ay sayılarına
    /// bakar ve yeni kümeler kontrolleri pozitiflere bağlamaz.</remarks>
    public static int MovedIfUsedInSplit(IReadOnlyList<Subject> positives, IReadOnlyDictionary<string, string> clusterOf, EvaluationConfig config)
    {
        ArgumentNullException.ThrowIfNull(positives);
        ArgumentNullException.ThrowIfNull(clusterOf);
        var before = Splitter.Split(positives, config);
        var rebuilt = positives
            .Select(s => new Subject(s.Id, s.Group, s.Subgroup, s.ReferenceTime, s.Bulletin, clusterOf[s.Id], firstCertificateAt: s.FirstCertificateAt))
            .ToList();
        var after = Splitter.Split(rebuilt, config);
        return positives.Count(s => before.PartOf(s.Id) != after.PartOf(s.Id));
    }

    /// <summary>Konsol metni. Küçük hücre kuralı ölçüm raporuyla aynı; alt satır, üst satırı küçük hücreyse yazılmaz.</summary>
    public static string ToText(ClusterAnalysis a, int minCountForRates)
    {
        ArgumentNullException.ThrowIfNull(a);
        var sb = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        var multi = a.Sizes.Where(n => n > 1).ToList();
        RateResult Count(int hits, int total) => new(hits, total, total < minCountForRates ? null : (double)hits / total, null, null);

        sb.AppendLine("Pozitif kümeleri (yalnız SPK pozitifleri; her pozitif yalnızca karar gününden önce bilinen kayıtla):");
        sb.Append("  Pozitif: ").AppendLine(EvaluationReport.Cell(a.Positives));
        var inMulti = Count(multi.Sum(), a.Positives);
        sb.Append("  Çok üyeli bir kümedeki pozitif: ").Append(EvaluationReport.Format(inMulti))
            .AppendLine(" | sınır: bağ yoksa küme yok demek değildir; görülmeyen sertifika ve ağ bağı kaçar");
        if (EvaluationReport.IsSmallCell(inMulti))
        {
            // Küme sayısı ve tek üyeliler buradan geri hesaplanırdı.
            sb.AppendLine("    Küme sayısı ve boyut dağılımı: - (üst satır küçük hücre: yazılmadı)");
        }
        else
        {
            // Tek üyeli küme sayısı = pozitif − çok üyelideki pozitif, yani yukarıdan bulunur. Toplam küme sayısı da yazılırsa
            // çok üyeli küme sayısı çıkarmayla bulunurdu; o küçükse toplam da yazılmaz.
            var multiSmall = multi.Count < EvaluationReport.SmallCellLimit;
            sb.Append("    Küme sayısı: ").Append(multiSmall ? "-" : EvaluationReport.Cell(a.Sizes.Count))
                .Append(", çok üyeli küme: ").AppendLine(EvaluationReport.Cell(multi.Count));
            sb.Append("    Çok üyeli kümelerin boyutu (küme sayısı): ").AppendLine(EvaluationReport.CompositionText(
            [
                ("2", multi.Count(n => n == 2)),
                ("3-4", multi.Count(n => n is 3 or 4)),
                ("5-9", multi.Count(n => n is >= 5 and <= 9)),
                ("10+", multi.Count(n => n >= 10)),
            ]));
            sb.Append("    Birden çok bültene yayılan küme (çok üyeliler içinde): ")
                .Append(multiSmall ? "- (çok üyeli küme küçük hücre)" : EvaluationReport.Format(Count(a.MultiBulletinClusters, multi.Count)))
                .AppendLine(" | sınır: bülten = pozitifin en erken bülteni");
        }

        sb.Append("  Ortak sertifikayla bağlanan pozitif: ").AppendLine(EvaluationReport.Format(Count(a.LinkedBy[ClusterEdgeKind.SharedCertificate], a.Positives)));
        sb.Append("  Ad gövdesiyle bağlanan pozitif: ").Append(EvaluationReport.Format(Count(a.LinkedBy[ClusterEdgeKind.NameStem], a.Positives)))
            .AppendLine(string.Create(c, $" | sınır: birebir gövde eşitliği, en az {MinStemLength} karakter; benzer ama farklı adlar bağlanmaz"));
        sb.Append("  CDN/bulut dışı ortak ağla bağlanan pozitif: ").Append(EvaluationReport.Format(Count(a.LinkedBy[ClusterEdgeKind.SharedNetwork], a.Positives)))
            .AppendLine(" | sınır: DNS ölçümü 2026-11-13'te başladı; karardan önce DNS'i olan pozitif çok az");
        sb.Append("  Yalnızca CDN/bulut üzerinden bağlanacak olan (bağlanmadı): ").AppendLine(EvaluationReport.Cell(a.HeldBackByCdn));
        sb.AppendLine("  Bölme: Bu kümeler ayar/test bölmesine VERİLMEDİ, bölme değişmedi.");
        sb.Append("    Verilseydi bölmesi değişecek pozitif: ").Append(EvaluationReport.Cell(a.MovedIfUsedInSplit))
            .Append(" (değişir mi: ").Append(a.MovedIfUsedInSplit > 0 ? "evet" : "hayır").AppendLine(")");
        return sb.ToString();
    }
}
