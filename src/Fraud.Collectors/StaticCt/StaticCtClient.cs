using System.Collections.Concurrent;
using System.IO.Compression;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Http;

namespace Fraud.Collectors.StaticCt;

/// <summary>Bir Static CT isteğinin sonucu. <see cref="Value"/> yalnızca hata yoksa doludur.</summary>
/// <param name="Attempts">Tekrar denemeler ve kısmi tile yedeği dahil gönderilen HTTP isteği sayısı.</param>
public sealed record CtFetch<T>(T? Value, CollectionErrorKind ErrorKind, int? HttpStatus, int Attempts)
    where T : class
{
    public bool IsSuccess => ErrorKind == CollectionErrorKind.None && Value is not null;
}

public static class CtFetch
{
    public static CtFetch<T> Failed<T>(CollectionErrorKind kind, int? status, int attempts)
        where T : class => new(null, kind, status, attempts);
}

/// <summary>Tek bir Static CT logunun izleme adresine erişim. Testlerde sahte uygulama kullanılır.</summary>
public interface IStaticCtClient
{
    /// <summary>Checkpoint'i okur ve imzasını logun anahtarıyla doğrular. İmza tutmazsa <see cref="CollectionErrorKind.VerificationFailed"/>.</summary>
    Task<CtFetch<Checkpoint>> GetCheckpointAsync(CancellationToken cancellationToken);

