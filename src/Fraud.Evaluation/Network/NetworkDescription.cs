using System.Globalization;
using System.Text;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;

namespace Fraud.Evaluation.Network;

/// <summary>
/// Yoğunlaşma özeti: Birimler birkaç ağda (ya da NS sağlayıcısında) mı toplanıyor?
/// </summary>
/// <param name="Subjects">Hesaba giren birim sayısı. Ekrana yazılmaz (yalnızca küçük hücre denetiminde kullanılır).</param>
/// <param name="Distinct">Farklı ASN (ya da sağlayıcı) sayısı.</param>
/// <param name="TopShare">En büyük <see cref="NetworkDescription.TopK"/> ağın payı (0–1).</param>
/// <param name="TopWeight">Aynı payın birim cinsinden karşılığı (pay × birim sayısı).</param>
/// <param name="Hhi">Herfindahl-Hirschman endeksi: payların karelerinin toplamı (0–1). 1 = hepsi tek ağda.</param>
public sealed record Concentration(int Subjects, int Distinct, double TopShare, double TopWeight, double Hhi);

/// <summary>Bir grubun ağ betimlemesi. Yalnızca sayılar; alan adı, IP ve (kontrolde) ASN yok.</summary>
/// <param name="Subjects">Gruptaki referanslı birim.</param>
/// <param name="DnsKnown">Taban: Görünüm anında adres sorgusu kaydı olan birim. Bir kapsama sayısıdır, site özelliği değildir.</param>
/// <param name="PortsAnswered">CDN/bulut dışı IP'si için InternetDB cevabı (200 ya da 404) bilinen birim. Kapsama sayısı.</param>
public sealed record NetworkGroupRow(
    string Label,
    bool IsPositive,
    int Subjects,
    bool BelowMinimum,
    int DnsKnown,
    RateResult NxDomain,
    RateResult ServerFailure,
    RateResult NsKnown,
    Concentration? Ns,
    RateResult OnlyCdn,
    RateResult OutsideCdn,
    Concentration? Asn,
    Concentration? AsnOutsideCdn,
    IReadOnlyList<(long Asn, RateResult Share)> SharedAsns,
    int PortsAnswered,
    RateResult PortsListed,
    RateResult PortsBeyondWeb);

/// <summary>
/// SPK pozitifleri ile her kontrol grubunun toplu ağ karşılaştırması (ROADMAP Hafta 8, "Ağ özellikleri ve betimleyici analiz").
/// </summary>
/// <remarks>
/// <para>Bu bir tespit sonucu değildir: Eşik, alarm ve yanlış alarm yok. Yalnızca "o gün bilinen" ağ durumunun betimlemesi.</para>
/// <para><b>Taban:</b> Her oranın paydası aynıdır: görünüm anında DNS kaydı bilinen birimler (adres dönmeyenler dahil). Neden tek
/// taban: İç içe paydalar (ör. "ASN'si bilinenler içinde CDN payı") her alt satırda üst satırın sayısını açık eder. Üst satırın
/// tümleyeni küçükse (ör. 41 bankanın 40'ı çözülüyor) küçük hücre kuralı bütün alt satırları gizlemek zorunda kalırdı.</para>
/// <para><b>Küçük hücre:</b> Kural ölçüm raporuyla aynıdır (<see cref="EvaluationReport.Format"/>): 5'ten küçük sayı ya da tümleyeni
/// yazılmaz; 10'dan az birimde oran ve aralık verilmez. Aynı tabandaki satırlar bir dağılımın bütün hücreleri değildir; hiç
/// yazılmayan bir "diğer" hücre (boş cevap, engel işareti, öteki hatalar, ASN'si bilinmeyen) her zaman vardır. Bu yüzden gizlenen
/// bir hücre öteki satırlardan çıkarma yapılarak bulunamaz. Yalnızca port satırları iç içedir; orada üst satır küçük hücreyse alt
/// satır yazılmaz.</para>
/// <para><b>Aralık:</b> Ölçüm raporundaki gibi küme ve (pozitifte) bülten düzeyinde bootstrap: Aynı operatörün ya da aynı toplu
/// kararın siteleri birbirinden bağımsız değildir.</para>
/// <para><b>ASN:</b> Numara yalnızca pozitiflerde ve en az 5 pozitifte (tümleyeni de en az 5) ortak olduğunda yazılır. Kontrol
/// grubunda hiçbir ASN, sağlayıcı ya da NS adı yazılmaz (Kural 4).</para>
/// </remarks>
public static class NetworkDescription
{
    /// <summary>Yoğunlaşmada "en büyük k ağ". 3 seçildi: Önceden yazıldı, veriye bakılarak seçilmedi.</summary>
    public const int TopK = 3;

