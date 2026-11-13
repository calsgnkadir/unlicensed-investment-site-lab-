using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;

namespace Fraud.Infrastructure.Observability;

/// <summary>
/// Metrik etiketlerinin izin listesi (Kural 4 ve 5). Prometheus'a giden her etiket anahtarı burada tanımlı olmalı ve
/// değeri sabit bir kümeden (enum adı, yapılandırmadaki kısa ad, port numarası) gelmeli.
/// </summary>
/// <remarks>
/// <para>
/// Neden: Etiket değerleri Grafana'da, Prometheus veritabanında ve ekran görüntülerinde görünür. Alan adı, IP, URL veya
/// sertifika konusu bir etikete girerse listede olmayan bir site adıyla yayımlanmış olur. Ayrıca serbest metin etiketi
/// her yeni değerde yeni bir zaman serisi açar (kardinalite patlaması).
/// </para>
/// <para>İki katmanlı denetim:</para>
/// <list type="number">
/// <item>Üretici tarafı: Kod etiketi yalnızca <see cref="Tag(string, string)"/> veya <see cref="Tag{TEnum}(string, TEnum)"/>
/// ile kurar. İzinsiz anahtar programlama hatasıdır ve istisna fırlatır; izinsiz değer <see cref="Rejected"/> olur
/// (değerin kendisi hiçbir yere yazılmaz).</item>
/// <item>Dışa aktarma tarafı: <see cref="MetricsSetup"/> her enstrümana bir görünüm (view) ekler ve yalnızca
/// <see cref="AllowedKeys"/> içindeki anahtarları geçirir. Böylece hazır kütüphanelerin (ASP.NET Core, .NET çalışma
/// zamanı) eklediği <c>server.address</c>, <c>error.type</c> gibi etiketler de düşer.</item>
/// </list>
/// <para>Integration testi <c>/metrics</c> çıktısını okuyup bu iki katmanı birlikte doğrular.</para>
/// </remarks>
public static partial class MetricLabels
{
    /// <summary>Değeri izin listesine uymayan etiketin yerine yazılan değer.</summary>
    public const string Rejected = "rejected";

    // Sıra önemli: Statik alanlar yazıldıkları sırayla kurulur; aşağıdaki sözlük bu iki listeyi kullanır.
    /// <summary>Toplama turu sonuçları (<c>fraud_collection_runs_total</c>).</summary>
    public static IReadOnlyList<string> RunOutcomes { get; } = ["Completed", "Partial", "Failed", "NoTargets", "Cancelled", "LockedOut", "Crashed"];

    /// <summary>Tazelik ölçülen tablolar. Sorgular sabittir (DataFreshnessMonitor).</summary>
    public static IReadOnlyList<string> FreshnessTables { get; } =
        ["official_actions", "collection_log", "ct_candidates", "ct_log_state", "dns_snapshots", "ip_asn_snapshots", "port_snapshots"];

