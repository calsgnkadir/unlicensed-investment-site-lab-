namespace Fraud.Collectors.Http;

/// <summary>
/// Her HTTP isteğinden önce <see cref="RequestPacer"/> sırasını bekler.
/// </summary>
/// <remarks>
/// Güvenli çekici yönlendirmeleri kendisi izler; bir çekme işi birden fazla istek olabilir. Hız sınırı
/// çekme işine değil buraya, isteğin kendisine konur; böylece yönlendirme adımları da sınırın içinde kalır.
/// </remarks>
public sealed class PacingHandler(RequestPacer pacer) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await pacer.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
