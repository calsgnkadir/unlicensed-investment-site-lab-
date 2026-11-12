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
    /// Güvenli sayfa çekiciyi kaydeder. Hafta 2'de hiçbir iş bunu çağırmaz; arşiv içeriği sonraki
    /// haftalarda, kural belgelendikten sonra çekilecek.
    /// </summary>
    public static IServiceCollection AddSafeFetcher(this IServiceCollection services, SafeFetcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.AddSingleton<ISafeFetcher>(sp => new SafeFetcher(
            SafeFetcher.CreateTransport(sp.GetRequiredService<IHostAddressResolver>()),
            options,
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
