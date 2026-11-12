namespace Fraud.Collectors.Http;

/// <summary>
/// Bir kaynağa giden isteklerin arasına en az <c>minimumInterval</c> kadar süre koyar.
/// Kaynak başına tek örnek (singleton) kullanılır; böylece o kaynağı kullanan bütün kod aynı sırayı bekler.
/// </summary>
/// <remarks>
/// Sayaç değil, "son isteğin başlangıç anı" tutulur: Bir sonraki istek ancak o andan
/// <c>minimumInterval</c> geçince başlar. Tekrar denemeler de buradan geçtiği için hata anında bile sınır aşılmaz.
/// Zaman <see cref="TimeProvider"/> ile ölçülür; testlerde sahte saat kullanılabilir.
/// </remarks>
public sealed class RequestPacer(TimeSpan minimumInterval, TimeProvider timeProvider) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long? _lastStart;

    public TimeSpan MinimumInterval => minimumInterval;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastStart is { } last)
            {
                var remaining = minimumInterval - timeProvider.GetElapsedTime(last);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }

            _lastStart = timeProvider.GetTimestamp();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
