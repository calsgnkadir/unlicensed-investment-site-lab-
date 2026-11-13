using System.Net;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Infrastructure.Fetching;

namespace Fraud.Collectors.InternetDb;

/// <summary>Bir IP için InternetDB sonucu. <see cref="Host"/> yalnızca Success'te doludur.</summary>
public sealed record InternetDbLookup(CollectionOutcome Outcome, CollectionErrorKind ErrorKind, InternetDbHost? Host, int? HttpStatus, int Attempts);

public interface IInternetDbClient
{
    Task<InternetDbLookup> LookupAsync(IPAddress ip, CancellationToken cancellationToken);
}

/// <summary>InternetDB istemcisi. İstek: <c>https://internetdb.shodan.io/IP</c>. Siteye değil InternetDB'ye gider.</summary>
public sealed class InternetDbClient(PoliteHttpClient http) : IInternetDbClient
{
    private static readonly IReadOnlySet<int> NotFound = new HashSet<int> { 404 };

    public static Uri BuildQueryUri(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        if (!IpAddressPolicy.IsPublic(ip))
        {
            throw new ArgumentException("Yalnızca herkese açık IP sorulur.", nameof(ip));
        }

        return new Uri(InternetDbOptions.BaseUri, Uri.EscapeDataString(ip.ToString()));
    }

    public async Task<InternetDbLookup> LookupAsync(IPAddress ip, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(BuildQueryUri(ip), NotFound, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return new InternetDbLookup(CollectionOutcome.Error, response.ErrorKind, null, response.HttpStatus, response.Attempts);
        }

        var body = response.Body ?? [];
        if (response.HttpStatus == 404)
        {
            // Yalnızca belgelenen gövdeyle gelen 404 "veri yok" sayılır; başka 404 (ör. yanlış adres) hatadır.
            return InternetDbResponseParser.IsNoInformation(body)
                ? new InternetDbLookup(CollectionOutcome.Empty, CollectionErrorKind.None, null, 404, response.Attempts)
                : new InternetDbLookup(CollectionOutcome.Error, CollectionErrorKind.ClientError, null, 404, response.Attempts);
        }

        var host = InternetDbResponseParser.ParseHost(body, ip);
        return host is null
            ? new InternetDbLookup(CollectionOutcome.Error, CollectionErrorKind.InvalidResponse, null, response.HttpStatus, response.Attempts)
            : new InternetDbLookup(CollectionOutcome.Success, CollectionErrorKind.None, host, response.HttpStatus, response.Attempts);
    }
}
