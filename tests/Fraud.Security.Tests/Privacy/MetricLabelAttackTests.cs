using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Fraud.Collectors.Collection;
using Fraud.Collectors.Monitoring;
using Fraud.Domain.Collection;
using Fraud.Domain.Network;
using Fraud.Infrastructure.Observability;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Privacy;

/// <summary>
/// Metrik etiketleri (Kural 4 ve 5): Alan adı, IP, URL, e-posta hiçbir etikete giremez; izinsiz değer tek bir sabit
/// değere ("rejected") düşer, yani saldırgan değerlerle sınırsız zaman serisi üretilemez. Port açılmaz; dışa aktarıcı
/// kurulmaz (uçtan uca /metrics denetimi Integration testinde).
/// </summary>
public sealed partial class MetricLabelAttackTests
{
    public static TheoryData<string> HostileValues => new()
    {
        "listede-olmayan.example", "ornek.example.", "192.0.2.10", "::1", "2001:db8::1", "http://ornek.example/a?b=c",
        "/health?x=ornek.example", "ali@posta.example", "xn--rnek-zoa", "örnek", "a\nb", "a\r\nfraud_x 1", "a\"b",
        "a}b", "rejected ", "Rejected", "", " ", "‮example", "cloudflare.com", new string('a', 33), "gen0;drop",
        "../../etc/passwd", "{domain}", "/" + new string('a', 120), "999999", "-1", "080 ",
    };

    public static TheoryData<string> AllKeys => new(MetricLabels.AllowedKeys);

    [Theory]
    [MemberData(nameof(HostileValues))]
    public void Hostile_values_become_the_single_rejected_value_for_every_key(string value)
    {
        foreach (var key in MetricLabels.AllowedKeys)
        {
            var tag = MetricLabels.Tag(key, value);
            var stored = Assert.IsType<string>(tag.Value);

            // Ya değer gerçekten izinli (ör. "999999" bir port DEĞİL ama "gen0" gibi sabitler) ya da "rejected".
            Assert.True(stored == MetricLabels.Rejected || MetricLabels.IsAllowedValue(key, stored), $"{key}={stored}");
            Assert.DoesNotMatch(@"[.@:?\s""{}\\]", key is "http.route" ? "" : stored);
        }
    }

    [Fact]
    public void Unbounded_attacker_values_cannot_create_more_than_one_series_per_key()
    {
        // Kardinalite saldırısı: 10.000 farklı alan adı / IP gelse bile etiket değer kümesi büyümez.
        foreach (var key in MetricLabels.AllowedKeys.Where(k => k is not ("port" or "http.response.status_code")))
        {
            var distinct = Enumerable.Range(0, 10_000)
                .Select(i => (string)MetricLabels.Tag(key, $"aday{i}.example").Value!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.Equal([MetricLabels.Rejected], distinct);
        }

        // Port etiketi en fazla 65536 değer alabilir (yalnızca kendi makinemizin soketleri).
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Port(-1).Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Port(65536).Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("port", "123456").Value);
    }

    [Theory]
    [InlineData("server.address")]
    [InlineData("server.port")]
    [InlineData("url.full")]
    [InlineData("url.path")]
    [InlineData("url.query")]
    [InlineData("url.scheme")]
    [InlineData("client.address")]
    [InlineData("network.peer.address")]
    [InlineData("error.type")]
    [InlineData("exception.message")]
    [InlineData("domain")]
    [InlineData("host")]
    [InlineData("ip")]
    [InlineData("db.query.text")]
    [InlineData("otel.scope.name")]
    [InlineData("service.instance.id")]
    public void Library_and_free_text_keys_are_refused_at_both_layers(string key)
    {
        Assert.Throws<ArgumentException>(() => MetricLabels.Tag(key, "x"));
        Assert.False(MetricLabels.IsAllowedKey(key));
        Assert.False(MetricLabels.IsAllowedPrometheusLabel(key.Replace('.', '_'), "x"));

        using var meter = new Meter("Fraud.Security.Tests.View");
        var counter = meter.CreateCounter<long>("x");
        var view = MetricsSetup.AllowListView(counter);
        Assert.NotNull(view);
        Assert.DoesNotContain(key, view.TagKeys!);
    }

    [Fact]
    public void Undefined_enum_values_are_rejected_not_printed_as_numbers()
    {
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("transport", (DnsTransport)99).Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("source", (CollectionSource)(-1)).Value);
        Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("error_kind", (CollectionErrorKind)12345).Value);
    }

    [Fact]
    public void Route_label_cannot_carry_a_query_string_host_or_encoded_payload()
    {
        foreach (var route in new[] { "/x?d=ornek.example", "//ornek.example/x", "/x%2F..", "/x#frag", "/x y", "http://a/", "/a.b" })
        {
            Assert.Equal(MetricLabels.Rejected, MetricLabels.Tag("http.route", route).Value);
        }

        Assert.Equal("/health/ready", MetricLabels.Tag("http.route", "/health/ready").Value);
    }

    [Fact]
    public void Metric_code_builds_tags_only_through_the_allow_list()
    {
        // İkinci katman (görünüm) yalnızca anahtarı süzer, değeri süzmez. Bu yüzden değer denetimi tek yerde,
        // MetricLabels.Tag içinde olmalı. Kaynak kodda elle kurulmuş etiket (KeyValuePair, TagList) olmamalı.
        var root = Path.Combine(RepoFiles.Root, "src");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.EndsWith("MetricLabels.cs", StringComparison.Ordinal))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("System.Diagnostics.Metrics", StringComparison.Ordinal) && RawTag().IsMatch(text);
            })
            .Select(f => Path.GetRelativePath(RepoFiles.Root, f))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Metric_descriptions_are_constant_text()
    {
        // # HELP satırı enstrüman açıklamasından gelir. İçine değişken (alan adı) girmemeli.
        var root = Path.Combine(RepoFiles.Root, "src");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => InterpolatedInstrument().Matches(File.ReadAllText(f)).Select(m => Path.GetFileName(f) + ": " + m.Value))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Run_with_no_targets_is_not_counted_as_a_successful_run()
    {
        // Bulgu M4: Classify, Failed == 0 ise Completed der. Hedef tablosu boşalırsa (yanlış yükleme, silme) her tur
        // "başarılı" görünür, son başarı zamanı güncellenir ve uyarı çıkmaz. Hepsi önbellekte taze olan tur ayrıdır
        // (SkippedFresh > 0) ve başarılı sayılabilir.
        var empty = new CollectionRunSummary(CollectionSource.Dns, 0, 0, 0, 0, 0, 0, false);

        Assert.NotEqual("Completed", CollectionMetrics.Classify(empty));
        Assert.NotEqual("Partial", CollectionMetrics.Classify(empty));
    }

    [GeneratedRegex(@"new\s+KeyValuePair<string,\s*object\?>\s*\(|\bTagList\b|new\s*\(\s*""[a-z_.]+""\s*,")]
    private static partial Regex RawTag();

    [GeneratedRegex(@"Create(Counter|Histogram|UpDownCounter|Gauge|ObservableGauge|ObservableCounter|ObservableUpDownCounter)(<[^>]+>)?\([^;]*\$""")]
    private static partial Regex InterpolatedInstrument();
}
