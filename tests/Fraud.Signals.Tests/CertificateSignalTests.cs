using Fraud.Domain.Signals;
using Fraud.Evaluation.Running;

namespace Fraud.Signals.Tests;

/// <summary>Sertifika sinyalleri: elle hesaplanmış örnekler ve gelecek sızıntısı.</summary>
public sealed class CertificateSignalTests
{
    private static readonly FinanceLexicon Lexicon = FinanceLexicon.Parse("# test sözlüğü\nyatirim\nfinans\n");

    private static NewCertificateLexiconSignal NewCert() => new(Lexicon, maxAgeDays: 28);

    private static CertificateBurstSignal Burst() => new(windowDays: 30, minCertificates: 5);

    [Theory]
    // Sertifika 1 Nisan öğlen. Görünüm günün sonuna yuvarlanır: 28 Nisan sonu − 1 Nisan öğlen = 27,5 gün ≤ 28 → 1.
    [InlineData(28, 1)]
    // 29 Nisan sonu − 1 Nisan öğlen = 28,5 gün > 28 → 0.
    [InlineData(29, 0)]
    [InlineData(1, 1)]
    public void New_cert_lexicon_age_boundary(int aprilDay, double expected) =>
        Assert.Equal(expected, NewCert().Score(Views.At(Views.EndOf(4, aprilDay),
            Views.Name("ornekfinans.example"), Views.Cert(Views.Noon(4, 1), "c-1"))));

    [Fact]
    public void New_cert_lexicon_needs_a_lexicon_match()
    {
        var view = Views.At(Views.EndOf(4, 2), Views.Name("ornek.example"), Views.Cert(Views.Noon(4, 1), "c-1"));
        Assert.Equal(0, NewCert().Score(view));
    }

    [Fact]
    public void New_cert_lexicon_uses_the_first_certificate_not_the_latest()
    {
        // İlk sertifika 100 gün önce: ad yeni değil. Dün alınan sertifika onu "yeni" yapmaz.
        var view = Views.At(Views.EndOf(5, 10),
            Views.Name("ornekyatirim.example"), Views.Cert(Views.Noon(5, 10).AddDays(-100), "c-1"), Views.Cert(Views.Noon(5, 9), "c-2"));
        Assert.Equal(0, NewCert().Score(view));
    }

    [Fact]
    public void New_cert_lexicon_without_certificate_scores_zero() =>
        Assert.Equal(0, NewCert().Score(Views.At(Views.EndOf(4, 2), Views.Name("ornekyatirim.example"))));

    [Fact]
    public void Burst_counts_distinct_certificates_in_the_window()
    {
        // 5 farklı sertifika, hepsi son 30 günde → 1.
        var five = Enumerable.Range(1, 5).Select(i => Views.Cert(Views.Noon(5, i), "c-" + i)).ToArray();
        Assert.Equal(1, Burst().Score(Views.At(Views.EndOf(5, 10), five)));

        // Aynı kimlik iki kez: 4 farklı sertifika → 0.
        var duplicate = five.Take(4).Append(Views.Cert(Views.Noon(5, 6), "c-1")).ToArray();
        Assert.Equal(0, Burst().Score(Views.At(Views.EndOf(5, 10), duplicate)));
    }

    [Fact]
    public void Burst_ignores_certificates_older_than_the_window()
    {
        // 4 sertifika 5 Mayıs'ta, 1'i 1 Nisan'da. 10 Mayıs sonu − 30 gün = 10 Nisan sonu: 1 Nisan pencere dışında → 4 → 0.
        var observations = Enumerable.Range(1, 4).Select(i => Views.Cert(Views.Noon(5, 5), "c-" + i))
            .Append(Views.Cert(Views.Noon(4, 1), "c-old")).ToArray();
        Assert.Equal(0, Burst().Score(Views.At(Views.EndOf(5, 10), observations)));
        // Aynı veri 20 Nisan'da: 5 Mayıs'takiler henüz bilinmiyor, yalnızca 1 Nisan'daki görünür → 1 sertifika → 0.
        Assert.Equal(0, Burst().Score(Views.At(Views.EndOf(4, 20), observations)));
    }

    /// <summary>Gelecek sızıntısı: Görünüm anından sonra bilinen sertifika sayılmaz.</summary>
    [Fact]
    public void Burst_does_not_see_certificates_after_as_of()
    {
        var observations = Enumerable.Range(1, 4).Select(i => Views.Cert(Views.Noon(5, i), "c-" + i))
            .Append(Views.Cert(Views.Noon(5, 11), "c-5")).ToArray();
        Assert.Equal(0, Burst().Score(Views.At(Views.EndOf(5, 10), observations)));
        Assert.Equal(1, Burst().Score(Views.At(Views.EndOf(5, 11), observations)));
    }

    /// <summary>
    /// Gelecek sızıntısı, Predictor üzerinden: Tek sertifika karar gününden SONRA. Sinyal hiçbir değerlendirme gününde onu
    /// görmez; en yüksek puan 0 kalır.
    /// </summary>
    [Fact]
    public void New_cert_lexicon_does_not_see_certificate_after_reference()
    {
        var observations = new[] { Views.Name("ornekyatirim.example"), Views.Cert(Views.Reference.AddDays(1), "c-1") };
        var prediction = Predictor.Predict(NewCert(), Views.Subject(), observations, threshold: 0.5);
        Assert.Equal(0, prediction.Score);
        Assert.False(prediction.Alarmed);

        // Kontrol: Aynı sertifika kararından 10 gün önceyse görülür ve alarm verir.
        var before = new[] { Views.Name("ornekyatirim.example"), Views.Cert(Views.Reference.AddDays(-10), "c-1") };
        Assert.True(Predictor.Predict(NewCert(), Views.Subject(), before, threshold: 0.5).Alarmed);
    }
}
