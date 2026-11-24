using System.Globalization;
using System.Text;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Running;

/// <summary>
/// Birleşik sinyal: parçaların AĞIRLIKSIZ TOPLAMI. Parçalar 0/1 puan verir; birleşik puan "kaç parça alarm veriyor"dur.
/// </summary>
/// <remarks>
/// <para>Neden bu kadar basit (ROADMAP Hafta 7): Etiketli veri az. Ağırlık öğrenmek ayar verisine aşırı uyar ve ablasyonu
/// okunmaz yapar. Baseline geçilmeden model katmanı (ML.NET) yazılmaz.</para>
/// <para>Neden VEYA değil toplam: Eşik ayar kontrollerinde %1 yanlış alarma göre seçilir. VEYA'da (en büyük parça) tek bir
/// gürültülü parça kontrollerin %1'inden fazlasında 1 verirse eşik 1'in üstüne çıkar ve birleşik sinyal HİÇ alarm vermez.
/// Toplamda eşik 1, 2, 3... arasından seçilebilir ("en az k parça"). Eşik 1 seçilirse sonuç VEYA ile aynıdır; yani toplam
/// VEYA'yı da kapsar. Eşik yine yalnızca ayar verisinde seçilir.</para>
/// </remarks>
public sealed class SumSignal(IReadOnlyList<ISignal> parts) : ISignal
{
    public string Name => "sum(" + string.Join("+", parts.Select(p => p.Name)) + ")";

    public double Score(AsOfView view) => parts.Sum(p => p.Score(view));
}

/// <summary>Bir ablasyon satırı: hangi sinyal kümesi ve sonuç.</summary>
/// <param name="Report">Satırın KENDİ eşiğiyle (G4 ayar kontrollerinden seçilmiş) sonuç.</param>
/// <param name="AtFullThreshold">Aynı satır, TOPLAM(hepsi)'nin eşiği sabit tutularak. TOPLAM(hepsi) satırında <c>null</c>.</param>
/// <param name="AtFullThresholdSkipped">Sabit eşikli sürüm uygulanmadıysa kısa gerekçe (satırın en yüksek puanı TOPLAM
/// eşiğine ulaşamıyor); o zaman <paramref name="AtFullThreshold"/> <c>null</c>.</param>
public sealed record AblationRow(
    string Variant, EvaluationReport Report, EvaluationReport? AtFullThreshold = null, string? AtFullThresholdSkipped = null);

/// <summary>
/// Ablasyon: Her parça tek başına, hepsinin toplamı ve her parça ÇIKARILMIŞ toplam ayrı ayrı aynı bölme, aynı eşik kuralı ve
/// aynı "şimdi" ile çalışır. "Bu sinyal çıkarılınca sonuç ne kadar kötüleşiyor" sorusunun cevabı, TOPLAM(hepsi) ile
/// "TOPLAM − sinyal" satırları arasındaki farktır.
/// </summary>
/// <remarks>
/// Deterministiktir: Sinyaller deterministik, bölme tohumlu, eşik ayar kontrollerinden kurala göre seçilir. Her satır yalnızca
/// geçmiş veri testinde çalışır (keşif aracı; kilitli ya da ileriye dönük testte tek, dondurulmuş sinyal çalışır).
/// </remarks>
public static class Ablation
{
    public const string FullName = "TOPLAM(hepsi)";

