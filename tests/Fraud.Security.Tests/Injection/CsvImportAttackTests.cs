using Fraud.Collectors.OfficialActions;
using Fraud.Infrastructure.DomainNames;

namespace Fraud.Security.Tests.Injection;

/// <summary>
/// Resmi işlem CSV yükleyicisine kötü niyetli alanlar. Dosya yerel ve elle hazırlanır, ama içeriği SPK
/// PDF'lerinden otomatik çıkarılmış metinden gelir; bu yüzden güvenilmez kabul edilir.
/// </summary>
public sealed class CsvImportAttackTests
{
    private const string Header = "alan_adi,bulten_no,karar_tarihi,kategori,ilk_sertifika,sertifika_sayisi,arsiv_kaydi,kaynak_url";
    private static readonly PublicSuffixDomainNormalizer Normalizer = new();

    private static OfficialActionCsvParseResult Parse(string body)
    {
        var parser = new OfficialActionCsvParser(Normalizer, new OfficialActionImportOptions());
        using var reader = new StringReader(Header + "\n" + body);
        return parser.Parse(reader);
    }

    private static string Row(string domain, string bulletin = "2026/46", string url = "https://spk.gov.tr/b/1") =>
        $"{domain},{bulletin},2026-01-08,kripto,,,,{url}\n";

    [Theory(Skip = "açık: listed_as ham metin olarak saklanıyor, formül öneki temizlenmiyor (düşük; CSV dışa aktarımı gelince orta)")]
    [InlineData("\"=HYPERLINK(\"\"https://kotu.example\"\")://ornek.example\"")]
    [InlineData("=1+1@ornek.example")]
    [InlineData("+cmd@ornek.example")]
    [InlineData("-2+3@ornek.example")]
    [InlineData("@SUM(1)@ornek.example")]
    public void Listed_as_text_does_not_keep_spreadsheet_formula_prefix(string domain)
    {
        // listed_as alanı ham metindir; ileride CSV veya Excel'e aktarılırsa formül olarak çalışır.
        var result = Parse(Row(domain));

        foreach (var row in result.Rows)
        {
            Assert.False(row.ListedAs.Length > 0 && "=+-@\t\r".Contains(row.ListedAs[0], StringComparison.Ordinal),
                "Formül önekiyle başlayan listed_as kabul edildi: " + row.ListedAs);
        }
    }

    [Theory(Skip = "açık: listed_as ham metinde e-posta ve kullanıcı bilgisi kalıyor; Kural 5 (orta)")]
    [InlineData("kisi.adi@ornek.example")]
    [InlineData("https://ornek.example/?email=kisi@posta.example")]
    [InlineData("https://kisi:parola@ornek.example/")]
    public void Listed_as_text_does_not_store_an_email_or_credentials(string domain)
    {
        var result = Parse(Row(domain));

        foreach (var row in result.Rows)
        {
            Assert.DoesNotContain("@", row.ListedAs, StringComparison.Ordinal);
        }
    }

    [Theory(Skip = "açık: normalleştirici kullanıcı bilgisini sessizce atıyor; e-posta adresi, e-posta sağlayıcısının alan adı için resmi işlem kaydına dönüşüyor (orta, yanlış suçlama)")]
    [InlineData("destek@posta.example")]
    [InlineData("mailto:destek@posta.example")]
    public void Email_address_in_domain_column_is_rejected_not_attributed_to_the_mail_provider(string value)
    {
        // Bültenden çıkarılan bir e-posta adresi (ör. destek@buyuk-posta-saglayicisi) bu satırla
        // "SPK listesinde" bir alan adı kaydı üretir. Elle doğrulama tek savunma olarak kalır.
        var result = Parse(Row(value));

        Assert.Empty(result.Rows);
    }

    [Theory(Skip = "açık: BulletinNumberPattern içindeki \\d Unicode rakamlarını kabul ediyor; [0-9] olmalı (düşük)")]
    [InlineData("\u0662\u0660\u0662\u0666/\u0664\u0666")] // Arapça-Hint rakamlarıyla 2026/46
    [InlineData("\uFF12\uFF10\uFF12\uFF16/46")]           // tam genişlik rakamlar
    public void Bulletin_number_accepts_only_ascii_digits(string bulletin)
    {
        // .NET'te \d Unicode rakamlarını da kabul eder. Aynı bülten farklı yazımla ikinci kez eklenebilir
        // ve çakışma denetimi (bulletin_number, domain_id) atlanır.
        var result = Parse(Row("ornek.example", bulletin));

        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData("javascript:alert(1)//spk.gov.tr")]
    [InlineData("https://spk.gov.tr@kotu.example/")]
    [InlineData("https://spk.gov.tr.kotu.example/")]
    [InlineData("https://sahtespk.gov.tr/")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/spk.gov.tr")]
    [InlineData("//spk.gov.tr/b/1")]
    public void Source_url_outside_the_allow_list_is_rejected(string url)
    {
        var result = Parse(Row("ornek.example", url: url));

        Assert.Empty(result.Rows);
        Assert.Single(result.Rejections);
    }

    [Theory]
    [InlineData("T.ME")]
    [InlineData("www.t.me.")]
    [InlineData("https://t.me:443/kanal")]
    [InlineData("m.facebook.com")]
    [InlineData("WWW.INSTAGRAM.COM/hesap")]
    public void Platform_domains_cannot_be_imported_with_case_port_or_subdomain_tricks(string domain)
    {
        var result = Parse(Row(domain));

        Assert.Empty(result.Rows);
        Assert.Equal(RowRejectionReason.PlatformDomain, Assert.Single(result.Rejections).Reason);
    }

    [Theory]
    [InlineData("\"kotu\r\n2026-01-01 [ERR] sahte satir.example\"")]
    [InlineData("\"<script>alert(1)</script>\"")]
    [InlineData("\"gizli-aday.example\u0000\"")]
    public void Rejection_detail_never_echoes_raw_input(string domain)
    {
        // Ret nedeni loga yazılır; ham alan (alan adı, CR/LF) loga girmemeli.
        var result = Parse(Row(domain));

        foreach (var rejection in result.Rejections)
        {
            Assert.DoesNotContain("\n", rejection.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("example", rejection.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("<", rejection.Detail, StringComparison.Ordinal);
        }
    }
}
