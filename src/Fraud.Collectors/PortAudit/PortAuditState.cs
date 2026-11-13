using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fraud.Collectors.PortAudit;

/// <summary>Durum dosyasındaki bir bulgu: protokol, port, (beklenmeyende) kapsam. Adres ve süreç adı yok.</summary>
public sealed record PortAuditFinding(ListenerProtocol Protocol, int Port, BindScope? Scope);

/// <summary>
/// Bir denetimin metriğe dönüşecek özeti. port-audit (host ağı) bunu dosyaya yazar, port-audit-exporter (iç ağ) okur.
/// Böylece host ağında hiçbir dinleyen soket açılmaz (THREAT_MODEL M1).
/// </summary>
public sealed record PortAuditSnapshot
{
    /// <summary>Son başarılı denetimin zamanı. Hiç başarılı denetim yoksa boş.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    public int TcpListeners { get; init; }

    public int UdpListeners { get; init; }

    public int MalformedLines { get; init; }

    public int ExpectedEntries { get; init; }

    public IReadOnlyList<PortAuditFinding> Unexpected { get; init; } = [];

    public IReadOnlyList<PortAuditFinding> Missing { get; init; } = [];

    public long? DiskAvailable { get; init; }

    public long? DiskTotal { get; init; }

    /// <summary>Süreç açıldığından beri tamamlanamayan denetim sayısı.</summary>
    public long Errors { get; init; }

    /// <summary>Denetim sonucundan özet; hata sayısı ve disk ölçümü önceki özetten taşınır.</summary>
    public static PortAuditSnapshot From(PortAuditResult result, int expectedEntries, DateTimeOffset completedAt, PortAuditSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new PortAuditSnapshot
        {
            CompletedAt = completedAt,
            TcpListeners = result.Listeners.Count(l => l.Protocol == ListenerProtocol.Tcp),
            UdpListeners = result.Listeners.Count(l => l.Protocol == ListenerProtocol.Udp),
            MalformedLines = result.MalformedLines,
            ExpectedEntries = expectedEntries,
            Unexpected = [.. result.Unexpected.Select(u => new PortAuditFinding(u.Protocol, u.Port, u.Scope))],
            Missing = [.. result.Missing.Select(m => new PortAuditFinding(m.Protocol, m.FromPort, null))],
            DiskAvailable = previous?.DiskAvailable,
            DiskTotal = previous?.DiskTotal,
            Errors = previous?.Errors ?? 0,
        };
    }

    /// <summary>
    /// Dosyadan gelen özeti sınırlar içine alır: negatif sayı sıfır olur, port 0-65535 dışı ve tanımsız enum atılır,
    /// liste en fazla <see cref="MaxFindings"/> kayıt. Dosya bizim yazdığımız dosyadır ama okuyan süreç ona körü körüne
    /// güvenmez (birim başka bir konteynerden bozulabilir).
    /// </summary>
    public PortAuditSnapshot Sanitized() => this with
    {
        TcpListeners = Math.Max(0, TcpListeners),
        UdpListeners = Math.Max(0, UdpListeners),
        MalformedLines = Math.Max(0, MalformedLines),
        ExpectedEntries = Math.Max(0, ExpectedEntries),
        Unexpected = Clean(Unexpected, requireScope: true),
        Missing = Clean(Missing, requireScope: false),
        DiskAvailable = DiskAvailable is >= 0 ? DiskAvailable : null,
        DiskTotal = DiskTotal is >= 0 ? DiskTotal : null,
        Errors = Math.Max(0, Errors),
    };

    /// <summary>Bir listede tutulan en fazla bulgu (kardinalite sınırı).</summary>
    public const int MaxFindings = 256;

    private static List<PortAuditFinding> Clean(IReadOnlyList<PortAuditFinding>? findings, bool requireScope) =>
    [
        .. (findings ?? [])
            .Where(f => f is not null
                        && Enum.IsDefined(f.Protocol)
                        && f.Port is >= 0 and <= 65535
                        && (requireScope ? f.Scope is { } s && Enum.IsDefined(s) : f.Scope is null))
            .Distinct()
            .Take(MaxFindings),
    ];
}

/// <summary>Durum dosyasını atomik yazar ve sınırlı boyutla okur.</summary>
public static class PortAuditStateFile
{
    /// <summary>Okunan dosyanın en büyük boyutu.</summary>
    public const int MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        MaxDepth = 8,
    };

    /// <summary>
    /// Önce aynı klasörde geçici dosyaya yazar, sonra yerine taşır. Okuyan taraf hiçbir zaman yarım dosya görmez
    /// (aynı dosya sisteminde yeniden adlandırma atomiktir).
    /// </summary>
    public static void Write(string path, PortAuditSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(snapshot, Json));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>İzin verilen saat farkı: Bundan daha ileri bir <see cref="PortAuditSnapshot.CompletedAt"/> reddedilir.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>Dosya yoksa <c>null</c>. Bozuk veya çok büyük dosyada <see cref="InvalidDataException"/>.</summary>
    public static PortAuditSnapshot? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = ReadBounded(stream);
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<PortAuditSnapshot>(bytes, Json)
                ?? throw new InvalidDataException("Port denetimi durum dosyası boş.");
            return snapshot.Sanitized();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Port denetimi durum dosyası okunamadı.", ex);
        }
    }

    /// <summary>
    /// <see cref="Read(string)"/> ve ek olarak zaman denetimi: <see cref="PortAuditSnapshot.CompletedAt"/>
    /// <paramref name="now"/>'dan <see cref="MaxClockSkew"/>'ten fazla ilerideyse dosya reddedilir. Kırpılmaz: Kırpılsaydı
    /// ileri tarihli bir dosya her okumada "şimdi" görünür ve PortAuditStale hiç ateşlenmezdi.
    /// </summary>
    public static PortAuditSnapshot? Read(string path, DateTimeOffset now)
    {
        var snapshot = Read(path);
        if (snapshot?.CompletedAt is { } completed && completed > now + MaxClockSkew)
        {
            throw new InvalidDataException("Port denetimi durum dosyasındaki zaman gelecekte.");
        }

        return snapshot;
    }

    /// <summary>
    /// Uzunluk dosya boyutuna değil okunan bayta göre sınırlanır: Dosya okunurken büyüse ya da boyutu yanlış raporlansa
    /// bile en fazla <see cref="MaxBytes"/> + 1 bayt okunur.
    /// </summary>
    private static byte[] ReadBounded(Stream stream)
    {
        var buffer = new byte[MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        if (total > MaxBytes)
        {
            throw new InvalidDataException("Port denetimi durum dosyası boyut sınırını aştı.");
        }

        return buffer[..total];
    }
}