    public static IReadOnlyList<AblationRow> Run(
        EvaluationConfig config, IReadOnlyList<Subject> subjects, IReadOnlyList<Observation> observations,
        IReadOnlyList<ISignal> parts, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(parts);
        var at = now ?? DateTimeOffset.UtcNow;
        // PartCount: satırdaki sinyal kaç parçadan oluşuyor. Yalnızca yapıdan gelir, sonuca bakmaz. Tek sinyal satırı 1 parça,
        // "TOPLAM − x" satırı (toplam − 1) parça. Eskiden burada elle verilen bir "tek parça" bayrağı vardı ve "TOPLAM − x"
        // satırlarında hep false idi; iki parçalı koşuda "TOPLAM − x" de tek parça kaldığı halde anlamsız fark basılıyordu.
        // Parça sayısı aynı zamanda satırın EN YÜKSEK puanıdır, çünkü her parça 0 ya da 1 verir (aşağıdaki SkipReason).
        var variants = new List<(string Name, ISignal Signal, int PartCount)>();
        variants.AddRange(parts.Select(p => (p.Name, p, 1)));
        variants.Add((FullName, new SumSignal(parts), parts.Count));
        if (parts.Count > 1)
        {
            variants.AddRange(parts.Select(p => (
                "TOPLAM − " + p.Name, (ISignal)new SumSignal([.. parts.Where(q => !ReferenceEquals(q, p))]), parts.Count - 1)));
        }

        // Önce TOPLAM(hepsi): eşiği, öteki satırların "sabit eşikli" sürümünde kullanılır.
        var fullSignal = variants.Single(v => v.Name == FullName).Signal;
        var full = EvaluationRunner.Run(config, EvaluationMode.Historical, subjects, observations, fullSignal, now: at);

        // Bir satırın sabit eşikli sürümü, en yüksek puanı TOPLAM eşiğine ulaşamıyorsa çalıştırılmaz (FixedThresholdSkipReason).
        string? SkipReason(int partCount) => FixedThresholdSkipReason(partCount, full.Threshold);

        return [.. variants.Select(v => v.Name == FullName
            ? new AblationRow(v.Name, full)
            : new AblationRow(v.Name,
                EvaluationRunner.Run(config, EvaluationMode.Historical, subjects, observations, v.Signal, now: at),
                SkipReason(v.PartCount) is null
                    ? EvaluationRunner.Run(config, EvaluationMode.Historical, subjects, observations, v.Signal, now: at, fixedThreshold: full.Threshold)
                    : null,
                SkipReason(v.PartCount)))];
    }

    /// <summary>
    /// Sabit eşikli sürüm neden uygulanmaz; uygulanırsa <c>null</c>. Yalnızca parça sayısına ve eşiğe bakar, sonuca bakmaz
    /// (DECISIONS 2026-11-24).
    /// </summary>
    /// <remarks>
    /// Her parça 0 ya da 1 puan verir, k parçalı satır en çok k puan alır. Alarm kuralı "puan ≥ eşik" (<see cref="Predictor"/>).
    /// Eşik ya ayardaki bir puanın kendisidir (ör. tam 3) ya da bir puanın hemen üstündeki double'dır
    /// (<c>Math.BitIncrement(2)</c>, yani "en az 3 parça"; <see cref="ThresholdSelector"/>). Demek ki satır eşiğe ancak
    /// k ≥ eşik ise ulaşabilir. k &lt; eşik ise recall her zaman 0 çıkar ve fark yalnızca "−TOPLAM recall" olur; bu sayı sinyal
    /// hakkında bilgi taşımaz, o yüzden basılmaz, yerine gerekçe yazılır. Eşik tam k ise (3 parça, eşik 3) satır eşiğe
    /// ulaşabilir ve normal çalışır: Karşılaştırma bu yüzden "&lt;", "≤" değil.
    /// </remarks>
    public static string? FixedThresholdSkipReason(int partCount, double fullThreshold) => partCount < fullThreshold
        ? string.Create(CultureInfo.InvariantCulture, $"uygulanmaz: {partCount} parçalı satır en çok {partCount} puan alır, TOPLAM eşiğine ulaşamaz")
        : null;

