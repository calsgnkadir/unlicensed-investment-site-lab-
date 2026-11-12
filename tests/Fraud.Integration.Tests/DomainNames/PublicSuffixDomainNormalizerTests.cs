using System.Globalization;
using Fraud.Domain.DomainNames;
using Fraud.Infrastructure.DomainNames;

namespace Fraud.Integration.Tests.DomainNames;

// Bu dosyadaki adların hepsi uydurmadır. Gerçek bir siteyi örnek olarak kullanmayın.
// .com için RFC 2606 ile ayrılmış example.com kullanılır. Gerçek çok etiketli sonek kuralı (com.tr, co.uk ...)
// gereken testlerde ayrılmış ad yoktur; orada anlamsız "uydurma-zq" etiketi kullanılır.
[Trait("Category", "Unit")]
public sealed class PublicSuffixDomainNormalizerTests
{
    private static readonly PublicSuffixDomainNormalizer Normalizer = new();

    private static NormalizedDomain Ok(string input)
    {
        var result = Normalizer.Normalize(input);
        Assert.True(result.IsSuccess, $"Beklenen başarı, gelen hata: {result.Error}");
        return result.Value!;
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("EXAMPLE.COM")]
    [InlineData("Example.Com")]
    [InlineData("www.example.com")]
    [InlineData("WWW.EXAMPLE.COM")]
    [InlineData("example.com.")]
    [InlineData("www.example.com.")]
    [InlineData("  example.com  ")]
    [InlineData("example[.]com")]
    [InlineData("https://example.com")]
    [InlineData("https://www.example.com/")]
    [InlineData("http://www.example.com/giris?ref=1#ust")]
    [InlineData("http://example.com:8080/yol")]
    [InlineData("HTTPS://WWW.EXAMPLE.COM/Giris")]
    [InlineData("example.com:443")]
    [InlineData("example.com/kampanya")]
    [InlineData("//example.com/yol")]
    public void Variants_of_the_same_name_normalize_to_one_host(string input)
    {
        var d = Ok(input);

        Assert.Equal("example.com", d.Host);
        Assert.Equal("example.com", d.RegistrableDomain);
        Assert.Equal("com", d.PublicSuffix);
        Assert.Null(d.Subdomain);
        Assert.False(d.IsSubdomain);
    }

    [Fact]
    public void Subdomain_is_kept_and_registrable_domain_is_separated()
    {
        var d = Ok("panel.uydurma-zq.com.tr");

        Assert.Equal("panel.uydurma-zq.com.tr", d.Host);
        Assert.Equal("uydurma-zq.com.tr", d.RegistrableDomain);
        Assert.Equal("com.tr", d.PublicSuffix);
        Assert.Equal("panel", d.Subdomain);
        Assert.True(d.IsSubdomain);
    }

    [Fact]
    public void Multi_label_subdomain_is_kept_whole()
    {
        var d = Ok("a.b.example.com");

        Assert.Equal("a.b", d.Subdomain);
        Assert.Equal("example.com", d.RegistrableDomain);
    }

    [Fact]
    public void Only_a_leading_www_label_is_removed()
    {
        Assert.Equal("panel.example.com", Ok("www.panel.example.com").Host);
        Assert.Equal("panel.www.example.com", Ok("panel.www.example.com").Host);
        Assert.Equal("www2.example.com", Ok("www2.example.com").Host);
    }

    [Fact]
    public void Www_is_kept_when_removing_it_would_leave_only_a_public_suffix()
    {
        var d = Ok("www.com.tr");

        Assert.Equal("www.com.tr", d.Host);
        Assert.Equal("www.com.tr", d.RegistrableDomain);
        Assert.Equal("com.tr", d.PublicSuffix);
    }

    [Theory]
    [InlineData("uydurma-zq.com.tr", "uydurma-zq.com.tr", "com.tr")]
    [InlineData("uydurma-zq.bel.tr", "uydurma-zq.bel.tr", "bel.tr")]
    [InlineData("uydurma-zq.tr", "uydurma-zq.tr", "tr")]
    [InlineData("panel.uydurma-zq.co.uk", "uydurma-zq.co.uk", "co.uk")]
    public void Multi_label_public_suffixes_are_respected(string input, string registrable, string suffix)
    {
        var d = Ok(input);

        Assert.Equal(registrable, d.RegistrableDomain);
        Assert.Equal(suffix, d.PublicSuffix);
    }

