namespace Fraud.Domain.Signals;

/// <summary>
/// Baseline'ın 8 terimi: TEK KAYNAK. CT akışının saklama filtresi (KeywordCandidateFilter), baseline sinyali, geniş
/// sözlüğün "baseline alt kümesi" ayrımı ve G3a denemesi bu listeyi kullanır.
/// </summary>
/// <remarks>
/// Bu liste donmuştur (FINANS_SOZLUGU.md Bölüm 5): Biri değişirse kilitli test geçersiz sayılır. Aşama 0 hacim ölçümü
/// (docs/phase0/STATIC_CT_HACIM.md) yalnızca bu liste için geçerlidir.
/// </remarks>
public static class BaselineTerms
{
    public static IReadOnlyList<string> All { get; } = ["yatirim", "borsa", "kripto", "forex", "trade", "invest", "capital", "menkul"];
}
