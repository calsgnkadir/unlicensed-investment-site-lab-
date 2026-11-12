using Fraud.Domain.DomainNames;
using Fraud.Domain.Fetching;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Fetching;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "FraudDb";

    public static IServiceCollection AddFraudInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"'ConnectionStrings:{ConnectionStringName}' tanımlı değil. Ortam değişkeni veya user-secrets ile verin.");
        }

        services.AddDbContext<FraudDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IDomainNameNormalizer, PublicSuffixDomainNormalizer>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Güvenli sayfa çekiciyi kaydeder. Şu an yalnızca Aşama 0 <c>archive-sample</c> komutu kullanır.
    /// </summary>
    /// <param name="decorateTransport">
    /// İsteğe bağlı sarmalayıcı (ör. hız sınırı). Gerçek ağ işleyicisi her zaman en içte kalır; böylece
    /// bağlantı anındaki IP denetimi (<see cref="SafeConnector"/>) atlanamaz. Sarmalayıcı yönlendirme
    /// adımları dahil her HTTP isteğini görür.
    /// </param>
    public static IServiceCollection AddSafeFetcher(
        this IServiceCollection services,
        SafeFetcherOptions options,
        Func<IServiceProvider, HttpMessageHandler, HttpMessageHandler>? decorateTransport = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.AddSingleton<ISafeFetcher>(sp =>
        {
            HttpMessageHandler transport = SafeFetcher.CreateTransport(sp.GetRequiredService<IHostAddressResolver>());
            if (decorateTransport is not null)
            {
                transport = decorateTransport(sp, transport);
            }

            return new SafeFetcher(transport, options, sp.GetRequiredService<TimeProvider>());
        });
        return services;
    }
}
