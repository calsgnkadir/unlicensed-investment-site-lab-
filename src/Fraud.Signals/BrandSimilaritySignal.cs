using System.Collections.Concurrent;
using Fraud.Domain.Signals;
using Fraud.Evaluation.Model;

namespace Fraud.Signals;

/// <summary>
/// Marka benzerliği: Soldaki etiket bir marka belirtecini İÇERİYORSA ya da ona çok YAKINSA 1. Etiket bir belirtecin
/// kendisiyse 0.
/// </summary>
/// <remarks>
/// <para>Kural, sırayla:</para>
/// <list type="number">
/// <item>Etiketin harf katlanmış yazımı bir markanın harf katlanmış yazımına BİREBİR eşitse 0 (<see cref="BrandList.Plain"/>;
/// tire ve görsel benzer karakter burada dönüştürülmez, yoksa <c>0rnekbank</c> markanın kendisi sayılırdı).
/// Neden: Bu ya kurumun kendisidir (G1'in kendi alan adları, KONTROL_GRUBU_YONTEMI.md 2.4 "kendi kendine eşleşme") ya da aynı markanın başka uzantısıdır (ör. resmi ülke uzantısı).
/// Sonuç: G1'in kendi adlarında bu sinyalin yanlış alarmı tanım gereği 0'dır; G1 oranı bu sinyal için ölçüm değildir.
/// Sinyalin sınavı pozitifler, G3 ve G4'tür. Bedeli: Markanın birebir adını başka uzantıda kullanan taklit kaçar.</item>
/// <item>Bundan sonra etiket ve belirteç ikisi de <see cref="BrandList.Normalize"/>'dan geçmiş olarak karşılaştırılır.
/// Etiket bir belirteci alt dize olarak içeriyorsa 1 (ör. <c>ornekbank-yatirim</c> ⊃ <c>ornekbank</c>).</item>
/// <item>Düzenleme uzaklığı (Levenshtein) belirteç 5–7 karakterse en çok 1, 8 ve üstüyse en çok 2 ise 1
/// (ör. <c>ornekbamk</c> ↔ <c>ornekbank</c> = 1). Kısa belirteçte 2 harf fark çok sayıda ilgisiz ada uyardı.</item>
/// </list>
/// <para>Ad zamanla değişmez; bu yüzden aynı etiketin sonucu önbellekte tutulur (Predictor her gün aynı adı sorar).</para>
/// </remarks>
public sealed class BrandSimilaritySignal(BrandList brands) : ISignal
{
    private readonly ConcurrentDictionary<string, double> _cache = new(StringComparer.Ordinal);

    // Uzundan kısaya (eşitlikte alfabetik): "ilk bulunan" = "en uzun". Bir kez sıralanır.
    private readonly List<string> _byLength = [.. brands.Tokens.OrderByDescending(t => t.Length).ThenBy(t => t, StringComparer.Ordinal)];

    public string Name => "brand";

    public double Score(AsOfView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var name = SubjectName.Registrable(view);
        return name is null ? 0 : _cache.GetOrAdd(name.Split('.')[0], ScoreLabel);
    }

    /// <summary>Soldaki etiketin puanı (ör. <c>ornekbank-yatirim</c>): eşleşen bir marka varsa 1.</summary>
    public double ScoreLabel(string label) => Match(label) is null ? 0 : 1;

    /// <summary>
    /// Etiketin eşleştiği marka belirteci (normalleştirilmiş). Yoksa ya da etiket bir markanın kendisiyse <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>EN UZUN eşleşme seçilir. Neden: Bazı markalar başka markaların alt dizesidir (<c>bankofchina</c> ⊂
    /// <c>agriculturalbankofchina</c>). Kısa olanı seçmek, uzun markanın taklidini yanlış markaya bağlardı. Puan aynı kalır
    /// (ikisi de 1), ama hangi markanın taklit edildiği sorusunun cevabı doğru olur.</para>
    /// <para>Önce alt dize eşleşmeleri (en uzun), yoksa düzenleme uzaklığı eşleşmeleri (en uzun) aranır: İçerme, yakınlıktan
    /// daha güçlü bir kanıttır.</para>
    /// </remarks>
    public string? Match(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var plain = FinanceLexicon.Fold(label);
        var normalized = BrandList.Normalize(label);
        if (plain is null || normalized is null || brands.Plain.Contains(plain))
        {
            return null; // çevrilemeyen IDN ya da markanın kendisi (her marka için, kısa ya da uzun)
        }

        var contained = _byLength.FirstOrDefault(t => normalized.Contains(t, StringComparison.Ordinal));
        if (contained is not null)
        {
            return contained;
        }

        // 8 ve üstü karakterli belirteçte en çok 2, daha kısasında en çok 1 harf fark.
        return _byLength.FirstOrDefault(t => Levenshtein(normalized, t) <= (t.Length >= 8 ? 2 : 1));
    }

    /// <summary>
    /// İki dize arasındaki en az ekleme/silme/değiştirme sayısı. Klasik dinamik programlama, iki satırlık tablo.
    /// Elle örnek: "kitten" → "sitting" = 3.
    /// </summary>
    public static int Levenshtein(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j; // boş a'dan b'nin ilk j harfine: j ekleme
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i; // a'nın ilk i harfinden boşa: i silme
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                var deletion = previous[j] + 1;
                var insertion = current[j - 1] + 1;
                current[j] = Math.Min(substitution, Math.Min(deletion, insertion));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
