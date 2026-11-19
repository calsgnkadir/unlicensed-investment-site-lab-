using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using Fraud.Collectors.Evaluation;
using Fraud.Evaluation.Commitments;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Evaluation;

/// <summary>
/// Gizli liste taahhüdü (Kural 8): açık dosya hiçbir kaydı veya tuzu açmamalı, kanıtlar yalnızca kendi kaydını açmalı,
/// sahte kanıt (başka indeks, başka ağaç boyutu, iç düğümü yaprak gibi gösterme) doğrulanmamalı. Gerçek alan adı yok.
/// </summary>
public sealed class CommitmentAttackTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static List<string> Records(int n) =>
        [.. Enumerable.Range(0, n).Select(i => "aday-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".example")];

    [Fact]
    public void Salts_come_from_a_csprng_one_per_record_and_the_public_file_opens_nothing()
    {
        var records = Records(300);
        var (pub, opening) = CommitmentTool.Create(records, At, previousPublicFile: null);

        var salts = opening.Entries.Select(e => e.Salt).ToList();
        Assert.Equal(300, salts.Distinct(StringComparer.Ordinal).Count());
        Assert.All(salts, s => Assert.Equal(CommitmentTool.SaltBytes * 2, s.Length));

        var json = CommitmentTool.ToJson(pub);
        Assert.DoesNotContain(".example", json, StringComparison.Ordinal);
        Assert.DoesNotContain("aday-", json, StringComparison.Ordinal);
        Assert.All(salts.Take(20), s => Assert.DoesNotContain(s, json, StringComparison.Ordinal));
        Assert.All(opening.Commitments.Take(20), c => Assert.DoesNotContain(c, json, StringComparison.Ordinal));
        Assert.Equal(["Count", "CreatedAt", "LeafRule", "Previous", "Root", "Version"],
            System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Leaf_order_does_not_follow_the_input_or_alphabetical_order()
    {
        // Sıralı girdi verilse bile indeks, kaydın alfabedeki yerini söylememeli (yapraklar c'ye göre sıralı).
        var records = Records(200);
        var (_, opening) = CommitmentTool.Create(records, At, null);

        var positionOfRecord = opening.Entries.ToDictionary(e => e.Record, e => e.Index, StringComparer.Ordinal);
        var inOrder = records.Zip(records.Skip(1)).Count(p => positionOfRecord[p.First] < positionOfRecord[p.Second]);
        Assert.InRange(inOrder, 60, 140); // rastgele sırada beklenen ~100/199
    }

    [Fact]
    public void Proof_opens_only_its_own_record_and_verifies()
    {
        var (pub, opening) = CommitmentTool.Create(Records(13), At, null);
        var proof = CommitmentTool.Prove(opening, 7);

        Assert.True(CommitmentTool.Verify(pub, proof));
        var json = CommitmentTool.ToJson(proof);
        foreach (var other in opening.Entries.Where(e => e.Index != 7))
        {
            Assert.DoesNotContain(other.Record, json, StringComparison.Ordinal);
            Assert.DoesNotContain(other.Salt, json, StringComparison.Ordinal);
        }
    }

    public static TheoryData<int> TreeSizes => new() { 1, 2, 3, 5, 8, 13, 64, 100 };

    [Theory]
    [MemberData(nameof(TreeSizes))]
    public void Forged_index_size_path_or_record_never_verifies(int n)
    {
        var (pub, opening) = CommitmentTool.Create(Records(n), At, null);
        for (var i = 0; i < n; i++)
        {
            var proof = CommitmentTool.Prove(opening, i);
            Assert.True(CommitmentTool.Verify(pub, proof));

            Assert.False(CommitmentTool.Verify(pub, proof with { Index = i + 1 }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Index = -1 }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Index = n }));
            Assert.False(CommitmentTool.Verify(pub, proof with { TreeSize = n + 1 }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Record = proof.Record.ToUpperInvariant() }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Record = proof.Record + " " }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Salt = proof.Salt[..^2] }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Salt = "zz" + proof.Salt[2..] }));
            Assert.False(CommitmentTool.Verify(pub, proof with { Path = [.. proof.Path, new string('0', 64)] }));
            if (proof.Path.Count > 0)
            {
                Assert.False(CommitmentTool.Verify(pub, proof with { Path = proof.Path.Skip(1).ToList() }));
                Assert.False(CommitmentTool.Verify(pub, proof with { Path = [proof.Path[0][..^2], .. proof.Path.Skip(1)] }));
            }

            Assert.False(CommitmentTool.Verify(pub with { Root = new string('0', 64) }, proof));
        }
    }

    [Fact]
    public void Interior_node_cannot_be_presented_as_a_leaf_of_a_smaller_tree()
    {
        // İkinci ön görüntü: 4 yapraklı ağacın kökü, "2 yapraklı ağaç (n01, n23)" ile aynı olsaydı iç düğüm yaprak gibi
        // gösterilebilirdi. 0x00/0x01 önekleri ve ağaç boyutu denetimi bunu engeller.
        var (pub, opening) = CommitmentTool.Create(Records(4), At, null);
        var leaves = opening.Commitments.Select(c => MerkleTree.LeafHash(Convert.FromHexString(c))).ToList();
        var n01 = MerkleTree.NodeHash(leaves[0], leaves[1]);
        var n23 = MerkleTree.NodeHash(leaves[2], leaves[3]);
        Assert.Equal(pub.Root, Convert.ToHexStringLower(MerkleTree.NodeHash(n01, n23)));

        // Yaprak, kayıttan hesaplanır: Bir iç düğümü yaprak yapacak kayıt+tuz bulmak SHA-256 ön görüntüsü ister.
        Assert.NotEqual(Convert.ToHexStringLower(n01), Convert.ToHexStringLower(MerkleTree.LeafHash(n01)));
        var forged = new InclusionProofFile(1, pub.Root, 2, 0, opening.Entries[0].Salt, opening.Entries[0].Record, [Convert.ToHexStringLower(n23)]);
        Assert.False(CommitmentTool.Verify(pub, forged));
        Assert.False(CommitmentTool.Verify(pub, forged with { TreeSize = 4 }));
    }

    [Fact]
    public void Timestamp_request_is_a_valid_rfc3161_structure_over_the_public_file()
    {
        var file = Encoding.UTF8.GetBytes(CommitmentTool.ToJson(CommitmentTool.Create(Records(3), At, null).Public));
        var tsq = CommitmentTool.TimestampRequest(file, 0x1234_5678_9ABC);

        var reader = new AsnReader(tsq, AsnEncodingRules.DER);
        var req = reader.ReadSequence();
        Assert.False(reader.HasData);
        Assert.Equal(1, (int)req.ReadInteger());
        var imprint = req.ReadSequence();
        var algorithm = imprint.ReadSequence();
        Assert.Equal("2.16.840.1.101.3.4.2.1", algorithm.ReadObjectIdentifier());
        algorithm.ReadNull();
        Assert.Equal(SHA256.HashData(file), imprint.ReadOctetString());
        Assert.False(imprint.HasData);
        Assert.Equal(0x1234_5678_9ABC, (long)req.ReadInteger());
        Assert.True(req.ReadBoolean());
        Assert.False(req.HasData);

        Assert.Throws<ArgumentOutOfRangeException>(() => CommitmentTool.TimestampRequest(file, 0));
    }

    [Theory]
    [InlineData("../disari")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    [InlineData("/etc/passwd")]
    [InlineData("ad.opening")]
    [InlineData("ad\u0000")]
    public async Task Private_file_name_cannot_escape_the_commitments_directory(string name)
    {
        // Ad doğrulaması dosya okunmadan ve yazılmadan önce yapılır (komut 64 ile çıkar).
        using var output = new StringWriter();
        var args = EvaluationCommands.ParseArgs(["--records", "yok.txt", "--name", name, "--public-out", "yok.json"]);

        Assert.Equal(64, await EvaluationCommands.CommitCreateAsync(args, output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Commitment_paths_are_private_and_git_ignored()
    {
        Assert.StartsWith("data/private/", EvaluationCommands.CommitmentsDirectory, StringComparison.Ordinal);
        Assert.StartsWith("data/private/", EvaluationCommands.SubjectKeyFile, StringComparison.Ordinal);
        Assert.StartsWith("data/private/", new Fraud.Evaluation.EvaluationConfig().LockFile, StringComparison.Ordinal);
        Assert.Contains("data/private/", RepoFiles.Read(".gitignore"), StringComparison.Ordinal);
    }

    // ---- Açık bulgular ----

    [Fact]
    public void Private_outputs_cannot_land_in_a_git_tracked_folder_when_run_from_another_directory()
    {
        // Bulgu E2: "data/private/commitments" Path.GetFullPath ile ÇALIŞMA KLASÖRÜNE göre çözülür. Komut ör.
        // src/Fraud.Collectors içinden çalıştırılırsa açıklama dosyası (bütün kayıtlar + tuzlar) src/Fraud.Collectors/
        // data/private/commitments altına yazılır. .gitignore'daki "data/private/" ortasında "/" olduğu için yalnızca
        // depo köküne bağlıdır; iç içe yol yok sayılmaz (git check-ignore ile doğrulandı) ve "git add ." onu alır.
        var ignored = GitCheckIgnore("src/Fraud.Collectors/data/private/commitments/liste.opening.json");

        Assert.True(ignored, "İç içe data/private git tarafından yok sayılmıyor");
    }

    [Fact]
    public void Creating_a_commitment_never_overwrites_an_existing_opening()
    {
        // Bulgu E3: File.WriteAllTextAsync var olan dosyayı sessizce değiştirir. Eski açık taahhüt repoda kalır ama
        // tuzları kaybolur; o tarihte o listeye sahip olunduğu artık gösterilemez (ileriye dönük test geçersizleşir).
        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/EvaluationCommands.cs");

        Assert.DoesNotContain("File.WriteAllTextAsync(openingPath", code, StringComparison.Ordinal);
        Assert.Contains("FileMode.CreateNew", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Commitment_chain_link_can_be_verified()
    {
        // Bulgu E5: Previous = SHA-256(önceki açık dosya) yazılıyor ama commit-verify bunu denetlemiyor ve Create,
        // --previous olarak verilen dosyanın gerçekten bir PublicCommitment olduğuna bakmıyor. Zincir yalnızca elle
        // sha256sum ile doğrulanabilir; eski taahhüdün değiştirilmesi ancak biri bakarsa görünür.
        var code = RepoFiles.Read("src/Fraud.Collectors/Evaluation/EvaluationCommands.cs")
                   + RepoFiles.Read("src/Fraud.Evaluation/Commitments/Commitment.cs");

        Assert.Matches(@"(?i)VerifyChain|VerifyPrevious|""previous""[^\n]*CommitVerify|CommitVerify[\s\S]*Arg\(args, ""previous""\)", code);
    }

    private static bool GitCheckIgnore(string relativePath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", "check-ignore -q -- " + relativePath)
        {
            WorkingDirectory = RepoFiles.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var git = System.Diagnostics.Process.Start(psi)!;
        git.WaitForExit(10_000);
        return git.ExitCode == 0;
    }
}