    [Fact]
    public void Private_section_suffixes_make_each_tenant_its_own_registrable_domain()
    {
        // Barındırma platformlarında her kiracı ayrı bir sahip sayılır; platformun tamamı etiketlenmez.
        var d = Ok("uydurma-zq.github.io");

        Assert.Equal("uydurma-zq.github.io", d.RegistrableDomain);
        Assert.Equal("github.io", d.PublicSuffix);
    }

    [Fact]
    public void Wildcard_and_exception_rules_are_applied()
    {
        // Listede "*.ck" ve "!www.ck" kuralları var.
        var wildcard = Ok("panel.uydurma-zq.alan.ck");
        Assert.Equal("uydurma-zq.alan.ck", wildcard.RegistrableDomain);
        Assert.Equal("alan.ck", wildcard.PublicSuffix);

        var exception = Ok("www.ck");
        Assert.Equal("www.ck", exception.RegistrableDomain);
        Assert.Equal("ck", exception.PublicSuffix);
    }

    [Fact]
    public void Unknown_top_level_domain_falls_back_to_the_default_rule()
    {
        // ".example" listede yok (RFC 2606 ile ayrılmış). Varsayılan kural "*": son etiket sonektir.
        var d = Ok("www.ornek-yatirim.example");

        Assert.Equal("ornek-yatirim.example", d.Host);
        Assert.Equal("ornek-yatirim.example", d.RegistrableDomain);
        Assert.Equal("example", d.PublicSuffix);
    }

    [Fact]
    public void Turkish_characters_are_converted_to_punycode()
    {
        var d = Ok("örnek-yatırım.example");

        Assert.StartsWith("xn--", d.Host, StringComparison.Ordinal);
        Assert.Equal(d.Host, d.RegistrableDomain);
        Assert.Equal("örnek-yatırım.example", d.UnicodeHost);
        Assert.Equal(new IdnMapping().GetAscii("örnek-yatırım.example"), d.Host);
    }

    [Fact]
    public void Punycode_and_unicode_inputs_give_the_same_result()
    {
        var fromUnicode = Ok("https://www.ÖRNEK-yatırım.example/");
        var fromAscii = Ok(new IdnMapping().GetAscii("örnek-yatırım.example"));

        Assert.Equal(fromUnicode, fromAscii);
    }