    /// <summary>
    /// Tablo metni: sinyal × grup × çerçeve. Yalnızca toplu sayılar; küçük hücre kuralı (<see cref="EvaluationReport.Format"/>).
    /// </summary>
    public static string ToText(IReadOnlyList<AblationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        sb.AppendLine("Ablasyon (geçmiş veri testi; her satırda eşik ayar kontrollerinde ayrı seçildi):");
        if (rows.Any(r => r.Variant.Contains("baseline", StringComparison.Ordinal)))
        {
            sb.AppendLine("  NOT: Çerçeve K (FrameTermConditioned) ct_candidates'tan gelir; o akış baseline'ın 8 terimiyle süzüldü.");
            sb.AppendLine("       Baseline K çerçevesinde TANIM GEREĞİ alarm verir; oradaki oran ölçüm değildir. Ana karşılaştırma");
            sb.AppendLine("       Çerçeve N (FrameNeutral) ve G1 / G2 / G4 tabakalarıdır.");
        }

        if (rows.Any(r => r.Variant.Contains("brand", StringComparison.Ordinal)))
        {
            sb.AppendLine("  NOT: Marka listesi G1'den kurulur; G1'in kendi adları marka sinyalinde tanım gereği 0 alır (yöntem 2.4).");
            sb.AppendLine("       G1 satırındaki oran marka sinyali için ölçüm değildir; sınav pozitifler, G3 ve G4'tür.");
        }

        if (rows.Any(r => r.Variant.Contains("new-cert-lexicon", StringComparison.Ordinal)))
        {
            sb.AppendLine("  NOT: G3 yeni sertifikalı ve sözlükle eşleşen adlardan kurulur; new-cert-lexicon G3'te tanım gereği alarm verir.");
        }

        sb.AppendLine("  Tek sinyalde eşik 1'den büyükse sinyal hiç alarm vermez: G4 ayar kontrollerinin hedef oranından fazlasında 1 aldı.");
        sb.AppendLine("  İki fark yazılır. 'kendi eşiğiyle': satır eşiğini kendisi seçer, bu yüzden fark sinyalin katkısıyla EŞİĞİN DEĞİŞMESİNİ");
        sb.AppendLine("  birlikte taşır. 'TOPLAM eşiğiyle': TOPLAM(hepsi)'nin eşiği sabit tutulur, fark yalnızca sinyalin çıkarılmasından gelir.");

        var full = rows.FirstOrDefault(r => r.Variant == FullName);
        foreach (var row in rows)
        {
            var r = row.Report;
            sb.Append("- ").Append(row.Variant).Append(": eşik=")
              .Append(double.IsNaN(r.Threshold) ? "-" : r.Threshold.ToString("R", CultureInfo.InvariantCulture))
              .Append("; recall ").Append(EvaluationReport.Format(r.Recall));
            if (full is not null && !ReferenceEquals(full, row))
            {
                sb.Append("; TOPLAM(hepsi)'ye göre fark: kendi eşiğiyle ").Append(DeltaText(Delta(full.Report.Recall, r.Recall)));
                if (row.AtFullThreshold is { } fixedRun)
                {
                    sb.Append(", TOPLAM eşiğiyle ").Append(DeltaText(Delta(full.Report.Recall, fixedRun.Recall)))
                      .Append(" (recall ").Append(EvaluationReport.Format(fixedRun.Recall)).Append(')');
                }
                else if (row.AtFullThresholdSkipped is { } reason)
                {
                    sb.Append(", TOPLAM eşiğiyle ").Append(reason);
                }
            }

            sb.AppendLine();
            foreach (var s in r.Strata)
            {
                // Ana raporla aynı biçimleyici: "yeterli örnek yok" ve Türkiye alt kümesinin tümleyen kuralı burada da geçerli.
                sb.Append("    ").Append(EvaluationReport.StratumLabel(s, " / pozitife bağlı"))
                  .Append(": yanlış alarm ").AppendLine(EvaluationReport.FormatStratum(s, r.Strata));
            }
        }

        sb.AppendLine("  Not: Yanlış alarm = SPK listesinde olmayan alan adında alarm. Bu bir yatırım tavsiyesi değildir.");
        return sb.ToString();
    }

    /// <summary>Fark metni; küçük hücrede "-" (sayı açığa çıkmasın).</summary>
    private static string DeltaText(double? delta) =>
        delta is { } d ? d.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " puan" : "-";

    /// <summary>Recall farkı (yüzde puanı). İki hücreden biri küçükse ya da oran yoksa <c>null</c>: fark da sayı açığa çıkarır.</summary>
    public static double? Delta(RateResult full, RateResult variant)
    {
        ArgumentNullException.ThrowIfNull(full);
        ArgumentNullException.ThrowIfNull(variant);
        if (full.Rate is not { } a || variant.Rate is not { } b || EvaluationReport.IsSmallCell(full) || EvaluationReport.IsSmallCell(variant))
        {
            return null;
        }

        return (b - a) * 100;
    }
}
