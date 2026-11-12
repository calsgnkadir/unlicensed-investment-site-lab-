using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

namespace Fraud.Collectors.CrtSh;

/// <summary>crt.sh JSON yanıtını ayrıştırma sonucu. <see cref="IsValid"/> yanlışsa yanıt bir hata sayfasıdır.</summary>
public sealed record CrtShParseResult(bool IsValid, IReadOnlyList<CrtShCertificate> Certificates, int Skipped)
{
    public static readonly CrtShParseResult Invalid = new(false, [], 0);
}

/// <summary>
/// crt.sh JSON çıktısını okur. Beklenen kök bir dizidir; her öğe bir sertifika ve eşleşen adlarıdır.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Kök dizi değilse (ör. HTML hata sayfası) yanıt geçersizdir; bu "sertifika yok" demek değildir.</item>
/// <item>Aynı crt.sh kimliği birden çok satırda gelebilir; adları birleştirilir, tek kayıt olur.</item>
/// <item>Zorunlu alanı eksik ya da tarihi okunamayan satır atlanır ve sayılır.</item>
/// <item>crt.sh tarihleri saat dilimi yazmadan UTC verir.</item>
/// </list>
/// </remarks>
public static class CrtShResponseParser
{
    public static CrtShParseResult Parse(ReadOnlyMemory<byte> json)
    {
        // Geçersiz UTF-8 baytı JsonDocument.Parse'tan geçer ama GetString'de istisna fırlatır; baştan reddedilir.
        if (!Utf8.IsValid(json.Span))
        {
            return CrtShParseResult.Invalid;
        }

        try
        {
            return ParseDocument(json);
        }
        catch (InvalidOperationException)
        {
            // Kaçış dizisiyle yazılmış yalnız vekil karakter (ör. \ud800) GetString'de istisna fırlatır.
            return CrtShParseResult.Invalid;
        }
    }

    private static CrtShParseResult ParseDocument(ReadOnlyMemory<byte> json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            return CrtShParseResult.Invalid;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return CrtShParseResult.Invalid;
            }

            var byId = new Dictionary<long, CrtShCertificate>();
            var skipped = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var certificate = ReadEntry(element);
                if (certificate is null)
                {
                    skipped++;
                    continue;
                }

                if (byId.TryGetValue(certificate.CrtShId, out var existing))
                {
                    var names = existing.Names.Union(certificate.Names, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    byId[certificate.CrtShId] = existing with { Names = names };
                }
                else
                {
                    byId.Add(certificate.CrtShId, certificate);
                }
            }

            return new CrtShParseResult(true, byId.Values.OrderBy(c => c.CrtShId).ToList(), skipped);
        }
    }

    private static CrtShCertificate? ReadEntry(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!e.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt64(out var id) || id <= 0)
        {
            return null;
        }

        var serial = GetString(e, "serial_number")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(serial) || serial.Length > 128)
        {
            return null;
        }

        var notBefore = GetDate(e, "not_before");
        var notAfter = GetDate(e, "not_after");
        if (notBefore is null || notAfter is null || notAfter < notBefore)
        {
            return null;
        }

        int? issuerCaId = e.TryGetProperty("issuer_ca_id", out var ca) && ca.ValueKind == JsonValueKind.Number && ca.TryGetInt32(out var caId)
            ? caId
            : null;

        return new CrtShCertificate(
            id,
            serial,
            issuerCaId,
            CertificateFieldSanitizer.SanitizeIssuer(GetString(e, "issuer_name")),
            CertificateFieldSanitizer.SanitizeName(GetString(e, "common_name")),
            CertificateFieldSanitizer.SanitizeNames(GetString(e, "name_value")),
            notBefore.Value,
            notAfter.Value,
            GetDate(e, "entry_timestamp"));
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement e, string name)
    {
        var text = GetString(e, name);
        if (text is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;
    }
}
