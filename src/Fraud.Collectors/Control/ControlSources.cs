using System.Text.Json;

namespace Fraud.Collectors.Control;

/// <summary>Seçimden ÖNCE sabitlenmiş kaynak listeleri (C1). Dosya: docs/evaluation/control_sources.json.</summary>
/// <param name="TrancoListId">Tranco kalıcı liste kimliği (ör. 5 karakter).</param>
/// <param name="TrancoDate">Listenin tarihi (yyyy-MM-dd); kayıt için, seçimi değiştirmez.</param>
/// <param name="WikidataContactUrl">G2 Wikidata sorgusunun User-Agent iletişim adresi: projenin herkese açık GitHub adresi
/// (Değişiklik kaydı 2026-11-21). Kişisel e-posta kabul edilmez. Yoksa <c>null</c>; Wikidata komutu o zaman durur.</param>
public sealed record ControlSources(string TrancoListId, DateOnly TrancoDate, string? WikidataContactUrl = null);

/// <summary>
/// Sabitlenmiş kaynak dosyasının okunması. Neden (bulgu C1): Tranco her gün yeni bir liste yayımlar. Liste kimliği komut
/// satırından gelseydi, önkoşul denemesi tutmazsa ertesi günün listesiyle yeniden denenebilir, seçim tohumu da değiştirilebilirdi.
/// Kimlik, seçimden önce commit edilmiş bir dosyada durur; komut yalnızca onu kabul eder.
/// </summary>
/// <remarks>
/// Biçim: <c>{ "tranco": { "listId": "AB12C", "date": "2026-11-21" }, "wikidata": { "contactUrl": "https://github.com/..." } }</c>.
/// <c>wikidata</c> bölümü isteğe bağlıdır. Dosya git'te izlenmiyorsa ya da çalışma ağacı kirliyse komut durur.
/// </remarks>
public static class ControlSourcesFile
{
    public const string RelativePath = "docs/evaluation/control_sources.json";

    /// <summary>Dosyayı git denetimleriyle okur. Sorun varsa nedenini yazar ve <c>null</c> döner.</summary>
    public static async Task<ControlSources?> LoadAsync(Fraud.Collectors.Evaluation.RepoContext repo, TextWriter output, CancellationToken cancellationToken)
    {
        var bytes = await PinnedFile.ReadAsync(
            repo, RelativePath, "Tranco liste kimliğini ve tarihini bu dosyaya yazıp seçimden önce commit edin.", output, cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        var sources = Parse(bytes);
        if (sources is null)
        {
            await output.WriteLineAsync("Komut durdu: " + RelativePath + " biçimi geçersiz (tranco.listId, tranco.date, wikidata.contactUrl).")
                .ConfigureAwait(false);
        }

        return sources;
    }

    public static ControlSources? Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tranco", out var tranco) || tranco.ValueKind != JsonValueKind.Object
                || !tranco.TryGetProperty("listId", out var id) || id.ValueKind != JsonValueKind.String || !TrancoApiParser.IsListId(id.GetString())
                || !tranco.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var day))
            {
                return null;
            }

            string? contact = null;
            if (doc.RootElement.TryGetProperty("wikidata", out var wikidata))
            {
                // Bölüm yazıldıysa geçerli olmalı: yanlış yazılmış bir adres sessizce "yok" sayılmasın.
                if (wikidata.ValueKind != JsonValueKind.Object || !wikidata.TryGetProperty("contactUrl", out var url)
                    || url.ValueKind != JsonValueKind.String || !IsContactUrl(url.GetString()))
                {
                    return null;
                }

                contact = url.GetString();
            }

            return new ControlSources(id.GetString()!, day, contact);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// İletişim adresi kuralı: <c>https://github.com/&lt;sahip&gt;/&lt;depo&gt;</c>, e-posta (<c>@</c>), sorgu, parça ve boşluk yok.
    /// User-Agent başlığına olduğu gibi girdiği için başlığı bozacak karakter de kabul edilmez.
    /// </summary>
    public static bool IsContactUrl(string? value) =>
        value is { Length: > 19 and <= 200 }
        && value.StartsWith("https://github.com/", StringComparison.Ordinal)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or ':')
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Host == "github.com" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsolutePath.Trim('/').Split('/').Length == 2;
}

/// <summary>
/// Seçimden önce commit edilmiş bir dosyanın okunması (C1 kuralı): dosya var, git'te izleniyor ve çalışma ağacı temiz.
/// Temiz ağaç şartı neden: Kirli ağaçta dosyanın diskteki hâli commit'teki hâli olmayabilir; "önceden kayıtlı" iddiası boşa düşer.
/// </summary>
public static class PinnedFile
{
    /// <summary>Sorun varsa nedeni yazar ve <c>null</c> döner.</summary>
    public static async Task<byte[]?> ReadAsync(
        Fraud.Collectors.Evaluation.RepoContext repo, string relativePath, string missingHint, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(output);
        var path = Path.Combine(repo.Root, relativePath);
        string? problem = null;
        if (!File.Exists(path))
        {
            problem = relativePath + " yok. " + missingHint;
        }
        else if (!repo.IsTracked(path))
        {
            problem = relativePath + " git'te izlenmiyor; seçimden önce commit edilmeli.";
        }
        else if (!repo.IsClean())
        {
            problem = "Çalışma ağacı temiz değil; sabitlenmiş dosya (" + relativePath + ") commit'teki hâli olmayabilir.";
        }

        if (problem is not null)
        {
            await output.WriteLineAsync("Komut durdu: " + problem).ConfigureAwait(false);
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
