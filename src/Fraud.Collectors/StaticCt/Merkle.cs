using System.Numerics;
using System.Security.Cryptography;

namespace Fraud.Collectors.StaticCt;

/// <summary>RFC 6962 Bölüm 2.1 Merkle özetleri.</summary>
/// <remarks>
/// Yaprak: <c>SHA-256(0x00 || veri)</c>. İç düğüm: <c>SHA-256(0x01 || sol || sağ)</c>.
/// Önekler, bir yaprağın iç düğüm gibi gösterilmesini (ikinci ön görüntü saldırısını) engeller.
/// </remarks>
public static class Merkle
{
    public const int HashSize = 32;

    /// <summary>Boş ağacın kökü: <c>SHA-256("")</c>.</summary>
    public static byte[] EmptyRoot() => SHA256.HashData([]);

    public static byte[] LeafHash(ReadOnlySpan<byte> leafData)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([0x00]);
        hash.AppendData(leafData);
        return hash.GetHashAndReset();
    }

    public static byte[] NodeHash(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        Span<byte> buffer = stackalloc byte[1 + (2 * HashSize)];
        buffer[0] = 0x01;
        left.CopyTo(buffer[1..]);
        right.CopyTo(buffer[(1 + HashSize)..]);
        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Art arda duran <paramref name="count"/> özetin (ikinin kuvveti) oluşturduğu tam alt ağacın kökü.
    /// </summary>
    public static byte[] SubtreeRoot(ReadOnlySpan<byte> hashes, int count)
    {
        if (count < 1 || !BitOperations.IsPow2(count) || hashes.Length < count * HashSize)
        {
            throw new ArgumentException("Özet sayısı ikinin kuvveti olmalı ve veri yetmeli.", nameof(count));
        }

        var level = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            level[i] = hashes.Slice(i * HashSize, HashSize).ToArray();
        }

        for (var width = count; width > 1; width /= 2)
        {
            for (var i = 0; i < width / 2; i++)
            {
                level[i] = NodeHash(level[2 * i], level[(2 * i) + 1]);
            }
        }

        return level[0];
    }
}

/// <summary>
/// [0, Size) aralığının "sağ kenarı" (compact range): Size'ın ikili açılımındaki her 1 biti için bir tam alt ağaç kökü,
/// büyükten küçüğe. Bu liste, ağacı baştan okumadan sonuna kayıt eklemeye ve herhangi bir boyutta kökü
/// hesaplamaya yeter.
/// </summary>
/// <remarks>
/// Örnek: Size = 6 = 4 + 2 ise liste [kök(0..3), kök(4..5)]. 7. yaprak eklenince [kök(0..3), kök(4..5), yaprak6] olur;
/// 8. yaprakta üçü birleşip tek kök kalır. RFC 6962'deki MTH tanımı, bu listenin sağdan sola katlanmasına eşittir.
/// </remarks>
public sealed class CompactRange
{
    private readonly List<(int Height, byte[] Hash)> _nodes = [];

    public long Size { get; private set; }

    public int NodeCount => _nodes.Count;

    /// <summary>Kaydedilmiş sağ kenardan (32 baytlık özetler, büyükten küçüğe) kurar.</summary>
    public static CompactRange FromFrontier(long size, ReadOnlySpan<byte> packed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        var heights = HeightsOf(size);
        if (packed.Length != heights.Count * Merkle.HashSize)
        {
            throw new ArgumentException("Sağ kenar özet sayısı boyutla uyuşmuyor.", nameof(packed));
        }

        var range = new CompactRange { Size = size };
        for (var i = 0; i < heights.Count; i++)
        {
            range._nodes.Add((heights[i], packed.Slice(i * Merkle.HashSize, Merkle.HashSize).ToArray()));
        }

        return range;
    }

    /// <summary>Size'ın ikili açılımındaki alt ağaç yükseklikleri, büyükten küçüğe.</summary>
    public static List<int> HeightsOf(long size)
    {
        var heights = new List<int>();
        for (var h = 62; h >= 0; h--)
        {
            if ((size & (1L << h)) != 0)
            {
                heights.Add(h);
            }
        }

        return heights;
    }

    public void AppendLeaf(byte[] leafHash) => Append(0, leafHash);

    /// <summary>
    /// Sona <c>2^height</c> yapraklık tam bir alt ağaç ekler. Size, <c>2^height</c>'in katı olmalı
    /// (alt ağaç hizalı olmalı); aynı yükseklikteki komşular birleştirilir.
    /// </summary>
    public void Append(int height, byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != Merkle.HashSize)
        {
            throw new ArgumentException("Özet 32 bayt olmalı.", nameof(hash));
        }

        if (height is < 0 or > 62 || (Size & ((1L << height) - 1)) != 0)
        {
            throw new InvalidOperationException("Alt ağaç sağ kenara hizalı değil.");
        }

        _nodes.Add((height, hash));
        while (_nodes.Count >= 2 && _nodes[^1].Height == _nodes[^2].Height)
        {
            var right = _nodes[^1];
            var left = _nodes[^2];
            _nodes.RemoveRange(_nodes.Count - 2, 2);
            _nodes.Add((left.Height + 1, Merkle.NodeHash(left.Hash, right.Hash)));
        }

        Size += 1L << height;
    }

    /// <summary>[0, Size) ağacının RFC 6962 kökü.</summary>
    public byte[] Root()
    {
        if (_nodes.Count == 0)
        {
            return Merkle.EmptyRoot();
        }

        var root = _nodes[^1].Hash;
        for (var i = _nodes.Count - 2; i >= 0; i--)
        {
            root = Merkle.NodeHash(_nodes[i].Hash, root);
        }

        return root;
    }

    public byte[] ToFrontier()
    {
        var packed = new byte[_nodes.Count * Merkle.HashSize];
        for (var i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].Hash.CopyTo(packed, i * Merkle.HashSize);
        }

        return packed;
    }

    public CompactRange Clone()
    {
        var copy = new CompactRange { Size = Size };
        copy._nodes.AddRange(_nodes);
        return copy;
    }

    /// <summary>
    /// [start, end) aralığını hizalı tam alt ağaçlara böler (soldan sağa, açgözlü): Her adımda başlangıca hizalı ve
    /// aralığa sığan en büyük alt ağaç seçilir. Dönen her öğe (yükseklik, o yükseklikteki düğüm sırası).
    /// </summary>
    public static IEnumerable<(int Height, long Index)> Decompose(long start, long end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        var x = start;
        while (x < end)
        {
            var maxBySize = 63 - BitOperations.LeadingZeroCount((ulong)(end - x));
            var maxByAlignment = x == 0 ? 62 : BitOperations.TrailingZeroCount(x);
            var h = Math.Min(maxBySize, maxByAlignment);
            yield return (h, x >> h);
            x += 1L << h;
        }
    }
}
