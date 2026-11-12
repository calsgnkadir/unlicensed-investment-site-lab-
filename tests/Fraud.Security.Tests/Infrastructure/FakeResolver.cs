using System.Net;
using Fraud.Infrastructure.Fetching;

namespace Fraud.Security.Tests.Infrastructure;

/// <summary>
/// Sahte DNS çözümleyici. Her çağrıda sıradaki cevabı döndürür; sıra biterse son cevabı tekrarlar.
/// DNS rebinding senaryosu için cevaplar çağrıdan çağrıya değiştirilebilir.
/// </summary>
/// <remarks>
/// Gerçek bağlantı kurulan testlerde bu sınıfa yalnızca özel veya ayrılmış adresler verilir;
/// böylece SafeConnector bağlantıyı reddeder ve hiçbir paket makineden çıkmaz.
/// </remarks>
public sealed class FakeResolver(params IPAddress[][] answers) : IHostAddressResolver
{
    private int _calls;

    public int Calls => _calls;

    public List<string> Hosts { get; } = [];

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Hosts.Add(host);
        if (answers.Length == 0)
        {
            return Task.FromResult(Array.Empty<IPAddress>());
        }

        var index = Math.Min(Interlocked.Increment(ref _calls) - 1, answers.Length - 1);
        return Task.FromResult(answers[index]);
    }

    /// <summary>Her çağrıda aynı adres kümesini döndürür.</summary>
    public static FakeResolver Always(params string[] addresses) =>
        new(addresses.Select(IPAddress.Parse).ToArray());

    /// <summary>Çağrı sırasına göre farklı tek adres döndürür (rebinding).</summary>
    public static FakeResolver Sequence(params string[] addressPerCall) =>
        new(addressPerCall.Select(a => new[] { IPAddress.Parse(a) }).ToArray());
}
