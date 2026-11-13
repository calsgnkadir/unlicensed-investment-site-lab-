using System.Text.Json;
using Fraud.Domain.Network;

namespace Fraud.Infrastructure.Persistence;

/// <summary>
/// dns_snapshots.answers sütununun JSON biçimi: <c>[{"type":"A","value":"192.0.2.1","ttl":300}, ...]</c>.
/// Yazma (toplayıcıdaki SQL) ve okuma (EF Core dönüştürücüsü) aynı ayarı kullanır.
/// </summary>
public static class DnsAnswerJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // Okurken sınır: Kendi yazdığımız veri, ama veritabanı da güvenilmez sayılır.
        MaxDepth = 4,
    };

    public static string Serialize(IReadOnlyList<DnsAnswer> answers) => JsonSerializer.Serialize(answers, Options);

    public static IReadOnlyList<DnsAnswer> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<DnsAnswer>>(json, Options) ?? [];
}
