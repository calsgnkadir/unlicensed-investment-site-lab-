using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Wayback;

namespace Fraud.Security.Tests.Privacy;

/// <summary>
/// Kural 5: Veri setinde kişi adı, e-posta, telefon tutulmaz. Sertifika ve arşiv alanları saklanmadan önce temizlenir.
/// </summary>
public sealed class PersonalDataLeakTests
{
    // ---------- Sertifika adları ----------

    [Fact]
    public void Smime_email_and_free_text_names_are_dropped_from_name_value()
    {
        var names = CertificateFieldSanitizer.SanitizeNames(
            "ornek.example\nkisi@ornek.example\nKISI.ADI@ORNEK.EXAMPLE\nAhmet Yılmaz\nmailto:kisi@ornek.example\n+90 555 111 22 33\n*.ornek.example");

        Assert.Equal(["*.ornek.example", "ornek.example"], names);
    }

    [Theory]
    [InlineData("kisi@ornek.example")]
    [InlineData("Ahmet Yılmaz")]
    [InlineData("<script>alert(1)</script>.ornek.example")]
    [InlineData("ornek.example\u202E")]
    [InlineData("xn--rnek-zoa.example\u0000")]
    public void Common_name_that_is_not_a_dns_name_is_dropped(string value)
    {
        Assert.Null(CertificateFieldSanitizer.SanitizeName(value));
    }

    [Fact]
    public void Dns_name_check_does_not_accept_a_trailing_newline()
    {
        // .NET'te "$" son satır sonundan önce de eşleşir; "\z" kullanılmazsa "ad\n" geçerli DNS adı sayılır.
        Assert.False(CertificateFieldSanitizer.IsDnsName("ornek.example\n"));
    }

    [Fact]
    public void CrtSh_query_builder_rejects_a_host_with_a_trailing_newline()
    {
        Assert.Throws<ArgumentException>(() => CrtShClient.BuildQueryUri("ornek.example\n"));
    }

    // ---------- Issuer ----------

    [Theory]
    [InlineData("emailAddress=kisi@ornek.example, CN=Ornek CA", "CN=Ornek CA")]
    [InlineData("E=kisi@ornek.example, O=Ornek, CN=Ornek CA", "O=Ornek, CN=Ornek CA")]
    [InlineData("CN=kisi@ornek.example, O=Ornek", "O=Ornek")]
    [InlineData("street=Ornek Sok. 1, telephoneNumber=+905551112233, postalCode=34000, C=TR, CN=Ornek CA", "C=TR, CN=Ornek CA")]
    [InlineData("O=\"Ornek, Inc.\", CN=R3", "O=\"Ornek, Inc.\", CN=R3")]
    [InlineData("O=a\\,emailAddress=kisi@ornek.example, CN=R3", "CN=R3")]
    [InlineData("cn=Ornek CA, l=Istanbul", "CN=Ornek CA")]
    public void Issuer_keeps_only_allowed_attributes_without_email(string dn, string expected)
    {
        Assert.Equal(expected, CertificateFieldSanitizer.SanitizeIssuer(dn));
    }

