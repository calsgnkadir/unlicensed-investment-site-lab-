using System.Globalization;
using System.Text;

namespace Fraud.Collectors.StaticCt;

/// <summary>
/// Tile adresleri (c2sp.org/static-ct-api, "Merkle Tree" ve "Log entries").
/// </summary>
/// <remarks>
/// <c>N</c> üç haneli parçalara bölünür, sonuncusu dışındakiler <c>x</c> ile başlar: 1234067 =&gt; <c>x001/x234/067</c>.
/// Kısmi tile için sona <c>.p/W</c> eklenir (W = 1..255).
/// </remarks>
public static class TilePath
{
    public const int Width = 256;
    public const int HashSize = 32;
    public const int MaxLevel = 5;

    public static string EncodeIndex(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var groups = new List<string> { (index % 1000).ToString("D3", CultureInfo.InvariantCulture) };
        index /= 1000;
        while (index > 0)
        {
            groups.Add("x" + (index % 1000).ToString("D3", CultureInfo.InvariantCulture));
            index /= 1000;
        }

        groups.Reverse();
        return string.Join('/', groups);
    }

    /// <summary>Hash tile adresi: <c>tile/L/N[.p/W]</c>.</summary>
    public static string HashTile(int level, long index, int width = Width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, MaxLevel);
        return Build("tile/" + level.ToString(CultureInfo.InvariantCulture) + "/", index, width);
    }

    /// <summary>Veri tile adresi: <c>tile/data/N[.p/W]</c>.</summary>
    public static string DataTile(long index, int width = Width) => Build("tile/data/", index, width);

    private static string Build(string prefix, long index, int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, Width);

        var sb = new StringBuilder(prefix).Append(EncodeIndex(index));
        if (width < Width)
        {
            sb.Append(".p/").Append(width.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
