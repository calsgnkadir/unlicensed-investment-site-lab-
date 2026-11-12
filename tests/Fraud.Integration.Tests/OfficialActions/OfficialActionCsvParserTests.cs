using Fraud.Collectors.OfficialActions;
using Fraud.Domain.OfficialActions;
using Fraud.Infrastructure.DomainNames;

namespace Fraud.Integration.Tests.OfficialActions;

// Bütün satırlar uydurmadır; bülten numaraları ve bağlantılar gerçek değildir.
[Trait("Category", "Unit")]
public sealed class OfficialActionCsvParserTests
{
    internal const string Header =
        "alan_adi,bulten_no,karar_tarihi,kategori,ilk_sertifika,sertifika_sayisi,arsiv_kaydi,kaynak_url";

    private static readonly PublicSuffixDomainNormalizer Normalizer = new();

    private static OfficialActionCsvParseResult Parse(string csv, OfficialActionImportOptions? options = null)
    {
        var parser = new OfficialActionCsvParser(Normalizer, options ?? new OfficialActionImportOptions());
        using var reader = new StringReader(csv);
        return parser.Parse(reader);
    }

    [Fact]
    public void Valid_rows_are_read_and_normalized()
    {
        var result = Parse($"""
            {Header}
            https://WWW.Ornek-Yatirim.example/giris,2026/46,2026-08-14,kripto,2026-05-02,7,var,https://spk.gov.tr/bulten/2026-46
            panel.ornek.com.tr,2026/47,2026-08-21,Kaldıraçlı,,,yok,https://www.spk.gov.tr/bulten/2026-47
            """);

        Assert.Empty(result.Rejections);
        Assert.Equal(2, result.Rows.Count);

        var first = result.Rows[0];
        Assert.Equal(2, first.RowNumber);
        Assert.Equal("ornek-yatirim.example", first.Domain.Host);
        Assert.Equal("https://WWW.Ornek-Yatirim.example/giris", first.ListedAs);
        Assert.Equal("2026/46", first.BulletinNumber);
        Assert.Equal(new DateOnly(2026, 8, 14), first.DecisionDate);
        Assert.Equal(OfficialActionCategory.Crypto, first.Category);
        Assert.Equal("https://spk.gov.tr/bulten/2026-46", first.SourceUrl.AbsoluteUri);

        var second = result.Rows[1];
        Assert.Equal("panel.ornek.com.tr", second.Domain.Host);
        Assert.Equal("ornek.com.tr", second.Domain.RegistrableDomain);
        Assert.Equal(OfficialActionCategory.Leveraged, second.Category);
    }

    [Fact]
    public void Optional_phase0_columns_may_be_absent()
    {
        var result = Parse("""
            kaynak_url,alan_adi,bulten_no,karar_tarihi,kategori
            https://spk.gov.tr/b/1,ornek.com,2026/1,2026-01-08,diğer
            """);

        Assert.Empty(result.Rejections);
        Assert.Equal(OfficialActionCategory.Other, Assert.Single(result.Rows).Category);
    }

    [Fact]
    public void Quoted_fields_with_commas_are_supported()
    {
        var result = Parse($"""
            {Header}
            "ornek.com",2026/3,2026-02-05,"kripto",,,,"https://spk.gov.tr/bulten?no=2026/3&a=1,2"
            """);

        Assert.Empty(result.Rejections);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void Missing_required_header_is_an_error_for_the_whole_file()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse("""
            alan_adi,bulten_no,karar_tarihi,kategori
            ornek.com,2026/1,2026-01-08,kripto
            """));

