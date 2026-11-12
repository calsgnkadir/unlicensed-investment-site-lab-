using System.Buffers.Binary;
using Fraud.Domain.CertificateTransparency;

namespace Fraud.Collectors.StaticCt;

/// <summary>Veri tile'ındaki bir kayıt. Sertifika baytları tile belleğine işaret eder; kopyalanmaz.</summary>
/// <param name="LeafHash">RFC 6962 yaprak özeti: <c>SHA-256(0x00 || MerkleTreeLeaf)</c>.</param>
/// <param name="Certificate">x509 kaydında tam sertifika (DER), ön sertifikada TBSCertificate (DER).</param>
/// <param name="IsTbsCertificate"><paramref name="Certificate"/> yalnızca TBSCertificate mı.</param>
public readonly record struct TileEntry(
    long Index,
    DateTimeOffset Timestamp,
    CtEntryType Type,
    byte[] LeafHash,
    ReadOnlyMemory<byte> Certificate,
    bool IsTbsCertificate);

/// <summary>
/// Veri tile'ını (<c>TileLeaf</c> dizisi) ayrıştırır. Biçim c2sp.org/static-ct-api "Log entries" bölümündedir.
/// </summary>
/// <remarks>
/// <code>
/// TileLeaf:
///   TimestampedEntry: uint64 timestamp, uint16 entry_type,
///     x509_entry:    ASN.1Cert (opaque&lt;1..2^24-1&gt;)
///     precert_entry: issuer_key_hash[32], TBSCertificate (opaque&lt;1..2^24-1&gt;)
///     CtExtensions   (opaque&lt;0..2^16-1&gt;, içinde leaf_index uzantısı)
///   precert_entry ise: pre_certificate (ASN.1Cert)
///   certificate_chain: Fingerprint&lt;0..2^16-1&gt; (32 baytlık özetler)
/// </code>
/// Tile güvenilmez veridir: Her uzunluk sınır içinde mi diye bakılır, kayıt sayısı tam tutmalı, her kaydın
/// <c>leaf_index</c> uzantısı beklenen sırayla aynı olmalı. Biri tutmazsa tile'ın tamamı reddedilir
/// (<see cref="InvalidDataException"/>); yarım tile işlenmez.
/// </remarks>
public static class DataTileParser
{
    public static List<TileEntry> Parse(ReadOnlyMemory<byte> data, long firstIndex, int expectedCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedCount, 1);

        var entries = new List<TileEntry>(expectedCount);
        var span = data.Span;
        var p = 0;
        while (p < span.Length)
        {
            if (entries.Count == expectedCount)
            {
                throw new InvalidDataException("Tile beklenenden fazla kayıt içeriyor.");
            }

            var index = firstIndex + entries.Count;
            var entryStart = p;
            var ms = ReadUInt64(span, ref p);
            var type = ReadUInt16(span, ref p);

            ReadOnlyMemory<byte> certificate;
            bool isTbs;
            CtEntryType entryType;
            switch (type)
            {
                case 0:
                    entryType = CtEntryType.X509;
                    certificate = ReadOpaque24(data, span, ref p);
                    isTbs = false;
                    break;
                case 1:
                    entryType = CtEntryType.Precert;
                    Skip(span, ref p, 32); // issuer_key_hash
                    certificate = ReadOpaque24(data, span, ref p);
                    isTbs = true;
                    break;
                default:
                    throw new InvalidDataException("Bilinmeyen kayıt türü.");
            }

            var extensionsLength = ReadUInt16(span, ref p);
            var extensions = Slice(span, ref p, extensionsLength);
            var timestampedEntryEnd = p;
            if (ReadLeafIndex(extensions) != index)
            {
                throw new InvalidDataException("leaf_index uzantısı kaydın sırasıyla uyuşmuyor.");
            }

            if (type == 1)
            {
                ReadOpaque24(data, span, ref p); // pre_certificate: ad için TBS yeterli, okunmaz.
            }

            var chainLength = ReadUInt16(span, ref p);
            if (chainLength % 32 != 0)
            {
                throw new InvalidDataException("Zincir parmak izleri 32 baytın katı olmalı.");
            }

            Skip(span, ref p, chainLength);

            if (ms > (ulong)DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            {
                throw new InvalidDataException("Zaman damgası aralık dışında.");
            }

            var leafHash = HashTimestampedEntry(span[entryStart..timestampedEntryEnd]);
            entries.Add(new TileEntry(index, DateTimeOffset.FromUnixTimeMilliseconds((long)ms), entryType, leafHash, certificate, isTbs));
        }

        if (entries.Count != expectedCount)
        {
            throw new InvalidDataException("Tile beklenenden az kayıt içeriyor.");
        }

        return entries;
    }

    /// <summary>
    /// <c>MerkleTreeLeaf</c> = sürüm (0) || yaprak türü (0 = timestamped_entry) || TimestampedEntry.
    /// Yaprak özeti bunun başına 0x00 eklenerek alınır.
    /// </summary>
    public static byte[] HashTimestampedEntry(ReadOnlySpan<byte> timestampedEntry)
    {
        var leaf = new byte[2 + timestampedEntry.Length];
        timestampedEntry.CopyTo(leaf.AsSpan(2));
        return Merkle.LeafHash(leaf);
    }

    /// <summary>CtExtensions içinden leaf_index (tür 0, 5 bayt büyük uçlu) değerini okur. Yoksa veya bozuksa hata.</summary>
    private static long ReadLeafIndex(ReadOnlySpan<byte> extensions)
    {
        long? leafIndex = null;
        var p = 0;
        while (p < extensions.Length)
        {
            var type = Slice(extensions, ref p, 1)[0];
            var length = ReadUInt16(extensions, ref p);
            var value = Slice(extensions, ref p, length);
            if (type != 0)
            {
                continue;
            }

            if (leafIndex is not null || length != 5)
            {
                throw new InvalidDataException("leaf_index uzantısı bozuk veya tekrarlı.");
            }

            long v = 0;
            foreach (var b in value)
            {
                v = (v << 8) | b;
            }

            leafIndex = v;
        }

        return leafIndex ?? throw new InvalidDataException("leaf_index uzantısı yok.");
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> span, ref int p) => BinaryPrimitives.ReadUInt64BigEndian(Slice(span, ref p, 8));

    private static int ReadUInt16(ReadOnlySpan<byte> span, ref int p) => BinaryPrimitives.ReadUInt16BigEndian(Slice(span, ref p, 2));

    private static ReadOnlyMemory<byte> ReadOpaque24(ReadOnlyMemory<byte> data, ReadOnlySpan<byte> span, ref int p)
    {
        var lengthBytes = Slice(span, ref p, 3);
        var length = (lengthBytes[0] << 16) | (lengthBytes[1] << 8) | lengthBytes[2];
        if (length == 0)
        {
            throw new InvalidDataException("Boş sertifika.");
        }

        var start = p;
        Skip(span, ref p, length);
        return data.Slice(start, length);
    }

    private static void Skip(ReadOnlySpan<byte> span, ref int p, int count) => Slice(span, ref p, count);

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> span, ref int p, int count)
    {
        if (count < 0 || span.Length - p < count)
        {
            throw new InvalidDataException("Tile beklenmedik yerde bitti.");
        }

        var slice = span.Slice(p, count);
        p += count;
        return slice;
    }
}
