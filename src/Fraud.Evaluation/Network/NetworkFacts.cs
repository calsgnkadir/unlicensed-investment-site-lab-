using Fraud.Domain.Network;

namespace Fraud.Evaluation.Network;

/// <summary>
/// Bir birim için tek bir DoH DNS sorgusunun özeti (Hafta 8). Alan adı yok, yalnızca opak birim kimliği.
/// </summary>
/// <param name="SubjectId">Birimin opak kimliği.</param>
/// <param name="QueriedAt">Sorgu anı. DNS'te "bilinme anı" budur (<see cref="Model.KnownAtPolicy.Network"/>).</param>
/// <param name="QueryType">Sorulan tür: <c>A</c>, <c>AAAA</c> ya da <c>NS</c>.</param>
/// <param name="Ips">A/AAAA cevabındaki herkese açık IP'ler, tek yazıma indirilmiş. Süzmeyi yükleyici yapar. Yalnızca bellekte durur.</param>
/// <param name="NsProviders">
/// NS cevabındaki sunucuların kayıtlı alan adı (ör. <c>ns1.saglayici.example</c> → <c>saglayici.example</c>). Hiçbir çıktıya
/// yazılmaz: NS sunucusu sitenin kendi adının altında olabilir, yani sağlayıcı adı basmak site adını basmak olabilirdi.
/// </param>
public sealed record DnsFact(
    string SubjectId,
    DateTimeOffset QueriedAt,
    string QueryType,
    DnsOutcome Outcome,
    DnsErrorKind ErrorKind,
    bool BlockMarker,
    IReadOnlyList<string> Ips,
    IReadOnlyList<string> NsProviders);

/// <summary>RIPEstat'tan BAŞARILI bir IP → ASN kaydı. Hata ve "duyurulmuyor" satırları buraya gelmez.</summary>
/// <param name="CdnOrCloud">
/// IP'nin ASN'i Hafta 4'teki CDN/bulut listesinde (<c>RipeStatOptions.CdnAsns</c>). Yükleyici hem kayıttaki işarete hem
/// bugünkü listeye bakar; ikisinden biri "evet" diyorsa "evet" olur (tutucu taraf: yanlışlıkla bağ kurmaz).
/// </param>
public sealed record IpAsnFact(string Ip, DateTimeOffset QueriedAt, IReadOnlyList<long> Asns, bool CdnOrCloud);

/// <summary>InternetDB kaydı. Hata satırı buraya gelmez: Hata "kayıt yok" demek değildir.</summary>
/// <param name="Listed"><c>true</c>: 200, InternetDB'de kayıt var. <c>false</c>: 404, "bilgi yok".</param>
public sealed record PortFact(string Ip, DateTimeOffset QueriedAt, bool Listed, IReadOnlyList<int> Ports);

/// <summary>Ağ betimlemesinin bütün girdisi. Hata satırı sayıları yalnızca "veri sınırı" satırında basılır.</summary>
public sealed record NetworkData(
    IReadOnlyList<DnsFact> Dns,
    IReadOnlyList<IpAsnFact> IpAsn,
    IReadOnlyList<PortFact> Ports)
{
    /// <summary>DNS ölçüm konumları (ör. <c>tr-ev-ubuntu</c>). Bizim etiketimiz, alan adı değil.</summary>
    public IReadOnlyList<string> Locations { get; init; } = [];

    /// <summary>Kullanılmayan hata satırları: RIPEstat ve InternetDB. "Kayıt yok" sayılmadı, ayrıca sayılır.</summary>
    public int IpAsnErrors { get; init; }

    public int PortErrors { get; init; }
}
