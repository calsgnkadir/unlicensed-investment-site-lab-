using System.Globalization;

namespace Fraud.Evaluation.Model;

/// <summary>
/// SPK bülten numarası, ör. <c>2026/12</c>. Metin olarak sıralanınca "2026/12" &lt; "2026/9" çıkardı; bu yüzden
/// yıl ve sıra numarası ayrı sayılar olarak tutulur ve öyle karşılaştırılır.
/// </summary>
public readonly record struct BulletinNumber(int Year, int Number) : IComparable<BulletinNumber>
{
    public static BulletinNumber Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Trim().Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || year < 2000 || number < 1)
        {
            throw new FormatException("Bülten numarası 'YYYY/N' biçiminde olmalı.");
        }

        return new BulletinNumber(year, number);
    }

    public int CompareTo(BulletinNumber other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Number.CompareTo(other.Number);

    /// <summary>Bülten [from, to] aralığında mı (iki uç dahil)?</summary>
    public bool IsBetween(BulletinNumber from, BulletinNumber to) => CompareTo(from) >= 0 && CompareTo(to) <= 0;

    public static bool operator <(BulletinNumber a, BulletinNumber b) => a.CompareTo(b) < 0;

    public static bool operator >(BulletinNumber a, BulletinNumber b) => a.CompareTo(b) > 0;

    public static bool operator <=(BulletinNumber a, BulletinNumber b) => a.CompareTo(b) <= 0;

    public static bool operator >=(BulletinNumber a, BulletinNumber b) => a.CompareTo(b) >= 0;

    public override string ToString() => Year.ToString(CultureInfo.InvariantCulture) + "/" + Number.ToString(CultureInfo.InvariantCulture);
}
