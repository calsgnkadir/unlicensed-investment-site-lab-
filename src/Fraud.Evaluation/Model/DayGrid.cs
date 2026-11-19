namespace Fraud.Evaluation.Model;

/// <summary>
/// Gün ızgarası: Bütün referans ve görünüm anları Türkiye saatiyle (UTC+3) gün sınırlarına hizalanır.
/// </summary>
/// <remarks>
/// <para>
/// Neden (bulgu E4): Pozitifin referansı karar günü 00:00 (TR). Kontrolün referansı ise "ilk sertifika + gecikme",
/// yani günün herhangi bir saati olsaydı, görünüm anının SAATİNE bakan bir sinyal etiketi kusursuz ayırırdı.
/// İki taraf da aynı ızgaraya hizalanınca görünüm anı yalnızca "hangi gün" bilgisini taşır; saat hep aynıdır.
/// </para>
/// <para>Izgara noktası: Bir TR gününün son anı, yani ertesi günün 00:00'ından 1 tik önce.</para>
/// </remarks>
public static class DayGrid
{
    public static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    /// <summary>t'nin içinde bulunduğu TR gününün başı (00:00, UTC+3).</summary>
    public static DateTimeOffset DayStart(DateTimeOffset t)
    {
        var local = t.ToOffset(TurkeyOffset);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, TurkeyOffset);
    }

    /// <summary>t anına eşit ya da ondan önceki en son ızgara noktası (bir günün son anı).</summary>
    /// <remarks>Örnek: 10 Mart 14:00 TR → 9 Mart 23:59:59.9999999 TR. 9 Mart 23:59:59.9999999 TR → kendisi.</remarks>
    public static DateTimeOffset EndOfDayAtOrBefore(DateTimeOffset t) => DayStart(t.AddTicks(1)).AddTicks(-1);

    /// <summary>t anına eşit ya da ondan sonraki ilk ızgara noktası.</summary>
    public static DateTimeOffset EndOfDayAtOrAfter(DateTimeOffset t)
    {
        var before = EndOfDayAtOrBefore(t);
        return before == t ? t : before.AddDays(1);
    }

    /// <summary>
    /// t'den KESİNLİKLE sonraki ilk gün başı. Kontrol referansı böyle seçilir: t anında bilinen kayıt (ör. ilk
    /// sertifika) referanstan önceki son görünümde görünür, referans da pozitiflerinki gibi bir gün başıdır.
    /// </summary>
    public static DateTimeOffset NextDayStartAfter(DateTimeOffset t) => DayStart(t).AddDays(1);

    /// <summary>t'ye eşit ya da ondan sonraki ilk gün başı.</summary>
    public static DateTimeOffset DayStartAtOrAfter(DateTimeOffset t) => DayStart(t) == t ? t : DayStart(t).AddDays(1);
}
