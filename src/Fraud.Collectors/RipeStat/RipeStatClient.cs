using System.Net;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Fetching;

namespace Fraud.Collectors.RipeStat;

/// <summary>Bir IP için RIPEstat sonucu. <see cref="Info"/> yalnızca Success ve Empty'de doludur.</summary>
public sealed record RipeStatLookup(CollectionOutcome Outcome, CollectionErrorKind ErrorKind, RipeStatNetworkInfo? Info, int? HttpStatus, int Attempts);

public interface IRipeStatClient
{
    Task<RipeStatLookup> LookupAsync(IPAddress ip, CancellationToken cancellationToken);
}

/// <summary>
/// RIPEstat <c>network-info</c> istemcisi. İstek: <c>https://stat.ripe.net/data/network-info/data.json?resource=IP&amp;sourceapp=AD</c>.
/// Alan adı gönderilmez, yalnızca IP.
/// </summary>
public sealed class RipeStatClient(PoliteHttpClient http, RipeStatOptions options) : IRipeStatClient
{
    /// <summary>Sorgu adresini kurar. Herkese açık olmayan IP için hiç istek kurulmaz.</summary>
    public static Uri BuildQueryUri(IPAddress ip, string sourceApp)
    {
        ArgumentNullException.ThrowIfNull(ip);
        if (!IpAddressPolicy.IsPublic(ip))
        {
            throw new ArgumentException("Yalnızca herkese açık IP sorulur.", nameof(ip));
        }

        // IPAddress.ToString() yalnızca rakam, nokta, iki nokta ve a-f üretir; yine de kaçışlanır.
        var query = "?resource=" + Uri.EscapeDataString(ip.ToString())
                    + "&sourceapp=" + Uri.EscapeDataString(sourceApp);
        return new Uri(RipeStatOptions.BaseUri + query);
    }

    public async Task<RipeStatLookup> LookupAsync(IPAddress ip, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(BuildQueryUri(ip, options.SourceApp), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return new RipeStatLookup(CollectionOutcome.Error, response.ErrorKind, null, response.HttpStatus, response.Attempts);
        }

        var info = RipeStatResponseParser.Parse(response.Body ?? [], ip);
        if (info is null)
        {
            return new RipeStatLookup(CollectionOutcome.Error, CollectionErrorKind.InvalidResponse, null, response.HttpStatus, response.Attempts);
        }

        // Cevap geçerli ama ASN ve önek yok: IP şu an internete duyurulmuyor. "Hata" değil, "boş".
        var outcome = info.IsEmpty ? CollectionOutcome.Empty : CollectionOutcome.Success;
        return new RipeStatLookup(outcome, CollectionErrorKind.None, info, response.HttpStatus, response.Attempts);
    }
}
