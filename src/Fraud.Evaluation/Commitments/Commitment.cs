using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fraud.Evaluation.Commitments;

/// <summary>
/// Açık taahhüt dosyası. Repoya girebilir: Alan adı veya kayıt içermez, yalnızca kök ve sayılar.
/// </summary>
/// <param name="Version">Biçim sürümü.</param>
/// <param name="CreatedAt">Taahhüdün oluşturulduğu an (UTC). Kanıt değeri için ayrıca zaman damgası gerekir.</param>
/// <param name="Count">Kayıt sayısı.</param>
/// <param name="Root">Merkle kökü (onaltılık).</param>
/// <param name="Previous">Bir önceki açık taahhüt dosyasının SHA-256'sı. İlk taahhütte <c>null</c>. Zincir, eski bir
/// taahhüdün sessizce değiştirilmesini görünür kılar.</param>
/// <param name="LeafRule">Yaprak kuralının açık yazımı: Doğrulayan kişi kodu okumadan aynı hesabı yapabilsin.</param>
public sealed record PublicCommitment(int Version, DateTimeOffset CreatedAt, int Count, string Root, string? Previous, string LeafRule);

/// <summary>Gizli açıklama dosyasındaki tek kayıt. data/private/commitments altında kalır.</summary>
public sealed record OpeningEntry(int Index, string Salt, string Record);

/// <summary>Gizli açıklama dosyası: her kaydın tuzu ve yeri, ayrıca bütün taahhüt özetleri (kanıt üretmek için).</summary>
public sealed record CommitmentOpening(int Version, string Root, IReadOnlyList<OpeningEntry> Entries, IReadOnlyList<string> Commitments);

/// <summary>Tek kayıt kanıtı. Kaydı ve tuzunu açar, diğer kayıtları açmaz.</summary>
public sealed record InclusionProofFile(int Version, string Root, int TreeSize, int Index, string Salt, string Record, IReadOnlyList<string> Path);

/// <summary>
/// Taahhüt aracı (ROADMAP Hafta 6). Liste gizli kalırken "bu tarihte bu listeye sahiptik" denebilsin diye.
/// </summary>
/// <remarks>
/// <code>
/// tuz_i     = 32 rastgele bayt (kayıt başına ayrı)
/// c_i       = SHA-256(tuz_i || UTF-8(kayıt_i))       // taahhüt: tuz bilinmeden kayıt tahmin edilemez
/// yaprak_i  = SHA-256(0x00 || c_i)                   // RFC 6962 yaprağı
/// kök       = RFC 6962 Merkle kökü; yapraklar c'ye göre sıralı
/// </code>
/// <para>Neden kayıt başına tuz: Tek bir tuz olsaydı, bir kaydı açmak tuzu da açar ve diğer kayıtlar tek tek
/// tahmin edilerek denenebilirdi. Ayrı tuzla her kayıt yalnızca kendi tuzu verilince açılır.</para>
/// <para>Neden c'ye göre sıralı: Kayıtlar alfabetik ya da eklenme sırasında dizilseydi, bir kaydın indeksi komşuları
/// hakkında ipucu verirdi. c rastgele göründüğü için sıra hiçbir şey söylemez.</para>
/// </remarks>
public static class CommitmentTool
{
    public const int SaltBytes = 32;
    public const int FormatVersion = 1;

