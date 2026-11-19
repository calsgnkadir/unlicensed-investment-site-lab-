using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Metrics;

/// <summary>Bir oran: ham sayılar her zaman, oran ve aralık yalnızca yeterli birim varsa.</summary>
/// <param name="Hits">İsabet (yakalanan pozitif ya da alarm veren kontrol).</param>
/// <param name="Total">Birim sayısı.</param>
/// <param name="Rate">Hits / Total. <c>Total</c> en küçük sayının altındaysa <c>null</c>: "yalnızca ham sayı" kuralı.</param>
/// <param name="ClusterInterval">Küme düzeyinde bootstrap %95 aralığı.</param>
/// <param name="BulletinInterval">Bülten düzeyinde bootstrap %95 aralığı (yalnızca pozitifler).</param>
public sealed record RateResult(int Hits, int Total, double? Rate, Interval? ClusterInterval, Interval? BulletinInterval);

/// <summary>Bir kontrol tabakasının sonucu (grup × alt grup × pozitife bağlı mı).</summary>
/// <param name="SamplePrecision">Örneklem içi precision: TP / (TP + FP). Pozitif/kontrol oranı bizim seçimimiz olduğu
/// için gerçek dünyayı göstermez; yalnızca karşılaştırma için.</param>
/// <param name="AdjustedPrecision">Taban orana göre düzeltilmiş precision (formül <see cref="MetricsCalculator.AdjustedPrecision"/>).</param>
/// <param name="ExpectedDailyFalseAlarms">Yalnızca Çerçeve N: yanlış alarm oranı × günlük yeni alan adı.</param>
public sealed record StratumResult(
    SubjectGroup Group, Subgroup Subgroup, bool LinkedToPositive, RateResult FalseAlarms,
    double? SamplePrecision, double? AdjustedPrecision, double? ExpectedDailyFalseAlarms);

/// <summary>Erken uyarı süresi: karar tarihi − ilk alarm (gün). SPK karar tarihi ilk tespit tarihi değildir.</summary>
public sealed record EarlyWarning(int AlarmedPositives, double? MedianDays, double? MinDays, double? MaxDays);

/// <summary>Bir test kümesindeki birimler, tahminleri ve metrik ayarları.</summary>
public sealed record MetricsInput(
    IReadOnlyList<Subject> Subjects,
    IReadOnlyDictionary<string, Prediction> Predictions,
    double BaseRate,
    double? DailyNewDomains,
    int MinCountForRates,
    int BootstrapIterations,
    string BootstrapSeed);

/// <summary>Bütün metrikler. Her formül burada, yorumuyla birlikte.</summary>
public static class MetricsCalculator
{
    /// <summary>
    /// Taban orana göre düzeltilmiş precision (PPV, KONTROL_GRUBU_YONTEMI.md 8.2):
    /// <code>PPV = r·π / (r·π + f·(1 − π))</code>
    /// r = recall, f = yanlış alarm oranı, π = yaygınlık (gerçek dünyada pozitif payı).
    /// </summary>
    /// <remarks>
    /// Neden: Örneklemde pozitif payını biz seçtik (ör. 48 pozitif, 1.000 kontrol). Gerçekte yeni alan adlarının
    /// binde biri bile pozitif olmayabilir. Bayes kuralı, aynı r ve f ile gerçek yaygınlıkta alarmların ne kadarının
    /// pozitif olacağını verir. Elle örnek: r = 0,5, f = 0,02, π = 0,001 → 0,0005 / (0,0005 + 0,01998) = 0,0244.
    /// f = 0 ise yazılmaz: "Hiç yanlış alarm görmedik" küçük örnekte f'nin sıfır olduğu demek değildir.
    /// </remarks>
    public static double? AdjustedPrecision(double? recall, double? falseAlarmRate, double baseRate)
    {
        if (recall is not { } r || falseAlarmRate is not { } f || f == 0)
        {
            return null;
        }

        var truePart = r * baseRate;
        var falsePart = f * (1 - baseRate);
        return truePart + falsePart == 0 ? null : truePart / (truePart + falsePart);
    }