    /// <summary>Web portları. Bunların dışındaki her açık port "80/443 dışında" sayılır.</summary>
    private static readonly int[] WebPorts = [80, 443];

    public static IReadOnlyList<NetworkGroupRow> Compute(
        IReadOnlyList<Subject> subjects, Func<Subject, NetworkProfile> profileOf, EvaluationConfig config)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(profileOf);
        ArgumentNullException.ThrowIfNull(config);

        // Referansı olmayan birim görünüm alamaz; pozitife bağlı kontrol (E05) ölçüm raporundaki gibi ana satırlara girmez.
        var groups = subjects
            .Where(s => s.ReferenceTime is not null && !s.LinkedToPositive)
            .GroupBy(Label, StringComparer.Ordinal)
            .OrderBy(g => g.First().Group).ThenBy(g => g.First().Subgroup);
        var rows = new List<NetworkGroupRow>();
        foreach (var g in groups)
        {
            var members = g.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            rows.Add(Row(g.Key, members, members.ToDictionary(s => s.Id, profileOf, StringComparer.Ordinal), config));
        }

        return rows;
    }

    /// <summary>Grup adı: pozitiflerde "Positive" (alt grup ayrılmaz), kontrolde ölçüm raporundaki gibi "grup / alt grup".</summary>
    public static string Label(Subject s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.IsPositive ? "Positive" : s.Group + " / " + s.Subgroup;
    }

    /// <summary>
    /// Yoğunlaşma. Her birim toplam 1 ağırlık taşır; birden çok ağı varsa ağırlığı onlara eşit bölünür (2 ASN → 0,5 + 0,5).
    /// Böylece çok IP'li bir site tek başına bir ağı "büyük" gösteremez. Pay = ağırlık / birim sayısı; HHI = Σ pay².
    /// </summary>
    /// <param name="keysPerSubject">Her birimin ağları. Ağı olmayan birim hesaba girmez.</param>
    public static Concentration? Concentrate(IReadOnlyList<IReadOnlyList<string>> keysPerSubject)
    {
        ArgumentNullException.ThrowIfNull(keysPerSubject);
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        var n = 0;
        foreach (var keys in keysPerSubject)
        {
            var distinct = keys.Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count == 0)
            {
                continue;
            }

            n++;
            foreach (var key in distinct)
            {
                weights[key] = weights.GetValueOrDefault(key) + 1.0 / distinct.Count;
            }
        }

        if (n == 0)
        {
            return null;
        }

        var sorted = weights.Values.OrderDescending().ToList();
        var top = sorted.Take(TopK).Sum();
        var hhi = sorted.Sum(w => (w / n) * (w / n));
        return new Concentration(n, weights.Count, top / n, top, hhi);
    }

    private static NetworkGroupRow Row(string label, List<Subject> members, Dictionary<string, NetworkProfile> profiles, EvaluationConfig config)
    {
        var isPositive = members[0].IsPositive;
        NetworkProfile P(Subject s) => profiles[s.Id];
        static bool AsnKnown(IpView ip) => ip.CdnOrCloud is not null;
        static bool OutsideCdn(IpView ip) => ip.CdnOrCloud == false;
        static bool BeyondWeb(IpView ip) => ip.PortsListed == true && ip.Ports.Any(p => !WebPorts.Contains(p));

        // Taban: DNS kaydı bilinen birimler. Bütün oranlar bu tabanda.
        var dnsKnown = members.Where(s => P(s).Dns is not null).ToList();
        RateResult Rate(Func<Subject, bool> hit, string key) => RateOf(dnsKnown, hit, config, label + "|" + key);

        // Önceden kayıtlı "yeterli örnek yok" kuralı (G2, DECISIONS 2026-11-24): Tek birim bile işaretliyse oran hesaplanmaz.
        var belowMinimum = members.Any(s => s.BuildBelowMinimum);

        // Yalnız port satırları iç içe: CDN dışı IP'si olan → InternetDB cevabı bilinen → InternetDB'de kaydı olan.
        var portsAnswered = dnsKnown.Where(s => P(s).Ips.Any(ip => OutsideCdn(ip) && ip.PortsListed is not null)).ToList();
        var listed = portsAnswered.Where(s => P(s).Ips.Any(ip => OutsideCdn(ip) && ip.PortsListed == true)).ToList();

        // ASN listesi yalnızca pozitiflerde. Kontrol grubunun ASN'i hiçbir biçimde basılmaz.
        var shared = new List<(long Asn, RateResult Share)>();
        if (isPositive && !belowMinimum)
        {
            foreach (var asn in dnsKnown.SelectMany(s => P(s).Ips.Where(AsnKnown).SelectMany(ip => ip.Asns)).Distinct().Order())
            {
                var share = Rate(s => P(s).Ips.Any(ip => AsnKnown(ip) && ip.Asns.Contains(asn)), "asn-" + asn.ToString(CultureInfo.InvariantCulture));
                if (!EvaluationReport.IsSmallCell(share))
                {
                    shared.Add((asn, share));
                }
            }
        }

        return new NetworkGroupRow(
            label, isPositive, members.Count, belowMinimum, dnsKnown.Count,
            Rate(s => P(s).Dns == DnsState.NxDomain, "nx"),
            Rate(s => P(s).Dns == DnsState.ServerFailure, "servfail"),
            Rate(s => P(s).NsProviders.Count > 0, "ns"),
            Concentrate([.. dnsKnown.Select(s => P(s).NsProviders)]),
            // "Yalnız CDN/bulut": ASN'si bilinen en az bir IP var ve hepsi CDN/bulut. "Dışında": en az bir IP CDN/bulut dışı.
            Rate(s => P(s).Ips.Any(AsnKnown) && !P(s).Ips.Any(OutsideCdn), "only-cdn"),
            Rate(s => P(s).Ips.Any(OutsideCdn), "outside-cdn"),
            Concentrate([.. dnsKnown.Select(s => AsnKeys(P(s).Ips.Where(AsnKnown)))]),
            Concentrate([.. dnsKnown.Select(s => AsnKeys(P(s).Ips.Where(OutsideCdn)))]),
            [.. shared.OrderByDescending(x => x.Share.Hits).ThenBy(x => x.Asn)],
            portsAnswered.Count,
            RateOf(portsAnswered, s => P(s).Ips.Any(ip => OutsideCdn(ip) && ip.PortsListed == true), config, label + "|ports-listed"),
            RateOf(listed, s => P(s).Ips.Any(ip => OutsideCdn(ip) && BeyondWeb(ip)), config, label + "|ports-beyond-web"));
    }

    private static List<string> AsnKeys(IEnumerable<IpView> ips) =>
        [.. ips.SelectMany(ip => ip.Asns).Select(a => a.ToString(CultureInfo.InvariantCulture))];

    /// <summary>
    /// Bir oran, ölçüm raporundaki <c>MetricsCalculator</c> kuralıyla: 10'dan az birimde oran ve aralık yok. Aralık küme
    /// düzeyinde; pozitifte ayrıca bülten düzeyinde. Tohum grup ve satır adından gelir: Aynı veri her zaman aynı aralığı verir.
    /// </summary>
    private static RateResult RateOf(List<Subject> of, Func<Subject, bool> hit, EvaluationConfig config, string key)
    {
        var hits = of.Count(hit);
        if (of.Count < config.MinCountForRates)
        {
            return new RateResult(hits, of.Count, null, null, null);
        }

        var seed = config.Seed + "|network|" + key;
        var byCluster = Bootstrap.PercentileInterval([.. of.Select(s => new BootstrapRow(s.ClusterId, hit(s)))], config.BootstrapIterations, seed + "|cluster");
        var byBulletin = of.All(s => s.Bulletin is not null)
            ? Bootstrap.PercentileInterval([.. of.Select(s => new BootstrapRow(s.Bulletin!.Value.ToString(), hit(s)))], config.BootstrapIterations, seed + "|bulletin")
            : null;
        return new RateResult(hits, of.Count, (double)hits / of.Count, byCluster, byBulletin);
    }

    /// <summary>Konsol metni. Her sayının yanında "sınır:" notu var (Kural 7).</summary>
    public static string ToText(IReadOnlyList<NetworkGroupRow> rows, int minCountForRates)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            // Birim ve taban kapsama sayılarıdır (ölçüm raporundaki "test birimi" gibi): yalnızca 5'ten küçükse gizlenir.
            sb.Append("Grup ").Append(r.Label).Append(": birim=").Append(EvaluationReport.Cell(r.Subjects))
                .Append(", taban (DNS kaydı bilinen)=").Append(EvaluationReport.Cell(r.DnsKnown))
                .Append(" | sınır: ").AppendLine(Limits.Dns);
            if (r.BelowMinimum)
            {
                sb.AppendLine("  " + EvaluationReport.BelowMinimumText + " (kurulumda 100'den az üye): ağ oranları verilmez.");
                continue;
            }

            Line(sb, 1, "NXDOMAIN", r.NxDomain, Limits.Outcome);
            Line(sb, 1, "SERVFAIL", r.ServerFailure, Limits.Outcome);
            Line(sb, 1, "NS sağlayıcısı bilinen", r.NsKnown, Limits.Ns);
            // Yoğunlaşma yüzdeleri paydayı ele verebilir (ör. %54,5 = 6/11). Bu yüzden yalnızca paydası basılabilen satırlarda yazılır.
            ConcentrationLine(sb, 2, "NS sağlayıcı yoğunlaşması", r.Ns, !EvaluationReport.IsSmallCell(r.NsKnown), minCountForRates, Limits.Ns);
            Line(sb, 1, "Yalnız CDN/bulut arkasında (ASN'si bilinen bütün IP'leri)", r.OnlyCdn, Limits.Cdn);
            Line(sb, 1, "En az bir IP'si CDN/bulut dışında", r.OutsideCdn, Limits.Cdn);
            // ASN'si bilinen birim = "yalnız CDN" + "CDN dışında". Biri gizliyken bu payda öbürünü geri verirdi.
            var cdnOk = !EvaluationReport.IsSmallCell(r.OnlyCdn) && !EvaluationReport.IsSmallCell(r.OutsideCdn);
            ConcentrationLine(sb, 1, "ASN yoğunlaşması (bütün IP'ler)", r.Asn, cdnOk, minCountForRates, Limits.Concentration);
            ConcentrationLine(sb, 1, "ASN yoğunlaşması (yalnız CDN/bulut dışı IP'ler)", r.AsnOutsideCdn, !EvaluationReport.IsSmallCell(r.OutsideCdn), minCountForRates, Limits.Concentration);
            if (r.IsPositive)
            {
                sb.Append("  Ortak ASN (en az 5 pozitif ve tümleyeni en az 5): ")
                    .Append(r.SharedAsns.Count == 0
                        ? "yok ya da küçük hücre"
                        : string.Join("; ", r.SharedAsns.Select(x => "AS" + x.Asn.ToString(CultureInfo.InvariantCulture) + " " + EvaluationReport.Format(x.Share))))
                    .Append(" | sınır: ").AppendLine(Limits.Asn);
            }

            // Port satırları iç içe: Üst satır küçük hücreyse alt satırın paydası onu açık ederdi, yazılmaz.
            var outsideOk = !EvaluationReport.IsSmallCell(r.OutsideCdn);
            sb.Append("    InternetDB cevabı bilinen (CDN/bulut dışı IP'si olanlardan): ")
                .Append(outsideOk ? EvaluationReport.Cell(r.PortsAnswered) : "- (üst satır küçük hücre: yazılmadı)")
                .Append(" | sınır: ").AppendLine(Limits.Ports);
            Line(sb, 3, "InternetDB'de kaydı olan (404 değil)", outsideOk ? r.PortsListed : null, Limits.Ports);
            Line(sb, 4, "80/443 dışında açık port", outsideOk && !EvaluationReport.IsSmallCell(r.PortsListed) ? r.PortsBeyondWeb : null, Limits.Ports);
        }

        return sb.ToString();
    }

    /// <summary>Bir oran satırı. <paramref name="rate"/> boşsa üst satır küçük hücredir: "-" yazılır, sayı yazılmaz.</summary>
    private static void Line(StringBuilder sb, int depth, string label, RateResult? rate, string limit) =>
        sb.Append(new string(' ', depth * 2)).Append(label).Append(": ")
            .Append(rate is null ? "- (üst satır küçük hücre: yazılmadı)" : EvaluationReport.Format(rate))
            .Append(" | sınır: ").AppendLine(limit);

    /// <summary>
    /// Yoğunlaşma satırı. Birim sayısı yazılmaz; ama yüzde, küçük sayılarda paydayı ele verebilir. Bu yüzden
    /// <paramref name="parentOk"/> yanlışsa (paydanın satırı küçük hücre) hiçbir şey yazılmaz. Pay ve HHI ancak en az 5 farklı ağ ve en az <paramref name="minCount"/> birim
    /// varsa yazılır: 2–4 ağda HHI ve en büyük pay, ağ başına sayıyı geri hesaplatabilirdi. En büyük k ağın birim cinsinden
    /// karşılığı ya da tümleyeni 5'ten küçükse o pay da yazılmaz.
    /// </summary>
    private static void ConcentrationLine(StringBuilder sb, int depth, string label, Concentration? c, bool parentOk, int minCount, string limit)
    {
        sb.Append(new string(' ', depth * 2)).Append(label).Append(": ");
        if (!parentOk)
        {
            sb.Append("- (paydası küçük hücre: yazılmadı)");
        }
        else if (c is null)
        {
            sb.Append("- (veri yok)");
        }
        else if (c.Subjects < minCount || c.Distinct < EvaluationReport.SmallCellLimit)
        {
            sb.Append("farklı ağ=").Append(EvaluationReport.Cell(c.Distinct)).Append(" (az birim ya da az ağ: pay ve HHI yazılmaz)");
        }
        else
        {
            var topHidden = c.TopWeight < EvaluationReport.SmallCellLimit || c.Subjects - c.TopWeight < EvaluationReport.SmallCellLimit;
            sb.Append("farklı ağ=").Append(EvaluationReport.Cell(c.Distinct))
                .Append(", en büyük ").Append(TopK.ToString(CultureInfo.InvariantCulture)).Append(" ağın payı=")
                .Append(topHidden ? "- (küçük hücre)" : Percent(c.TopShare))
                .Append(", HHI=").Append(c.Hhi.ToString("0.000", CultureInfo.InvariantCulture));
        }

        sb.Append(" | sınır: ").AppendLine(limit);
    }

    private static string Percent(double value) => (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>Her satırın yanına yazılan veri sınırları. Kısa tutuldu; ayrıntı başlıkta ve DECISIONS.md'de.</summary>
    public static class Limits
    {
        public const string Dns = "tek ölçüm konumu, yalnız DoH, referanstan önceki son ölçüm günü; DNS ölçümü 2026-11-13'te başladı";
        public const string Outcome = "payda taban; o günkü çözücü cevabı, engel DNS cevabını değiştirebilir (DoH ile azaltıldı, kanıtlanmadı)";
        public const string Ns = "payda taban; sağlayıcı = NS sunucu adının kayıtlı alan adı, adı hiç yazılmaz";
        public const string Asn = "payda taban; RIPEstat yalnız sorgu günündeki ASN'i verir, ASN'si bilinmeyen IP sayılmaz";
        public const string Cdn = "payda taban (adres dönmeyenler dahil); CDN/bulut = RipeStat:CdnAsns, listede olmayan büyük barındırıcı 'dışında' sayılır";
        public const string Concentration = "aralık hesaplanmadı; çok ağlı birimin ağırlığı ağlarına bölünür";
        public const string Ports = "InternetDB haftalık ve geçmişsiz; CDN/bulut IP'leri hariç; hata 'kayıt yok' sayılmaz";
    }
}
