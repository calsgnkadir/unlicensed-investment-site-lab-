using System.Globalization;
using Fraud.Collectors.Collection;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;

namespace Fraud.Collectors.Wayback;

/// <summary>
/// Wayback CDX istemcisi. Yalnızca dizini okur; arşivlenmiş sayfa içeriği bu istemciyle indirilmez.
/// </summary>
public sealed class WaybackCdxClient(PoliteHttpClient http, WaybackCdxOptions options) : IWaybackCdxClient
{
    public Uri BuildQueryUri(string host, string? resumeKey)
    {
        if (!CertificateFieldSanitizer.IsDnsName(host) || host.StartsWith('*'))
        {
            throw new ArgumentException("Sorgu için normalleştirilmiş bir alan adı gerekir.", nameof(host));
        }

        var query = new List<string>
        {
            "url=" + Uri.EscapeDataString(host),
            "matchType=domain",
            "output=json",
            "fl=" + WaybackCdxOptions.Fields,
            "limit=" + options.PageSize.ToString(CultureInfo.InvariantCulture),
            "showResumeKey=true",
        };

        if (!string.IsNullOrWhiteSpace(options.Collapse))
        {
            query.Add("collapse=" + Uri.EscapeDataString(options.Collapse));
        }

        if (resumeKey is not null)
        {
            query.Add("resumeKey=" + Uri.EscapeDataString(resumeKey));
        }

        return new Uri(WaybackCdxOptions.BaseUri + "?" + string.Join('&', query));
    }

    public async Task<SourceResult<CdxCapture>> ListCapturesAsync(string host, CancellationToken cancellationToken)
    {
        var captures = new List<CdxCapture>();
        var skipped = 0;
        var attempts = 0;
        int? lastStatus = null;
        string? resumeKey = null;

        for (var page = 0; page < options.MaxPages; page++)
        {
            var response = await http.GetAsync(BuildQueryUri(host, resumeKey), cancellationToken).ConfigureAwait(false);
            attempts += response.Attempts;
            lastStatus = response.HttpStatus;

            if (!response.IsSuccess)
            {
                // Önceki sayfalar geçerli gözlemdir; saklanır ama iş "hata" olarak işaretlenir.
                return SourceResult.Failed<CdxCapture>(response.ErrorKind, lastStatus, attempts, captures, skipped);
            }

            var parsed = CdxResponseParser.Parse(response.Body ?? []);
            if (!parsed.IsValid)
            {
                return SourceResult.Failed<CdxCapture>(CollectionErrorKind.InvalidResponse, lastStatus, attempts, captures, skipped);
            }

            captures.AddRange(parsed.Captures);
            skipped += parsed.Skipped;
            resumeKey = parsed.ResumeKey;
            if (resumeKey is null)
            {
                return SourceResult.FromParsed<CdxCapture>(captures, skipped, lastStatus, attempts, truncated: false);
            }
        }

        // Sayfa sınırına ulaşıldı ve hâlâ devam anahtarı var: sonuç eksik.
        return SourceResult.FromParsed<CdxCapture>(captures, skipped, lastStatus, attempts, truncated: true);
    }
}
