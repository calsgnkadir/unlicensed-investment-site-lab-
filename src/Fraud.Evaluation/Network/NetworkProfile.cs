using System.Net;
using System.Net.Sockets;
using Fraud.Domain.Network;
using Fraud.Evaluation.Model;

namespace Fraud.Evaluation.Network;

/// <summary>
/// Bir birimin adres sorgusu (A/AAAA) sonucu. Sayı sırası önceliktir: Aynı gün birden çok cevap varsa en küçük sayı kazanır.
/// </summary>
/// <remarks>
/// Neden bu sıra: İki çözücüden biri gerçek bir cevap verdiyse ad "çalışıyor" demektir; öteki çözücünün hatası bunu
/// değiştirmez. Hata türleri en sonda: Hata "kayıt yok" ya da "ad yok" gibi yazılmaz, yalnızca başka hiçbir şey yoksa sayılır.
/// </remarks>
public enum DnsState
{
    /// <summary>NOERROR ve en az bir cevap, engel işareti yok.</summary>
    Resolves = 1,

    /// <summary>Cevapta bilinen bir engel sayfası IP'si var. Barındırma bilgisi değildir.</summary>
    BlockMarker = 2,

    /// <summary>NXDOMAIN: Çözücü "bu ad yok" dedi.</summary>
    NxDomain = 3,

    /// <summary>NOERROR ama cevap boş: Ad var, bu türde kaydı yok.</summary>
    NoData = 4,

    /// <summary>SERVFAIL: Çözücü cevabı bulamadı (çoğunlukla yetkili sunucu bozuk).</summary>
    ServerFailure = 5,

    /// <summary>Öteki hatalar (zaman aşımı, ağ hatası, bozuk paket ...).</summary>
    OtherError = 6,
}

/// <summary>Birimin bir IP'si, görünüm anında bilinenlerle.</summary>
/// <param name="Ip">IP metni. Yalnızca bellekte; hiçbir çıktıya yazılmaz.</param>
/// <param name="Block">IPv4 için /24, IPv6 için /48 blok anahtarı ("aynı ağ" kenarı için).</param>
/// <param name="CdnOrCloud"><c>null</c>: Görünüm anında bu IP için başarılı RIPEstat kaydı yok, ASN bilinmiyor.</param>
/// <param name="PortsListed"><c>null</c>: Görünüm anında InternetDB cevabı yok (ya da yalnızca hata var).</param>
public sealed record IpView(string Ip, string Block, IReadOnlyList<long> Asns, bool? CdnOrCloud, bool? PortsListed, IReadOnlyList<int> Ports);

/// <summary>Bir birimin görünüm anındaki ağ özeti.</summary>
/// <param name="AsOf">Görünüm anı. Bu andan sonra sorgulanmış hiçbir kayıt bu nesnede yok.</param>
/// <param name="Dns">Son ölçüm gününün adres sonucu. <c>null</c>: Görünüm anında adres sorgusu kaydı yok.</param>
public sealed record NetworkProfile(string SubjectId, DateTimeOffset AsOf, DnsState? Dns, IReadOnlyList<IpView> Ips, IReadOnlyList<string> NsProviders);

/// <summary>
/// Ağ kayıtlarını birime ve IP'ye göre dizinler, her birim için "o gün bilinen" ağ özetini çıkarır.
/// </summary>
/// <remarks>
/// <para><b>Görünüm anı kuralı (gelecek sızıntısı yok):</b> Görünüm anı, sinyallerle AYNI kuraldan gelir:
/// <c>AsOfView.Create(birim, referans − 1 tik)</c>. Yani referans gününden önceki son TR gününün son anı. DNS, RIPEstat ve
/// InternetDB kaydı yalnızca sorgu anı bu andan önceyse görünür (<see cref="KnownAtPolicy.Network"/>).</para>
/// <para><b>Hangi gün:</b> Bir birim her gün yeniden ölçülür. Görünüm anından önceki SON ölçüm günü kullanılır. Eski
/// günlerle birleştirmek, artık geçerli olmayan bir IP'yi bugünün IP'si gibi gösterirdi.</para>
/// <para><b>IP'nin ASN ve port kaydı:</b> Aynı IP için görünüm anından önceki en son RIPEstat ve InternetDB kaydı.</para>
/// </remarks>
public sealed class NetworkIndex
{
    private readonly Dictionary<string, List<DnsFact>> _dns;
    private readonly Dictionary<string, List<IpAsnFact>> _asn;
    private readonly Dictionary<string, List<PortFact>> _ports;

