using System.IO.Compression;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>Saldırgan yanıt gövdeleri üretir.</summary>
public static class Payloads
{
    public static byte[] Gzip(byte[] data) => Compress(data, s => new GZipStream(s, CompressionLevel.SmallestSize, leaveOpen: true));

    public static byte[] Deflate(byte[] data) => Compress(data, s => new ZLibStream(s, CompressionLevel.SmallestSize, leaveOpen: true));

    public static byte[] Brotli(byte[] data) => Compress(data, s => new BrotliStream(s, CompressionLevel.SmallestSize, leaveOpen: true));

    /// <summary>Sıkıştırma türüne göre bomba: sıfırlardan oluşan, açılınca çok büyüyen gövde.</summary>
    public static byte[] ZeroBomb(string encoding, int decompressedBytes) => encoding switch
    {
        "gzip" => Gzip(new byte[decompressedBytes]),
        "deflate" => Deflate(new byte[decompressedBytes]),
        "br" => Brotli(new byte[decompressedBytes]),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>Sıkıştırılmış gibi işaretlenen ama bozuk veri.</summary>
    public static byte[] Garbage(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Compress(byte[] data, Func<Stream, Stream> factory)
    {
        using var output = new MemoryStream();
        using (var compressor = factory(output))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }
}
