using System.Security.Cryptography;

namespace Fraud.Evaluation.Commitments;

/// <summary>
/// RFC 6962 Bölüm 2.1 Merkle ağacı: kök, tek kayıt kanıtı (audit path) ve doğrulama.
/// </summary>
/// <remarks>
/// <para>Önekler: Yaprak <c>SHA-256(0x00 || veri)</c>, iç düğüm <c>SHA-256(0x01 || sol || sağ)</c>. Farklı önek, bir
/// iç düğümün yaprak gibi gösterilmesini (ikinci ön görüntü saldırısı) engeller.</para>
/// <para>Bölme kuralı: n yaprak için k = n'den küçük en büyük ikinin kuvveti. Sol alt ağaç ilk k yaprak, sağ alt
/// ağaç kalanlar. Tek sayıda yaprakta yaprak kopyalanmaz (Bitcoin'den farklı); ağaç dengesiz olur.</para>
/// <para>Bu kod Collectors'taki CT Merkle kodundan bilerek ayrı ve özyinelemelidir: Evaluation yalnızca Domain'e
/// bağlı ve burada hız değil okunabilirlik önemli. Doğrulama, kanıt üretiminin adımlarını tersten izler.</para>
/// </remarks>
public static class MerkleTree
{
    public static byte[] LeafHash(ReadOnlySpan<byte> data)
    {
        var input = new byte[data.Length + 1];
        input[0] = 0x00;
        data.CopyTo(input.AsSpan(1));
        return SHA256.HashData(input);
    }

    public static byte[] NodeHash(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var input = new byte[1 + left.Length + right.Length];
        input[0] = 0x01;
        left.CopyTo(input.AsSpan(1));
        right.CopyTo(input.AsSpan(1 + left.Length));
        return SHA256.HashData(input);
    }

    /// <summary>MTH(D[n]): yaprak özetlerinden kök. Boş ağaç taahhüt edilmez.</summary>
    public static byte[] Root(IReadOnlyList<byte[]> leafHashes)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);
        if (leafHashes.Count == 0)
        {
            throw new ArgumentException("Boş liste taahhüt edilmez.", nameof(leafHashes));
        }

        return RootOf(leafHashes, 0, leafHashes.Count);
    }

    /// <summary>PATH(m, D[n]): m. yaprağın kökle bağı. Liste alttan üste sıralı (ilk eleman yaprağın kardeşi).</summary>
    public static IReadOnlyList<byte[]> InclusionProof(IReadOnlyList<byte[]> leafHashes, int index)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, leafHashes.Count);
        var path = new List<byte[]>();
        Path(leafHashes, index, 0, leafHashes.Count, path);
        return path;
    }

    /// <summary>
    /// Kanıtı doğrular: yaprak ve kanıttan kökü yeniden kurar, verilen kökle karşılaştırır. Diğer yaprakların
    /// içeriğine gerek yoktur; kanıttaki özetler hiçbir kaydı açmaz.
    /// </summary>
    public static bool Verify(byte[] leafHash, int index, int treeSize, IReadOnlyList<byte[]> proof, byte[] expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(leafHash);
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(expectedRoot);
        if (index < 0 || index >= treeSize || proof.Any(p => p.Length != 32))
        {
            return false;
        }

        var root = RootFromProof(leafHash, index, treeSize, proof, proof.Count);
        return root is not null && CryptographicOperations.FixedTimeEquals(root, expectedRoot);
    }

    /// <summary>n'den küçük en büyük ikinin kuvveti (n &gt; 1). Örnek: 5 → 4, 8 → 4, 9 → 8.</summary>
    public static int SplitPoint(int n)
    {
        var k = 1;
        while (k * 2 < n)
        {
            k *= 2;
        }

        return k;
    }

    private static byte[] RootOf(IReadOnlyList<byte[]> leaves, int start, int count)
    {
        if (count == 1)
        {
            return leaves[start];
        }

        var k = SplitPoint(count);
        return NodeHash(RootOf(leaves, start, k), RootOf(leaves, start + k, count - k));
    }

    private static void Path(IReadOnlyList<byte[]> leaves, int index, int start, int count, List<byte[]> path)
    {
        if (count == 1)
        {
            return;
        }

        var k = SplitPoint(count);
        if (index < k)
        {
            // Yaprak solda: önce sol alt ağacın içindeki yol, sonra sağ alt ağacın kökü.
            Path(leaves, index, start, k, path);
            path.Add(RootOf(leaves, start + k, count - k));
        }
        else
        {
            Path(leaves, index - k, start + k, count - k, path);
            path.Add(RootOf(leaves, start, k));
        }
    }

    /// <summary>
    /// Path'in tersi: Kanıtın son elemanı en üstteki kardeştir. Aynı bölme kuralıyla aşağı inilir; yaprak tek
    /// başına kalınca (count == 1) bütün kanıt tüketilmiş olmalıdır, yoksa kanıt bu ağaç boyutuna ait değildir.
    /// </summary>
    private static byte[]? RootFromProof(byte[] leaf, int index, int count, IReadOnlyList<byte[]> proof, int used)
    {
        if (count == 1)
        {
            return used == 0 ? leaf : null;
        }

        if (used == 0)
        {
            return null;
        }

        var k = SplitPoint(count);
        var sibling = proof[used - 1];
        if (index < k)
        {
            var left = RootFromProof(leaf, index, k, proof, used - 1);
            return left is null ? null : NodeHash(left, sibling);
        }

        var right = RootFromProof(leaf, index - k, count - k, proof, used - 1);
        return right is null ? null : NodeHash(sibling, right);
    }
}
