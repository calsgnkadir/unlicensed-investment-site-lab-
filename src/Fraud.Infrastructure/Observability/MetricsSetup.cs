using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Fraud.Infrastructure.Observability;

/// <summary>
/// <c>Metrics</c> bölümü. Varsayılan kapalıdır: Geliştirme makinesinde ve testlerde hiçbir port açılmaz.
/// Docker kurulumunda ortam değişkeniyle açılır (<c>Metrics__Enabled=true</c>).
/// </summary>
public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";

    public bool Enabled { get; set; }

    /// <summary>
    /// Dinlenecek adres. Konteynerde <c>*</c> (konteynerin kendi ağ arayüzleri; port host'a yayımlanmaz),
    /// port denetiminde izleme köprüsünün ağ geçidi IP'si, testte <c>localhost</c>.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Uygulamanın kendi portundan ayrı iç port. Prometheus geleneği: 9464.</summary>
    public int Port { get; set; } = 9464;

    /// <summary>Bütün arayüzler (<c>*</c> veya <c>+</c>).</summary>
    public bool IsWildcard => Host is "*" or "+";

    public void Validate()
    {
        if (Port is < 1024 or > 65535)
        {
            throw new InvalidOperationException("Metrics:Port 1024-65535 arasında olmalı.");
        }

        // Ad çözümü yok: yalnızca joker, localhost veya IPv4 yazımı. Böylece dinleme adresi bir DNS cevabına bağlı olmaz.
        if (!IsWildcard && Host != "localhost"
            && !(IPAddress.TryParse(Host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
        {
            throw new InvalidOperationException("Metrics:Host '*', 'localhost' veya bir IPv4 adresi olmalı.");
        }
    }
}

/// <summary>
/// Prometheus dışa aktarımı: .NET'in <see cref="System.Diagnostics.Metrics"/> API'si + OpenTelemetry SDK +
/// Prometheus HttpListener dışa aktarıcısı.
/// </summary>
/// <remarks>
/// <para>
/// Seçim (prometheus-net yerine OpenTelemetry): Ölçümler zaten <see cref="Meter"/> ile yazılıyordu (StaticCtMetrics);
/// OpenTelemetry bu API'yi doğrudan okur, kod belirli bir izleme ürününe bağlanmaz. .NET çalışma zamanı ve ASP.NET Core
/// metrikleri de aynı API ile gelir. prometheus-net kararlıdır ama 2024'ten beri yeni sürüm çıkmadı. Bedeli: Prometheus
/// dışa aktarıcısı hâlâ "beta" etiketli; sürüm Directory.Packages.props içinde tam sabittir.
/// </para>
/// <para>
/// HttpListener: Toplayıcı imajında ASP.NET Core yok; API'de de metrik uç noktası uygulamanın kendi portundan
/// (ve AllowedHosts filtresinden) ayrı kalsın diye aynı yol kullanılır. Port host'a yayımlanmaz; yalnızca iç Docker
/// ağındaki Prometheus erişir.
/// </para>
/// <para>
/// <c>otel_scope_*</c> etiketleri ve <c>target_info</c> kapalıdır: Biri nokta içeren meter adlarını, diğeri süreç
/// kaynak bilgisini etikete taşır. İzin listesi dışında kalır.
/// </para>
/// </remarks>
public static class MetricsSetup
{
    /// <summary>.NET 9+ çalışma zamanının kendi meter'ı (GC, iş parçacığı havuzu, CPU, bellek).</summary>
    public const string RuntimeMeterName = "System.Runtime";

    /// <summary>
    /// Prometheus dışa aktarımını kaydeder. Kapalıysa hiçbir şey eklenmez; metrik sınıfları yine çalışır
    /// (<see cref="IMeterFactory"/> host kurucusundan gelir), yalnızca kimse okumaz.
    /// </summary>
    public static IServiceCollection AddFraudMetrics(this IServiceCollection services, MetricsOptions options, params string[] meterNames)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterNames);

        if (!options.Enabled)
        {
            return services;
        }

        options.Validate();
        services.AddOpenTelemetry().WithMetrics(builder => Configure(builder, options, meterNames));
        return services;
    }

    /// <summary>Ortak ayar: meter listesi, etiket izin listesi görünümü ve dışa aktarıcı. Test de bunu kullanır.</summary>
    public static MeterProviderBuilder Configure(MeterProviderBuilder builder, MetricsOptions options, IEnumerable<string> meterNames)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        return builder
            .AddMeter([.. meterNames, RuntimeMeterName])
            .AddView(AllowListView)
            .AddPrometheusHttpListener(o =>
            {
                // Dışa aktarıcı öneki UriBuilder ile kurar; "*" geçerli bir ana bilgisayar adı değildir. Joker istenirse
                // önce geçerli bir ad verilir, sonra HttpListener'ın öneki "http://+:PORT/" ile değiştirilir. "+" her
                // Host başlığını kabul eder (Prometheus "ct-follow:9464" diye sorar); belirli IP'de ise yalnızca o adres.
                o.Host = options.IsWildcard ? "localhost" : options.Host;
                o.Port = options.Port;
                o.ScrapeEndpointPath = "/metrics";
                o.ScopeInfoEnabled = false;
                o.TargetInfoEnabled = false;
                if (options.IsWildcard)
                {
                    o.ConfigureHttpListener = (_, listener) =>
                    {
                        listener.Prefixes.Clear();
                        listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://+:{options.Port}/"));
                    };
                }
            });
    }

    /// <summary>
    /// Her enstrüman için yalnızca izinli etiket anahtarlarını geçiren görünüm. Histogram sınırları enstrümanın kendi
    /// önerisinden (<see cref="InstrumentAdvice{T}"/>) gelir; burada değiştirilmez.
    /// </summary>
    public static MetricStreamConfiguration? AllowListView(Instrument instrument) =>
        new() { TagKeys = [.. MetricLabels.AllowedKeys] };
}
