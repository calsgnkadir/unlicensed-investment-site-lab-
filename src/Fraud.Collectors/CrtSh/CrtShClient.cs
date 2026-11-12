using Fraud.Collectors.Collection;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;

namespace Fraud.Collectors.CrtSh;

/// <summary>
/// crt.sh JSON istemcisi. Sorgu: <c>?q=%.ALANADI&amp;output=json&amp;deduplicate=Y</c>.
/// </summary>
/// <remarks>
/// <c>%.ALANADI</c> alt alan adlarını kapsar. <c>deduplicate=Y</c>, ön sertifika ile son sertifika çiftini
/// tek satıra indirir (topluluk kaynaklarına göre; resmi belgesi bulunamadı, DATA_SOURCES Bölüm 2).
/// Süresi dolmuş sertifikalar dışlanmaz: Karar öncesi tarihe bakılacağı için geçmiş gerekli.
/// </remarks>
public sealed class CrtShClient(PoliteHttpClient http) : ICrtShClient
{
    public static Uri BuildQueryUri(string host)
    {
        if (!CertificateFieldSanitizer.IsDnsName(host) || host.StartsWith('*'))
        {
            throw new ArgumentException("Sorgu için normalleştirilmiş bir alan adı gerekir.", nameof(host));
        }

        // Uri.EscapeDataString("%.ornek.example") => "%25.ornek.example"
        var query = "?q=" + Uri.EscapeDataString("%." + host) + "&output=json&deduplicate=Y";
        return new Uri(CrtShOptions.BaseUri, query);
    }

    public async Task<SourceResult<CrtShCertificate>> SearchAsync(string host, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(BuildQueryUri(host), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return SourceResult.Failed<CrtShCertificate>(response.ErrorKind, response.HttpStatus, response.Attempts);
        }

        var parsed = CrtShResponseParser.Parse(response.Body ?? []);
        if (!parsed.IsValid)
        {
            return SourceResult.Failed<CrtShCertificate>(CollectionErrorKind.InvalidResponse, response.HttpStatus, response.Attempts);
        }

        return SourceResult.FromParsed<CrtShCertificate>(
            parsed.Certificates, parsed.Skipped, response.HttpStatus, response.Attempts, truncated: false);
    }
}
