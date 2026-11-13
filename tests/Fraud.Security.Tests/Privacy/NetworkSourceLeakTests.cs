using System.Net;
using System.Text;
using System.Text.Json;
using Fraud.Collectors.InternetDb;
using Fraud.Collectors.RipeStat;

namespace Fraud.Security.Tests.Privacy;

/// <summary>
/// RIPEstat ve InternetDB cevaplarındaki kişi verisi (Kural 5) ve üçüncü kişi alan adları (Kural 4) saklanmamalı;
/// zehirli veya tutarsız alanlar bütün cevabı geçersiz yapmalı.
/// </summary>
public sealed class NetworkSourceLeakTests
{
    private static readonly IPAddress Ip = IPAddress.Parse("192.0.2.10");

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Internetdb_hostnames_vulns_and_email_like_values_are_never_kept()
    {
        var body = Json("""
            {"ip":"192.0.2.10","ports":[80,443],
             "hostnames":["listede-olmayan-site.example","kisi.adi.example"],
             "vulns":["CVE-2026-0001"],
             "tags":["cdn","ali@posta.example","<script>","Self-Signed","a b"],
             "cpes":["cpe:/a:nginx:nginx:1.25","cpe:/a:ali@posta.example:x","cpe:/a:x\"onload=\"y","cpe:/a:a b:c"]}
            """);

        var host = InternetDbResponseParser.ParseHost(body, Ip);

        Assert.NotNull(host);
        var stored = JsonSerializer.Serialize(host);
        foreach (var forbidden in new[] { "listede-olmayan", "kisi.adi", "CVE-", "@", "<", "\\u0022", " " })
        {
            Assert.DoesNotContain(forbidden, stored, StringComparison.Ordinal);
        }

        Assert.Equal(["cdn"], host.Tags);
        Assert.Equal(["cpe:/a:nginx:nginx:1.25"], host.Cpes);
        Assert.Equal(7, host.SkippedEntries);
    }

    [Theory]
    [InlineData("cpe:/a:ali%40posta.example:x")]   // yüzde kodlu @
    [InlineData("cpe:/a:ali%2540posta.example:x")] // çift kodlu
    public void Internetdb_cpe_with_percent_encoded_email_is_not_kept(string cpe)
    {
        // Bulgu N5 (olası): CPE kalıbı '%' kabul ediyor; yüzde kodlu e-posta (Kural 5) saklanabilir.
        var body = Json($$"""{"ip":"192.0.2.10","ports":[],"cpes":["{{cpe}}"]}""");

        var host = InternetDbResponseParser.ParseHost(body, Ip);

        Assert.NotNull(host);
        Assert.Empty(host.Cpes);
    }

    [Theory]
    [InlineData("""{"ip":"192.0.2.11","ports":[80]}""")]                  // başka IP'nin cevabı
    [InlineData("""{"ip":"::ffff:192.0.2.10","ports":[80]}""")]           // aynı IP'nin başka yazımı bile kabul edilmez
    [InlineData("""{"ip":"192.0.2.10","ports":[70000]}""")]
    [InlineData("""{"ip":"192.0.2.10","ports":[-1]}""")]
    [InlineData("""{"ip":"192.0.2.10","ports":[80.5]}""")]
    [InlineData("""{"ip":"192.0.2.10","ports":["80"]}""")]
    [InlineData("""{"ip":"192.0.2.10"}""")]
    [InlineData("""{"ip":"192.0.2.10","ports":[],"tags":"cdn"}""")]
    [InlineData("""{"ip":"192.0.2.10","ports":[[[[[[[[[[1]]]]]]]]]]}""")]
    public void Internetdb_inconsistent_answer_is_rejected_as_a_whole(string body)
    {
        Assert.Null(InternetDbResponseParser.ParseHost(Json(body), Ip));
    }

    [Fact]
    public void Internetdb_oversized_lists_are_rejected_without_regex_blowup()
    {
        var ports = string.Join(',', Enumerable.Range(1, 1025));
        var tags = string.Join(',', Enumerable.Repeat("\"" + new string('a', 30) + "!\"", 129));
        var cpe = "\"cpe:/a" + string.Concat(Enumerable.Repeat(":aaaa", 2000)) + "!\"";
        var started = System.Diagnostics.Stopwatch.StartNew();

        Assert.Null(InternetDbResponseParser.ParseHost(Json($$"""{"ip":"192.0.2.10","ports":[{{ports}}]}"""), Ip));
        Assert.Null(InternetDbResponseParser.ParseHost(Json($$"""{"ip":"192.0.2.10","ports":[],"tags":[{{tags}}]}"""), Ip));
        var host = InternetDbResponseParser.ParseHost(Json($$"""{"ip":"192.0.2.10","ports":[],"cpes":[{{cpe}}]}"""), Ip);

        Assert.NotNull(host);
        Assert.Empty(host.Cpes);
        Assert.True(started.ElapsedMilliseconds < 2000, $"Süre {started.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Ripestat_holder_names_and_contacts_are_never_kept()
    {
        var body = Json("""
            {"status":"ok","data":{"asns":["64500"],"prefix":"192.0.2.0/24",
             "holder":"Ali Veli Hosting","contact":"ali@posta.example","related":["listede-olmayan.example"]},
             "messages":[["info","<script>"]]}
            """);

        var info = RipeStatResponseParser.Parse(body, Ip);

        Assert.NotNull(info);
        var stored = JsonSerializer.Serialize(info);
        Assert.DoesNotContain("Ali", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("@", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("listede", stored, StringComparison.Ordinal);
        Assert.Equal([64500L], info.Asns);
        Assert.Equal("192.0.2.0/24", info.Prefix);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("128.0.0.0/1")]
    [InlineData("192.0.0.0/4")]
    public void Ripestat_implausibly_short_prefix_is_rejected(string prefix)
    {
        // Bulgu N7 (düşük): Önek yalnızca "IP'yi içeriyor mu" diye denetleniyor. /0 bütün IPv4 adreslerini kapsar;
        // zehirli veya hatalı bir cevap, bütün IP'leri "aynı ağ" kümesine sokar. İnternette /8'den kısa önek duyurulmaz.
        var body = Json($$$"""{"status":"ok","data":{"asns":["64500"],"prefix":"{{{prefix}}}"}}""");

        Assert.Null(RipeStatResponseParser.Parse(body, Ip));
    }

    [Theory]
    [InlineData("""{"status":"ok","data":{"asns":["+64500"]}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500 "]}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["٦٤٥٠٠"]}}""")] // Arapça-Hint rakamları
    [InlineData("""{"status":"ok","data":{"asns":[6.45e4]}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"192.0.2.0/24\n"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"192.0.2.0/24 <x>"}}""")]
    public void Ripestat_odd_number_spellings_are_rejected(string body)
    {
        Assert.Null(RipeStatResponseParser.Parse(Json(body), Ip));
    }

    [Theory]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"192.0.2.0/24"},"status":"error"}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"192.0.2.0/024"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"192.000.002.000/24"}}""")]
    [InlineData("""{"status":"ok","data":{"asns":["64500"],"prefix":"3221225984/24"}}""")]
    public void Ripestat_ambiguous_spellings_are_rejected_or_stored_only_in_canonical_form(string body)
    {
        // Saklanan önek IPNetwork.ToString() ile yeniden yazılır; garip yazım veritabanına girmez.
        var info = RipeStatResponseParser.Parse(Json(body), Ip);

        Assert.True(info is null || info.Prefix == "192.0.2.0/24", info?.Prefix);
    }
}