    /// <summary>Tam (256 kayıtlık) veri tile'ını açılmış olarak döner. Tile diske yazılmaz.</summary>
    Task<CtFetch<byte[]>> GetDataTileAsync(long tileIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Hash tile'ını döner (tam olarak <paramref name="width"/> x 32 bayt). Kısmi tile artık yoksa (404) tam tile
    /// istenir ve ilk <paramref name="width"/> özet kullanılır (static-ct-api önerisi).
    /// </summary>
    Task<CtFetch<byte[]>> GetHashTileAsync(int level, long tileIndex, int width, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP üzerinden Static CT istemcisi. İstekler <see cref="PoliteHttpClient"/> ile gider: hız sınırı, deneme başına süre,
/// boyut sınırı ve geçici hatalarda geri çekilmeli tekrar deneme.
/// </summary>
/// <remarks>
/// Yalnızca yapılandırmadaki izleme önekine istek atılır; yollar sayılardan kurulur, gelen veriden adres üretilmez.
/// Sunucu gzip'i <c>Content-Encoding</c> başlığıyla gönderirse HTTP katmanı açar. Başlık olmadan gzip baytı
/// gelirse (bazı nesne depoları böyle yapar) burada, aynı boyut sınırıyla açılır.
/// </remarks>
public sealed class StaticCtClient(PoliteHttpClient http, CtLogOptions log, CtLogKey key, long maxResponseBytes) : IStaticCtClient
{
    private readonly Uri _prefix = log.MonitoringPrefix ?? throw new ArgumentException("İzleme öneki yok.", nameof(log));

    public async Task<CtFetch<Checkpoint>> GetCheckpointAsync(CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(new Uri(_prefix, "checkpoint"), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return CtFetch.Failed<Checkpoint>(response.ErrorKind, response.HttpStatus, response.Attempts);
        }

        var (checkpoint, error) = CheckpointVerifier.Verify(response.Body, key);
        if (checkpoint is null)
        {
            var kind = error == CheckpointError.Malformed ? CollectionErrorKind.InvalidResponse : CollectionErrorKind.VerificationFailed;
            return CtFetch.Failed<Checkpoint>(kind, response.HttpStatus, response.Attempts);
        }

        return new CtFetch<Checkpoint>(checkpoint, CollectionErrorKind.None, response.HttpStatus, response.Attempts);
    }

    public async Task<CtFetch<byte[]>> GetDataTileAsync(long tileIndex, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(new Uri(_prefix, TilePath.DataTile(tileIndex)), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return CtFetch.Failed<byte[]>(response.ErrorKind, response.HttpStatus, response.Attempts);
        }

        var body = await GunzipIfNeededAsync(response.Body!, cancellationToken).ConfigureAwait(false);
        return body is null
            ? CtFetch.Failed<byte[]>(CollectionErrorKind.InvalidResponse, response.HttpStatus, response.Attempts)
            : new CtFetch<byte[]>(body, CollectionErrorKind.None, response.HttpStatus, response.Attempts);
    }

    public async Task<CtFetch<byte[]>> GetHashTileAsync(int level, long tileIndex, int width, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, TilePath.Width);

        var response = await http.GetAsync(new Uri(_prefix, TilePath.HashTile(level, tileIndex, width)), cancellationToken).ConfigureAwait(false);
        var attempts = response.Attempts;
        if (!response.IsSuccess && width < TilePath.Width && response.HttpStatus == 404)
        {
            // Kısmi tile, tam tile çıkınca silinebilir. Tam tile'ın ilk özetleri aynıdır.
            response = await http.GetAsync(new Uri(_prefix, TilePath.HashTile(level, tileIndex)), cancellationToken).ConfigureAwait(false);
            attempts += response.Attempts;
            if (response.IsSuccess && response.Body!.Length == TilePath.Width * TilePath.HashSize)
            {
                return new CtFetch<byte[]>(response.Body[..(width * TilePath.HashSize)], CollectionErrorKind.None, response.HttpStatus, attempts);
            }
        }

        if (!response.IsSuccess)
        {
            return CtFetch.Failed<byte[]>(response.ErrorKind, response.HttpStatus, attempts);
        }

        return response.Body!.Length == width * TilePath.HashSize
            ? new CtFetch<byte[]>(response.Body, CollectionErrorKind.None, response.HttpStatus, attempts)
            : CtFetch.Failed<byte[]>(CollectionErrorKind.InvalidResponse, response.HttpStatus, attempts);
    }

    private async Task<byte[]?> GunzipIfNeededAsync(byte[] body, CancellationToken cancellationToken)
    {
        // Veri tile'ı 8 baytlık zaman damgasıyla başlar (bugün 0x00 0x00 ...). 1F 8B gzip imzasıdır.
        if (body.Length < 2 || body[0] != 0x1F || body[1] != 0x8B)
        {
            return body;
        }

        try
        {
            var gzip = new GZipStream(new MemoryStream(body, writable: false), CompressionMode.Decompress);
            await using (gzip.ConfigureAwait(false))
            {
                return await BoundedStreamReader.ReadAsync(gzip, maxResponseBytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}

/// <summary>Yapılandırmadaki her log için bir istemci kurar. Hız sınırlayıcı log başına tektir.</summary>
public interface IStaticCtClientFactory
{
    IStaticCtClient Create(CtLogOptions log);
}

/// <param name="metrics">İsteğe bağlı: Verilirse her logun HTTP tekrar denemesi ve okunan baytı sayılır.</param>
public sealed class StaticCtClientFactory(
    IHttpClientFactory httpClientFactory, StaticCtOptions options, TimeProvider timeProvider, StaticCtMetrics? metrics = null)
    : IStaticCtClientFactory, IDisposable
{
    public const string HttpClientName = "StaticCt";

    private readonly ConcurrentDictionary<string, (RequestPacer Pacer, CtLogKey Key)> _perLog = new(StringComparer.Ordinal);

    public IStaticCtClient Create(CtLogOptions log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var (pacer, key) = _perLog.GetOrAdd(log.Name, _ =>
            (new RequestPacer(options.MinRequestInterval, timeProvider), CtLogKey.Create(log.Origin, log.PublicKey)));
        var http = new PoliteHttpClient(httpClientFactory.CreateClient(HttpClientName), pacer, options, timeProvider,
            metrics?.HttpObserver(log.Name));
        return new StaticCtClient(http, log, key, options.MaxResponseBytes);
    }

    public void Dispose()
    {
        foreach (var (pacer, key) in _perLog.Values)
        {
            pacer.Dispose();
            key.Dispose();
        }

        _perLog.Clear();
    }
}
