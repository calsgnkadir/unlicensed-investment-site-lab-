using System.Net;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Persistence;

namespace Fraud.Collectors.Network;

/// <summary>Tek bir IP sorgusunun sonucu (kaydedildikten sonra).</summary>
/// <param name="Stored">Tabloya yeni satır girdi ya da aynı günkü hatalı satır değiştirildi.</param>
public sealed record IpCollectResult(CollectionOutcome Outcome, CollectionErrorKind ErrorKind, int Attempts, bool Stored);

/// <summary>Alan adına değil IP'ye bağlı bir kaynak (RIPEstat, InternetDB). <see cref="IpCollectionRunner"/> çalıştırır.</summary>
public interface IIpSourceCollector
{
    CollectionSource Source { get; }

    /// <summary>Bir IP bu süre içinde hatasız sorulduysa yeniden sorulmaz (ikisi için de 7 gün).</summary>
    TimeSpan CacheTtl { get; }

    /// <summary>IP'lerin alınacağı DNS ölçümlerinin en eski zamanı için geriye bakış süresi.</summary>
    TimeSpan DnsLookback { get; }

    /// <summary><paramref name="since"/> sonrasında hatasız sorulmuş IP'ler.</summary>
    Task<HashSet<IPAddress>> RecentlyAnsweredAsync(FraudDbContext db, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// IP'yi sorgular ve sonucu tabloya yazar. Ağ ve biçim hataları sonuç olarak döner ve hata satırı olarak yazılır.
    /// Yalnızca dışarıdan iptal edilirse <see cref="OperationCanceledException"/> fırlatır (o zaman satır yazılmaz).
    /// </summary>
    Task<IpCollectResult> CollectAsync(FraudDbContext db, IPAddress ip, string location, CancellationToken cancellationToken);
}
