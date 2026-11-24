using Fraud.Domain.Network;
using Fraud.Evaluation.Clustering;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Network;
using Fraud.Evaluation.Splitting;

namespace Fraud.Evaluation.Tests;

/// <summary>
/// Hafta 8: ağ betimlemesi ve pozitif küme analizi. Kenar kuralları (CDN hiç bağlamaz), görünüm anı (gelecek sızıntısı yok)
/// ve küçük hücre kuralı. Adlar uydurmadır (.example), IP'ler belgeleme aralığındandır.
/// </summary>
public sealed class NetworkAnalysisTests
{
    // Karar günü 2026/30: Synthetic.DecisionOf(30) = 1 Ocak + 120 gün, TR gün başı.
    private static readonly DateTimeOffset Decision = Synthetic.DecisionOf(30);

    private static Subject Positive(string id, int bulletin = 30) =>
        new(id, SubjectGroup.Positive, Subgroup.PositiveNew, Synthetic.DecisionOf(bulletin), new BulletinNumber(2026, bulletin), id,
            firstCertificateAt: Synthetic.DecisionOf(bulletin).AddDays(-60));

    private static Subject Control(string id, DateTimeOffset reference) =>
        new(id, SubjectGroup.ControlRandom, Subgroup.FrameNeutral, reference, null, id, firstCertificateAt: reference.AddDays(-60));

    private static Observation Name(string id, string name) => new(id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue, name);

    private static Observation Certificate(string id, string issuance, DateTimeOffset knownAt) => new(id, ObservationKinds.Certificate, knownAt, knownAt, issuance);

    private static DnsFact A(string id, DateTimeOffset at, params string[] ips) =>
        new(id, at, "A", DnsOutcome.Success, DnsErrorKind.None, false, ips, []);

    private static IpAsnFact Asn(string ip, DateTimeOffset at, long asn, bool cdn) => new(ip, at, [asn], cdn);

    private static NetworkIndex Index(IEnumerable<DnsFact> dns, IEnumerable<IpAsnFact>? asn = null, IEnumerable<PortFact>? ports = null) =>
        new(new NetworkData([.. dns], [.. asn ?? []], [.. ports ?? []]));

    // --- Ad gövdesi ---

    [Theory]
    [InlineData("borsa-24.example", "borsa")]
    [InlineData("yatirim7.example", "yatirim")]
    [InlineData("kripto2026.example", "kripto")]
    [InlineData("1234.example", null)] // yalnızca rakam: boş gövde hiçbir şeyi bağlamaz
    [InlineData("fx99.example", null)] // 5 karakterden kısa
    public void Name_stem_drops_digits_and_short_stems(string name, string? expected) =>
        Assert.Equal(expected, PositiveClusters.NameStem(name));

    // --- Kenar kuralları ---

    [Fact]
    public void Shared_cdn_ip_never_links_but_shared_non_cdn_ip_does()
    {
        var day = Decision.AddDays(-5);
        var positives = new List<Subject> { Positive("p1"), Positive("p2"), Positive("p3"), Positive("p4") };
        // p1 ve p2 aynı CDN IP'sinde; p3 ve p4 aynı CDN dışı IP'de.
        var index = Index(
            [A("p1", day, "192.0.2.10"), A("p2", day, "192.0.2.10"), A("p3", day, "198.51.100.7"), A("p4", day, "198.51.100.7")],
            [Asn("192.0.2.10", day, 13335, cdn: true), Asn("198.51.100.7", day, 64500, cdn: false)]);

        var result = PositiveClusters.Analyze(positives, [], index.ProfileOf, new EvaluationConfig());

        Assert.NotEqual(result.ClusterOf["p1"], result.ClusterOf["p2"]);
        Assert.Equal(result.ClusterOf["p3"], result.ClusterOf["p4"]);
        Assert.Equal(2, result.HeldBackByCdn);
        Assert.Equal(2, result.LinkedBy[ClusterEdgeKind.SharedNetwork]);
    }

    [Fact]
    public void Same_block_links_only_when_both_ips_are_known_non_cdn()
    {
        var day = Decision.AddDays(-5);
        var positives = new List<Subject> { Positive("p1"), Positive("p2"), Positive("p3") };
        // Aynı /24: p1 ve p2'nin IP'si CDN dışı → bağlanır. p3'ün IP'si aynı blokta ama ASN'i bilinmiyor → bağlanmaz.
        var index = Index(
            [A("p1", day, "203.0.113.5"), A("p2", day, "203.0.113.200"), A("p3", day, "203.0.113.99")],
            [Asn("203.0.113.5", day, 64500, false), Asn("203.0.113.200", day, 64500, false)]);

        var result = PositiveClusters.Analyze(positives, [], index.ProfileOf, new EvaluationConfig());

        Assert.Equal(result.ClusterOf["p1"], result.ClusterOf["p2"]);
        Assert.NotEqual(result.ClusterOf["p1"], result.ClusterOf["p3"]);
    }

