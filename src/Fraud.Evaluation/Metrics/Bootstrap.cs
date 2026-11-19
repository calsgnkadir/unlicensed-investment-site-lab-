namespace Fraud.Evaluation.Metrics;

/// <summary>Bootstrap'a giren bir satır: bir birimin hangi örnekleme biriminde (küme ya da bülten) olduğu ve sonucu.</summary>
/// <param name="Unit">Örnekleme birimi: küme kimliği ya da bülten numarası.</param>
/// <param name="Hit">Pozitifte "yakalandı", kontrolde "alarm verdi".</param>
public sealed record BootstrapRow(string Unit, bool Hit);

/// <summary>%95 güven aralığı.</summary>
public sealed record Interval(double Low, double High);

/// <summary>
/// Birim düzeyinde (küme ya da bülten), tohumlu yüzdelik bootstrap.
/// </summary>
/// <remarks>
/// <para>Neden birim düzeyinde: Aynı kümedeki ya da bültendeki alan adları birbirinden bağımsız değildir (aynı
/// operatör, aynı toplu karar). Tek tek alan adını yeniden örneklemek aralığı yapay olarak daraltırdı.
/// Bu yüzden önce birimler seçilir, seçilen birimin bütün alan adları birlikte gelir (KONTROL_GRUBU_YONTEMI.md 7.5).</para>
/// <para>Adımlar: (1) Satırları birime göre topla: birim başına isabet ve toplam. (2) B kez: birim sayısı kadar birimi
/// yerine koyarak çek, oranı Σisabet / Σtoplam olarak hesapla. (3) B oranı sırala, %2,5 ve %97,5 sıradaki değerleri al.</para>
/// <para>Determinizm: Birimler sıralı, üreteç tohumlu (<see cref="SplitMix64"/>). Aynı girdi ve tohum aynı aralığı verir.</para>
/// </remarks>
public static class Bootstrap
{
    public static Interval? PercentileInterval(IReadOnlyList<BootstrapRow> rows, int iterations, string seed)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        if (rows.Count == 0)
        {
            return null;
        }

        var units = rows
            .GroupBy(r => r.Unit, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Hits: g.Count(r => r.Hit), Total: g.Count()))
            .ToArray();

        var random = new SplitMix64(SeededHash.Seed64(seed));
        var rates = new double[iterations];
        for (var b = 0; b < iterations; b++)
        {
            int hits = 0, total = 0;
            for (var i = 0; i < units.Length; i++)
            {
                var unit = units[random.NextIndex(units.Length)];
                hits += unit.Hits;
                total += unit.Total;
            }

            rates[b] = (double)hits / total;
        }

        Array.Sort(rates);
        return new Interval(rates[Rank(0.025, iterations)], rates[Rank(0.975, iterations)]);
    }

    /// <summary>En yakın sıra: q·(B−1) yuvarlanır. B = 2000 için %2,5 → 50., %97,5 → 1949. sıra (0 tabanlı).</summary>
    private static int Rank(double q, int count) => (int)Math.Round(q * (count - 1), MidpointRounding.AwayFromZero);
}
