using System.Security.Cryptography;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.Collection;

namespace Fraud.Integration.Tests.StaticCt;

public sealed class MerkleTests
{
    // RFC 6962 test vektörleri (certificate-transparency deposunun Merkle testlerindeki 8 yaprak).
    private static readonly string[] Leaves = ["", "00", "10", "2021", "3031", "40414243", "5051525354555657", "606162636465666768696a6b6c6d6e6f"];

    private static readonly string[] Roots =
    [
        "6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d",
        "fac54203e7cc696cf0dfcb42c92a1d9dbaf70ad9e621f4bd8d98662f00e3c125",
        "aeb6bcfe274b70a14fb067a5e5578264db0fa9b51af5e0ba159158f329e06e77",
        "d37ee418976dd95753c1c73862b9398fa2a2cf9b4ff0fdfe8b30cd95209614b7",
        "4e3bbb1f7b478dcfe71fb631631519a3bca12c9aefca1612bfce4c13a86264d4",
        "76e67dadbcdf1e10e1b74ddc608abd2f98dfb16fbce75277b5232a127f2087ef",
        "ddb89be403809e325750d3d263cd78929c2942b7942a34b77e122c9594a74c8c",
        "5dc9da79a70659a9ad559cb701ded9a2ab9d823aad2f4960cfe370eff4604328",
    ];

    [Fact]
    public void Compact_range_matches_rfc6962_test_vectors()
    {
        var range = new CompactRange();
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Convert.ToHexStringLower(range.Root()));

        for (var i = 0; i < Leaves.Length; i++)
        {
            range.AppendLeaf(Merkle.LeafHash(Convert.FromHexString(Leaves[i])));
            Assert.Equal(Roots[i], Convert.ToHexStringLower(range.Root()));
        }
    }

    [Fact]
    public void Compact_range_matches_the_recursive_definition_and_survives_a_round_trip()
    {
        var hashes = Enumerable.Range(0, 1100).Select(i => SHA256.HashData(BitConverter.GetBytes(i))).ToList();
        var range = new CompactRange();
        for (var n = 1; n <= hashes.Count; n++)
        {
            range.AppendLeaf(hashes[n - 1]);
            if (n % 37 == 0 || n is 255 or 256 or 257 or 511 or 512 or 1024)
            {
                Assert.Equal(Reference.Mth(hashes, 0, n), range.Root());

                // Veritabanına yazılan sağ kenardan geri kurulunca aynı kök çıkar.
                var restored = CompactRange.FromFrontier(n, range.ToFrontier());
                Assert.Equal(range.Root(), restored.Root());
            }
        }
    }

    [Fact]
    public void Frontier_with_wrong_length_is_refused()
    {
        Assert.Throws<ArgumentException>(() => CompactRange.FromFrontier(3, new byte[32]));
    }

    [Theory]
    [InlineData(0, 70000)]
    [InlineData(768, 1281)]
    [InlineData(5, 6)]
    [InlineData(65536, 65537)]
    public void Decompose_covers_the_range_with_aligned_subtrees(long start, long end)
    {
        var x = start;
        foreach (var (h, i) in CompactRange.Decompose(start, end))
        {
            Assert.Equal(x, i << h);
            x += 1L << h;
        }

        Assert.Equal(end, x);
    }

    [Theory]
    [InlineData(1234067, "x001/x234/067")]
    [InlineData(0, "000")]
    [InlineData(999, "999")]
    [InlineData(1000, "x001/000")]
    public void Tile_index_is_encoded_in_three_digit_groups(long index, string expected)
    {
        Assert.Equal(expected, TilePath.EncodeIndex(index));
    }

    [Fact]
    public void Tile_paths_follow_the_spec()
    {
        Assert.Equal("tile/data/005", TilePath.DataTile(5));
        Assert.Equal("tile/0/x001/234.p/17", TilePath.HashTile(0, 1234, 17));
        Assert.Equal("tile/2/000.p/1", TilePath.HashTile(2, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePath.HashTile(6, 0));
    }

    [Theory]
    [InlineData(70000)] // Belgedeki örnek: 273 tam + 112'lik kısmi L0, 1 tam + 17'lik kısmi L1, 1'lik kısmi L2.
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(1)]
    public async Task Root_rebuilt_from_hash_tiles_matches_the_recursive_definition(int size)
    {
        var hashes = Enumerable.Range(0, size).Select(i => SHA256.HashData(BitConverter.GetBytes(i))).ToList();
        var client = new LeafHashClient(hashes);
        var reader = new TreeHashReader(client, size);

        var range = await reader.BuildFrontierAsync(size, TestContext.Current.CancellationToken);

        Assert.Equal(Reference.Mth(hashes, 0, size), range.Root());
        if (size == 70000)
        {
            Assert.Contains("0:273:112", client.Requested);
            Assert.Contains("1:1:17", client.Requested);
            Assert.Contains("2:0:1", client.Requested);
        }
    }

    [Fact]
    public async Task Frontier_from_tiles_plus_streamed_leaves_equals_the_root()
    {
        var hashes = Enumerable.Range(0, 3000).Select(i => SHA256.HashData(BitConverter.GetBytes(i))).ToList();
        var reader = new TreeHashReader(new LeafHashClient(hashes), 3000);

        var range = await reader.BuildFrontierAsync(1280, TestContext.Current.CancellationToken);
        for (var i = 1280; i < 2048; i++)
        {
            range.AppendLeaf(hashes[i]);
        }

        await reader.AppendAsync(range, 3000, TestContext.Current.CancellationToken);

        Assert.Equal(Reference.Mth(hashes, 0, 3000), range.Root());
    }

    /// <summary>Yalnızca yaprak özetlerinden hash tile sunan sahte istemci.</summary>
    private sealed class LeafHashClient(IReadOnlyList<byte[]> hashes) : IStaticCtClient
    {
        public List<string> Requested { get; } = [];

        public Task<CtFetch<Checkpoint>> GetCheckpointAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CtFetch<byte[]>> GetDataTileAsync(long tileIndex, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CtFetch<byte[]>> GetHashTileAsync(int level, long tileIndex, int width, CancellationToken cancellationToken)
        {
            Requested.Add($"{level}:{tileIndex}:{width}");
            var span = 1L << (8 * level);
            var body = new byte[width * 32];
            for (var i = 0; i < width; i++)
            {
                var start = ((tileIndex * 256) + i) * span;
                Reference.Mth(hashes, start, start + span).CopyTo(body, i * 32);
            }

            return Task.FromResult(new CtFetch<byte[]>(body, CollectionErrorKind.None, 200, 1));
        }
    }
}
