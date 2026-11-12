using Fraud.Domain.DomainNames;
using Fraud.Infrastructure.DomainNames;
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
}
