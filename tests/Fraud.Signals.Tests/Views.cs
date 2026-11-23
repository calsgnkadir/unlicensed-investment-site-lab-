using Fraud.Evaluation.Model;

namespace Fraud.Signals.Tests;

/// <summary>
/// Elle kurulan küçük görünümler. Yalnızca <c>.example</c> / <c>.test</c> adları (depo herkese açık, Kural 4).
/// </summary>
internal static class Views
{
    public const string Id = "s-test";

    /// <summary>Karar anı: 2026-06-01 TR gün başı.</summary>
    public static readonly DateTimeOffset Reference = new(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(3));

    /// <summary>TR saatiyle öğlen: gün içinde bir an (görünüm anı günün sonuna yuvarlanır).</summary>
    public static DateTimeOffset Noon(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.FromHours(3));

    public static Subject Subject() =>
        new(Id, SubjectGroup.Positive, Subgroup.PositiveNew, Reference, new BulletinNumber(2026, 30), Id);

    public static Observation Name(string registrable) =>
        new(Id, ObservationKinds.Name, DateTimeOffset.MinValue, DateTimeOffset.MinValue, registrable);

    public static Observation Cert(DateTimeOffset knownAt, string certificateId) =>
        new(Id, ObservationKinds.Certificate, knownAt, knownAt, certificateId);

    /// <summary>O TR gününün son anı (gün ızgarası noktası). Görünümler bununla kurulur: Hangi günün sonu olduğu açık olsun.</summary>
    /// <remarks>AsOfView gün içindeki bir anı ÖNCEKİ günün sonuna yuvarlar (tutucu); öğlen vermek bir gün kaydırırdı.</remarks>
    public static DateTimeOffset EndOf(int month, int day) => DayGrid.EndOfDayAtOrAfter(Noon(month, day));

    /// <summary>Verilen anın görünümü (bir ızgara noktasına aşağı yuvarlanır).</summary>
    public static AsOfView At(DateTimeOffset asOf, params Observation[] observations) =>
        AsOfView.Create(Subject(), asOf, observations);

    /// <summary>Yalnızca adı olan görünüm (ad sinyalleri için).</summary>
    public static AsOfView Named(string registrable) => At(EndOf(5, 1), Name(registrable));
}
