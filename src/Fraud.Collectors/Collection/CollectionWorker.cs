using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.Collection;

/// <summary>
/// Arka planda belirli aralıklarla bütün kaynakları sırayla toplar (<c>worker</c> komutu).
/// Her tur kendi DI kapsamını açar; böylece her turda yeni bir veritabanı bağlamı kullanılır.
/// </summary>
public sealed class CollectionWorker(
    IServiceScopeFactory scopeFactory,
    WorkerOptions options,
    TimeProvider timeProvider,
    ILogger<CollectionWorker> logger) : BackgroundService
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Bir kaynağın beklenmeyen hatası diğer kaynakları ve sonraki turları durdurmamalı; hata türü loglanır.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var source in options.EffectiveSources)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<CollectionRunner>();
                    await runner.RunAsync(source, force: false, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Yalnızca istisna türü yazılır; mesaj, sorgu adresi veya veri içerebilir.
                    CollectorLog.RunCrashed(logger, source, ex.GetType().Name);
                }
            }

            CollectorLog.NextRun(logger, options.Interval);
            try
            {
                await Task.Delay(options.Interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
