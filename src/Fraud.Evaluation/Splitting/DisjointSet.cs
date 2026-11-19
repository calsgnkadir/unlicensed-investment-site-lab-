namespace Fraud.Evaluation.Splitting;

/// <summary>
/// Birleşim-bul (union-find): "Bu iki öğe aynı bileşende mi?" sorusunu cevaplayan klasik yapı.
/// Kümeleme (7.5) ve "aynı bülten ya da aynı küme iki tarafa girmez" kuralı bununla uygulanır.
/// </summary>
/// <remarks>
/// Her öğenin bir "ebeveyni" var; kök, kendi ebeveyni olan öğedir. İki öğe aynı köke çıkıyorsa aynı bileşendedir.
/// Yol sıkıştırma (Find içinde ebeveyni köke bağlamak) yalnızca hız içindir, sonucu değiştirmez.
/// </remarks>
public sealed class DisjointSet
{
    private readonly Dictionary<string, string> _parent = new(StringComparer.Ordinal);

    public void Add(string item) => _parent.TryAdd(item, item);

    public string Find(string item)
    {
        Add(item);
        var root = item;
        while (_parent[root] != root)
        {
            root = _parent[root];
        }

        // Yol sıkıştırma: Yoldaki her öğeyi doğrudan köke bağla.
        while (_parent[item] != root)
        {
            var next = _parent[item];
            _parent[item] = root;
            item = next;
        }

        return root;
    }

    /// <summary>İki bileşeni birleştirir. Kök, sıralamada küçük olan seçilir; böylece sonuç ekleme sırasından bağımsızdır.</summary>
    public void Union(string a, string b)
    {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA == rootB)
        {
            return;
        }

        if (string.CompareOrdinal(rootA, rootB) < 0)
        {
            _parent[rootB] = rootA;
        }
        else
        {
            _parent[rootA] = rootB;
        }
    }
}

/// <summary>İki birim arasında küme kenarı nedeni (KONTROL_GRUBU_YONTEMI.md 7.5).</summary>
public enum ClusterEdgeKind
{
    /// <summary>Aynı sertifikanın SAN listesinde birlikte.</summary>
    SharedCertificate = 1,

    /// <summary>Aynı IPv4 /24 veya IPv6 /48.</summary>
    SharedNetwork = 2,

    /// <summary>Rakamlar ve TLD atıldıktan sonra aynı gövde.</summary>
    NameStem = 3,
}

/// <summary>Bir küme kenarı. <paramref name="ViaCdnOrCloud"/> doğruysa kenar KULLANILMAZ.</summary>
public sealed record ClusterEdge(string A, string B, ClusterEdgeKind Kind, bool ViaCdnOrCloud = false);

/// <summary>
/// Kenarlardan küme kimliği üretir. Kümeler bölmeden önce bir kez hesaplanıp dondurulur.
/// </summary>
public static class ClusterBuilder
{
    /// <summary>Birim kimliği → küme kimliği. Küme kimliği, kümedeki sıralamada en küçük birim kimliğidir.</summary>
    /// <remarks>
    /// CDN veya bulut üzerinden gelen ağ kenarları atlanır: Binlerce ilgisiz site aynı CDN IP'sini paylaşır; bu kenar
    /// kullanılsaydı neredeyse her şey tek kümeye düşerdi ve "aynı operatör" anlamı kalmazdı.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Build(IEnumerable<string> subjectIds, IEnumerable<ClusterEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(subjectIds);
        ArgumentNullException.ThrowIfNull(edges);
        var set = new DisjointSet();
        var ids = subjectIds.ToList();
        foreach (var id in ids)
        {
            set.Add(id);
        }

        foreach (var edge in edges.Where(e => !e.ViaCdnOrCloud))
        {
            set.Union(edge.A, edge.B);
        }

        return ids.ToDictionary(id => id, set.Find, StringComparer.Ordinal);
    }
}