    public NetworkIndex(NetworkData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        _dns = data.Dns.GroupBy(f => f.SubjectId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        _asn = data.IpAsn.GroupBy(f => f.Ip, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        _ports = data.Ports.GroupBy(f => f.Ip, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    public NetworkData Data { get; }

    /// <summary>
    /// Birimin görünüm anı: sinyallerin son görünümüyle aynı (<see cref="AsOfView"/> ve <see cref="DayGrid"/>). Kural tek yerde
    /// kalsın diye burada yeniden yazılmaz, AsOfView'dan alınır.
    /// </summary>
    public static DateTimeOffset AsOf(Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var reference = subject.ReferenceTime ?? throw new ArgumentException("Referans zamanı olmayan birimin görünüm anı yok.", nameof(subject));
        return AsOfView.Create(subject, reference.AddTicks(-1), []).AsOf;
    }

    public NetworkProfile ProfileOf(Subject subject)
    {
        var asOf = AsOf(subject);
        var known = _dns.GetValueOrDefault(subject.Id, []).Where(f => f.QueriedAt <= asOf).ToList();
        if (known.Count == 0)
        {
            return new NetworkProfile(subject.Id, asOf, null, [], []);
        }

        // Son ölçüm günü (UTC günü, dns_snapshots.measured_on ile aynı).
        var lastDay = known.Max(f => DateOnly.FromDateTime(f.QueriedAt.UtcDateTime));
        var day = known.Where(f => DateOnly.FromDateTime(f.QueriedAt.UtcDateTime) == lastDay).ToList();

        var address = day.Where(f => f.QueryType is "A" or "AAAA").ToList();
        DnsState? state = address.Count == 0 ? null : address.Min(StateOf);
        var ips = address
            .Where(f => StateOf(f) == DnsState.Resolves)
            .SelectMany(f => f.Ips)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(ip => ViewOf(ip, asOf))
            .ToList();
        var ns = day
            .Where(f => f.QueryType == "NS" && StateOf(f) == DnsState.Resolves)
            .SelectMany(f => f.NsProviders)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new NetworkProfile(subject.Id, asOf, state, ips, ns);
    }

    /// <summary>Tek bir sorgunun sonucu. Saklanan sonuç türünden doğrudan çıkar; yorum yok.</summary>
    public static DnsState StateOf(DnsFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return fact.Outcome switch
        {
            DnsOutcome.Success => fact.BlockMarker ? DnsState.BlockMarker : DnsState.Resolves,
            DnsOutcome.NxDomain => DnsState.NxDomain,
            DnsOutcome.Empty => DnsState.NoData,
            _ => fact.ErrorKind == DnsErrorKind.ServerFailure ? DnsState.ServerFailure : DnsState.OtherError,
        };
    }

    /// <summary>
    /// "Aynı ağ" blok anahtarı: IPv4 için ilk 3 bayt (/24), IPv6 için ilk 6 bayt (/48). KONTROL_GRUBU_YONTEMI.md 7.5 madde 2.
    /// </summary>
    public static string BlockOf(string ip)
    {
        var address = IPAddress.Parse(ip);
        var bytes = address.GetAddressBytes();
        var length = address.AddressFamily == AddressFamily.InterNetwork ? 3 : 6;
        return (length == 3 ? "v4:" : "v6:") + Convert.ToHexStringLower(bytes.AsSpan(0, length));
    }

    private IpView ViewOf(string ip, DateTimeOffset asOf)
    {
        // Görünüm anından önceki EN SON kayıt. Sonraki kayıtlar hiç okunmaz.
        var asn = _asn.GetValueOrDefault(ip, []).Where(f => f.QueriedAt <= asOf).MaxBy(f => f.QueriedAt);
        var port = _ports.GetValueOrDefault(ip, []).Where(f => f.QueriedAt <= asOf).MaxBy(f => f.QueriedAt);
        return new IpView(ip, BlockOf(ip), asn?.Asns ?? [], asn?.CdnOrCloud, port?.Listed, port?.Ports ?? []);
    }
}
