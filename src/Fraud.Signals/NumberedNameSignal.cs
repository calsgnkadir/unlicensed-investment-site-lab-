using Fraud.Evaluation.Model;

namespace Fraud.Signals;

/// <summary>
/// Numaralı ad (isteğe bağlı sinyal): Soldaki etiket bir rakamla bitiyorsa ya da içinde art arda en az iki rakam varsa 1.
/// </summary>
/// <remarks>
/// Neden: Engellenen siteler kapatılınca aynı adın numaralı sürümüyle geri döner (ornek2, ornek-24). Tek rakam ortada
/// (ör. "web3x") yaygın ve masum olduğu için sayılmaz. Elle örnekler: <c>ornek2</c> → 1, <c>ornek-24</c> → 1,
/// <c>a24b</c> → 1, <c>3dornek</c> → 0, <c>ornek</c> → 0.
/// </remarks>
public sealed class NumberedNameSignal : ISignal
{
    public string Name => "numbered";

    public double Score(AsOfView view)
    {
        var label = SubjectName.FoldedLeftLabel(view);
        if (string.IsNullOrEmpty(label))
        {
            return 0;
        }

        if (char.IsAsciiDigit(label[^1]))
        {
            return 1;
        }

        for (var i = 1; i < label.Length; i++)
        {
            if (char.IsAsciiDigit(label[i]) && char.IsAsciiDigit(label[i - 1]))
            {
                return 1;
            }
        }

        return 0;
    }
}
