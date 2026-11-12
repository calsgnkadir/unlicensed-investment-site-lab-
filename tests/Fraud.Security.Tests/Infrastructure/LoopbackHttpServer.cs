using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>
/// Yalnızca 127.0.0.1 üzerinde dinleyen ham HTTP/1.1 sunucusu. Her bağlantıya aynı yanıtı verir.
/// Gerçek <see cref="SocketsHttpHandler"/> davranışını (otomatik sıkıştırma açma gibi) dış ağa çıkmadan sınamak için.
/// </summary>
public sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;

    public LoopbackHttpServer(IReadOnlyList<(string Name, string Value)> headers, byte[] body)
    {
        _listener.Start();
        _serve = ServeAsync(headers, body, _stop.Token);
    }

    public Uri BaseUri => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

    public int Connections { get; private set; }

    private async Task ServeAsync(IReadOnlyList<(string Name, string Value)> headers, byte[] body, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(ct);
                Connections++;
                var stream = client.GetStream();

                // İstek başlıklarını boş satıra kadar oku; içerik önemsiz.
                var buffer = new byte[8192];
                var received = new StringBuilder();
                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var n = await stream.ReadAsync(buffer, ct);
                    if (n == 0)
                    {
                        break;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, n));
                }

                var head = new StringBuilder("HTTP/1.1 200 OK\r\n");
                foreach (var (name, value) in headers)
                {
                    head.Append(name).Append(": ").Append(value).Append("\r\n");
                }

                head.Append("Content-Length: ").Append(body.Length).Append("\r\nConnection: close\r\n\r\n");
                try
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct);
                    await stream.WriteAsync(body, ct);
                    await stream.FlushAsync(ct);
                }
                catch (IOException)
                {
                    // İstemci gövdeyi okumayı bırakıp bağlantıyı kapatabilir (boyut sınırı).
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _serve;
        _stop.Dispose();
    }
}
