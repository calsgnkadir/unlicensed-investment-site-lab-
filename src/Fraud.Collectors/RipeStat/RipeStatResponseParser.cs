using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Fraud.Collectors.RipeStat;

/// <summary><c>network-info</c> cevabından alınan iki alan. Başka alan saklanmaz.</summary>
/// <param name="Asns">Öneki duyuran ASN'ler, küçükten büyüğe.</param>
/// <param name="Prefix">Önek, standart yazımla (ör. <c>192.0.2.0/24</c>). Duyurulmuyorsa boş.</param>
public sealed record RipeStatNetworkInfo(IReadOnlyList<long> Asns, string? Prefix)
{
    public bool IsEmpty => Asns.Count == 0 && Prefix is null;
}

/// <summary>
/// RIPEstat <c>network-info</c> JSON cevabını katı biçimde okur.
/// </summary>
/// <remarks>
/// Beklenen biçim (yalnızca kullandığımız alanlar):
/// <code>
/// {"status": "ok", "data": {"asns": ["64500"], "prefix": "192.0.2.0/24"}, ...}
/// </code>
/// Kurallar: <c>status</c> "ok" olmalı; ASN 1 ile 4294967295 arasında tam sayı (metin veya sayı) ve internette
/// duyurulamayacak ayrılmış bir ASN olmamalı; en fazla 32 ASN; önek geçerli bir CIDR olmalı, sorduğumuz IP ile aynı
/// ailede olmalı, IP'yi içermeli ve makul uzunlukta olmalı (IPv4 en az /8, IPv6 en az /16). Biri tutmazsa bütün cevap
/// geçersiz sayılır (<c>null</c>); kısmen doğru veri saklanmaz (THREAT_MODEL N7).
/// </remarks>
public static class RipeStatResponseParser
{
    private const int MaxAsns = 32;

    /// <summary>İnternette /8'den (IPv4) ve /16'dan (IPv6) kısa önek duyurulmaz; böyle bir önek bütün IP'leri tek "ağ" yapar.</summary>
    private const int MinPrefixLengthV4 = 8;

    private const int MinPrefixLengthV6 = 16;

    public static RipeStatNetworkInfo? Parse(byte[] body, IPAddress queried)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(queried);

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
                || status.GetString() != "ok"
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var asns = new SortedSet<long>();
            if (data.TryGetProperty("asns", out var asnArray))
            {
                if (asnArray.ValueKind != JsonValueKind.Array || asnArray.GetArrayLength() > MaxAsns)
                {
                    return null;
                }

                foreach (var item in asnArray.EnumerateArray())
                {
                    if (ReadAsn(item) is not { } asn)
                    {
                        return null;
                    }

                    asns.Add(asn);
                }
            }

            string? prefix = null;
            if (data.TryGetProperty("prefix", out var prefixElement) && prefixElement.ValueKind != JsonValueKind.Null)
            {
                if (prefixElement.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var text = prefixElement.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    prefix = NormalizePrefix(text, queried);
                    if (prefix is null)
                    {
                        return null;
                    }
                }
            }

            return new RipeStatNetworkInfo([.. asns], prefix);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? ReadAsn(JsonElement item)
    {
        string? text = item.ValueKind switch
        {
            JsonValueKind.String => item.GetString(),
            JsonValueKind.Number => item.GetRawText(),
            _ => null,
        };

        // Yalnızca ASCII rakam: "+1", "1e3", " 1" gibi yazımlar reddedilir.
        if (text is null || text.Length is 0 or > 10 || !text.All(char.IsAsciiDigit))
        {
            return null;
        }

        var value = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return value is >= 1 and <= uint.MaxValue && !IsReservedAsn(value) ? value : null;
    }

    /// <summary>
    /// İnternette köken olarak görünmemesi gereken ASN'ler:
    /// 23456 (AS_TRANS, RFC 6793), 64512-65534 ve 4200000000-4294967294 (özel kullanım, RFC 6996),
    /// 65535 ve 4294967295 (ayrılmış, RFC 7300). Belgeleme aralıkları (64496-64511 ve 65536-65551, RFC 5398)
    /// gerçek veride görülmez ve testlerde örnek olarak kullanıldığı için reddedilmez.
    /// </summary>
    public static bool IsReservedAsn(long asn) =>
        asn == 23456 || asn is >= 64512 and <= 65535 || asn >= 4200000000;

    /// <summary>"adres/uzunluk" biçimini doğrular ve sorulan IP'yi içerdiğini denetler.</summary>
    private static string? NormalizePrefix(string text, IPAddress queried)
    {
        if (text.Length > 64)
        {
            return null;
        }

        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == text.Length - 1)
        {
            return null;
        }

        var lengthText = text[(slash + 1)..];
        if (!IPAddress.TryParse(text[..slash], out var network) || lengthText.Length > 3 || !lengthText.All(char.IsAsciiDigit))
        {
            return null;
        }

        var length = int.Parse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture);
        var isV4 = network.AddressFamily == AddressFamily.InterNetwork;
        var (minLength, maxLength) = isV4 ? (MinPrefixLengthV4, 32) : (MinPrefixLengthV6, 128);
        if (network.AddressFamily != queried.AddressFamily || length < minLength || length > maxLength)
        {
            return null;
        }

        // Ağ adresinin önek dışındaki bitleri sıfır olmalı: "192.0.2.1/24" yazımı tutarsızdır, reddedilir.
        // (IPNetwork.TryParse bunu kendisi düzeltip kabul edebildiği için taban adres ayrıca karşılaştırılır.)
        if (!IPNetwork.TryParse(text, out var parsed) || !parsed.BaseAddress.Equals(network) || !parsed.Contains(queried))
        {
            return null;
        }

        return parsed.ToString();
    }
}
