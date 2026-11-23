using System.Globalization;
using Fraud.Domain.Signals;

namespace Fraud.Signals.Tests;

/// <summary>Baseline ve numaralı ad: elle hesaplanmış örnekler.</summary>
public sealed class NameSignalTests
{
    [Theory]
    [InlineData("ornekyatirim.example", 1)]   // "yatirim" soldaki etikette
    [InlineData("borsa-ornek.test", 1)]       // tire önemsiz, alt dize
    [InlineData("ornekinvest.example", 1)]
    [InlineData("ornek.example", 0)]
    [InlineData("ornek.borsa.example", 0)]    // yalnızca soldaki etiket: "borsa" ikinci etikette, sayılmaz
    [InlineData("YATIRIMORNEK.example", 1)]   // büyük harf ve noktasız I katlanır
    public void Baseline_hand_examples(string name, double expected) =>
        Assert.Equal(expected, new BaselineSignal().Score(Views.Named(name)));

    [Fact]
    public void Baseline_folds_turkish_idn_label()
    {
        // "yatırımornek" punycode'a çevrilir; sinyal onu geri çevirip katlar ve "yatirim" bulur.
        var ascii = new IdnMapping().GetAscii("yatırımornek");
        Assert.StartsWith("xn--", ascii, StringComparison.Ordinal);
        Assert.Equal(1, new BaselineSignal().Score(Views.Named(ascii + ".example")));
    }

    [Fact]
    public void Baseline_without_name_observation_scores_zero() =>
        Assert.Equal(0, new BaselineSignal().Score(Views.At(Views.EndOf(5, 1))));

    /// <summary>
    /// K çerçevesi tanım gereği: ct_candidates akışı bu 8 terimle süzülür. Soldaki etiketinde terimlerden biri geçen her ad
    /// baseline'da 1 alır. Terimler aynı kaynaktan (BaselineTerms.All) okunur; bu test o kaynağın her terimini dener.
    /// </summary>
    [Fact]
    public void Baseline_alarms_on_every_term_of_the_frame_K_filter()
    {
        Assert.Equal(8, BaselineTerms.All.Count);
        foreach (var term in BaselineTerms.All)
        {
            Assert.Equal(1, new BaselineSignal().Score(Views.Named("on" + term + "son.example")));
        }
    }

    [Theory]
    [InlineData("ornek2.example", 1)]     // rakamla biter
    [InlineData("ornek-24.example", 1)]   // rakamla biter
    [InlineData("a24b.example", 1)]       // art arda iki rakam
    [InlineData("3dornek.example", 0)]    // tek rakam başta
    [InlineData("web3x.example", 0)]      // tek rakam ortada
    [InlineData("ornek.example", 0)]
    [InlineData("ornek.v2.example", 0)]   // yalnızca soldaki etiket
    public void Numbered_hand_examples(string name, double expected) =>
        Assert.Equal(expected, new NumberedNameSignal().Score(Views.Named(name)));
}
