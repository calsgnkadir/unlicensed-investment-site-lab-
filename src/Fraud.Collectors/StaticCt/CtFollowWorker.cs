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
/// Her tur kendi DI kapsamını (yeni veritabanı bağlamı) açar. Kalınan yer veritabanında olduğu için program
/// kapanıp açılınca aynı yerden devam edilir. Aynı log için iki süreç aynı anda çalıştırılmamalı.
/// </remarks>
public sealed class CtFollowWorker(
    IServiceScopeFactory scopeFactory,
    StaticCtOptions options,
    TimeProvider timeProvider,
    ILogger<CtFollowWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logs = options.Logs.Select((log, i) => (log, Number: i + 1)).Where(x => x.log.Enabled).ToList();
        if (logs.Count == 0)
        {
            throw new InvalidOperationException("StaticCt:Logs içinde etkin log yok.");
        }

        return Task.WhenAll(logs.Select(x => FollowAsync(x.log, x.Number, stoppingToken)));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir turun beklenmeyen hatası akışı durdurmamalı; yalnızca istisna türü loglanır, sonra beklenip yeniden denenir.")]
    private async Task FollowAsync(CtLogOptions log, int number, CancellationToken stoppingToken)
    {
        // Ana iş parçacığını bırak: Loglar birbirini beklemesin.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            CtBatchResult? result = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var follower = StaticCtFollowerFactory.Create(scope.ServiceProvider, log, number);
                result = await follower.RunOnceAsync(stoppingToken).ConfigureAwait(false);
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