    [Fact]
    public void Same_cdn_block_never_links_even_with_different_ips()
    {
        var day = Decision.AddDays(-5);
        var index = Index(
            [A("p1", day, "192.0.2.1"), A("p2", day, "192.0.2.2")],
            [Asn("192.0.2.1", day, 16509, true), Asn("192.0.2.2", day, 16509, true)]);

        var edges = PositiveClusters.Edges([Positive("p1"), Positive("p2")], [], index.ProfileOf);

        Assert.All(edges, e => Assert.True(e.ViaCdnOrCloud));
        var clusters = ClusterBuilder.Build(["p1", "p2"], edges);
        Assert.NotEqual(clusters["p1"], clusters["p2"]);
    }

    [Fact]
    public void Certificate_and_name_edges_link_and_clusters_spanning_bulletins_are_counted()
    {
        // p1 (2026/5, ayar) ve p2 (2026/30, test) aynı sertifikada. p3 ve p4 aynı ad gövdesinde, ikisi de 2026/40.
        var positives = new List<Subject> { Positive("p1", 5), Positive("p2", 30), Positive("p3", 40), Positive("p4", 40) };
        var observations = new List<Observation>
        {
            Certificate("p1", "c-ortak", Synthetic.DecisionOf(5).AddDays(-10)),
            Certificate("p2", "c-ortak", Synthetic.DecisionOf(5).AddDays(-10)),
            Name("p3", "borsa1.example"),
            Name("p4", "borsa2.example"),
        };

        var result = PositiveClusters.Analyze(positives, observations, Index([]).ProfileOf, new EvaluationConfig());

        Assert.Equal(result.ClusterOf["p1"], result.ClusterOf["p2"]);
        Assert.Equal(result.ClusterOf["p3"], result.ClusterOf["p4"]);
        Assert.All(result.Sizes, size => Assert.Equal(2, size));
        Assert.Equal(2, result.Sizes.Count);
        Assert.Equal(1, result.MultiBulletinClusters);
        // Bölmeye verilseydi p2 ayara taşınırdı. Bölmenin kendisi değişmez; yalnızca sayı.
        Assert.Equal(1, result.MovedIfUsedInSplit);
    }

    [Fact]
    public void Edges_are_deterministic_regardless_of_input_order()
    {
        var positives = new List<Subject> { Positive("p3"), Positive("p1"), Positive("p2") };
        var observations = new List<Observation> { Name("p1", "kripto1.example"), Name("p2", "kripto2.example"), Name("p3", "kripto3.example") };

        var a = PositiveClusters.Edges(positives, observations, Index([]).ProfileOf);
        var b = PositiveClusters.Edges([.. positives.AsEnumerable().Reverse()], [.. observations.AsEnumerable().Reverse()], Index([]).ProfileOf);

        Assert.Equal(a, b);
    }

    // --- Görünüm anı ---

    [Fact]
    public void Records_queried_on_or_after_the_decision_day_are_not_visible()
    {
        var p = Positive("p1");
        // Karar günü saat 10:00 TR: karar günü görünmez. Bir gün önceki kayıt görünür.
        var index = Index(
            [A("p1", Decision.AddDays(-1).AddHours(12), "198.51.100.1"), A("p1", Decision.AddHours(10), "198.51.100.2")],
            [Asn("198.51.100.1", Decision.AddDays(5), 64500, false)]);

        var profile = index.ProfileOf(p);

        Assert.Equal(DnsState.Resolves, profile.Dns);
        var ip = Assert.Single(profile.Ips);
        Assert.Equal("198.51.100.1", ip.Ip);
        // RIPEstat kaydı karardan SONRA sorgulandı: ASN bilinmiyor sayılır, IP hiçbir ağ kenarı üretmez.
        Assert.Null(ip.CdnOrCloud);
        Assert.True(profile.AsOf < Decision);
    }

    [Fact]
    public void Certificate_known_after_the_decision_creates_no_edge()
    {
        var positives = new List<Subject> { Positive("p1"), Positive("p2") };
        var observations = new List<Observation>
        {
            Certificate("p1", "c-sonra", Decision.AddDays(3)),
            Certificate("p2", "c-sonra", Decision.AddDays(3)),
        };

        Assert.Empty(PositiveClusters.Edges(positives, observations, Index([]).ProfileOf));
    }