    /// <summary>Anahtar başına değer denetimi. Anahtarlar OpenTelemetry biçimindedir (Prometheus'ta nokta alt çizgi olur).</summary>
    private static readonly FrozenDictionary<string, Func<string, bool>> Validators = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
    {
        // Yapılandırmadaki Static CT log kısa adı (ör. sycamore2027h1). Nokta yok: alan adına benzeyemez.
        ["log"] = v => ShortName().IsMatch(v),
        ["source"] = OneOf(Enum.GetNames<CollectionSource>()),
        // Toplama sonucu, tur sonucu ve DNS sonucu adları. Hepsi koddaki sabitlerdir.
        ["outcome"] = OneOf([.. Enum.GetNames<CollectionOutcome>(), .. Enum.GetNames<DnsOutcome>(), .. RunOutcomes, "SkippedFresh"]),
        ["error_kind"] = OneOf([.. Enum.GetNames<CollectionErrorKind>(), .. Enum.GetNames<DnsErrorKind>()]),
        // DNS çözücü kısa adı (koddaki katalog: cloudflare, google).
        ["resolver"] = v => ShortName().IsMatch(v),
        ["transport"] = OneOf(Enum.GetNames<DnsTransport>()),
        // Son DNS turunun sınıfları (bkz. DnsMetrics): cevap, NXDOMAIN, SERVFAIL, diğer hata.
        ["result"] = OneOf("answer", "nxdomain", "servfail", "error"),
        ["table"] = OneOf(FreshnessTables),
        ["fetch"] = OneOf("checkpoint", "data_tile", "hash_tile"),
        // Port denetimi: yalnızca kendi makinemiz. Port numarası etiket olabilir; adres ve süreç adı olamaz.
        ["protocol"] = OneOf("tcp", "udp"),
        ["port"] = v => PortNumber().IsMatch(v),
        ["bind"] = OneOf("loopback", "specific", "wildcard"),
        // .NET çalışma zamanı metrikleri (System.Runtime) ve ASP.NET Core.
        ["gc.heap.generation"] = OneOf("gen0", "gen1", "gen2", "loh", "poh"),
        ["cpu.mode"] = OneOf("user", "system"),
        ["http.request.method"] = OneOf("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE", "CONNECT", "_OTHER"),
        ["http.response.status_code"] = v => StatusCode().IsMatch(v),
        // Koddaki rota şablonu (ör. /health/ready). Eşleşmeyen isteklerde bu etiket hiç yoktur.
        ["http.route"] = v => RouteTemplate().IsMatch(v),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Dışa aktarılabilen etiket anahtarları (OpenTelemetry biçimi).</summary>
    public static IReadOnlyCollection<string> AllowedKeys => Validators.Keys;

    /// <summary>Prometheus çıktısındaki karşılıkları (nokta yerine alt çizgi). Test bunu kullanır.</summary>
    public static IReadOnlySet<string> AllowedPrometheusKeys { get; } =
        Validators.Keys.Select(k => k.Replace('.', '_')).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Anahtarın izinli olup olmadığı.</summary>
    public static bool IsAllowedKey(string key) => Validators.ContainsKey(key);

    /// <summary>Değerin bu anahtar için izinli olup olmadığı. Bilinmeyen anahtarda <c>false</c>.</summary>
    public static bool IsAllowedValue(string key, string? value) =>
        value is not null && Validators.TryGetValue(key, out var validate) && (value == Rejected || validate(value));

    /// <summary>Prometheus biçimindeki anahtar ve değeri denetler (test ve kendi kendine denetim için).</summary>
    public static bool IsAllowedPrometheusLabel(string prometheusKey, string value) =>
        Validators.Any(kv => kv.Key.Replace('.', '_') == prometheusKey && (value == Rejected || kv.Value(value)));

    /// <summary>Denetlenmiş etiket. İzinsiz anahtar istisna, izinsiz değer <see cref="Rejected"/> olur.</summary>
    public static KeyValuePair<string, object?> Tag(string key, string value)
    {
        if (!Validators.TryGetValue(key, out var validate))
        {
            throw new ArgumentException("Metrik etiket anahtarı izin listesinde yok: " + key, nameof(key));
        }

        return new KeyValuePair<string, object?>(key, value is not null && validate(value) ? value : Rejected);
    }

    /// <summary>Enum değerinden etiket. Tanımsız sayısal değer (ör. <c>(DnsTransport)99</c>) <see cref="Rejected"/> olur.</summary>
    public static KeyValuePair<string, object?> Tag<TEnum>(string key, TEnum value)
        where TEnum : struct, Enum =>
        Tag(key, Enum.IsDefined(value) ? value.ToString() : Rejected);

    /// <summary>Port numarasından etiket.</summary>
    public static KeyValuePair<string, object?> Port(int port) =>
        Tag("port", port is >= 0 and <= 65535 ? port.ToString(CultureInfo.InvariantCulture) : Rejected);

    private static Func<string, bool> OneOf(params string[] values)
    {
        var set = values.ToFrozenSet(StringComparer.Ordinal);
        return set.Contains;
    }

    private static Func<string, bool> OneOf(IReadOnlyList<string> values) => OneOf([.. values]);

    [GeneratedRegex("^[a-z][a-z0-9]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortName();

    [GeneratedRegex("^[0-9]{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex PortNumber();

    [GeneratedRegex("^[1-5][0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex StatusCode();

    [GeneratedRegex("^/[A-Za-z0-9/_{}:-]{0,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RouteTemplate();
}
