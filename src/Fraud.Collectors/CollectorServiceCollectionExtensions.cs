using System.Net;
using System.Net.Http.Headers;
using Fraud.Collectors.Collection;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Dns;
using Fraud.Collectors.InternetDb;
using Fraud.Collectors.Network;
using Fraud.Collectors.RipeStat;
using Fraud.Collectors.Http;
using Fraud.Collectors.Quality;
using Fraud.Collectors.StaticCt;
using Fraud.Collectors.Wayback;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Collectors;

public static class CollectorServiceCollectionExtensions
{
    /// <summary>crt.sh, Wayback ve Static CT toplayıcılarını, çalıştırıcıyı ve kalite raporunu kaydeder.</summary>
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

        AddStaticCt(services, configuration);
        AddNetworkCollectors(services, configuration, collection.UserAgent);

        services.AddScoped<ISourceCollector, CrtShCollector>();
        services.AddScoped<ISourceCollector, WaybackCollector>();
        services.AddScoped<CollectionRunner>();
        services.AddScoped<IpCollectionRunner>();
        services.AddScoped<CollectionDispatcher>();
        services.AddScoped<QualityReportBuilder>();
        services.AddScoped<DnsCompareReportBuilder>();
        return services;
    }

    /// <summary>
    /// DNS, RIPEstat ve InternetDB. Kayıt sırasında doğrulama yapılmaz (Dns:Location yalnızca DNS kullanılınca zorunlu);
    /// <c>Program</c> ilgili komut başlarken <see cref="DnsOptions.Validate"/> çağırır.
    /// </summary>
    public static IServiceCollection AddNetworkCollectors(IServiceCollection services, IConfiguration configuration, string userAgent)
    {
        var dns = Bind<DnsOptions>(configuration, DnsOptions.SectionName);
        var ripe = Bind<RipeStatOptions>(configuration, RipeStatOptions.SectionName);
        ripe.Validate();
        var internetDb = Bind<InternetDbOptions>(configuration, InternetDbOptions.SectionName);
        internetDb.Validate();

        services.AddSingleton(dns);
        services.AddSingleton(ripe);
        services.AddSingleton(internetDb);
        services.AddSingleton(IpTargetFilter.PublicOnly);

        // DNS: Bütün yollar (UDP, TCP, DoH) tek hız sınırlayıcıyı paylaşır. Ayar daha kısa bir süre verse bile
        // (Validate çağrılmamış bir yol) saniyede 1 paket tabanı burada da uygulanır (THREAT_MODEL N6).
        services.AddKeyedSingleton(CollectionSource.Dns,
            (sp, _) => new RequestPacer(dns.MinQueryInterval > TimeSpan.FromSeconds(1) ? dns.MinQueryInterval : TimeSpan.FromSeconds(1), sp.GetRequiredService<TimeProvider>()));
        services.AddHttpClient(DohHttpClientName)
            .ConfigureHttpClient(c =>
            {
                c.Timeout = Timeout.InfiniteTimeSpan; // süre DohDnsTransport içinde
                c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => AllowedHostConnector.CreateHandler(DnsResolverCatalog.DohHosts));
        services.AddSingleton<IDnsQueryClient>(sp => new DnsQueryClient(
            [
                new UdpDnsTransport(dns.Timeout),
                new TcpDnsTransport(dns.Timeout),
                new DohDnsTransport(sp.GetRequiredService<IHttpClientFactory>().CreateClient(DohHttpClientName), dns.Timeout),
            ],
            sp.GetRequiredKeyedService<RequestPacer>(CollectionSource.Dns),
            sp.GetRequiredService<TimeProvider>(),
            dns.MaxRetries));
        services.AddScoped<ISourceCollector, DnsCollector>();

        // RIPEstat ve InternetDB: kaynak başına hız sınırı, tek bağlantı, yalnızca kendi ana bilgisayarı.
        services.AddKeyedSingleton(CollectionSource.RipeStat,
            (sp, _) => new RequestPacer(ripe.MinRequestInterval, sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedSingleton(CollectionSource.InternetDb,
            (sp, _) => new RequestPacer(internetDb.MinRequestInterval, sp.GetRequiredService<TimeProvider>()));

        services.AddHttpClient<IRipeStatClient, RipeStatClient>((http, sp) => new RipeStatClient(
                new PoliteHttpClient(http, sp.GetRequiredKeyedService<RequestPacer>(CollectionSource.RipeStat), ripe,
                    sp.GetRequiredService<TimeProvider>()),
                ripe))
            .ConfigureHttpClient(c => ConfigureClient(c, userAgent))
            .ConfigurePrimaryHttpMessageHandler(() => AllowedHostConnector.CreateHandler(new HashSet<string> { RipeStatOptions.Host }));

        services.AddHttpClient<IInternetDbClient, InternetDbClient>((http, sp) => new InternetDbClient(
                new PoliteHttpClient(http, sp.GetRequiredKeyedService<RequestPacer>(CollectionSource.InternetDb), internetDb,
                    sp.GetRequiredService<TimeProvider>())))
            .ConfigureHttpClient(c => ConfigureClient(c, userAgent))
            .ConfigurePrimaryHttpMessageHandler(() => AllowedHostConnector.CreateHandler(new HashSet<string> { InternetDbOptions.Host }));

        services.AddScoped<IIpSourceCollector, RipeStatCollector>();
        services.AddScoped<IIpSourceCollector, InternetDbCollector>();

        // Süreçler arası kilit (THREAT_MODEL N8). Bağlantı metni yalnızca kilit ilk kez istendiğinde okunur.
        services.AddSingleton(_ => new NetworkSourceLock(
            configuration.GetConnectionString(Fraud.Infrastructure.ServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException("Ağ toplama kilidi için veritabanı bağlantı metni yok.")));
        return services;
    }

    /// <summary>DoH istemcisinin HttpClientFactory adı.</summary>
    public const string DohHttpClientName = "dns-doh";

    /// <summary>Static CT istemcisi, saklama filtresi, durum deposu ve metrikler.</summary>
    public static IServiceCollection AddStaticCt(IServiceCollection services, IConfiguration configuration)
    {
        var staticCt = Bind<StaticCtOptions>(configuration, StaticCtOptions.SectionName);
        staticCt.Validate();
        var filter = new KeywordCandidateFilter(Bind<CandidateFilterOptions>(configuration, CandidateFilterOptions.SectionName));
        var userAgent = Bind<CollectionOptions>(configuration, CollectionOptions.SectionName).UserAgent;

        services.AddSingleton(staticCt);
        services.AddSingleton<ICandidateFilter>(filter);
        services.AddSingleton(sp => new StaticCtMetrics(sp.GetService<System.Diagnostics.Metrics.IMeterFactory>()));
        services.AddSingleton<IStaticCtClientFactory, StaticCtClientFactory>();
        services.AddScoped<ICtStateStore, EfCtStateStore>();

        // Bağlantı metni yalnızca kilit ilk kez istendiğinde okunur; veritabanı gerektirmeyen komutlar etkilenmez.
        services.AddSingleton<ICtLogLockProvider>(_ => new PostgresCtLogLockProvider(
            configuration.GetConnectionString(Fraud.Infrastructure.ServiceCollectionExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException("CT log kilidi için veritabanı bağlantı metni yok.")));
        services.AddHttpClient(StaticCtClientFactory.HttpClientName)
            .ConfigureHttpClient(c => ConfigureStaticCtClient(c, userAgent))
            .ConfigurePrimaryHttpMessageHandler(() => CreateStaticCtHandler(staticCt.MaxConcurrency));
        return services;
    }

    /// <summary>Static CT için HTTP istemci ayarı. Belge gzip ve identity kabul edilmesini ister.</summary>
    public static void ConfigureStaticCtClient(HttpClient client, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
    }

    /// <summary>Static CT bağlantıları: yalnızca gzip açılır, yönlendirme izlenmez, bağlantı sayısı eşzamanlılıkla sınırlı.</summary>
    public static SocketsHttpHandler CreateStaticCtHandler(int maxConcurrency) => new()
    {
        AutomaticDecompression = DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        UseCookies = false,
        MaxConnectionsPerServer = maxConcurrency,
        ConnectTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

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