        Assert.Contains("kaynak_url", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_file_is_an_error()
    {
        Assert.Throws<InvalidDataException>(() => Parse(string.Empty));
    }

    [Theory]
    [InlineData(",2026/1,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.MissingDomain)]
    [InlineData("ornek..com,2026/1,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.InvalidDomain)]
    [InlineData("192.0.2.10,2026/1,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.InvalidDomain)]
    [InlineData("https://t.me/ornekkanal,2026/1,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.PlatformDomain)]
    [InlineData("https://www.instagram.com/ornek,2026/1,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.PlatformDomain)]
    [InlineData("bit.ly/abc123,2026/1,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.PlatformDomain)]
    [InlineData("ornek.com,,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.MissingBulletinNumber)]
    [InlineData("ornek.com,46,2026-01-08,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.InvalidBulletinNumber)]
    [InlineData("ornek.com,2026/1,,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.MissingDecisionDate)]
    [InlineData("ornek.com,2026/1,08.01.2026,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.InvalidDecisionDate)]
    [InlineData("ornek.com,2026/1,2026-02-30,kripto,,,,https://spk.gov.tr/b/1", RowRejectionReason.InvalidDecisionDate)]
    [InlineData("ornek.com,2026/1,2026-01-08,,,,,https://spk.gov.tr/b/1", RowRejectionReason.MissingCategory)]
    [InlineData("ornek.com,2026/1,2026-01-08,forex,,,,https://spk.gov.tr/b/1", RowRejectionReason.UnknownCategory)]
    [InlineData("ornek.com,2026/1,2026-01-08,kripto,,,,", RowRejectionReason.MissingSourceUrl)]
    [InlineData("ornek.com,2026/1,2026-01-08,kripto,,,,spk.gov.tr/b/1", RowRejectionReason.InvalidSourceUrl)]
    [InlineData("ornek.com,2026/1,2026-01-08,kripto,,,,ftp://spk.gov.tr/b/1", RowRejectionReason.InvalidSourceUrl)]
    [InlineData("ornek.com,2026/1,2026-01-08,kripto,,,,https://haber-ornek.example/spk", RowRejectionReason.SourceHostNotAllowed)]
    [InlineData("ornek.com,2026/1,2026-01-08,kripto,,,,https://spk.gov.tr.ornek.example/b/1", RowRejectionReason.SourceHostNotAllowed)]
    [InlineData("ornek.com,2026/1,2026-01-08,kripto,,,,https://sahte-spk.gov.tr.example/b/1", RowRejectionReason.SourceHostNotAllowed)]
    public void Invalid_rows_are_rejected_with_row_number_and_reason(string line, RowRejectionReason expected)
    {
        var result = Parse($"{Header}\n{line}\n");

        Assert.Empty(result.Rows);
        var rejection = Assert.Single(result.Rejections);
        Assert.Equal(expected, rejection.Reason);
        Assert.Equal(2, rejection.RowNumber);
    }

    [Fact]
    public void Rejection_detail_does_not_contain_the_domain_name()
    {
        var result = Parse($"{Header}\nornek-gizli.com,46,2026-01-08,kripto,,,,https://spk.gov.tr/b/1\n");

        var rejection = Assert.Single(result.Rejections);
        Assert.DoesNotContain("ornek-gizli", rejection.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_and_invalid_rows_are_separated_and_blank_lines_ignored()
    {
        var result = Parse($"""
            {Header}
            ornek-a.com,2026/10,2026-03-05,kripto,,,,https://spk.gov.tr/b/10
            ornek-b.com,,2026-03-05,kripto,,,,https://spk.gov.tr/b/10
            ,,,,,,,
            ornek-c.com,2026/10,2026-03-05,diger,,,,https://spk.gov.tr/b/10
            """);

        Assert.Equal(["ornek-a.com", "ornek-c.com"], result.Rows.Select(r => r.Domain.Host));
        Assert.Equal([2, 5], result.Rows.Select(r => r.RowNumber));
        Assert.Equal(3, Assert.Single(result.Rejections).RowNumber);
    }

    [Fact]
    public void Configured_delimiter_is_used()
    {
        var options = new OfficialActionImportOptions { Delimiter = ";" };

        var result = Parse("""
            alan_adi;bulten_no;karar_tarihi;kategori;kaynak_url
            ornek.com;2026/1;2026-01-08;kripto;https://spk.gov.tr/b/1
            """, options);

        Assert.Single(result.Rows);
    }

    [Theory]
    [InlineData("kripto", OfficialActionCategory.Crypto)]
    [InlineData("KRİPTO", OfficialActionCategory.Crypto)]
    [InlineData("Kripto", OfficialActionCategory.Crypto)]
    [InlineData("kaldıraçlı", OfficialActionCategory.Leveraged)]
    [InlineData("KALDIRAÇLI", OfficialActionCategory.Leveraged)]
    [InlineData("kaldiracli", OfficialActionCategory.Leveraged)]
    [InlineData("diğer", OfficialActionCategory.Other)]
    [InlineData("DİĞER", OfficialActionCategory.Other)]
    [InlineData("diger", OfficialActionCategory.Other)]
    public void Category_spelling_variants_are_accepted(string value, OfficialActionCategory expected)
    {
        Assert.Equal(expected, OfficialActionCsvParser.ParseCategory(value));
    }
}
