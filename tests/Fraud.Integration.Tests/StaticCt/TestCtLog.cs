using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.CertificateTransparency;

namespace Fraud.Integration.Tests.StaticCt;

/// <summary>
/// Testte üretilen bir Static CT logu: Sertifikalar burada oluşturulur, tile'lar ve imzalı checkpoint buradan sunulur.
/// Merkle kökleri üretim kodundan bağımsız, RFC 6962'nin özyinelemeli tanımıyla hesaplanır.
/// </summary>
internal sealed class TestCtLog : IDisposable
{
    public const string Origin = "log.test-ct.example/2026";
    public static readonly Uri Prefix = new("https://mon.test-ct.example/2026/");
    public static readonly DateTimeOffset BaseTime = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    // Adlar .example altında; hepsi "ornek" içerir ki sızıntı testi tek kelimeyle arayabilsin.
    public const string KeywordName = "yatirim-ornek.example";
    public const string PlainName = "sade-ornek.example";
    public static readonly string IdnKeywordName = new IdnMapping().GetAscii("yatırım-ornek.example");

    private readonly List<Entry> _entries = [];

    public TestCtLog()
    {
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PublicKey = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
    }

    public ECDsa Key { get; }

    public string PublicKey { get; }

    public int Count => _entries.Count;

    public CtLogOptions Options(string startAt = "0", string name = "testlog") => new()
    {
        Name = name,
        MonitoringPrefix = Prefix,
        Origin = Origin,
        PublicKey = PublicKey,
        StartAt = startAt,
    };

    /// <summary>
    /// Kayıt ekler. Desen: her 10 kayıttan 0. ve 7.'si anahtar kelimeli (7. ön sertifika), 3.'sü IDN anahtar kelimeli,
    /// kalanı sade. Her 4. kayıt ön sertifika.
    /// </summary>
    public TestCtLog AddEntries(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var index = _entries.Count;
            var kind = (index % 10) switch
            {
                0 or 7 => CertKind.Keyword,
                3 => CertKind.IdnKeyword,
                _ => CertKind.Plain,
            };
            var precert = index % 4 == 3 || index % 10 == 7;
            _entries.Add(BuildEntry(index, kind, precert));
        }

