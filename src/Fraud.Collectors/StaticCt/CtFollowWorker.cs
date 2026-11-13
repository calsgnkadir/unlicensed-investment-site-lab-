using System.Diagnostics.CodeAnalysis;
using Fraud.Collectors.Collection;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.StaticCt;

/// <summary>
/// Canlı akış (<c>ct-follow</c> komutu): Etkin her log için ayrı bir döngü. Geride tam tile kaldıkça beklemeden,
/// yetişince <see cref="StaticCtOptions.PollInterval"/> kadar bekleyerek devam eder.
/// </summary>
/// <remarks>
/// <para>
/// Her tur kendi DI kapsamını (yeni veritabanı bağlamı) açar. Kalınan yer veritabanında olduğu için program
/// kapanıp açılınca aynı yerden devam edilir.
/// </para>
/// <para>
/// Aynı log için iki süreç aynı anda tur yapamaz: Döngü, tur yapmadan önce o logun veritabanı kilidini
/// (<see cref="ICtLogLockProvider"/>) almış olmalıdır. Kilit doluysa süreç bekler ve kilit boşalınca devralır.
/// Kilit her turdan önce yeniden kontrol edilir; bağlantı koptuysa önce kilit yeniden alınır.
/// </para>
/// <para>
/// Sağlık dosyası (<see cref="StaticCtOptions.HeartbeatFile"/>): Etkin her log son yazımdan bu yana en az bir turu
/// hatasız bitirdiyse yeniden yazılır. Bir log sürekli hata alırsa veya kilidi alamazsa dosya eskir.
/// </para>
/// </remarks>
public sealed class CtFollowWorker(
    IServiceScopeFactory scopeFactory,
    StaticCtOptions options,
    TimeProvider timeProvider,
    ILogger<CtFollowWorker> logger,
    ICtLogLockProvider lockProvider,
    StaticCtMetrics? metrics = null) : BackgroundService
{
    private readonly HashSet<string> _healthySinceLastBeat = new(StringComparer.Ordinal);
    private int _enabledCount;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logs = options.Logs.Select((log, i) => (log, Number: i + 1)).Where(x => x.log.Enabled).ToList();
        if (logs.Count == 0)
        {
            throw new InvalidOperationException("StaticCt:Logs içinde etkin log yok.");
        }

        _enabledCount = logs.Count;
        return Task.WhenAll(logs.Select(x => FollowAsync(x.log, x.Number, stoppingToken)));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir turun beklenmeyen hatası akışı durdurmamalı; yalnızca istisna türü loglanır, sonra beklenip yeniden denenir.")]
    private async Task FollowAsync(CtLogOptions log, int number, CancellationToken stoppingToken)
    {
        // Ana iş parçacığını bırak: Loglar birbirini beklemesin.
        await Task.Yield();

        // Her etkin logun serisi ilk tur bitmeden oluşur; hiç tur bitiremeyen log da uyarıda görünür.
        metrics?.FollowerStarted(log.Name);
        ICtLogLock? held = null;
        var busyReported = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                CtBatchResult? result = null;
                try
                {
                    if (held is not null && !await held.IsHeldAsync(stoppingToken).ConfigureAwait(false))
                    {
                        StaticCtLog.LockLost(logger, number);
                        var lost = held;
                        held = null;
                        await lost.DisposeAsync().ConfigureAwait(false);
                    }

                    if (held is null)
                    {
                        held = await lockProvider.TryAcquireAsync(log.Name, stoppingToken).ConfigureAwait(false);
                        if (held is not null)
                        {
                            StaticCtLog.LockAcquired(logger, number);
                        }
                    }

                    if (held is null)
                    {
                        if (!busyReported)
                        {
                            StaticCtLog.LockBusy(logger, number);
                            busyReported = true;
                        }
                    }
                    else
                    {
                        busyReported = false;
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var follower = StaticCtFollowerFactory.Create(scope.ServiceProvider, log, number);
                        result = await follower.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                        if (result.Status != CtBatchStatus.Failed)
                        {
                            MarkHealthy(log.Name);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Mesaj veya istisna nesnesi yazılmaz: adres ya da çekilen veri içerebilir.
                    CollectorLog.RunCrashed(logger, CollectionSource.StaticCt, ex.GetType().Name);
                }

                if (result is { HasMoreWork: true })
                {
                    continue;
                }

                try
                {
                    await Task.Delay(options.PollInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            if (held is not null)
            {
                await held.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Sağlık dosyasının yazılamaması izlemeyi durdurmamalı; yalnızca HResult sayısı loglanır (yol veya mesaj yazılmaz).")]
    private void MarkHealthy(string logName)
    {
        if (string.IsNullOrWhiteSpace(options.HeartbeatFile))
        {
            return;
        }

        lock (_healthySinceLastBeat)
        {
            _healthySinceLastBeat.Add(logName);
            if (_healthySinceLastBeat.Count < _enabledCount)
            {
                return;
            }

            _healthySinceLastBeat.Clear();
            try
            {
                File.WriteAllText(options.HeartbeatFile,
                    timeProvider.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                StaticCtLog.HeartbeatFailed(logger, ex.HResult);
            }
        }
    }
}

/// <summary>Bir DI kapsamından <see cref="StaticCtFollower"/> kurar.</summary>
public static class StaticCtFollowerFactory
{
    public static StaticCtFollower Create(IServiceProvider services, CtLogOptions log, int logNumber)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new StaticCtFollower(
            log,
            logNumber,
            services.GetRequiredService<IStaticCtClientFactory>().Create(log),
            services.GetRequiredService<ICtStateStore>(),
            new TileProcessor(services.GetRequiredService<ICandidateFilter>()),
            services.GetRequiredService<StaticCtOptions>(),
            services.GetRequiredService<CollectionOptions>(),
            services.GetRequiredService<StaticCtMetrics>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger<StaticCtFollower>());
    }
}
