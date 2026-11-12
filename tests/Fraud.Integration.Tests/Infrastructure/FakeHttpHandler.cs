using System.Net;
using System.Text;

namespace Fraud.Integration.Tests.Infrastructure;

/// <summary>
/// Sıradaki yanıtı döndüren sahte HTTP işleyicisi. Ağa hiç çıkmaz; gelen istekleri kaydeder.
/// Sırada yanıt kalmamışsa test başarısız olur (beklenmeyen istek).
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<Uri> Requests { get; } = [];

    public List<string> UserAgents { get; } = [];

    public List<string> Accepts { get; } = [];

    /// <summary>Her isteğin HTTP sürümü ve sürüm politikası.</summary>
    public List<(Version Version, HttpVersionPolicy Policy)> Versions { get; } = [];

    public FakeHttpHandler Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responses.Enqueue(responder);
        return this;
    }

    public FakeHttpHandler Enqueue(Func<HttpResponseMessage> responder) => Enqueue((_, _) => Task.FromResult(responder()));

    public FakeHttpHandler EnqueueText(string body, string mediaType = "application/json", HttpStatusCode status = HttpStatusCode.OK) =>
        Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });

    public FakeHttpHandler EnqueueStatus(HttpStatusCode status, Action<HttpResponseMessage>? configure = null) =>
        Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };
            configure?.Invoke(response);
            return response;
        });

    public FakeHttpHandler EnqueueRedirect(string location, HttpStatusCode status = HttpStatusCode.Found) =>
        Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.TryAddWithoutValidation("Location", location);
            return response;
        });

    /// <summary>İptal edilene kadar yanıt vermeyen sunucu.</summary>
    public FakeHttpHandler EnqueueHang() =>
        Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("ulaşılmaz");
        });

    public FakeHttpHandler EnqueueNetworkError() =>
        Enqueue((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("bağlantı reddedildi (sahte)")));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        UserAgents.Add(request.Headers.UserAgent.ToString());
        Accepts.Add(request.Headers.Accept.ToString());
        Versions.Add((request.Version, request.VersionPolicy));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("Beklenmeyen istek: " + request.RequestUri);
        }

        return _responses.Dequeue()(request, cancellationToken);
    }
}
