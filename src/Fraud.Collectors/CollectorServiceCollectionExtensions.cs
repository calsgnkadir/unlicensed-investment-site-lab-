using System.Net;
using System.Net.Http.Headers;
using Fraud.Collectors.Collection;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Collectors.Quality;
using Fraud.Collectors.Wayback;
using Fraud.Domain.Collection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Collectors;

public static class CollectorServiceCollectionExtensions
{
    /// <summary>crt.sh ve Wayback toplayıcılarını, çalıştırıcıyı ve kalite raporunu kaydeder.</summary>
    public static IServiceCollection AddCollectors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var collection = Bind<CollectionOptions>(configuration, CollectionOptions.SectionName);
        collection.Validate();
        var crtSh = Bind<CrtShOptions>(configuration, CrtShOptions.SectionName);
        var wayback = Bind<WaybackCdxOptions>(configuration, WaybackCdxOptions.SectionName);
        var worker = Bind<WorkerOptions>(configuration, WorkerOptions.SectionName);

        services.AddSingleton(collection);
        services.AddSingleton(crtSh);
        services.AddSingleton(wayback);
        services.AddSingleton(worker);

        // Kaynak başına tek hız sınırlayıcı: O kaynağa giden bütün istekler (tekrar denemeler dahil) aynı sırayı bekler.
        services.AddKeyedSingleton(CollectionSource.CrtSh,
            (sp, _) => new RequestPacer(crtSh.MinRequestInterval, sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedSingleton(CollectionSource.WaybackCdx,
            (sp, _) => new RequestPacer(wayback.MinRequestInterval, sp.GetRequiredService<TimeProvider>()));

        // HttpClientFactory'nin varsayılan logları istek adresini (dolayısıyla alan adını) yazar; kapatılır.
        services.ConfigureHttpClientDefaults(b => b.RemoveAllLoggers());

        services.AddHttpClient<ICrtShClient, CrtShClient>((http, sp) => new CrtShClient(
                new PoliteHttpClient(http, sp.GetRequiredKeyedService<RequestPacer>(CollectionSource.CrtSh), crtSh,
                    sp.GetRequiredService<TimeProvider>())))
            .ConfigureHttpClient(c => ConfigureClient(c, collection.UserAgent))
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);

        services.AddHttpClient<IWaybackCdxClient, WaybackCdxClient>((http, sp) => new WaybackCdxClient(
                new PoliteHttpClient(http, sp.GetRequiredKeyedService<RequestPacer>(CollectionSource.WaybackCdx), wayback,
                    sp.GetRequiredService<TimeProvider>()),
                wayback))
            .ConfigureHttpClient(c => ConfigureClient(c, collection.UserAgent))
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);

        services.AddScoped<ISourceCollector, CrtShCollector>();
        services.AddScoped<ISourceCollector, WaybackCollector>();
        services.AddScoped<CollectionRunner>();
        services.AddScoped<QualityReportBuilder>();
        return services;
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var options = new T();
        configuration.GetSection(section).Bind(options);
        return options;
    }

    private static void ConfigureClient(HttpClient client, string userAgent)
    {
        // Süre sınırı deneme başına PoliteHttpClient içinde uygulanır.
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static SocketsHttpHandler CreatePrimaryHandler() => new()
    {
        // Sıkıştırılmış yanıt açılır; boyut sınırı açılmış bayt üzerinden sayılır.
        AutomaticDecompression = DecompressionMethods.All,
        // Bu kaynaklar yönlendirme yapmaz; yapılırsa başka bir yere gitmeyiz, hata sayılır.
        AllowAutoRedirect = false,
        UseCookies = false,
        MaxConnectionsPerServer = 1,
        ConnectTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}
