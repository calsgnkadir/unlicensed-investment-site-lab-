using System.Globalization;
using System.Text;
using Fraud.Evaluation.Metrics;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Splitting;

namespace Fraud.Evaluation.Running;

/// <summary>Bir çalıştırmanın toplu sonucu. Alan adı ve birim kimliği içermez; yalnızca sayılar.</summary>
/// <param name="Threshold">Eşik. İleriye dönük testte eşik yoktur (alarmlar taahhüt dosyasından gelir): <see cref="double.NaN"/>.</param>
public sealed record EvaluationReport(
    EvaluationMode Mode,
    string SignalName,
    double Threshold,
    double TargetFalseAlarmRate,
    int TuningCalibrationControls,
    IReadOnlyDictionary<SplitPart, int> SplitCounts,
    int MovedForIntegrity,
    int LagPoolSize,
    int ControlsWithoutReference,
    int TestPositives,
    int TestControls,
    RateResult Recall,
    IReadOnlyList<(Subgroup Subgroup, RateResult Recall)> RecallBySubgroup,
    IReadOnlyList<StratumResult> Strata,
    EarlyWarning EarlyWarning,
    double BaseRate,
    int BootstrapIterations,
    LockedRunTicket? Lock)
{
    /// <summary>Bu sayının altındaki her sayı "&lt;5" olarak yazılır (E6, küçük hücre bastırma).</summary>
    public const int SmallCellLimit = 5;

    /// <summary>Ölçümden düşen birimlerin nedenleri (sayı). Kural değişikliklerinin etkisi buradan görünür.</summary>
    public IReadOnlyList<(string Reason, int Count)> Dropped { get; init; } = [];

    /// <summary>Veri kapsaması uyarıları (<see cref="CoverageCheck"/>).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Çalıştırmanın "şimdi" kabul ettiği an (kontrol referansının üst sınırı).</summary>
    public DateTimeOffset? EvaluatedAt { get; init; }

    /// <summary>Kontrol referans kuralı (MatchedLag ya da FirstCertificate).</summary>
    public ControlReferencePolicy? ControlReference { get; init; }

    /// <summary>Eşik kümesinin (ayar bölmesi) kaynak grubuna göre sayıları: G4a, G4b. İleriye dönük testte boş (eşik yok).</summary>
    public IReadOnlyList<(string Source, int Count)> CalibrationComposition { get; init; } = [];

    /// <summary>Konsola basılan metin. Her oranın yanında ham sayı, aralık ve sınır notu var (Kural 7).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        if (Lock is { IsRerun: true })
        {
            sb.AppendLine("*** YENİDEN ÇALIŞTIRMA: Kilitli test daha önce çalıştı. Bu sonuç ilk sonuç DEĞİLDİR. ***");
            sb.AppendLine(Line("İlk çalıştırma", Lock.First.StartedAt.ToString("u", CultureInfo.InvariantCulture) + " commit " + Lock.First.Commit));
            sb.AppendLine(Line("Gerekçe", Lock.Current.RerunReason ?? "-"));
        }

        foreach (var warning in Warnings)
        {
            sb.AppendLine(warning);
        }

        sb.AppendLine(Line("Kip", Mode.ToString()));
        sb.AppendLine(Line("Sinyal", SignalName));
        sb.AppendLine(Line("Eşik", double.IsNaN(Threshold)
            ? "yok (alarmlar taahhüt edilmiş listeden okundu)"
            : Threshold.ToString("R", CultureInfo.InvariantCulture)
              + " (ayar kontrollerinde hedef yanlış alarm " + Percent(TargetFalseAlarmRate) + ", n=" + TuningCalibrationControls + ")"));
        if (CalibrationComposition.Count > 0)
        {
            sb.AppendLine(Line("Eşik kümesi (ayar)", CompositionText(CalibrationComposition)));
        }

        if (EvaluatedAt is { } at)
        {
            sb.AppendLine(Line("Değerlendirme anı", at.ToString("u", CultureInfo.InvariantCulture)));
        }

        sb.AppendLine(Line("Bölme", string.Join(", ", SplitCounts.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))
            + "; bütünlük için taşınan=" + MovedForIntegrity));
        sb.AppendLine(Line("Kontrol referansı", (ControlReference?.ToString() ?? "-") + ", gecikme havuzu (ayar pozitifi)=" + LagPoolSize
            + ", referanssız kontrol=" + ControlsWithoutReference));
        if (Dropped.Count > 0)
        {
            sb.AppendLine(Line("Dışarıda kalan", string.Join(", ", Dropped.Select(d => d.Reason + "=" + d.Count))));
        }

        sb.AppendLine(Line("Test", "pozitif=" + Cell(TestPositives) + ", kontrol=" + Cell(TestControls)));
        sb.AppendLine(Line("Recall", Format(Recall)));
        foreach (var (subgroup, recall) in RecallBySubgroup)
        {
            sb.AppendLine(Line("  " + subgroup, Format(recall)));
        }

        sb.AppendLine("Kontrol tabakaları (yanlış alarm = SPK listesinde olmayan alan adında alarm):");
        foreach (var s in Strata)
        {
            sb.Append("  ").Append(s.Group).Append(" / ").Append(s.Subgroup).Append(s.LinkedToPositive ? " / pozitife bağlı (E05, ana orana girmez)" : string.Empty)
                .Append(": ").Append(Format(s.FalseAlarms));
            // Precision ve günlük alarm, isabet sayılarından geri hesaplanabilir: Hücre küçükse (bu tabakada ya da
            // recall'da) onlar da yazılmaz (mentör 5.4/2).
            var hideDerived = IsSmallCell(s.FalseAlarms) || IsSmallCell(Recall);
            sb.Append("; örneklem içi precision=").Append(hideDerived ? "-" : Number(s.SamplePrecision))
                .Append("; π=").Append(BaseRate.ToString(CultureInfo.InvariantCulture)).Append(" ile precision=")
                .Append(hideDerived ? "-" : Number(s.AdjustedPrecision));
            if (!hideDerived && s.ExpectedDailyFalseAlarms is { } daily)
            {
                sb.Append("; beklenen günlük yanlış alarm=").Append(daily.ToString("0.0", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
        }

        sb.AppendLine(Line("Erken uyarı", EarlyWarning.MedianDays is null
            ? "yakalanan pozitif=" + (IsSmallCell(Recall) ? "-" : Cell(EarlyWarning.AlarmedPositives)) + " (10'dan az: süre yazılmaz)"
            : "n=" + (IsSmallCell(Recall) ? "-" : Cell(EarlyWarning.AlarmedPositives)) + ", ortanca " + Number(EarlyWarning.MedianDays) + " gün (en az "
              + Number(EarlyWarning.MinDays) + ", en çok " + Number(EarlyWarning.MaxDays) + ")"));
        sb.AppendLine("Notlar:");
        sb.AppendLine("  - Aralıklar: %95 yüzdelik bootstrap, " + BootstrapIterations + " tekrar, küme ve bülten düzeyinde.");
        sb.AppendLine("  - 10'dan az birimde oran verilmez, yalnızca ham sayı; 5'ten küçük sayılar '<5' yazılır.");
        sb.AppendLine("  - π (taban oran) ÖLÇÜLMEDİ, varsayımdır. Precision 'SPK etiketine göre'dir.");
        sb.AppendLine("  - Etiket 'SPK erişim engeli listesinde' demektir, 'dolandırıcı' demek değildir.");
        sb.AppendLine("  - Erken uyarı: SPK karar tarihi ilk tespit tarihi değildir.");
        if (Mode == EvaluationMode.Historical)
        {
            sb.AppendLine("  - Geçmiş veri testi temiz değildir: Aşama 0 analizi bu bültenlere baktı.");
        }

        sb.AppendLine("  - Bu bir yatırım tavsiyesi değildir.");
        return sb.ToString();
    }

    /// <summary>
    /// Küçük hücre bastırma (E6): 5'ten küçük sayılar yazılmaz. "1/1" yazılsaydı, tek birimli bir tabakada okuyan o
    /// birimin hangi site olduğunu biliyorsa sonucu adıyla çıkarabilirdi (Kural 4, çıkarım yoluyla). 0 da bastırılır,
    /// çünkü "hiç alarm yok" da tek bir site hakkında bilgi verir.
    /// </summary>
    public static string Cell(int count) => count < SmallCellLimit ? "<" + SmallCellLimit.ToString(CultureInfo.InvariantCulture) : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Eşik kümesinin dökümü, küçük hücre kuralıyla (E6). 5'ten küçük sayı "&lt;5" yazılır. Böyle bir sayı varsa öteki sayılar
    /// da yazılmaz ("-"): Toplam n zaten basıldığı için "n − öteki" gizlenen sayıyı geri verirdi (tümleyen kuralı).
    /// </summary>
    public static string CompositionText(IReadOnlyList<(string Source, int Count)> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var anySmall = parts.Any(p => p.Count < SmallCellLimit);
        return string.Join(", ", parts.Select(p => p.Source + "=" + (p.Count < SmallCellLimit ? Cell(p.Count) : anySmall ? "-" : Cell(p.Count))));
    }

    private static string Line(string key, string value) => key + ": " + value;

    private static string Percent(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Number(double? value) => value is { } v ? v.ToString("0.####", CultureInfo.InvariantCulture) : "-";

    /// <summary>
    /// Küçük hücre (E6, mentör 5.4/2): İsabet ya da tümleyeni (toplam − isabet) 5'ten küçük. Böyle bir hücrede oran,
    /// aralık ve tümleyen yazılmaz: "&lt;5/40 = %7,5" yazısından isabet geri hesaplanırdı; "11/12" de tek kalan birimi
    /// (alarm vermeyen tek siteyi) ele verirdi.
    /// </summary>
    public static bool IsSmallCell(RateResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Hits < SmallCellLimit || r.Total - r.Hits < SmallCellLimit;
    }

    /// <summary>Bir oranın metni. Küçük hücrede yalnızca aralık biçiminde sayı: "&lt;5/40" ya da "≥36/40".</summary>
    public static string Format(RateResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (IsSmallCell(r))
        {
            var hits = r.Hits < SmallCellLimit
                ? "<" + SmallCellLimit.ToString(CultureInfo.InvariantCulture)
                : "≥" + (r.Total - SmallCellLimit + 1).ToString(CultureInfo.InvariantCulture);
            return hits + "/" + Cell(r.Total) + " (küçük hücre: sayı, oran ve aralık yazılmaz)";
        }

        var text = Cell(r.Hits) + "/" + Cell(r.Total);
        if (r.Rate is not { } rate)
        {
            return text + " (birim sayısı az: yalnızca ham sayı)";
        }

        text += " = " + Percent(rate);
        if (r.ClusterInterval is { } c)
        {
            text += ", küme %95 [" + Percent(c.Low) + ", " + Percent(c.High) + "]";
        }

        if (r.BulletinInterval is { } b)
        {
            text += ", bülten %95 [" + Percent(b.Low) + ", " + Percent(b.High) + "]";
        }

        return text;
    }
}

/// <summary>
/// Bütün hat: referans → bölme → eşik (yalnızca ayarda) → test tahminleri → metrikler.
/// </summary>
/// <remarks>
/// Sıra önemlidir ve bilerek tek yerde yazılmıştır: Eşik test verisi görülmeden seçilir, sonra değişmez.
/// Test birimlerinin tahmini, eşik sabitlendikten SONRA yapılır.
/// </remarks>
public static class EvaluationRunner
{
    /// <summary>Geçmiş ve kilitli test. İleriye dönük test için <see cref="RunProspective"/>.</summary>
    /// <param name="now">Kontrol referansının üst sınırı. Boşsa sistem saati. Tekrar edilebilir çalıştırma için verilmeli.</param>
    /// <param name="fixedThreshold">Yalnızca ablasyon: eşik seçilmez, bu değer kullanılır ("TOPLAM'ın eşiği sabit"). Yalnızca
    /// geçmiş veri testinde kabul edilir; kilitli testte eşik her zaman kuralla seçilir.</param>
    public static EvaluationReport Run(
        EvaluationConfig config, EvaluationMode mode, IReadOnlyList<Subject> subjects, IReadOnlyList<Observation> observations,
        ISignal signal, LockedRunTicket? lockTicket = null, DateTimeOffset? now = null, double? fixedThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(signal);
        if (mode == EvaluationMode.Prospective)
        {
            throw new InvalidOperationException("İleriye dönük test taahhüt edilmiş listeyle çalışır: RunProspective.");
        }

        if (fixedThreshold is not null && mode != EvaluationMode.Historical)
        {
            throw new InvalidOperationException("Sabit eşik yalnızca geçmiş veri testindeki ablasyon içindir.");
        }

        if (mode == EvaluationMode.Locked && lockTicket is null)
        {
            throw new InvalidOperationException("Kilitli test yalnızca LockedRunGuard biletiyle çalışır.");
        }

        config.ValidateFor(mode);
        var at = now ?? DateTimeOffset.UtcNow;

        // 1) Kontrollerin referans zamanı (yalnızca ayar pozitiflerinden çekilen gecikmeyle, "şimdi"yi aşmadan).
        var assignment = ReferenceTimeAssigner.Assign(subjects, config, at);
        var all = assignment.Subjects;

        // 2) Bölme.
        var split = Splitter.Split(all, config);
        var byId = observations.GroupBy(o => o.SubjectId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Observation>)[.. g], StringComparer.Ordinal);
        IReadOnlyList<Observation> ObservationsOf(Subject s) => byId.GetValueOrDefault(s.Id) ?? [];

        // 3) Eşik: yalnızca ayar bölmesindeki, pozitife bağlı olmayan G4 kontrolleri (DECISIONS 2026-11-21). G4 alan adındaki
        // kelimeye bakılmadan seçilir; G1, G2, G3 ve Çerçeve K eşiğe girmez, seçilen eşikte grup ve çerçeve başına ayrı raporlanır.
        // Çerçeve K koşulu ayrıca yazılır (DECISIONS 2026-11-24): Bugün G4b yalnızca N'den kurulur, ama ileride K taşıyan bir
        // G4 birimi gelirse yalnızca gruba bakan süzgeç onu sessizce eşiğe alırdı. K kelime filtresinden geçtiği için eşiği yukarı iter.
        var calibration = all
            .Where(s => split.PartOf(s.Id) == SplitPart.Tuning && !s.IsPositive && !s.LinkedToPositive
                        && s.Group == SubjectGroup.ControlRandom && s.Subgroup != Subgroup.FrameTermConditioned)
            .ToList();
        var calibrationScores = calibration.Select(s => Predictor.Predict(signal, s, ObservationsOf(s), threshold: null).Score).ToList();
        var threshold = fixedThreshold ?? ThresholdSelector.Select(calibrationScores, config.TargetFalseAlarmRate);

        // 4) Test kümesi ve tahminler (eşik artık sabit).
        var testPart = mode == EvaluationMode.Locked ? SplitPart.Locked : SplitPart.Historical;
        var test = all.Where(s => split.PartOf(s.Id) == testPart).ToList();
        var predictions = test.ToDictionary(s => s.Id, s => Predictor.Predict(signal, s, ObservationsOf(s), threshold), StringComparer.Ordinal);

        return Report(config, mode, signal.Name, threshold, calibration.Count, assignment, split, test, observations, predictions, lockTicket, at)
            with { CalibrationComposition = CompositionOf(calibration) };
    }

    /// <summary>
    /// Eşik kümesinin kaynak grubuna göre dökümü (DECISIONS 2026-11-24). Ölçüm modelinde G4a ile G4b aynı gruptur
    /// (<see cref="SubjectGroup.ControlRandom"/>); onları çerçeve ayırır: G4a çerçevesizdir (Tranco), G4b Çerçeve N'dir.
    /// Okuyan, "n=" sayısının hangi kaynaktan geldiğini görmezse eşiğin hangi kontrollere dayandığını bilemez.
    /// </summary>
    private static IReadOnlyList<(string Source, int Count)> CompositionOf(List<Subject> calibration) =>
    [
        ("G4a", calibration.Count(s => s.Subgroup == Subgroup.None)),
        ("G4b", calibration.Count(s => s.Subgroup == Subgroup.FrameNeutral)),
    ];

    /// <summary>
    /// İleriye dönük test (mentör maddesi 1). Alarm kümesi YENİDEN HESAPLANMAZ: Taahhüt dosyasında açılmış kayıtlardan
    /// okunur. Sinyal bugün yeniden çalıştırılsaydı, taahhütten sonra öğrenilen bilgi (ör. sonradan çekilmiş
    /// sertifika) "önceden biliniyordu" sayılabilirdi.
    /// </summary>
    /// <param name="committedSubjectIds">Taahhüt dosyasındaki kayıtların opak kimlikleri (kanıtları doğrulanmış olarak).</param>
    /// <param name="commitmentTime">Taahhüt anı C. Dış zaman damgasıyla (README) ayrıca doğrulanmalıdır.</param>
    /// <remarks>
    /// Test kümesi: Referansı C'den sonra ve "şimdi"den önce olan, ayarda olmayan ve ayardaki hiçbir birimle aynı
    /// bileşende bulunmayan birimler. Pozitif için referans karar tarihidir: C'den sonra karar verilmiş ve C'de listede
    /// olan pozitif "önceden yakalanmış" sayılır.
    /// </remarks>
    public static EvaluationReport RunProspective(
        EvaluationConfig config, IReadOnlyList<Subject> subjects, IReadOnlyList<Observation> observations,
        IReadOnlySet<string> committedSubjectIds, DateTimeOffset commitmentTime, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(committedSubjectIds);
        // TSA kökü burada gerekmez: Zaman damgası komut tarafında doğrulanır ve buraya yalnızca doğrulanmış zaman gelir.
        config.Validate();
        var at = now ?? DateTimeOffset.UtcNow;

        var assignment = ReferenceTimeAssigner.Assign(subjects, config, at);
        var split = Splitter.Split(assignment.Subjects, config);
        var test = assignment.Subjects
            .Where(s => split.PartOf(s.Id) != SplitPart.Tuning && !split.TiedToTuning.Contains(s.Id)
                        // Mentör 5.4/1: İlk sertifikası bilinmeyen birim bu teste de girmez; pozitif ve kontrol için aynı
                        // kural. Bülten aralığı dışında olduğu için "dışarıda" kalan pozitif ise tam olarak bu testin
                        // birimidir (karar C'den sonra), o yüzden yalnızca bölme etiketi değil sertifika şartı uygulanır.
                        && s.FirstCertificateAt is not null
                        && s.ReferenceTime is { } r && r > commitmentTime)
            .ToList();
        var predictions = test.ToDictionary(
            s => s.Id,
            s => committedSubjectIds.Contains(s.Id) ? new Prediction(s.Id, 1, commitmentTime) : new Prediction(s.Id, 0, null),
            StringComparer.Ordinal);

        return Report(config, EvaluationMode.Prospective, "taahhüt", double.NaN, 0, assignment, split, test, observations, predictions, null, at);
    }

    private static EvaluationReport Report(
        EvaluationConfig config, EvaluationMode mode, string signalName, double threshold, int calibrationCount,
        ReferenceAssignment assignment, SplitResult split, List<Subject> test, IReadOnlyList<Observation> observations,
        Dictionary<string, Prediction> predictions, LockedRunTicket? lockTicket, DateTimeOffset at)
    {
        var (recall, bySubgroup, strata, early) = MetricsCalculator.Compute(new MetricsInput(
            test, predictions, config.BaseRate, config.DailyNewDomains, config.MinCountForRates, config.BootstrapIterations, config.Seed));

        var used = assignment.Subjects.Where(s => s.ReferenceTime is not null && split.PartOf(s.Id) != SplitPart.Excluded).ToList();
        var counts = Enum.GetValues<SplitPart>().ToDictionary(p => p, split.Count);
        return new EvaluationReport(
            mode, signalName, threshold, config.TargetFalseAlarmRate, calibrationCount, counts, split.MovedForIntegrity,
            assignment.LagPoolSize, assignment.ControlsWithoutCertificate, test.Count(s => s.IsPositive), test.Count(s => !s.IsPositive),
            recall, bySubgroup, strata, early, config.BaseRate, config.BootstrapIterations, lockTicket)
        {
            Dropped =
            [
                ("ilk sertifikası bilinmeyen pozitif", split.PositivesWithoutCertificate),
                ("ilk sertifikası bilinmeyen kontrol", assignment.ControlsWithoutCertificate),
                ("referansı şimdiden sonra olan kontrol", assignment.ControlsBeyondNow),
                ("referansı F'den sonra olan kontrol", split.ControlsAfterFreeze),
            ],
            Warnings = CoverageCheck.Warnings(used, observations, config.CoverageWarningGap),
            EvaluatedAt = at,
            ControlReference = config.ControlReference,
        };
    }
}