        return this;
    }

    public bool IsKeyword(long index) => _entries[(int)index].Kind != CertKind.Plain;

    public int KeywordCount(long start, long end) => Enumerable.Range((int)start, (int)(end - start)).Count(i => IsKeyword(i));

    public DateTimeOffset TimestampOf(long index) => _entries[(int)index].Timestamp;

    /// <summary>Bir kaydın sertifikasını değiştirir (geçmişi yeniden yazan log).</summary>
    public void Rewrite(long index) => _entries[(int)index] = BuildEntry((int)index, CertKind.Plain, false, salt: 1);

    public byte[] LeafHash(long index) => _entries[(int)index].LeafHash;

    public byte[] DataTile(long tileIndex, int width = 256)
    {
        using var ms = new MemoryStream();
        for (var i = 0; i < width; i++)
        {
            ms.Write(_entries[(int)((tileIndex * 256) + i)].TileLeaf);
        }

        return ms.ToArray();
    }

    /// <summary>Tile'daki <paramref name="entryOffset"/>. kaydın sertifikasının son baytının tile içindeki yeri.</summary>
    public int CertificateLastByteOffset(long tileIndex, int entryOffset)
    {
        var offset = 0;
        for (var i = 0; i < entryOffset; i++)
        {
            offset += _entries[(int)((tileIndex * 256) + i)].TileLeaf.Length;
        }

        return offset + _entries[(int)((tileIndex * 256) + entryOffset)].CertificateEnd - 1;
    }

    public byte[] HashTile(int level, long tileIndex, int width)
    {
        var span = 1L << (8 * level);
        var result = new byte[width * 32];
        for (var i = 0; i < width; i++)
        {
            var start = ((tileIndex * 256) + i) * span;
            Reference.Mth(LeafHashes(), start, start + span).CopyTo(result, i * 32);
        }

        return result;
    }

    public byte[] Root(long size) => Reference.Mth(LeafHashes(), 0, size);

    public IReadOnlyList<byte[]> LeafHashes() => _entries.Select(e => e.LeafHash).ToList();

    /// <summary>İmzalı checkpoint. İsteğe bağlı: başka anahtar, başka kök, ek imza satırları.</summary>
    public string Checkpoint(long size, ECDsa? signer = null, byte[]? root = null, uint? keyIdOverride = null, string? extraSignatureLines = null)
    {
        root ??= Root(size);
        var text = Origin + "\n" + size + "\n" + Convert.ToBase64String(root) + "\n";
        var timestamp = (ulong)BaseTime.AddMinutes(5).ToUnixTimeMilliseconds();

        var signed = new byte[50];
        signed[0] = 0;
        signed[1] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(signed.AsSpan(2), timestamp);
        BinaryPrimitives.WriteUInt64BigEndian(signed.AsSpan(10), (ulong)size);
        root.CopyTo(signed, 18);
        var der = (signer ?? Key).SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var spki = Key.ExportSubjectPublicKeyInfo();
        var keyId = keyIdOverride ?? KeyId(Origin, SHA256.HashData(spki));
        var body = new byte[4 + 8 + 4 + der.Length];
        BinaryPrimitives.WriteUInt32BigEndian(body, keyId);
        BinaryPrimitives.WriteUInt64BigEndian(body.AsSpan(4), timestamp);
        body[12] = 4;
        body[13] = 3;
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(14), (ushort)der.Length);
        der.CopyTo(body, 16);

        return text + "\n" + (extraSignatureLines ?? string.Empty) + "— " + Origin + " " + Convert.ToBase64String(body) + "\n";
    }

    /// <summary>Belgedeki formül, üretim kodundan bağımsız yazıldı: SHA-256(ad || 0x0A || 0x05 || LogID)[:4].</summary>
    public static uint KeyId(string name, byte[] logId)
    {
        var input = Encoding.UTF8.GetBytes(name).Concat(new byte[] { 0x0A, 0x05 }).Concat(logId).ToArray();
        return BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(input));
    }

    public void Dispose() => Key.Dispose();

    private static byte[] TbsOf(byte[] certificateDer)
    {
        var reader = new AsnReader(certificateDer, AsnEncodingRules.DER).ReadSequence();
        return reader.ReadEncodedValue().ToArray();
    }

    private static readonly ConcurrentDictionary<(CertKind, int), byte[]> CertificateCache = new();

    private static byte[] Certificate(CertKind kind, int salt) => CertificateCache.GetOrAdd((kind, salt), k =>
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var name = new X500DistinguishedNameBuilder();
        name.AddCountryOrRegion("TR");
        name.AddOrganizationName("Ornek Test CA");
        name.AddCommonName("Ornek Test R1");
        name.AddEmailAddress("ca-kisi@ornek.example");
        var request = new CertificateRequest(name.Build(), key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        switch (k.Item1)
        {
            case CertKind.Keyword:
                san.AddDnsName(KeywordName);
                san.AddDnsName("www." + KeywordName);
                break;
            case CertKind.IdnKeyword:
                san.AddDnsName(IdnKeywordName);
                break;
            default:
                san.AddDnsName(k.Item2 == 0 ? PlainName : "baska-" + PlainName);
                break;
        }

        san.AddEmailAddress("kisi@ornek.example");
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(BaseTime.AddDays(-1), BaseTime.AddDays(89));
        return cert.RawData;
    });

    private static Entry BuildEntry(int index, CertKind kind, bool precert, int salt = 0)
    {
        var certificate = Certificate(kind, salt);
        var timestamp = BaseTime.AddSeconds(index);
        using var te = new MemoryStream();
        Span<byte> u64 = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(u64, (ulong)timestamp.ToUnixTimeMilliseconds());
        te.Write(u64);
        te.Write([0, (byte)(precert ? 1 : 0)]);
        int certificateEnd;
        if (precert)
        {
            te.Write(SHA256.HashData(Encoding.ASCII.GetBytes("issuer-key")));
            var tbs = TbsOf(certificate);
            WriteUInt24(te, tbs.Length);
            te.Write(tbs);
            certificateEnd = (int)te.Position;
        }
        else
        {
            WriteUInt24(te, certificate.Length);
            te.Write(certificate);
            certificateEnd = (int)te.Position;
        }

        // CtExtensions: leaf_index (tür 0, 5 bayt).
        te.Write([0, 8, 0, 0, 5]);
        var idx = new byte[5];
        for (var i = 0; i < 5; i++)
        {
            idx[4 - i] = (byte)((long)index >> (8 * i));
        }

        te.Write(idx);
        var timestampedEntry = te.ToArray();

        using var leaf = new MemoryStream();
        leaf.Write(timestampedEntry);
        if (precert)
        {
            WriteUInt24(leaf, certificate.Length);
            leaf.Write(certificate);
        }

        leaf.Write([0, 64]);
        leaf.Write(new byte[64]);

        var leafHash = SHA256.HashData(new byte[] { 0, 0, 0 }.Concat(timestampedEntry).ToArray());
        return new Entry(kind, timestamp, leaf.ToArray(), leafHash, certificateEnd);
    }

    private static void WriteUInt24(Stream s, int value) => s.Write([(byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private enum CertKind
    {
        Plain,
        Keyword,
        IdnKeyword,
    }

    private sealed record Entry(CertKind Kind, DateTimeOffset Timestamp, byte[] TileLeaf, byte[] LeafHash, int CertificateEnd);
}

/// <summary>RFC 6962 Bölüm 2.1'in doğrudan yazımı. Üretimdeki CompactRange'i bu referansla karşılaştırırız.</summary>
internal static class Reference
{
    public static byte[] Mth(IReadOnlyList<byte[]> leafHashes, long start, long end)
    {
        var n = end - start;
        if (n == 0)
        {
            return SHA256.HashData([]);
        }

        if (n == 1)
        {
            return leafHashes[(int)start];
        }

        var k = 1L;
        while (k * 2 < n)
        {
            k *= 2;
        }

        var left = Mth(leafHashes, start, start + k);
        var right = Mth(leafHashes, start + k, end);
        return SHA256.HashData(new byte[] { 1 }.Concat(left).Concat(right).ToArray());
    }
}

/// <summary>
/// Sahte Static CT sunucusu (HTTP işleyicisi). Ağa çıkmaz. Yayımlanan boyuta göre checkpoint, tam ve kısmi tile döner.
/// </summary>
internal sealed class FakeCtServer(TestCtLog log) : HttpMessageHandler
{
    public ConcurrentQueue<string> Paths { get; } = new();

    public long PublishedSize { get; set; }

    /// <summary>Checkpoint metnini değiştirmek için (ör. başka anahtarla imza).</summary>
    public Func<long, string>? CheckpointOverride { get; set; }

    /// <summary>Yol için yanıtı değiştirir; <c>null</c> dönerse normal yanıt verilir.</summary>
    public Func<string, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>Kısmi tile'lar silinmiş gibi davran (404).</summary>
    public bool PartialTilesDeleted { get; set; }

    /// <summary>Veri tile'ını Content-Encoding başlığı olmadan gzip baytı olarak gönder.</summary>
    public bool GzipWithoutHeader { get; set; }

    public int Count(string path) => Paths.Count(p => p == path);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (!uri.AbsoluteUri.StartsWith(TestCtLog.Prefix.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Beklenmeyen adres.");
        }

        var path = uri.AbsoluteUri[TestCtLog.Prefix.AbsoluteUri.Length..];
        Paths.Enqueue(path);
        if (Intercept?.Invoke(path) is { } intercepted)
        {
            return Task.FromResult(intercepted);
        }

        return Task.FromResult(Respond(path));
    }

    private HttpResponseMessage Respond(string path)
    {
        if (path == "checkpoint")
        {
            var text = CheckpointOverride?.Invoke(PublishedSize) ?? log.Checkpoint(PublishedSize);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
        }

        if (!TryParseTilePath(path, out var level, out var index, out var width))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (width < 256 && PartialTilesDeleted)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var span = level < 0 ? 1L : 1L << (8 * level);
        if (((index * 256) + width) * span > PublishedSize)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var body = level < 0 ? log.DataTile(index, width) : log.HashTile(level, index, width);
        if (level < 0 && GzipWithoutHeader)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(body);
            }

            body = ms.ToArray();
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    /// <summary><c>tile/data/x001/234.p/5</c> gibi yolları çözer. Veri tile'ı için level -1.</summary>
    public static bool TryParseTilePath(string path, out int level, out long index, out int width)
    {
        level = 0;
        index = 0;
        width = 256;
        var parts = path.Split('/').ToList();
        if (parts.Count < 3 || parts[0] != "tile")
        {
            return false;
        }

        if (parts.Count >= 2 && parts[^2].EndsWith(".p", StringComparison.Ordinal))
        {
            width = int.Parse(parts[^1], CultureInfo.InvariantCulture);
            parts[^2] = parts[^2][..^2];
            parts.RemoveAt(parts.Count - 1);
        }

        level = parts[1] == "data" ? -1 : int.Parse(parts[1], CultureInfo.InvariantCulture);
        foreach (var group in parts.Skip(2))
        {
            index = (index * 1000) + long.Parse(group.TrimStart('x'), CultureInfo.InvariantCulture);
        }

        return true;
    }
}
