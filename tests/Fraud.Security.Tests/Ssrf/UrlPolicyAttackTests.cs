using Fraud.Domain.Fetching;
using Fraud.Infrastructure.Fetching;

namespace Fraud.Security.Tests.Ssrf;

/// <summary>
/// Adres metni denetimi (SafeUrlPolicy). Saldırgan, izin listesini atlatmak için IP'nin başka yazımlarını,
/// kullanıcı bilgisi hilelerini ve farklı şemaları dener. Hiçbiri ağa çıkmaz; yalnızca karar sınanır.
/// </summary>
public sealed class UrlPolicyAttackTests
{
    private static readonly SafeFetcherOptions Options = new();

    [Theory]
    // IPv4'ün alternatif yazımları
    [InlineData("https://0x7f.0x0.0x0.0x1/")]
    [InlineData("https://0x7f.1/")]
    [InlineData("https://017700000001/")]
    [InlineData("https://0177.0.0.01/")]
    [InlineData("https://127.000.000.001/")]
    [InlineData("https://0/")]
    [InlineData("https://0.0.0.0/")]
    [InlineData("https://4294967295/")]
    [InlineData("https://2852039166/")]            // 169.254.169.254 ondalık
    [InlineData("https://0xa9fea9fe/")]            // 169.254.169.254 onaltılık
    [InlineData("https://0251.0376.0251.0376/")]   // 169.254.169.254 sekizlik
    // IPv6 eşlemeli ve gömülü IPv4
    [InlineData("https://[::ffff:7f00:1]/")]
    [InlineData("https://[0:0:0:0:0:ffff:127.0.0.1]/")]
    [InlineData("https://[::ffff:a9fe:a9fe]/")]
    [InlineData("https://[::ffff:169.254.169.254]/")]
    [InlineData("https://[64:ff9b::a9fe:a9fe]/")]
    [InlineData("https://[2002:a9fe:a9fe::1]/")]
    [InlineData("https://[fd00:ec2::254]/")]
    [InlineData("https://[fe80::1%25eth0]/")]
    // Ad gibi görünen iç adresler ve benzer adlar
    [InlineData("https://localhost./")]
    [InlineData("https://169.254.169.254.nip.io/")]
    [InlineData("https://web.archive.org.127.0.0.1.nip.io/")]
    [InlineData("https://xweb.archive.org/")]
    [InlineData("https://web-archive.org/")]
    [InlineData("https://w\u0435b.archive.org/")]   // Kiril "\u0435": görsel benzer ad
    [InlineData("https://\u2460\u2461\u2466.0.0.1/")] // Unicode rakamlar
    // Kullanıcı bilgisi ve parça hileleri
    [InlineData("https://web.archive.org&@127.0.0.1/")]
    [InlineData("https://web.archive.org:443@127.0.0.1/")]
    [InlineData("https://127.0.0.1#@web.archive.org/")]
    [InlineData("https://127.0.0.1?@web.archive.org/")]
    [InlineData("https://user:pass@web.archive.org/")]
    [InlineData("https://web.archive.org\\@127.0.0.1/")]
    // İzinli ad, izinsiz port
    [InlineData("https://web.archive.org:80/")]
    [InlineData("https://web.archive.org:6379/")]
    public void Ip_notations_and_host_tricks_never_pass_the_url_policy(string raw)
    {
        // .NET ayrıştırıcısı bazı girdileri hiç kabul etmeyebilir; bu da güvenli sonuçtur.
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url))
        {
            return;
        }

        Assert.NotEqual(SafeFetchBlockReason.None, SafeUrlPolicy.Check(url, Options));
    }

    [Theory]
    [InlineData("file://web.archive.org/etc/passwd")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("gopher://web.archive.org:70/_GET%20/")]
    [InlineData("ftp://web.archive.org/")]
    [InlineData("dict://web.archive.org:11211/stat")]
    [InlineData("ldap://web.archive.org/")]
    [InlineData("ws://web.archive.org/")]
    [InlineData("wss://web.archive.org/")]
    [InlineData("http://web.archive.org/")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("jar:https://web.archive.org/x.jar!/")]
    [InlineData("netdoc:///etc/passwd")]
    public void Non_https_schemes_are_refused(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url))
        {
            return;
        }

        Assert.Equal(SafeFetchBlockReason.SchemeNotAllowed, SafeUrlPolicy.Check(url, Options));
    }

    [Theory]
    [InlineData("https://web.archive.org/web/2026/http://ornek.example/")]
    [InlineData("HTTPS://WEB.ARCHIVE.ORG/")]
    [InlineData("https://web.archive.org./")]
    [InlineData("https://web.archive.org:443/")]
    public void Legitimate_archive_urls_still_pass(string raw)
    {
        var url = new Uri(raw);

        Assert.Equal(SafeFetchBlockReason.None, SafeUrlPolicy.Check(url, Options));
        Assert.Equal("web.archive.org", url.IdnHost.TrimEnd('.').ToLowerInvariant());
    }

    [Fact]
    public void Configured_allow_list_entry_with_spaces_or_case_is_not_a_wildcard()
    {
        var options = new SafeFetcherOptions();
        options.AllowedHosts.Add("  Archive.Example  ");

        Assert.Equal(SafeFetchBlockReason.None, SafeUrlPolicy.Check(new Uri("https://archive.example/"), options));
        Assert.Equal(SafeFetchBlockReason.HostNotAllowed, SafeUrlPolicy.Check(new Uri("https://sub.archive.example/"), options));
        Assert.Equal(SafeFetchBlockReason.HostNotAllowed, SafeUrlPolicy.Check(new Uri("https://archive.example.evil.example/"), options));
    }
}