    [Theory]
    [InlineData("C=TR, CN=Ornek CA + telephoneNumber=+905551112233")]
    [InlineData("C=TR, CN=Ornek CA + serialNumber=12345678901")]
    [InlineData("C=TR, CN=Ornek CA+UID=ahmet.yilmaz")]
    public void Issuer_multi_valued_rdn_does_not_smuggle_other_attributes(string dn)
    {
        // OpenSSL tek satır biçimi çok değerli RDN'i "+" ile yazar. Bölünmezse telefon, kimlik no gibi
        // izin listesinde olmayan alanlar CN değerinin içinde saklanır.
        var sanitized = CertificateFieldSanitizer.SanitizeIssuer(dn);

        Assert.DoesNotContain("telephoneNumber", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serialNumber", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UID", sanitized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("O=Ornek\u0000Corp, CN=R3")]
    [InlineData("O=Ornek\r\nINFO sahte log satiri, CN=R3")]
    [InlineData("CN=\u202Etxt.exe")]
    [InlineData("CN=Ornek\u0007CA")]
    public void Issuer_control_and_bidi_characters_are_removed(string dn)
    {
        // NUL karakteri PostgreSQL metin alanına yazılamaz (tüm işlem düşer); CR/LF ve yön karakterleri
        // ileride log ve panelde sahte satır veya ters çevrilmiş metin üretir.
        var sanitized = CertificateFieldSanitizer.SanitizeIssuer(dn);

        Assert.DoesNotContain(sanitized, c => char.IsControl(c) || c is '\u202E' or '\u202D' or '\u2066' or '\u2067' or '\u2068');
    }

    [Fact]
    public void Issuer_truncation_does_not_split_a_surrogate_pair()
    {
        // 1023 karakter + 2 karakterlik emoji: 1024'te kesilince yalnız kalan yüksek vekil (lone surrogate) oluşur.
        var dn = "CN=" + new string('a', 1020) + "\U0001F600";

        var sanitized = CertificateFieldSanitizer.SanitizeIssuer(dn);

        Assert.False(sanitized.Length > 0 && char.IsHighSurrogate(sanitized[^1]), "Kesme vekil çiftini böldü.");
    }

    // ---------- Arşiv adresleri ----------

    [Theory]
    [InlineData("http://kullanici:parola@ornek.example/giris?email=kisi@ornek.example&tel=905551112233#kisi", "http://ornek.example/giris", true)]
    [InlineData("https://ornek.example/abonelik/kisi@ornek.example/iptal", "https://ornek.example/abonelik/[redacted]/iptal", false)]
    [InlineData("https://ornek.example/abonelik/kisi%40ornek.example", "https://ornek.example/abonelik/[redacted]", false)]
    [InlineData("ornek.example/a", "http://ornek.example/a", false)]
    [InlineData("HTTP://ORNEK.EXAMPLE:8080/A", "http://ornek.example:8080/A", false)]
    [InlineData("http://örnek.example/", "http://xn--rnek-4qa.example/", false)]
    public void Archive_url_drops_query_fragment_userinfo_and_email_segments(string raw, string expected, bool queryRemoved)
    {
        var result = ArchiveUrlSanitizer.Sanitize(raw);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Value.Url);
        Assert.Equal(queryRemoved, result.Value.QueryRemoved);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://ornek.example/")]
    [InlineData("vbscript:msgbox(1)")]
    public void Archive_url_with_dangerous_scheme_is_dropped(string raw)
    {
        Assert.Null(ArchiveUrlSanitizer.Sanitize(raw));
    }

    [Theory]
    [InlineData("http://ornek.example/<script>alert(1)</script>")]
    [InlineData("http://ornek.example/\"onmouseover=alert(1)")]
    [InlineData("http://ornek.example/\u202Egnp.exe")]
    [InlineData("http://ornek.example/a\r\nINFO sahte")]
    public void Archive_url_markup_newlines_and_bidi_are_percent_encoded(string raw)
    {
        var result = ArchiveUrlSanitizer.Sanitize(raw);

        if (result is { } r)
        {
            Assert.DoesNotContain(r.Url, c => c is '<' or '>' or '"' or '\r' or '\n' or '\u202E');
        }
    }

    [Fact]
    public void Archive_url_double_encoded_email_is_redacted()
    {
        // "%2540" iki kez çözülünce "@" olur. Yalnızca "@" ve "%40" aranıyor.
        var result = ArchiveUrlSanitizer.Sanitize("https://ornek.example/abonelik/kisi%2540ornek.example");

        Assert.NotNull(result);
        Assert.DoesNotContain("ornek.example/abonelik/kisi", result.Value.Url, StringComparison.Ordinal);
    }
}
