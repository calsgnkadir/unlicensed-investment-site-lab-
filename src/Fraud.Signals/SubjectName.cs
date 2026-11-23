using Fraud.Domain.Signals;
using Fraud.Evaluation.Model;

namespace Fraud.Signals;

/// <summary>
/// Görünümdeki ad gözleminden (<see cref="ObservationKinds.Name"/>) soldaki etiketi okur. Ad sinyallerinin ortak yardımcısı.
/// </summary>
/// <remarks>
/// <para>Neden ad bir gözlem: <see cref="AsOfView"/> sinyale yalnızca opak kimlik, an ve gözlem verir (güvenlik testi bu üç
/// özelliği zorunlu kılar). Adı ayrı bir özellik olarak eklemek bu sözleşmeyi bozardı. Ad zaman taşımadığı için
/// <c>KnownAt = DateTimeOffset.MinValue</c> ile her görünümde vardır; gelecek bilgisi taşımaz.</para>
/// <para>Ad yalnızca bellekte okunur, hiçbir sinyal onu puan dışında bir yere yazmaz (Kural 4).</para>
/// </remarks>
public static class SubjectName
{
    /// <summary>Kayıtlı alan adı (ör. <c>ornek.example</c>). Ad gözlemi yoksa <c>null</c>: sinyal 0 verir.</summary>
    public static string? Registrable(AsOfView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Observations.FirstOrDefault(o => o.Kind == ObservationKinds.Name)?.Value;
    }

    /// <summary>
    /// Soldaki etiket, Türkçe harfleri katlanmış (<see cref="FinanceLexicon.Fold"/>). <c>ornek.com.tr</c> → <c>ornek</c>.
    /// TLD ve kamu son eki sayılmaz: Yükleyici adı zaten kayıtlı alan adına indirmiştir.
    /// </summary>
    public static string? FoldedLeftLabel(AsOfView view) =>
        Registrable(view) is { } name ? FinanceLexicon.Fold(name.Split('.')[0]) : null;
}
