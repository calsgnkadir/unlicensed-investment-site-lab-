namespace Fraud.Signals.Tests;

/// <summary>Marka benzerliği: elle hesaplanmış örnekler ve yabancı liste kuralları.</summary>
public sealed class BrandSimilarityTests
{
    // Uydurma yerli markalar: gerçek G1 listesi gizlidir ve testlere girmez. "abc" 5 karakterden kısa: belirteç olmaz.
    private static readonly BrandList Brands = BrandList.Create(["ornekbank", "deneme-katilim", "abc"]);

    [Theory]
    [InlineData("kitten", "sitting", 3)]  // k→s, e→i, +g
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("ornekbamk", "ornekbank", 1)]
    [InlineData("ornekbnak", "ornekbank", 2)] // yer değiştirme = iki değiştirme
    public void Levenshtein_hand_examples(string a, string b, int expected) =>
        Assert.Equal(expected, BrandSimilaritySignal.Levenshtein(a, b));

    [Theory]
    [InlineData("We11s-Fargo", "weiisfargo")]   // büyük harf, 1→i, l→i, tire silinir
    [InlineData("rnizuho", "mizuho")]           // rn→m
    [InlineData("0rnekbank", "omekbank")]       // önce rn→m, sonra 0→o
    [InlineData("оrnekbank", "omekbank")]       // Kiril "о"
    public void Normalize_maps_lookalikes(string label, string expected) =>
        Assert.Equal(expected, BrandList.Normalize(label));

    [Theory]
    [InlineData("ornekbank.example", 0)]          // markanın kendisi (G1'in kendi adı ya da başka uzantı)
    [InlineData("denemekatilim.example", 1)]      // tiresiz yazım markanın kendisi değil: belirteç alt dize
    [InlineData("deneme-katilim.test", 0)]        // markanın birebir yazımı
    [InlineData("ornekbank-yatirim.example", 1)]  // belirteci içeriyor
    [InlineData("0rnekbank.example", 1)]          // görsel benzer: normalleşince belirteci içeriyor, ama kendisi değil
    // Belirteç normalleşince "omekbank" (rn→m, 8 karakter: en çok 2). Etiketler de aynı dönüşümden geçer.
    [InlineData("ornekbamk.example", 1)]          // "omekbamk": uzaklık 1
    [InlineData("ornekbnak.example", 1)]          // "omekbnak": uzaklık 2
    [InlineData("ornxkbnak.example", 0)]          // "omxkbnak": uzaklık 3
    [InlineData("abcx.example", 0)]               // "abc" belirteç değil
    [InlineData("kitapci.example", 0)]            // ilgisiz
    [InlineData("wellsfargo-giris.example", 1)]   // gömülü yabancı listeden
    [InlineData("wellsfargo.example", 0)]         // yabancı markanın kendisi
    public void Brand_hand_examples(string name, double expected) =>
        Assert.Equal(expected, new BrandSimilaritySignal(Brands).Score(Views.Named(name)));

    [Fact]
    public void Short_tokens_use_distance_one()
    {
        var brands = BrandList.Create(["kalem"]); // normalleşince "kaiem", 5 karakter: en çok 1
        var signal = new BrandSimilaritySignal(brands);
        Assert.Equal(1, signal.ScoreLabel("kalam"));  // "kaiam": uzaklık 1
        Assert.Equal(0, signal.ScoreLabel("kolam"));  // "koiam": uzaklık 2
    }

    [Fact]
    public void Brand_score_is_stable_across_calls()
    {
        var signal = new BrandSimilaritySignal(Brands);
        var view = Views.Named("ornekbank-yatirim.example");
        Assert.Equal(signal.Score(view), signal.Score(view));
    }

    [Fact]
    public void Foreign_list_follows_its_rules()
    {
        var text = BrandList.ForeignText();
        Assert.Contains("Kaynak:", text, StringComparison.Ordinal);
        Assert.Contains("FSB 2025 G-SIB listesi (27 Kasım 2025), erişim 2026-11-21", text, StringComparison.Ordinal);
        var lines = BrandList.ReadLines(text).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, l =>
        {
            Assert.True(l.Length >= BrandList.MinTokenLength, l);
            Assert.True(l.All(char.IsAsciiLetterLower), l);
        });
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(lines.Count, BrandList.Create([]).ForeignCount);
    }

    [Fact]
    public void Foreign_list_matches_the_fsb_2025_corrections()
    {
        var lines = BrandList.ReadLines(BrandList.ForeignText()).ToList();
        Assert.DoesNotContain("citibank", lines); // alt marka, resmi listede yok
        Assert.Contains("bankofcommunications", lines);
        Assert.Contains("mitsubishiufj", lines);
        Assert.Contains("industrialandcommercialbankofchina", lines);
        Assert.Equal(26, lines.Count);
    }

    /// <summary>Alt dize olan markalar: en uzun eşleşme seçilir; her biri kendi adıyla 0 alır.</summary>
    [Theory]
    [InlineData("bankofchina-giris", "bankofchina")]
    [InlineData("agriculturalbankofchina-giris", "agriculturalbankofchina")]
    [InlineData("industrialandcommercialbankofchina-giris", "industrialandcommercialbankofchina")]
    [InlineData("yeni-agriculturalbankofchina", "agriculturalbankofchina")]
    public void Longest_brand_wins(string label, string expectedBrand)
    {
        var signal = new BrandSimilaritySignal(BrandList.Create([]));
        Assert.Equal(BrandList.Normalize(expectedBrand), signal.Match(label));
        Assert.Equal(1, signal.ScoreLabel(label));
    }

    [Fact]
    public void Every_foreign_brand_is_its_own_exception_and_its_imitation_maps_to_itself()
    {
        var signal = new BrandSimilaritySignal(BrandList.Create([]));
        foreach (var brand in BrandList.ReadLines(BrandList.ForeignText()))
        {
            Assert.Equal(0, signal.Score(Views.Named(brand + ".example")));     // markanın kendisi (başka uzantı)
            Assert.Null(signal.Match(brand));
            Assert.Equal(BrandList.Normalize(brand), signal.Match(brand + "-giris")); // taklit, en uzun = kendisi
        }
    }

    [Fact]
    public void Domestic_counts_drop_short_tokens()
    {
        Assert.Equal(2, Brands.DomesticCount);
        Assert.DoesNotContain("abc", Brands.Tokens);
        Assert.Contains("abc", Brands.Plain); // ama "abc" etiketi yine markanın kendisi sayılır
    }
}
