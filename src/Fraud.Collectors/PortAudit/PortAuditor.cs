using System.Net.Sockets;
using System.Text;

namespace Fraud.Collectors.PortAudit;

/// <summary><c>PortAudit</c> bölümü.</summary>
public sealed class PortAuditOptions
{
    public const string SectionName = "PortAudit";

    /// <summary>
    /// Soket tablolarının klasörü. Konteyner <c>network_mode: host</c> ile çalışınca <c>/proc/net</c> host'un ağ
    /// ad alanını gösterir (bu dosyalar okuyan sürecin ağ ad alanını yansıtır). Testte örnek dosya klasörü verilir.
    /// </summary>
    public string ProcNetDirectory { get; set; } = "/proc/net";

    /// <summary>Beklenen liste dosyası (bkz. <see cref="ExpectedListeners"/>). Linux kurulumunda salt okunur bağlanır.</summary>
    public string ExpectedFile { get; set; } = "/etc/fraud-lab/port-audit.expected";

    /// <summary>İki denetim arasındaki süre.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Bir soket tablosu dosyasının en büyük boyutu. Aşılırsa denetim hata sayılır (sessizce kesilmez).</summary>
    public int MaxFileBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Boş alanı ölçülen yol. Konteynerde <c>/</c>, Docker'ın veri klasörünün (veritabanı birimi de orada) bulunduğu
    /// diski gösterir: Konteynerin kök dosya sistemi o diskteki bir overlay katmanıdır. Boşsa ölçülmez.
    /// </summary>
    public string? DiskPath { get; set; } = "/";

    /// <summary>
    /// Sonucun yazıldığı dosya (THREAT_MODEL M1). port-audit host ağında HTTP ucu açmaz; sonucu paylaşılan Docker
    /// birimindeki bu dosyaya yazar, <c>port-audit-exporter</c> (iç ağda) okuyup Prometheus'a sunar. Boşsa yazılmaz.
    /// </summary>
    public string? StateFile { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProcNetDirectory) || string.IsNullOrWhiteSpace(ExpectedFile))
        {
            throw new InvalidOperationException("PortAudit:ProcNetDirectory ve PortAudit:ExpectedFile boş olamaz.");
        }

        if (Interval < TimeSpan.FromSeconds(5) || Interval > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("PortAudit:Interval 5 saniye ile 1 saat arasında olmalı.");
        }

        if (MaxFileBytes is < 4096 or > 64 * 1024 * 1024)
        {
            throw new InvalidOperationException("PortAudit:MaxFileBytes 4 KB ile 64 MB arasında olmalı.");
        }
    }
}

/// <summary>
/// Kendi makinemizin port denetimi (Kural 2: port denetimi yalnızca kendi makinelerimizde). Ağa hiçbir şey göndermez;
/// çekirdeğin soket tablolarını okur ve beklenen listeyle karşılaştırır.
/// </summary>
public sealed class PortAuditor(PortAuditOptions options)
{
    private static readonly (string File, ListenerProtocol Protocol, AddressFamily Family, bool Optional)[] Tables =
    [
        ("tcp", ListenerProtocol.Tcp, AddressFamily.InterNetwork, false),
        ("tcp6", ListenerProtocol.Tcp, AddressFamily.InterNetworkV6, true),
        ("udp", ListenerProtocol.Udp, AddressFamily.InterNetwork, false),
        ("udp6", ListenerProtocol.Udp, AddressFamily.InterNetworkV6, true),
    ];

    /// <summary>Beklenen listeyi dosyadan okur.</summary>
    public IReadOnlyList<ExpectedListener> LoadExpected() => ExpectedListeners.Parse(File.ReadLines(options.ExpectedFile));

    /// <summary>
    /// Dört soket tablosunu okur. IPv6 kapalı makinede <c>tcp6</c>/<c>udp6</c> yoktur; bu hata sayılmaz.
    /// Diğer okuma hataları istisna olarak çıkar (denetim "temiz" diye yazılmaz).
    /// </summary>
    public (IReadOnlyList<Listener> Listeners, int Malformed) ReadListeners()
    {
        var listeners = new List<Listener>();
        var malformed = 0;
        foreach (var (file, protocol, family, optional) in Tables)
        {
            var path = Path.Combine(options.ProcNetDirectory, file);
            if (optional && !File.Exists(path))
            {
                continue;
            }

            var content = ReadBounded(path, options.MaxFileBytes);
            listeners.AddRange(ProcNetParser.Parse(content, protocol, family, out var bad));
            malformed += bad;
        }

        return (listeners, malformed);
    }

    /// <summary>Okur ve karşılaştırır.</summary>
    public PortAuditResult Run(IReadOnlyList<ExpectedListener> expected)
    {
        var (listeners, malformed) = ReadListeners();
        return ExpectedListeners.Compare(listeners, expected, malformed);
    }

    /// <summary>
    /// /proc dosyalarının boyutu 0 görünür (içerik okununca üretilir); bu yüzden boyut okurken sayılır.
    /// </summary>
    private static string ReadBounded(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException("Soket tablosu boyut sınırını aştı.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
