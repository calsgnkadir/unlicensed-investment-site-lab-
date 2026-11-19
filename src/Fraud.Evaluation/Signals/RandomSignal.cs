using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Signals;

/// <summary>
/// Sahte model (ROADMAP Hafta 6 "Bitti sayılır"): Birim kimliğinin tohumlu özetinden [0, 1) puan. Hiçbir veriye bakmaz.
/// </summary>
/// <remarks>
/// Hat doğruysa bu sinyal şanstan iyi olamaz: Yanlış alarm oranı α olacak şekilde seçilen eşikte recall da yaklaşık α
/// çıkmalıdır. Çıkmazsa bölmede, eşikte ya da metrikte bir hata (ör. etiket sızıntısı) vardır.
/// </remarks>
public sealed class RandomSignal(string seed) : ISignal
{
    public string Name => "random";

    public double Score(AsOfView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return SeededHash.Unit(seed, view.SubjectId);
    }
}

/// <summary>
/// Yalnızca "ilk sertifikadan bu yana geçen gün"e bakan sahte sinyal. Kontrolün referans zamanı doğru seçilmezse bu
/// sinyal etiketi tanım gereği bulur; testler bunu iki düzende de gösterir.
/// </summary>
public sealed class CertificateAgeSignal : ISignal
{
    public string Name => "certificate-age";

    public double Score(AsOfView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var first = view.Observations.FirstOrDefault(o => o.Kind == ObservationKinds.Certificate);
        return first is null ? 0 : (view.AsOf - first.KnownAt).TotalDays;
    }
}
