namespace Fraud.Infrastructure.Http;

/// <summary>
/// Akıştan en fazla belirli sayıda bayt okur. Sınır aşılırsa okumayı keser ve <c>null</c> döner.
/// </summary>
/// <remarks>
/// <c>Content-Length</c> başlığına güvenmek yetmez: Sunucu başlığı hiç göndermeyebilir ya da yanlış
/// gönderebilir, sıkıştırılmış yanıt açıldığında çok büyüyebilir. Bu yüzden sayım okunan gerçek bayt üzerinden yapılır.
/// </remarks>
public static class BoundedStreamReader
{
    public static async Task<byte[]?> ReadAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
