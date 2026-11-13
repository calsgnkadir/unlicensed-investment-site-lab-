using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fraud.Collectors.InternetDb;

/// <summary>InternetDB cevabından saklanan alanlar.</summary>
/// <param name="SkippedEntries">Biçime uymadığı için atlanan etiket veya CPE sayısı.</param>
public sealed record InternetDbHost(IReadOnlyList<int> Ports, IReadOnlyList<string> Tags, IReadOnlyList<string> Cpes, int SkippedEntries);

/// <summary>
/// InternetDB JSON cevabını okur.
/// </summary>
/// <remarks>
/// <para>
/// 200 cevabı: <c>{"cpes": [...], "hostnames": [...], "ip": "...", "ports": [...], "tags": [...], "vulns": [...]}</c>.
/// <c>vulns</c> ve <c>hostnames</c> alanlarına HİÇ bakılmaz (okunmaz, saklanmaz): Proje zafiyet araştırması değil,
/// ve hostnames aynı IP'yi paylaşan üçüncü kişilerin alan adlarıdır (Kural 4).
/// </para>
/// <para>
/// 404 cevabı: <c>{"detail": "No information available"}</c>. Bu "veri yok" demektir ve Empty yazılır.
/// Başka gövdeli 404 hata sayılır.
/// </para>
/// </remarks>
public static partial class InternetDbResponseParser
{
    private const int MaxPorts = 1024;
    private const int MaxEntries = 128;

    /// <summary>200 gövdesini okur. Biçim bozuksa veya IP sorduğumuz IP değilse <c>null</c>.</summary>
    public static InternetDbHost? ParseHost(byte[] body, IPAddress queried)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(queried);

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Cevap başka bir IP için olamaz.
            if (!root.TryGetProperty("ip", out var ipElement) || ipElement.ValueKind != JsonValueKind.String
                || !IPAddress.TryParse(ipElement.GetString(), out var answeredIp) || !answeredIp.Equals(queried))
            {
                return null;
            }

            if (!root.TryGetProperty("ports", out var portsElement) || portsElement.ValueKind != JsonValueKind.Array
                || portsElement.GetArrayLength() > MaxPorts)
            {
                return null;
            }

            var ports = new SortedSet<int>();
            foreach (var p in portsElement.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out var port) || port is < 0 or > 65535)
                {
                    return null;
                }

                ports.Add(port);
            }

            var skipped = 0;
            var tags = ReadStrings(root, "tags", TagPattern(), ref skipped);
            var cpes = ReadStrings(root, "cpes", CpePattern(), ref skipped);
            if (tags is null || cpes is null)
            {
                return null;
            }

            return new InternetDbHost([.. ports], tags, cpes, skipped);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>404 gövdesi tam olarak "No information available" mı.</summary>
    public static bool IsNoInformation(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("detail", out var detail)
                   && detail.ValueKind == JsonValueKind.String
                   && detail.GetString() == "No information available";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Metin dizisini okur. Kalıba uymayan öğe atlanır ve sayılır (çekilen metin saklanmaz). Alan yoksa boş dizi;
    /// alan dizi değilse veya çok uzunsa <c>null</c> (bütün cevap geçersiz).
    /// </summary>
    private static List<string>? ReadStrings(JsonElement root, string property, Regex pattern, ref int skipped)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaxEntries)
        {
            return null;
        }

        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text && pattern.IsMatch(text))
            {
                values.Add(text);
            }
            else
            {
                skipped++;
            }
        }

        return [.. values];
    }

    /// <summary>Etiket: küçük harf, rakam ve tire (ör. <c>cdn</c>, <c>self-signed</c>).</summary>
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,31}\z")]
    private static partial Regex TagPattern();

    /// <summary>
    /// CPE 2.2 URI biçimi, ör. <c>cpe:/a:nginx:nginx:1.18.0</c>. Boşluk, tırnak, açılı ayraç yok. <c>%</c> de yok:
    /// Yüzde kodlama <c>%40</c> ile e-posta adresi (kişi verisi, Kural 5) taşıyabilir (THREAT_MODEL N5).
    /// </summary>
    [GeneratedRegex(@"\Acpe:/[aho](:[A-Za-z0-9._~+\-]*){0,8}\z")]
    private static partial Regex CpePattern();
}
