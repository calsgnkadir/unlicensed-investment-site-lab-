using Fraud.Domain.Collection;

namespace Fraud.Collectors.StaticCt;

/// <summary>Hash tile indirilemedi veya biçimi bozuk. Mesaj yalnızca sabit metindir; adres içermez.</summary>
public sealed class StaticCtFetchException : Exception
{
    public StaticCtFetchException()
    {
    }

    public StaticCtFetchException(string message)
        : base(message)
    {
    }

    public StaticCtFetchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public StaticCtFetchException(CollectionErrorKind errorKind, int? httpStatus, int attempts)
        : base("Hash tile okunamadı.")
    {
        ErrorKind = errorKind;
        HttpStatus = httpStatus;
        Attempts = attempts;
    }

    public CollectionErrorKind ErrorKind { get; } = CollectionErrorKind.InvalidResponse;

    public int? HttpStatus { get; }

    public int Attempts { get; }
}

/// <summary>
/// Belirli bir checkpoint boyutundaki ağacın düğüm özetlerini hash tile'larından okur.
/// </summary>
/// <remarks>
/// <para>
/// L düzeyindeki tile, ağacın <c>8L</c> yüksekliğindeki düğümlerini 256'lık gruplar halinde tutar.
/// <c>8L + k</c> yüksekliğindeki bir düğüm, aynı tile'daki <c>2^k</c> komşu özetin alt ağaç köküdür.
/// Böylece ağacın her tam alt ağacının kökü tek bir tile'dan hesaplanabilir.
/// </para>
/// <para>
/// Ağacın sağ kenarındaki tile'lar kısmidir: L düzeyinde <c>floor(boyut / 256^L) mod 256</c> özet içerir.
/// Bu genişlik yalnızca imzası doğrulanmış checkpoint boyutundan hesaplanır (belge, doğrulanmış checkpoint
/// olmadan kısmi tile istemeyi yasaklar). Okunan tile'lar bu nesne yaşadığı sürece bellekte tutulur.
/// </para>
/// </remarks>
public sealed class TreeHashReader(IStaticCtClient client, long treeSize)
{
    private readonly Dictionary<(int Level, long Index), byte[]> _tiles = [];

    public long TreeSize => treeSize;

    /// <summary>Bu nesnenin gönderdiği HTTP isteği sayısı.</summary>
    public int Requests { get; private set; }

    public int? LastHttpStatus { get; private set; }

    /// <summary>[0, size) aralığının sağ kenarını hash tile'larından kurar.</summary>
    public async Task<CompactRange> BuildFrontierAsync(long size, CancellationToken cancellationToken)
    {
        var range = new CompactRange();
        await AppendAsync(range, size, cancellationToken).ConfigureAwait(false);
        return range;
    }

    /// <summary>[range.Size, end) aralığını hizalı alt ağaçlara bölüp köklerini <paramref name="range"/>'e ekler.</summary>
    public async Task AppendAsync(CompactRange range, long end, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (end > treeSize)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Aralık checkpoint boyutunu aşıyor.");
        }

        foreach (var (height, index) in CompactRange.Decompose(range.Size, end))
        {
            range.Append(height, await NodeAsync(height, index, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>Yüksekliği <paramref name="height"/>, o yükseklikte sırası <paramref name="index"/> olan düğümün özeti.</summary>
    public async Task<byte[]> NodeAsync(int height, long index, CancellationToken cancellationToken)
    {
        var level = height / 8;
        var k = height % 8;
        if (level > TilePath.MaxLevel)
        {
            throw new StaticCtFetchException("Ağaç desteklenen yüksekliği aşıyor.");
        }

        var count = 1 << k;
        var first = index << k;                // 8L yüksekliğinde ilk düğüm
        var tileIndex = first / TilePath.Width;
        var offset = (int)(first % TilePath.Width);
        var completeAtLevel = treeSize >> (8 * level);
        var width = (int)Math.Min(TilePath.Width, completeAtLevel - (tileIndex * TilePath.Width));
        if (width < offset + count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Düğüm bu checkpoint boyutunda tam değil.");
        }

        var tile = await TileAsync(level, tileIndex, width, cancellationToken).ConfigureAwait(false);
        return Merkle.SubtreeRoot(tile.AsSpan(offset * Merkle.HashSize), count);
    }

    private async Task<byte[]> TileAsync(int level, long tileIndex, int width, CancellationToken cancellationToken)
    {
        if (_tiles.TryGetValue((level, tileIndex), out var cached) && cached.Length >= width * Merkle.HashSize)
        {
            return cached;
        }

        var fetch = await client.GetHashTileAsync(level, tileIndex, width, cancellationToken).ConfigureAwait(false);
        Requests += fetch.Attempts;
        LastHttpStatus = fetch.HttpStatus ?? LastHttpStatus;
        if (!fetch.IsSuccess)
        {
            throw new StaticCtFetchException(fetch.ErrorKind, fetch.HttpStatus, fetch.Attempts);
        }

        _tiles[(level, tileIndex)] = fetch.Value!;
        return fetch.Value!;
    }
}