    [Fact]
    public void Uppercase_dotted_and_dotless_i_follow_invariant_rules()
    {
        // Büyük "I" her zaman "i" olur, "ı" olmaz. Türkçe kültürde ToLower() bunu "ı" yapardı ve
        // "ORNEKI.EXAMPLE" yanlışlıkla "ornekı.example" (başka bir IDN ad) olurdu.
        Assert.Equal("orneki.example", Ok("ORNEKI.EXAMPLE").Host);

        // Büyük "İ" de "i" olur.
        Assert.Equal("istanbul-ornek.example", Ok("İSTANBUL-ORNEK.EXAMPLE").Host);

        // Küçük "ı" ise gerçekten farklı bir harftir ve korunur.
        Assert.NotEqual(Ok("ornekı.example").Host, Ok("orneki.example").Host);
        Assert.StartsWith("xn--", Ok("ornekı.example").Host, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_does_not_depend_on_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var underTurkish = Ok("WWW.YATIRIM-ORNEKI.EXAMPLE");

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var underInvariant = Ok("WWW.YATIRIM-ORNEKI.EXAMPLE");

            Assert.Equal("yatirim-orneki.example", underTurkish.Host);
            Assert.Equal(underInvariant, underTurkish);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Internationalized_suffix_rules_match_both_forms()
    {
        // Listede "公司.cn" kuralı var; ASCII biçimi "xn--55qx5d.cn".
        var fromUnicode = Ok("uydurma-zq.公司.cn");
        var fromAscii = Ok("uydurma-zq.xn--55qx5d.cn");

        Assert.Equal("xn--55qx5d.cn", fromUnicode.PublicSuffix);
        Assert.Equal("uydurma-zq.xn--55qx5d.cn", fromUnicode.RegistrableDomain);
        Assert.Equal(fromUnicode, fromAscii);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("WWW.Panel.Uydurma-ZQ.COM.TR.")]
    [InlineData("https://örnek-yatırım.example/a")]
    [InlineData("ornek-yatirim.example")]
    public void Normalization_is_idempotent(string input)
    {
        var once = Ok(input);
        var twice = Ok(once.Host);

        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData(null, DomainNormalizationError.Empty)]
    [InlineData("", DomainNormalizationError.Empty)]
    [InlineData("   ", DomainNormalizationError.Empty)]
    [InlineData("https://", DomainNormalizationError.Unparseable)]
    [InlineData("example com", DomainNormalizationError.Unparseable)]
    [InlineData("example.com:abc", DomainNormalizationError.Unparseable)]
    [InlineData("192.0.2.10", DomainNormalizationError.IpAddress)]
    [InlineData("http://192.0.2.10:8080/panel", DomainNormalizationError.IpAddress)]
    [InlineData("http://[2001:db8::1]/", DomainNormalizationError.IpAddress)]
    [InlineData("example..com", DomainNormalizationError.InvalidSyntax)]
    [InlineData("-example.com", DomainNormalizationError.InvalidSyntax)]
    [InlineData("example-.com", DomainNormalizationError.InvalidSyntax)]
    [InlineData("ornek_yatirim.example", DomainNormalizationError.InvalidSyntax)]
    [InlineData("example!.com", DomainNormalizationError.InvalidSyntax)]
    [InlineData("example.com..", DomainNormalizationError.InvalidSyntax)]
    [InlineData("com", DomainNormalizationError.PublicSuffixOnly)]
    [InlineData("com.tr", DomainNormalizationError.PublicSuffixOnly)]
    [InlineData("www.com", DomainNormalizationError.None)]
    [InlineData("http://kullanici:parola@example.com:8080/yol", DomainNormalizationError.EmailOrUserInfo)]
    [InlineData("destek@posta.example", DomainNormalizationError.EmailOrUserInfo)]
    [InlineData("MAILTO:destek@posta.example", DomainNormalizationError.EmailOrUserInfo)]
    [InlineData("mailto:posta.example", DomainNormalizationError.EmailOrUserInfo)]
    [InlineData("https://example.com/?email=kisi@posta.example", DomainNormalizationError.EmailOrUserInfo)]
    public void Invalid_inputs_are_rejected_with_a_reason(string? input, DomainNormalizationError expected)
    {
        var result = Normalizer.Normalize(input);

        Assert.Equal(expected, result.Error);
        Assert.Equal(expected == DomainNormalizationError.None, result.IsSuccess);
    }

    [Fact]
    public void Labels_longer_than_63_characters_are_rejected()
    {
        var label = new string('a', 64);

        Assert.Equal(DomainNormalizationError.InvalidSyntax, Normalizer.Normalize($"{label}.com").Error);
        Assert.True(Normalizer.Normalize($"{label[..63]}.com").IsSuccess);
    }

    [Fact]
    public void Names_longer_than_253_characters_are_rejected()
    {
        var label = new string('a', 60);
        var tooLong = string.Join('.', Enumerable.Repeat(label, 5)) + ".com"; // 5*61 + 3 = 308

        Assert.Equal(DomainNormalizationError.InvalidSyntax, Normalizer.Normalize(tooLong).Error);
    }

    [Fact]
    public void Custom_list_can_be_supplied()
    {
        // Gömülü liste yerine küçük bir liste ile de çalışır (sonraki liste güncellemelerini denemek için).
        var custom = new PublicSuffixDomainNormalizer("// ===BEGIN ICANN DOMAINS===\ncom\nexample.com\n// ===END ICANN DOMAINS===\n");

        var d = custom.Normalize("panel.example.com").Value!;

        Assert.Equal("example.com", d.PublicSuffix);
        Assert.Equal("panel.example.com", d.RegistrableDomain);
    }
}