    [Fact]
    public void Only_the_last_measurement_day_counts_and_a_real_answer_beats_an_error()
    {
        var p = Positive("p1");
        var old = Decision.AddDays(-10);
        var last = Decision.AddDays(-2);
        var index = Index(
        [
            A("p1", old, "198.51.100.50"),
            new DnsFact("p1", last, "A", DnsOutcome.NxDomain, DnsErrorKind.None, false, [], []),
            new DnsFact("p1", last.AddMinutes(1), "A", DnsOutcome.Error, DnsErrorKind.ServerFailure, false, [], []),
        ]);

        var profile = index.ProfileOf(p);

        // Son gün NXDOMAIN ve SERVFAIL: NXDOMAIN önce gelir. Eski günün IP'si bugünün IP'si sayılmaz.
        Assert.Equal(DnsState.NxDomain, profile.Dns);
        Assert.Empty(profile.Ips);
    }

    // --- Betimleme ve küçük hücre ---

    [Fact]
    public void Concentration_splits_a_multi_asn_subject_and_matches_a_hand_computed_hhi()
    {
        // Ağırlıklar: a = 1 + 1 + 0,5 = 2,5; b = 0,5; c = 1. n = 4. Paylar 0,625 / 0,125 / 0,25.
        // HHI = 0,390625 + 0,015625 + 0,0625 = 0,46875.
        var c = NetworkDescription.Concentrate([["a"], ["a"], ["a", "b"], ["c"]])!;

        Assert.Equal(4, c.Subjects);
        Assert.Equal(3, c.Distinct);
        Assert.Equal(0.46875, c.Hhi, 10);
        Assert.Equal(1.0, c.TopShare, 10);
    }

