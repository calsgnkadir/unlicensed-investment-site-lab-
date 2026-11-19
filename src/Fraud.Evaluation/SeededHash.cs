using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Fraud.Evaluation;

/// <summary>
/// Tohumlu, sürümden bağımsız "rastgele" sayılar. .NET'in <see cref="Random"/> sınıfı kullanılmaz: Algoritması
/// sürümler arasında değişebilir; sonuç başka bir makinede tekrar edilemez (KONTROL_GRUBU_YONTEMI.md 1.3).
/// </summary>
public static class SeededHash
{
    /// <summary>
    /// <c>SHA-256(tohum + "|" + metin)</c> özetinden [0, 1) aralığında bir sayı. Aynı girdi her zaman aynı sayıyı verir.
    /// </summary>
    /// <remarks>
    /// İlk 8 bayt 64 bitlik sayı olarak okunur, en üstteki 53 biti alınır ve 2^53'e bölünür. Neden 53: double'ın
    /// tam olarak tutabildiği bit sayısı bu. Böylece sonuç 1'e yuvarlanamaz ve aralık eşit adımlı olur.
    /// </remarks>
    public static double Unit(string seed, string text)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed + "|" + text));
        var x = BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8));
        return (x >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>Tohum metninden 64 bitlik başlangıç değeri (bootstrap üreteci için).</summary>
    public static ulong Seed64(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 8));
    }
}

/// <summary>
/// SplitMix64: Bilinen, çok kısa bir sözde rastgele sayı üreteci (Steele, Lea, Flood 2014). Bootstrap'ta binlerce
/// sayı gerektiği için her sayıyı SHA-256'dan almak yavaş olurdu. Sabitler algoritmanın yayımlanmış sabitleridir.
/// Kriptografik değildir; burada yalnızca tekrar edilebilirlik gerekiyor.
/// </summary>
public sealed class SplitMix64(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// [0, n) aralığında bir tam sayı. Mod alma çok küçük bir eğilim yaratır (n / 2^64 düzeyinde); birkaç bin birimde
    /// bu ihmal edilebilir ve kodu basit tutar.
    /// </summary>
    public int NextIndex(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        return (int)(Next() % (ulong)n);
    }
}