    public static (RateResult Recall, IReadOnlyList<(Subgroup Subgroup, RateResult Recall)> BySubgroup, IReadOnlyList<StratumResult> Strata, EarlyWarning EarlyWarning)
        Compute(MetricsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var positives = input.Subjects.Where(s => s.IsPositive).ToList();
        var recall = Rate(positives, input, withBulletins: true, "recall");
        var bySubgroup = positives
            .GroupBy(s => s.Subgroup)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, Rate([.. g], input, withBulletins: true, "recall-" + g.Key)))
            .ToList();

        var truePositives = positives.Count(p => input.Predictions[p.Id].Alarmed);
        var strata = input.Subjects
            .Where(s => !s.IsPositive)
            .GroupBy(s => (s.Group, s.Subgroup, s.LinkedToPositive))
            .OrderBy(g => g.Key.Group).ThenBy(g => g.Key.Subgroup).ThenBy(g => g.Key.LinkedToPositive)
            .Select(g =>
            {
                var falseAlarms = Rate([.. g], input, withBulletins: false, "fa-" + g.Key.Group + "-" + g.Key.Subgroup + "-" + g.Key.LinkedToPositive);
                // Tabakada 10'dan az birim varsa precision da yazılmaz (mentör maddesi 6): ham sayı kuralı burada da geçerli.
                double? samplePrecision = recall.Rate is null || falseAlarms.Rate is null || truePositives + falseAlarms.Hits == 0
                    ? null
                    : (double)truePositives / (truePositives + falseAlarms.Hits);
                var adjusted = AdjustedPrecision(recall.Rate, falseAlarms.Rate, input.BaseRate);
                double? daily = g.Key.Subgroup == Subgroup.FrameNeutral && falseAlarms.Rate is { } f && input.DailyNewDomains is { } n
                    ? f * n
                    : null;
                return new StratumResult(g.Key.Group, g.Key.Subgroup, g.Key.LinkedToPositive, falseAlarms, samplePrecision, adjusted, daily);
            })
            .ToList();

        return (recall, bySubgroup, strata, EarlyWarningOf(positives, input.Predictions, input.MinCountForRates));
    }

    /// <summary>
    /// Erken uyarı: yakalanan her pozitif için (karar − ilk alarm) gün. Ortanca, sıralı listenin ortası; çift sayıda
    /// ise ortadaki ikisinin ortalaması. Yakalanan pozitif <paramref name="minCount"/>'tan azsa yalnızca sayı verilir
    /// (mentör maddesi 6): 3 sitenin ortancası bir "ölçüm" değildir ve tek tek sitelere çok yakındır.
    /// </summary>
    public static EarlyWarning EarlyWarningOf(IReadOnlyList<Subject> positives, IReadOnlyDictionary<string, Prediction> predictions, int minCount)
    {
        ArgumentNullException.ThrowIfNull(positives);
        ArgumentNullException.ThrowIfNull(predictions);
        var leads = positives
            .Select(p => (p.ReferenceTime, predictions[p.Id].FirstAlarmAt))
            .Where(x => x.FirstAlarmAt is not null)
            .Select(x => (x.ReferenceTime!.Value - x.FirstAlarmAt!.Value).TotalDays)
            .Order()
            .ToList();
        if (leads.Count < Math.Max(1, minCount))
        {
            return new EarlyWarning(leads.Count, null, null, null);
        }

        var middle = leads.Count / 2;
        var median = leads.Count % 2 == 1 ? leads[middle] : (leads[middle - 1] + leads[middle]) / 2;
        return new EarlyWarning(leads.Count, median, leads[0], leads[^1]);
    }

    private static RateResult Rate(List<Subject> subjects, MetricsInput input, bool withBulletins, string seedSuffix)
    {
        var hits = subjects.Count(s => input.Predictions[s.Id].Alarmed);
        if (subjects.Count < input.MinCountForRates)
        {
            // 10'dan az birim: yüzde ve aralık yok, yalnızca "hits / total" ham sayısı.
            return new RateResult(hits, subjects.Count, null, null, null);
        }

        var seed = input.BootstrapSeed + "|" + seedSuffix;
        var byCluster = Bootstrap.PercentileInterval(
            [.. subjects.Select(s => new BootstrapRow(s.ClusterId, input.Predictions[s.Id].Alarmed))], input.BootstrapIterations, seed + "|cluster");
        var byBulletin = withBulletins
            ? Bootstrap.PercentileInterval(
                [.. subjects.Select(s => new BootstrapRow(s.Bulletin!.Value.ToString(), input.Predictions[s.Id].Alarmed))],
                input.BootstrapIterations, seed + "|bulletin")
            : null;
        return new RateResult(hits, subjects.Count, (double)hits / subjects.Count, byCluster, byBulletin);
    }
}
