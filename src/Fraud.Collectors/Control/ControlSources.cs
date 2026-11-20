using System.Text.Json;

namespace Fraud.Collectors.Control;

/// <summary>Seçimden ÖNCE sabitlenmiş kaynak listeleri (C1). Dosya: docs/evaluation/control_sources.json.</summary>
/// <param name="TrancoListId">Tranco kalıcı liste kimliği (ör. 5 karakter).</param>
/// <param name="TrancoDate">Listenin tarihi (yyyy-MM-dd); kayıt için, seçimi değiştirmez.</param>
public sealed record ControlSources(string TrancoListId, DateOnly TrancoDate);

/// <summary>
/// Sabitlenmiş kaynak dosyasının okunması. Neden (bulgu C1): Tranco her gün yeni bir liste yayımlar. Liste kimliği komut
/// satırından gelseydi, önkoşul denemesi tutmazsa ertesi günün listesiyle yeniden denenebilir, seçim tohumu da değiştirilebilirdi.
/// Kimlik, seçimden önce commit edilmiş bir dosyada durur; komut yalnızca onu kabul eder.
/// </summary>
/// <remarks>
/// Biçim: <c>{ "tranco": { "listId": "AB12C", "date": "2026-11-21" } }</c>. Dosya git'te izlenmiyorsa ya da çalışma ağacı
/// kirliyse (dosya commit'teki hâlinden farklı olabilir) komut durur.
/// </remarks>
public static class ControlSourcesFile
{
    public const string RelativePath = "docs/evaluation/control_sources.json";

    /// <summary>Dosyayı git denetimleriyle okur. Sorun varsa nedenini yazar ve <c>null</c> döner.</summary>
    public static async Task<ControlSources?> LoadAsync(Fraud.Collectors.Evaluation.RepoContext repo, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(output);
        var path = Path.Combine(repo.Root, RelativePath);
        string? problem = null;
        ControlSources? sources = null;
        if (!File.Exists(path))
        {
            problem = RelativePath + " yok. Tranco liste kimliğini ve tarihini bu dosyaya yazıp seçimden önce commit edin.";
        }
        else if (!repo.IsTracked(path))
        {
            problem = RelativePath + " git'te izlenmiyor; seçimden önce commit edilmeli.";
        }
        else if (!repo.IsClean())
        {
            problem = "Çalışma ağacı temiz değil; sabitlenmiş kaynak dosyası commit'teki hâli olmayabilir.";
        }
        else if ((sources = Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))) is null)
        {
            problem = RelativePath + " biçimi geçersiz (tranco.listId, tranco.date).";
        }

        if (problem is not null)
        {
            await output.WriteLineAsync("Komut durdu: " + problem).ConfigureAwait(false);
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

            return new ControlSources(id.GetString()!, day);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
