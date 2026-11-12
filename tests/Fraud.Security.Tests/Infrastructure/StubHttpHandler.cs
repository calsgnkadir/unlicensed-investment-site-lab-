using System.Net;
using System.Net.Http.Headers;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>
/// Sıradaki yanıtı döndüren sahte HTTP işleyicisi. Ağa hiç çıkmaz. Sırada yanıt kalmamışsa
/// beklenmeyen istek sayılır ve test başarısız olur.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<Uri> Requests { get; } = [];

    public StubHttpHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    public StubHttpHandler EnqueueBytes(byte[] body, string mediaType, string? contentEncoding = null, HttpStatusCode status = HttpStatusCode.OK) =>
        Enqueue(_ =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            if (contentEncoding is not null)
            {
                content.Headers.ContentEncoding.Add(contentEncoding);
            }

            return new HttpResponseMessage(status) { Content = content };
        });

    public StubHttpHandler EnqueueText(string body, string mediaType = "application/json") =>
        EnqueueBytes(System.Text.Encoding.UTF8.GetBytes(body), mediaType);

    /// <summary>Gövde akışı ve isteğe bağlı olarak yalan söyleyen Content-Length başlığı.</summary>
    public StubHttpHandler EnqueueStream(Stream body, string mediaType, long? declaredLength = null) =>
        Enqueue(_ =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            content.Headers.ContentLength = declaredLength;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

    public StubHttpHandler EnqueueRedirect(string location, HttpStatusCode status = HttpStatusCode.Found) =>
        Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.TryAddWithoutValidation("Location", location);
            return response;
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Beklenmeyen istek: " + request.RequestUri);
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
