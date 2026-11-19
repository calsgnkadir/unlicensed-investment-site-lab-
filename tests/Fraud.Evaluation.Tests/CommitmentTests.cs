using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using Fraud.Evaluation.Commitments;

namespace Fraud.Evaluation.Tests;

/// <summary>Merkle ağacı ve taahhüt aracı: RFC 6962 vektörleri, tek ve tek sayıda yaprak, bozulmuş kanıt.</summary>
public sealed class CommitmentTests
{
    // RFC 6962 test vektörleri (certificate-transparency deposu; Integration MerkleTests ile aynı).
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

    private static List<byte[]> LeafHashes(int n) => [.. Enumerable.Range(0, n).Select(i => MerkleTree.LeafHash(BitConverter.GetBytes(i)))];

    [Fact]
    public void Roots_match_rfc6962_test_vectors()
    {
        var leaves = Leaves.Select(l => MerkleTree.LeafHash(Convert.FromHexString(l))).ToList();
        for (var n = 1; n <= leaves.Count; n++)
        {
            Assert.Equal(Roots[n - 1], Convert.ToHexStringLower(MerkleTree.Root(leaves[..n])));
        }
    }

    [Fact]
    public void Single_leaf_tree_has_an_empty_proof_and_the_leaf_as_root()
    {
        var leaves = LeafHashes(1);
        Assert.Equal(leaves[0], MerkleTree.Root(leaves));
        Assert.Empty(MerkleTree.InclusionProof(leaves, 0));
        Assert.True(MerkleTree.Verify(leaves[0], 0, 1, [], leaves[0]));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(33)]
    public void Every_leaf_proof_verifies_including_odd_sizes(int n)
    {
        var leaves = LeafHashes(n);
        var root = MerkleTree.Root(leaves);
        for (var i = 0; i < n; i++)
        {
            Assert.True(MerkleTree.Verify(leaves[i], i, n, MerkleTree.InclusionProof(leaves, i), root));
        }
    }

    [Fact]
    public void Tampered_wrong_index_or_wrong_size_proofs_fail()
    {
        var leaves = LeafHashes(7);
        var root = MerkleTree.Root(leaves);
        var proof = MerkleTree.InclusionProof(leaves, 4).Select(p => (byte[])p.Clone()).ToList();
        Assert.True(MerkleTree.Verify(leaves[4], 4, 7, proof, root));

        Assert.False(MerkleTree.Verify(leaves[4], 3, 7, proof, root));       // yanlış indeks
        // Yanlış boyut: 5 yapraklı ağaçta 4. yaprağın yolu tek adımdır, 3 adımlık kanıt bu ağaca uymaz.
        // (Not: 7 ve 8 yaprakta 4. yaprağın yol şekli aynıdır; orada boyutu kök farkı ve açık dosyadaki sayı yakalar.)
        Assert.False(MerkleTree.Verify(leaves[4], 4, 5, proof, root));
        Assert.False(MerkleTree.Verify(leaves[5], 4, 7, proof, root));       // başka yaprak
        Assert.False(MerkleTree.Verify(leaves[4], 4, 7, proof[..^1], root)); // eksik kanıt
        proof[0][0] ^= 0x01;                                                  // bozulmuş bayt
        Assert.False(MerkleTree.Verify(leaves[4], 4, 7, proof, root));
    }

    [Fact]
    public void One_record_is_verified_without_opening_the_others()
    {
        string[] records = ["kayit-a.example", "kayit-b.example", "kayit-c.example", "kayit-d.example", "kayit-e.example"];
        var (publicCommitment, opening) = CommitmentTool.Create(records, new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), null);

        // Açık dosya hiçbir kaydı içermez.
        var publicJson = CommitmentTool.ToJson(publicCommitment);
        Assert.All(records, r => Assert.DoesNotContain(r, publicJson, StringComparison.Ordinal));
        Assert.Equal(5, publicCommitment.Count);
        Assert.Null(publicCommitment.Previous);

        var index = opening.Entries.Single(e => e.Record == "kayit-c.example").Index;
        var proof = CommitmentTool.Prove(opening, index);
        var proofJson = CommitmentTool.ToJson(proof);
        // Kanıt yalnızca kendi kaydını açar.
        Assert.All(records.Where(r => r != "kayit-c.example"), r => Assert.DoesNotContain(r, proofJson, StringComparison.Ordinal));

        var roundTripPublic = CommitmentTool.FromJson<PublicCommitment>(publicJson);
        var roundTripProof = CommitmentTool.FromJson<InclusionProofFile>(proofJson);
        Assert.True(CommitmentTool.Verify(roundTripPublic, roundTripProof));
        Assert.False(CommitmentTool.Verify(roundTripPublic, roundTripProof with { Record = "kayit-x.example" }));
        Assert.False(CommitmentTool.Verify(roundTripPublic, roundTripProof with { Salt = new string('0', 64) }));
    }

    [Fact]
    public void Salts_are_32_bytes_per_record_and_the_chain_links_the_previous_file()
    {
        var (first, opening) = CommitmentTool.Create(["a1", "a2", "a3"], DateTimeOffset.UnixEpoch, null);
        Assert.All(opening.Entries, e => Assert.Equal(64, e.Salt.Length));
        Assert.Equal(3, opening.Entries.Select(e => e.Salt).Distinct().Count());

        var firstBytes = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(first));
        var (second, _) = CommitmentTool.Create(["b1"], DateTimeOffset.UnixEpoch, firstBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(firstBytes)), second.Previous);
    }

    [Fact]
    public void Duplicate_or_empty_records_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CommitmentTool.Create(["a", "a"], DateTimeOffset.UnixEpoch, null));
        Assert.Throws<ArgumentException>(() => CommitmentTool.Create([], DateTimeOffset.UnixEpoch, null));
    }

    [Fact]
    public void Timestamp_request_is_a_valid_rfc3161_structure()
    {
        var file = Encoding.UTF8.GetBytes("{\"root\":\"00\"}");
        var tsq = CommitmentTool.TimestampRequest(file, 12345);

        var reader = new AsnReader(tsq, AsnEncodingRules.DER).ReadSequence();
        Assert.Equal(1, (int)reader.ReadInteger());
        var imprint = reader.ReadSequence();
        var algorithm = imprint.ReadSequence();
        Assert.Equal("2.16.840.1.101.3.4.2.1", algorithm.ReadObjectIdentifier());
        algorithm.ReadNull();
        Assert.Equal(SHA256.HashData(file), imprint.ReadOctetString());
        Assert.Equal(12345, (long)reader.ReadInteger());
        Assert.True(reader.ReadBoolean());
        Assert.False(reader.HasData);
    }
}
