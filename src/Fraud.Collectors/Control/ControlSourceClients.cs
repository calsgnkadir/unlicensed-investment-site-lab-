using System.IO.Compression;
using System.Net.Http.Headers;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;

namespace Fraud.Collectors.Control;

/// <summary>Kontrol listesi kaynaklarının HTTP ayarları. Her kaynak yalnızca kendi ana bilgisayarına bağlanır.</summary>
public sealed class ControlSourceOptions : SourceHttpOptions
{
    public ControlSourceOptions(string host, TimeSpan interval, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        Host = host;
        MinRequestInterval = interval;
        MaxResponseBytes = maxBytes;
        AttemptTimeout = TimeSpan.FromMinutes(2);
        RetryBaseDelay = TimeSpan.FromSeconds(10);
    }

    public string Host { get; }

    /// <summary>BDDK: tek sayfa, tek istek (KONTROL_LISTESI_KURULUM.md 1.1).</summary>
    public static ControlSourceOptions Bddk() => new("www.bddk.org.tr", TimeSpan.FromSeconds(5), 8L * 1024 * 1024) { MaxRetryAttempts = 1 };

    /// <summary>Tranco: liste bilgisi ve tek indirme. İlk 1 milyon satır yaklaşık 20–30 MB.</summary>
    public static ControlSourceOptions Tranco() => new("tranco-list.eu", TimeSpan.FromSeconds(5), 96L * 1024 * 1024);

    /// <summary>Cloudflare Radar: saniyede en çok 1 istek (genel sınır 5 dakikada 1.200; biz 300).</summary>
    public static ControlSourceOptions Radar() => new("api.cloudflare.com", TimeSpan.FromSeconds(1), 1024 * 1024);
}

/// <summary>
/// İzin listeli, hız sınırlı HTTP istemcisi kurar: Bağlantı anında yalnızca <see cref="ControlSourceOptions.Host"/>:443'e
/// izin verilir (<see cref="AllowedHostConnector"/>), yönlendirme izlenmez, gövde boyutu sınırlıdır.
/// </summary>
public static class ControlHttp
{
    public static (PoliteHttpClient Client, HttpClient Http) Create(ControlSourceOptions options, string userAgent, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var http = new HttpClient(handler ?? AllowedHostConnector.CreateHandler(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { options.Host }))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return (new PoliteHttpClient(http, new RequestPacer(options.MinRequestInterval, TimeProvider.System), options, TimeProvider.System), http);
    }
}

/// <summary>Radar kategori sorgusunun sonucu. Hata ile "kategori yok" ayrıdır.</summary>
public sealed record RadarLookup(IReadOnlyList<string>? Categories, CollectionErrorKind ErrorKind, int? HttpStatus)
{
    public bool IsError => Categories is null;
}

public interface IRadarClient
{
    Task<RadarLookup> CategoriesAsync(string registrableDomain, CancellationToken cancellationToken);
}

/// <summary>
/// Cloudflare Radar istemcisi. Token yalnızca <c>Authorization</c> başlığında durur: Hiçbir log, hata mesajı ya da
/// çıktı onu içermez. Adres yalnızca kayıtlı alan adından kurulur.
/// </summary>
public sealed class RadarClient : IRadarClient, IDisposable
{
    private readonly PoliteHttpClient _client;
    private readonly HttpClient _http;

    public RadarClient(string token, string userAgent, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        (_client, _http) = ControlHttp.Create(ControlSourceOptions.Radar(), userAgent, handler);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<RadarLookup> CategoriesAsync(string registrableDomain, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrableDomain);
        if (!Fraud.Collectors.CrtSh.CertificateFieldSanitizer.IsDnsName(registrableDomain))
        {
            return new RadarLookup(null, CollectionErrorKind.InvalidResponse, null);
        }

        var uri = new Uri("https://api.cloudflare.com/client/v4/radar/ranking/domain/" + Uri.EscapeDataString(registrableDomain));
        var response = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return new RadarLookup(null, response.ErrorKind, response.HttpStatus);
        }

        var categories = RadarCategoryParser.Parse(response.Body ?? []);
        return categories is null
            ? new RadarLookup(null, CollectionErrorKind.InvalidResponse, response.HttpStatus)
            : new RadarLookup(categories, CollectionErrorKind.None, response.HttpStatus);
    }

    public void Dispose() => _http.Dispose();
}

public interface ITrancoClient
{
    Task<TrancoListInfo?> ListInfoAsync(string listId, CancellationToken cancellationToken);

    /// <summary>Listenin ilk 1 milyon satırı (metin). Hata ya da beklenmeyen biçimde <c>null</c>.</summary>
    Task<byte[]?> DownloadAsync(string listId, CancellationToken cancellationToken);
}

/// <summary>Tranco istemcisi: liste kimliğiyle bilgi ve tek indirme (KONTROL_LISTESI_KURULUM.md 2.1).</summary>
public sealed class TrancoClient(string userAgent, HttpMessageHandler? handler = null) : ITrancoClient, IDisposable
{
    private readonly (PoliteHttpClient Client, HttpClient Http) _http = ControlHttp.Create(ControlSourceOptions.Tranco(), userAgent, handler);

    public async Task<TrancoListInfo?> ListInfoAsync(string listId, CancellationToken cancellationToken)
    {
        if (!TrancoApiParser.IsListId(listId))
        {
            return null;
        }

        var response = await _http.Client.GetAsync(new Uri("https://tranco-list.eu/api/lists/id/" + listId), cancellationToken).ConfigureAwait(false);
        return response.IsSuccess ? TrancoApiParser.ParseInfo(response.Body ?? []) : null;
    }

    public async Task<byte[]?> DownloadAsync(string listId, CancellationToken cancellationToken)
    {
        if (!TrancoApiParser.IsListId(listId))
        {
            return null;
        }

        var response = await _http.Client.GetAsync(new Uri("https://tranco-list.eu/download/" + listId + "/1000000"), cancellationToken)
            .ConfigureAwait(false);
        return response.IsSuccess ? Unzip(response.Body ?? []) : null;
    }

    /// <summary>
    /// Dosya biçimi doğrulanmadı (kurulum belgesi 2.1). ZIP gelirse ilk girdi açılır (boyut sınırıyla), değilse olduğu gibi.
    /// </summary>
    public static byte[] Unzip(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length < 2 || body[0] != (byte)'P' || body[1] != (byte)'K')
        {
            return body;
        }

        using var zip = new ZipArchive(new MemoryStream(body), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault() ?? throw new InvalidDataException("ZIP boş.");
        if (entry.Length > ControlSourceOptions.Tranco().MaxResponseBytes)
        {
            throw new InvalidDataException("ZIP girdisi çok büyük.");
        }

        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose() => _http.Http.Dispose();
}
