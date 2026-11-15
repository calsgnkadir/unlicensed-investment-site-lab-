using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fraud.Domain.DomainNames;

namespace Fraud.Collectors.StaticCt;

/// <summary>Tarafsız örneklem ayarları (bölüm: <c>NeutralSample</c>).</summary>
public sealed class NeutralSampleOptions
{
    public const string SectionName = "NeutralSample";

    /// <summary>Varsayılan oran: kayıtlı alan adlarının yaklaşık %1'i (KONTROL_GRUBU_YONTEMI.md 10.1, karar 3).</summary>
    public const double DefaultRate = 0.01;

    /// <summary>En kısa tuz: 16 bayt (32 onaltılık karakter). install.sh 32 bayt üretir.</summary>
    public const int MinSaltBytes = 16;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Onaltılık tuz. Kurulumda bir kez üretilir (deploy/linux/install.sh), .env içinde durur, hiçbir yere basılmaz.
    /// Tuz değişirse örneklemin tamamı değişir; bu yüzden kurulduktan sonra değiştirilmez.
    /// </summary>
    public string? Salt { get; set; }

    /// <summary>
    /// Örnekleme oranı (0 ile 1 arasında, ikisi de hariç). Karar 3: Ölçülen hacme göre yalnızca dondurmadan önce
    /// bir kez ayarlanabilir. Oran yükseltilirse eski seçilenlerin hepsi yine seçilir (eşik büyür, küme genişler).
    /// </summary>
    public double Rate { get; set; } = DefaultRate;

    /// <summary>ct-follow başlamadan çağrılır. Açıksa tuz zorunludur: Örneklem sessizce kapalı kalmasın.</summary>
    public void ValidateForFollow()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Salt))
        {
            // Tuzun kendisi mesaja yazılmaz; yalnızca nerede olması gerektiği.
            throw new InvalidOperationException(
                "NeutralSample:Salt yok. deploy/linux/install.sh tuzu .env içine (CT_NEUTRAL_SALT) bir kez üretir.");
        }

        _ = ParseSalt(Salt);
        if (!(Rate > 0 && Rate < 1))
        {
            throw new InvalidOperationException("NeutralSample:Rate 0 ile 1 arasında olmalı (ikisi de hariç).");
        }
    }

    /// <summary>Onaltılık metni bayta çevirir. Hata mesajı tuzu içermez.</summary>
    public static byte[] ParseSalt(string salt)
    {
        ArgumentNullException.ThrowIfNull(salt);
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(salt.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("NeutralSample:Salt onaltılık (hex) olmalı.");
        }

        if (bytes.Length < MinSaltBytes)
        {
            throw new InvalidOperationException("NeutralSample:Salt en az " + MinSaltBytes + " bayt olmalı.");
        }

        return bytes;
    }
}

/// <summary>
/// Tarafsız örneklem kuralı: Bir kayıtlı alan adı, içindeki kelimelere bakılmadan, yalnızca tuzlu özetine göre seçilir.
/// </summary>
/// <remarks>
/// <para>Kural (KONTROL_GRUBU_YONTEMI.md Bölüm 4.5):</para>
/// <code>
/// h = SHA-256(tuz || kayıtlı_alan_adı)        // alan adı UTF-8 (ASCII/punycode) baytları
/// x = h'nin ilk 8 baytı, büyük uçlu (big-endian) işaretsiz 64 bit sayı
/// seç  ⇔  x &lt; oran × 2^64
/// </code>
/// <para>
/// Neden böyle: SHA-256 çıktısı tuzu bilmeyen biri için rastgele gibidir. x, 0 ile 2^64 arasında düzgün dağılır;
/// x'in oran × 2^64'ten küçük olma olasılığı tam olarak orandır. Aynı ad her zaman aynı x'i verir, yani karar
/// deterministiktir: Program yeniden başlasa da, iki log aynı adı görse de karar değişmez.
/// </para>
/// <para>
/// Neden kayıtlı alan adı ve neden alan adı düzeyinde: Örnekleme sertifika düzeyinde yapılsaydı aynı alan adının bazı
/// sertifikaları örnekte olur, bazıları olmazdı. Alan adı düzeyinde seçince, seçilen alan adının bütün kayıtları
/// sayılır (<c>seen_count</c>).
/// </para>
/// <para>
/// Neden tuz: Tuz olmasaydı herkes hangi alan adlarının örnekte olduğunu hesaplayabilir, örneğe girmek ya da
/// girmemek için ad seçebilirdi. Tuz gizli kaldıkça seçim önceden tahmin edilemez.
/// </para>
/// </remarks>
public sealed class NeutralSampler
{
    private readonly byte[] _salt;
    private readonly IDomainNameNormalizer _normalizer;

    public NeutralSampler(NeutralSampleOptions options, IDomainNameNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(normalizer);
        options.ValidateForFollow();
        _salt = NeutralSampleOptions.ParseSalt(options.Salt!);
        _normalizer = normalizer;
        Threshold = ThresholdFor(options.Rate);
    }

    /// <summary>Eşik: oran × 2^64. x bundan küçükse seçilir.</summary>
    public ulong Threshold { get; }

    /// <summary>oran × 2^64. Oran (0, 1) aralığında olduğu için sonuç ulong'a sığar.</summary>
    public static ulong ThresholdFor(double rate)
    {
        if (!(rate > 0 && rate < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Oran 0 ile 1 arasında olmalı.");
        }

        // 2^64 bir double olarak tam yazılabilir. Çarpım double'da yapılır; aynı oran her makinede aynı eşiği verir.
        const double TwoTo64 = 18446744073709551616.0;
        return (ulong)(rate * TwoTo64);
    }

    /// <summary>Kuralın kendisi: tuz ve kayıtlı alan adından karar. Saf fonksiyon, test edilebilir.</summary>
    public static bool IsSelected(ReadOnlySpan<byte> salt, string registrableDomain, ulong threshold)
    {
        ArgumentNullException.ThrowIfNull(registrableDomain);
        var name = Encoding.UTF8.GetBytes(registrableDomain);
        var input = new byte[salt.Length + name.Length];
        salt.CopyTo(input);
        name.CopyTo(input.AsSpan(salt.Length));

        var hash = SHA256.HashData(input);
        var x = BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8));
        return x < threshold;
    }

    /// <summary>
    /// Bir sertifikanın DNS adlarından örneğe giren kayıtlı alan adlarını döner (tekrarsız, sıralı).
    /// Ad kayıtlı alan adına indirgenemezse (geçersiz söz dizimi, yalnızca genel sonek) atlanır.
    /// </summary>
    public IReadOnlyList<string> Select(IReadOnlyList<string> dnsNames)
    {
        ArgumentNullException.ThrowIfNull(dnsNames);
        SortedSet<string>? selected = null;
        foreach (var name in dnsNames)
        {
            // "*.ornek.example" joker adı: kayıtlı alan adı joker olmayan kısımdan çıkar.
            var host = name.StartsWith("*.", StringComparison.Ordinal) ? name[2..] : name;
            var normalized = _normalizer.Normalize(host);
            if (normalized.Value is not { } domain)
            {
                continue;
            }

            if (IsSelected(_salt, domain.RegistrableDomain, Threshold))
            {
                (selected ??= new SortedSet<string>(StringComparer.Ordinal)).Add(domain.RegistrableDomain);
            }
        }

        return selected is null ? [] : [.. selected];
    }
}
