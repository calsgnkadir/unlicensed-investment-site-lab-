using Fraud.Domain.Signals;
using Fraud.Evaluation.Model;

namespace Fraud.Signals;

/// <summary>
/// Basit referans (ROADMAP Hafta 7): Soldaki etikette baseline'ın 8 teriminden biri geçiyorsa 1, yoksa 0.
/// Daha karmaşık her sinyal bunu geçmek zorundadır.
/// </summary>
/// <remarks>
/// <para>Terimler <see cref="BaselineTerms.All"/>'dan gelir: ct_candidates akışını süzen filtre (KeywordCandidateFilter) de
/// aynı listeyi okur. Kopya liste olsaydı biri değişince ikisi ayrışır ve "K çerçevesi tanım gereği alarm" iddiası
/// sessizce yanlış olurdu.</para>
/// <para>K ÇERÇEVESİ NOTU: K çerçevesindeki kontroller (G3 ve G4b'nin K kısmı) bu terimlerle süzülmüş akıştan gelir. Bu sinyal
/// orada tanım gereği alarm verir; K'daki oran ölçüm değildir. Ana karşılaştırma N çerçevesi ve G1/G2/G4'tür.
/// Küçük fark: Filtre terimi BÜTÜN DNS adında arar (alt alan adı dahil), bu sinyal yalnızca kayıtlı adın soldaki
/// etiketinde. Bu yüzden K'da %100'ün biraz altı görülebilir (terim yalnızca alt alan adında geçiyorsa).</para>
/// <para>Hariç tutma dizeleri (FINANS_SOZLUGU.md) UYGULANMAZ: Baseline filtreyle aynı, en basit kural kalır.</para>
/// </remarks>
public sealed class BaselineSignal : ISignal
{
    public string Name => "baseline";

    public double Score(AsOfView view)
    {
        var label = SubjectName.FoldedLeftLabel(view);
        if (label is null)
        {
            return 0;
        }

        return BaselineTerms.All.Any(t => label.Contains(t, StringComparison.Ordinal)) ? 1 : 0;
    }
}
