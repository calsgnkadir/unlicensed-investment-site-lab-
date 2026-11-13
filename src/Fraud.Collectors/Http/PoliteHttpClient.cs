using System.Net.Http.Headers;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Http;
using Polly;
using Polly.Retry;

namespace Fraud.Collectors.Http;

/// <summary>Bir GET isteğinin sonucu. <see cref="Body"/> yalnızca hata yoksa doludur.</summary>
public sealed record HttpFetchResult(byte[]? Body, int? HttpStatus, int Attempts, CollectionErrorKind ErrorKind)
{
    public bool IsSuccess => ErrorKind == CollectionErrorKind.None;
}

/// <summary>
/// Dış kaynaklara nazik GET isteği atar: hız sınırı, deneme başına süre, boyut sınırı ve
/// geçici hatalarda üstel geri çekilmeyle tekrar deneme.
/// </summary>
/// <remarks>
/// Her deneme bir <see cref="AttemptResult"/> üretir; istisnalar burada yakalanıp hata türüne çevrilir.
/// Polly yalnızca "bu sonuç tekrar denenmeli mi, ne kadar beklenmeli" sorusunu cevaplar.
/// Tekrar denenen hatalar: 429, 408, 5xx, zaman aşımı, bağlantı hatası.
/// Tekrar denenmeyenler: diğer 4xx, boyut aşımı (aynı yanıt yine büyük gelir), bozuk sıkıştırma.
/// </remarks>
public sealed class PoliteHttpClient
{
    private readonly HttpClient _http;
    private readonly RequestPacer _pacer;
    private readonly SourceHttpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ResiliencePipeline<AttemptResult> _pipeline;

    public PoliteHttpClient(HttpClient http, RequestPacer pacer, SourceHttpOptions options, TimeProvider timeProvider)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        var builder = new ResiliencePipelineBuilder<AttemptResult> { TimeProvider = timeProvider };
        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<AttemptResult>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay,
                MaxDelay = options.MaxRetryDelay,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result is { } r && IsTransient(r.ErrorKind)),
                DelayGenerator = args => ValueTask.FromResult(RetryAfterDelay(args.Outcome.Result?.RetryAfter, options.MaxRetryAfter)),
            });
        }

        _pipeline = builder.Build();
    }

    public Task<HttpFetchResult> GetAsync(Uri uri, CancellationToken cancellationToken) =>
        GetAsync(uri, null, cancellationToken);

    /// <param name="bodyStatuses">
    /// Hata sayılmayıp gövdesi okunacak ek durum kodları (ör. InternetDB'nin "kayıt yok" için döndürdüğü 404).
    /// Bu kodlar tekrar denenmez; sonucun <see cref="HttpFetchResult.HttpStatus"/> alanına bakıp karar vermek çağırana kalır.
    /// </param>
    public async Task<HttpFetchResult> GetAsync(Uri uri, IReadOnlySet<int>? bodyStatuses, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var attempts = 0;
        var result = await _pipeline.ExecuteAsync(
            async token =>
            {
                attempts++;
                return await SendOnceAsync(uri, bodyStatuses, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return new HttpFetchResult(result.Body, result.HttpStatus, attempts, result.ErrorKind);
    }

    /// <summary>
    /// Sunucu <c>Retry-After</c> verdiyse o kadar (üst sınırla) beklenir; vermediyse <c>null</c> döner ve
    /// Polly üstel geri çekilmeyi kullanır.
    /// </summary>
    public static TimeSpan? RetryAfterDelay(TimeSpan? retryAfter, TimeSpan max)
    {
        if (retryAfter is not { } value)
        {
            return null;
        }

        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > max ? max : value;
    }

    public static bool IsTransient(CollectionErrorKind kind) => kind is
        CollectionErrorKind.RateLimited or
        CollectionErrorKind.ServerError or
        CollectionErrorKind.Timeout or
        CollectionErrorKind.NetworkError;

    private async ValueTask<AttemptResult> SendOnceAsync(Uri uri, IReadOnlySet<int>? bodyStatuses, CancellationToken cancellationToken)
    {
        await _pacer.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptTimeout.CancelAfter(_options.AttemptTimeout);
        var token = attemptTimeout.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            switch (status)
            {
                case var s when bodyStatuses?.Contains(s) == true:
                    break; // çağıran bu kodun gövdesini istiyor; aşağıda 2xx gibi okunur
                case 429:
                    return AttemptResult.Failed(CollectionErrorKind.RateLimited, status, ReadRetryAfter(response.Headers.RetryAfter));
                case 408:
                    return AttemptResult.Failed(CollectionErrorKind.Timeout, status, null);
                case >= 500:
                    return AttemptResult.Failed(CollectionErrorKind.ServerError, status, ReadRetryAfter(response.Headers.RetryAfter));
                case < 200 or > 299:
                    return AttemptResult.Failed(CollectionErrorKind.ClientError, status, null);
            }

            if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
            {
                return AttemptResult.Failed(CollectionErrorKind.ResponseTooLarge, status, null);
            }

            try
            {
                var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    var body = await BoundedStreamReader.ReadAsync(stream, _options.MaxResponseBytes, token).ConfigureAwait(false);
                    return body is null
                        ? AttemptResult.Failed(CollectionErrorKind.ResponseTooLarge, status, null)
                        : new AttemptResult(body, status, CollectionErrorKind.None, null);
                }
            }
            catch (InvalidDataException)
            {
                // Bozuk gzip veya deflate gövdesi. Aynı yanıt yine bozuk gelir; tekrar denenmez.
                return AttemptResult.Failed(CollectionErrorKind.InvalidResponse, status, null);
            }
            catch (InvalidOperationException)
            {
                // BrotliStream bozuk veride InvalidOperationException fırlatır. Yalnızca gövde okunurken yakalanır.
                return AttemptResult.Failed(CollectionErrorKind.InvalidResponse, status, null);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Dış iptal yok ama deneme süresi doldu: zaman aşımı.
            return AttemptResult.Failed(CollectionErrorKind.Timeout, null, null);
        }
        catch (HttpRequestException)
        {
            return AttemptResult.Failed(CollectionErrorKind.NetworkError, null, null);
        }
        catch (IOException)
        {
            return AttemptResult.Failed(CollectionErrorKind.NetworkError, null, null);
        }
    }

    private TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            return date - _timeProvider.GetUtcNow();
        }

        return null;
    }

    /// <summary>Tek denemenin sonucu (Polly'nin üzerinde karar verdiği değer).</summary>
    public sealed record AttemptResult(byte[]? Body, int? HttpStatus, CollectionErrorKind ErrorKind, TimeSpan? RetryAfter)
    {
        public static AttemptResult Failed(CollectionErrorKind kind, int? status, TimeSpan? retryAfter) =>
            new(null, status, kind, retryAfter);
    }
}