    [Fact]
    public void Small_groups_print_no_exact_counts_and_no_asn()
    {
        var day = Decision.AddDays(-3);
        var positives = Enumerable.Range(1, 3).Select(i => Positive("p" + i)).ToList();
        var index = Index(
            positives.Select(p => A(p.Id, day, "198.51.100.9")),
            [Asn("198.51.100.9", day, 64500, false)]);

        var text = NetworkDescription.ToText(NetworkDescription.Compute(positives, index.ProfileOf, new EvaluationConfig { BootstrapIterations = 50 }), 10);

        Assert.Contains("birim=<5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("3/3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AS64500", text, StringComparison.Ordinal);
        Assert.DoesNotContain("198.51.100", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Asn_is_listed_only_for_positives_and_only_when_count_and_complement_are_at_least_five()
    {
        var day = Decision.AddDays(-3);
        // 20 pozitif: 12'si AS64500 (tümleyen 8: yazılır), 8'i AS64501 (tümleyen 12: yazılır).
        // 20 kontrol: hepsi AS64502. Kontrol grubunun ASN'i hiçbir koşulda yazılmaz.
        var positives = Enumerable.Range(1, 20).Select(i => Positive("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var controls = Enumerable.Range(1, 20).Select(i => Control("k" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Decision)).ToList();
        var dns = positives.Select((p, i) => A(p.Id, day, i < 12 ? "198.51.100.1" : "198.51.100.2"))
            .Concat(controls.Select(k => A(k.Id, day, "203.0.113.1")));
        var index = Index(dns,
        [
            Asn("198.51.100.1", day, 64500, false), Asn("198.51.100.2", day, 64501, false), Asn("203.0.113.1", day, 64502, false),
        ]);

        var rows = NetworkDescription.Compute([.. positives, .. controls], index.ProfileOf, new EvaluationConfig { BootstrapIterations = 50 });
        var text = NetworkDescription.ToText(rows, 10);

        Assert.Contains("AS64500 12/20", text, StringComparison.Ordinal);
        Assert.Contains("AS64501 8/20", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AS64502", text, StringComparison.Ordinal);
        Assert.Empty(rows.Single(r => !r.IsPositive).SharedAsns);
    }

    [Fact]
    public void Asn_with_small_complement_is_not_listed()
    {
        var day = Decision.AddDays(-3);
        // 12 pozitifin 10'u AS64500: tümleyen 2 (< 5). "10/12" yazılsaydı öteki 2 sitenin bu ağda olmadığı anlaşılırdı.
        var positives = Enumerable.Range(1, 12).Select(i => Positive("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var index = Index(
            positives.Select((p, i) => A(p.Id, day, i < 10 ? "198.51.100.1" : "198.51.100.2")),
            [Asn("198.51.100.1", day, 64500, false), Asn("198.51.100.2", day, 64501, false)]);

        var row = NetworkDescription.Compute(positives, index.ProfileOf, new EvaluationConfig { BootstrapIterations = 50 }).Single();

        Assert.Empty(row.SharedAsns);
    }

    [Fact]
    public void Port_lines_are_hidden_when_the_outside_cdn_line_is_a_small_cell()
    {
        var day = Decision.AddDays(-3);
        // 12 pozitifin 9'u CDN dışında: tümleyen 3 (< 5), satır gizli. Port satırlarının paydası (InternetDB cevabı bilinen)
        // 9 olurdu ve gizlenen sayıyı geri verirdi; bu yüzden yazılmaz.
        var positives = Enumerable.Range(1, 12).Select(i => Positive("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var index = Index(
            positives.Select((p, i) => A(p.Id, day, i < 9 ? "198.51.100.1" : "192.0.2.1")),
            [Asn("198.51.100.1", day, 64500, false), Asn("192.0.2.1", day, 13335, true)],
            [new PortFact("198.51.100.1", day, true, [443])]);

        var text = NetworkDescription.ToText(NetworkDescription.Compute(positives, index.ProfileOf, new EvaluationConfig { BootstrapIterations = 50 }), 10);

        Assert.Contains("InternetDB cevabı bilinen (CDN/bulut dışı IP'si olanlardan): - (üst satır küçük hücre", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/9", text, StringComparison.Ordinal);
        Assert.Contains("taban (DNS kaydı bilinen)=12", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Concentration_is_not_printed_when_its_denominator_line_is_a_small_cell()
    {
        var day = Decision.AddDays(-3);
        // 11 pozitifin hepsinin NS sağlayıcısı biliniyor: "11/11" küçük hücre (tümleyen 0). 6 sağlayıcıyla en büyük 3'ün payı
        // %54,5 = 6/11 olurdu ve gizlenen paydayı (11) ele verirdi.
        var positives = Enumerable.Range(1, 11).Select(i => Positive("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var index = Index(positives.Select((p, i) =>
            new DnsFact(p.Id, day, "NS", DnsOutcome.Success, DnsErrorKind.None, false, [], ["saglayici-" + (i % 6) + ".example"]))
            .Concat(positives.Select(p => new DnsFact(p.Id, day, "A", DnsOutcome.NxDomain, DnsErrorKind.None, false, [], []))));

        var text = NetworkDescription.ToText(NetworkDescription.Compute(positives, index.ProfileOf, new EvaluationConfig { BootstrapIterations = 50 }), 10);

        Assert.Contains("NS sağlayıcı yoğunlaşması: - (paydası küçük hücre", text, StringComparison.Ordinal);
        Assert.DoesNotContain("54.5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("saglayici", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cluster_total_is_hidden_when_multi_member_clusters_are_few()
    {
        // 12 pozitif, tek bir 5'li küme: çok üyelideki pozitif 5/12 yazılır, tek üyeli = 7 de buradan bulunur. Toplam küme (8)
        // yazılsaydı çok üyeli küme sayısı (1) çıkarmayla bulunurdu.
        var positives = Enumerable.Range(1, 12).Select(i => Positive("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var observations = Enumerable.Range(1, 5).Select(i => Name("p0" + i, "borsa" + i + ".example")).ToList();

        var analysis = PositiveClusters.Analyze(positives, observations, Index([]).ProfileOf, new EvaluationConfig());
        var text = PositiveClusters.ToText(analysis, 10);

        Assert.Equal(8, analysis.Sizes.Count);
        Assert.Contains("Çok üyeli bir kümedeki pozitif: 5/12", text, StringComparison.Ordinal);
        Assert.Contains("Küme sayısı: -, çok üyeli küme: <5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Port_profile_ignores_cdn_ips()
    {
        var day = Decision.AddDays(-3);
        var p = Positive("p1");
        var index = Index(
            [A("p1", day, "192.0.2.1", "198.51.100.1")],
            [Asn("192.0.2.1", day, 13335, true), Asn("198.51.100.1", day, 64500, false)],
            [new PortFact("192.0.2.1", day, true, [80, 443, 8080]), new PortFact("198.51.100.1", day, true, [443])]);

        var row = NetworkDescription.Compute([p], index.ProfileOf, new EvaluationConfig()).Single();

        // CDN IP'sindeki 8080 sitenin değil CDN'in portudur: "80/443 dışında" sayılmaz.
        Assert.Equal(0, row.PortsBeyondWeb.Hits);
        Assert.Equal(1, row.PortsListed.Hits);
    }
}