    public const string LeafRuleText =
        "c = SHA-256(salt32 || UTF-8(record)); leaf = SHA-256(0x00 || c); node = SHA-256(0x01 || left || right); "
        + "RFC 6962 tree; leaves ordered by c ascending";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static byte[] CommitmentOf(byte[] salt, string record)
    {
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(record);
        return SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(record)]);
    }

    /// <param name="saltSource">Tuz üreteci. Gerçek kullanımda <see cref="RandomNumberGenerator.Fill"/>; testte sabit olabilir.</param>
    public static (PublicCommitment Public, CommitmentOpening Opening) Create(
        IReadOnlyList<string> records, DateTimeOffset createdAt, byte[]? previousPublicFile, Action<Span<byte>>? saltSource = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        var cleaned = records.Select(r => r.Trim()).ToList();
        if (cleaned.Count == 0 || cleaned.Any(r => r.Length == 0))
        {
            throw new ArgumentException("Kayıt listesi boş olamaz ve boş satır içeremez.", nameof(records));
        }

        if (cleaned.Distinct(StringComparer.Ordinal).Count() != cleaned.Count)
        {
            throw new ArgumentException("Aynı kayıt iki kez var.", nameof(records));
        }

        var fill = saltSource ?? RandomNumberGenerator.Fill;
        var items = cleaned
            .Select(record =>
            {
                var salt = new byte[SaltBytes];
                fill(salt);
                return (Record: record, Salt: salt, C: CommitmentOf(salt, record));
            })
            .OrderBy(x => Convert.ToHexStringLower(x.C), StringComparer.Ordinal)
            .ToList();

        var leaves = items.Select(x => MerkleTree.LeafHash(x.C)).ToList();
        var root = Convert.ToHexStringLower(MerkleTree.Root(leaves));
        // E5: Önceki dosya gerçekten bir açık taahhüt olmalı; rastgele bir dosyanın özeti zinciri anlamsız kılar.
        if (previousPublicFile is not null && ParsePublic(previousPublicFile) is null)
        {
            throw new ArgumentException("Önceki dosya geçerli bir açık taahhüt değil.", nameof(previousPublicFile));
        }

        var previous = previousPublicFile is null ? null : Convert.ToHexStringLower(SHA256.HashData(previousPublicFile));

        var publicCommitment = new PublicCommitment(FormatVersion, createdAt.ToUniversalTime(), items.Count, root, previous, LeafRuleText);
        var opening = new CommitmentOpening(
            FormatVersion,
            root,
            [.. items.Select((x, i) => new OpeningEntry(i, Convert.ToHexStringLower(x.Salt), x.Record))],
            [.. items.Select(x => Convert.ToHexStringLower(x.C))]);
        return (publicCommitment, opening);
    }

    /// <summary>Açıklama dosyasından tek kayıt kanıtı. Yalnızca o kaydın tuzu ve kardeş özetleri çıkar.</summary>
    public static InclusionProofFile Prove(CommitmentOpening opening, int index)
    {
        ArgumentNullException.ThrowIfNull(opening);
        var leaves = opening.Commitments.Select(c => MerkleTree.LeafHash(Convert.FromHexString(c))).ToList();
        var entry = opening.Entries[index];
        var path = MerkleTree.InclusionProof(leaves, index);
        return new InclusionProofFile(FormatVersion, opening.Root, leaves.Count, index, entry.Salt, entry.Record,
            [.. path.Select(Convert.ToHexStringLower)]);
    }

    /// <summary>Kanıtı açık taahhüde karşı doğrular. Diğer kayıtlar açılmaz.</summary>
    public static bool Verify(PublicCommitment publicCommitment, InclusionProofFile proof)
    {
        ArgumentNullException.ThrowIfNull(publicCommitment);
        ArgumentNullException.ThrowIfNull(proof);
        if (proof.TreeSize != publicCommitment.Count || !string.Equals(proof.Root, publicCommitment.Root, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        byte[] root;
        List<byte[]> path;
        try
        {
            salt = Convert.FromHexString(proof.Salt);
            root = Convert.FromHexString(publicCommitment.Root);
            path = [.. proof.Path.Select(Convert.FromHexString)];
        }
        catch (FormatException)
        {
            return false;
        }

        var leaf = MerkleTree.LeafHash(CommitmentOf(salt, proof.Record));
        return salt.Length == SaltBytes && MerkleTree.Verify(leaf, proof.Index, proof.TreeSize, path, root);
    }

    /// <summary>
    /// Açıklama dosyası açık taahhütle tutuyor mu? Her kaydın taahhüdü tuzundan yeniden hesaplanır, yerine bakılır ve
    /// bütün yapraklardan kök yeniden kurulur. İleriye dönük testte alarm kümesi yalnızca bu denetimden geçerse okunur.
    /// </summary>
    public static bool VerifyOpening(PublicCommitment publicCommitment, CommitmentOpening opening)
    {
        ArgumentNullException.ThrowIfNull(publicCommitment);
        ArgumentNullException.ThrowIfNull(opening);
        if (opening.Entries.Count != publicCommitment.Count || opening.Commitments.Count != publicCommitment.Count)
        {
            return false;
        }

        try
        {
            foreach (var entry in opening.Entries)
            {
                var salt = Convert.FromHexString(entry.Salt);
                if (salt.Length != SaltBytes || entry.Index < 0 || entry.Index >= opening.Commitments.Count
                    || !string.Equals(Convert.ToHexStringLower(CommitmentOf(salt, entry.Record)), opening.Commitments[entry.Index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            var leaves = opening.Commitments.Select(c => MerkleTree.LeafHash(Convert.FromHexString(c))).ToList();
            return string.Equals(Convert.ToHexStringLower(MerkleTree.Root(leaves)), publicCommitment.Root, StringComparison.Ordinal)
                   && opening.Entries.Select(e => e.Index).Distinct().Count() == opening.Entries.Count;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Zincir bağı (E5): <paramref name="current"/>.Previous, <paramref name="previousPublicFile"/>'ın SHA-256'sı mı ve o
    /// dosya geçerli bir açık taahhüt mü? Eski bir taahhüt sonradan değiştirilirse özet tutmaz ve bu görünür olur.
    /// </summary>
    public static bool VerifyPrevious(PublicCommitment current, byte[] previousPublicFile)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previousPublicFile);
        return current.Previous is { } expected
               && ParsePublic(previousPublicFile) is not null
               && string.Equals(expected, Convert.ToHexStringLower(SHA256.HashData(previousPublicFile)), StringComparison.Ordinal);
    }

    /// <summary>Baytları açık taahhüt olarak okur ve alanlarını denetler; geçersizse <c>null</c>.</summary>
    public static PublicCommitment? ParsePublic(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            var value = JsonSerializer.Deserialize<PublicCommitment>(file);
            return value is { Version: FormatVersion, Count: > 0, Root.Length: 64 } && value.Root.All(char.IsAsciiHexDigitLower)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T FromJson<T>(string json) =>
        JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("JSON boş.");

    /// <summary>
    /// RFC 3161 zaman damgası isteği (.tsq, DER). Ağa bağlanmaz; dosya elle bir zaman damgası servisine gönderilir.
    /// </summary>
    /// <remarks>
    /// <code>
    /// TimeStampReq ::= SEQUENCE {
    ///   version        INTEGER (1),
    ///   messageImprint SEQUENCE { hashAlgorithm AlgorithmIdentifier (sha256, NULL), hashedMessage OCTET STRING },
    ///   nonce          INTEGER,           -- aynı isteğin tekrar oynatılmasına karşı rastgele sayı
    ///   certReq        BOOLEAN (TRUE) }   -- cevapta imzalayan sertifika da gelsin
    /// </code>
    /// </remarks>
    public static byte[] TimestampRequest(byte[] fileBytes, long nonce)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nonce);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1"); // id-sha256
                    writer.WriteNull();
                }

                writer.WriteOctetString(SHA256.HashData(fileBytes));
            }

            writer.WriteInteger(nonce);
            writer.WriteBoolean(true);
        }

        return writer.Encode();
    }
}
