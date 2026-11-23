using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Fraud.Domain.Control;

namespace Fraud.Collectors.Control;

/// <summary>Donmuş G2 sorgu dosyası: metin, SHA-256 ve tohum.</summary>
/// <param name="Text">Gönderilen sorgu: dosyanın tamamı (yorumlar dahil).</param>
/// <param name="Sha256">Dosya baytlarının SHA-256'sı. Deneme kaydına ve manifeste yazılır.</param>
/// <param name="Seed">İlk satırdaki <c>#tohum:</c> değeri. Seçim sırası bununla kurulur.</param>
public sealed partial record G2WikidataQuery(string Text, string Sha256, string Seed)
{
    public const string RelativePath = "docs/evaluation/g2_wikidata_query.rq";

    /// <summary>En büyük sorgu dosyası. GET adresine girdiği için küçük tutulur.</summary>
    public const int MaxBytes = 8 * 1024;

    /// <summary>
    /// İlk satır <c>#tohum: kontrol-g2-wikidata-yyyy-MM-dd</c> olmalı. Tohum dosyanın içinde durduğu için SHA-256 onu da kapsar:
    /// Sorgu ya da tohum değişirse özet değişir ve eski cevap yeni seçime kullanılamaz.
    /// </summary>
    public static G2WikidataQuery? Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is 0 or > MaxBytes)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var firstLine = text.Split('\n')[0].TrimEnd('\r');
        var match = SeedLine().Match(firstLine);
        if (!match.Success || !text.Contains("SELECT", StringComparison.Ordinal))
        {
            return null;
        }

        return new G2WikidataQuery(text, ControlCommands.Sha(bytes), match.Groups[1].Value);
    }

    [GeneratedRegex(@"^#tohum: (kontrol-g2-wikidata-\d{4}-\d{2}-\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex SeedLine();
}

/// <summary>Ayrıştırılmış bir sonuç satırı: resmi web sitesi adresi ve Türkiye etiketi. Wikidata öğe kimliği saklanmaz.</summary>
public sealed record WikidataSite(string Url, bool TurkeyLinked);

/// <summary>SPARQL JSON cevabının ayrıştırma sonucu. Cevap bütünüyle geçersizse <see cref="WikidataSparqlParser.Parse"/> <c>null</c> döner.</summary>
public sealed record WikidataParseResult(IReadOnlyList<WikidataSite> Sites, int Rows, int InvalidRows, int DistinctItems);

/// <summary>
/// SPARQL sonuç JSON'u (<c>application/sparql-results+json</c>) ayrıştırıcısı. Çekilen içerik GÜVENİLMEZDİR (Kural 10): Boyut,
/// derinlik ve satır sayısı sınırlıdır; beklenen alanlar dışındaki her şey yok sayılır; geçersiz satır sayılır ama kullanılmaz.
/// </summary>
public static partial class WikidataSparqlParser
{
    /// <summary>Ham cevabın en büyük boyutu (indirme sınırıyla aynı). Beklenen boyut birkaç yüz KB.</summary>
    public const long MaxBytes = 16L * 1024 * 1024;

    /// <summary>En çok satır. Beklenen 200–1.500 (plan 2.5, varsayım). Fazlası sorgunun ya da cevabın bozuk olduğunu gösterir.</summary>
    public const int MaxRows = 100_000;

    /// <summary>Tek bir adresin en büyük uzunluğu.</summary>
    public const int MaxUrlLength = 2048;

    public static WikidataParseResult? Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxBytes)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Object
                || !results.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array
                || bindings.GetArrayLength() > MaxRows)
            {
                return null;
            }

            var sites = new List<WikidataSite>();
            var items = new HashSet<string>(StringComparer.Ordinal);
            int rows = 0, invalid = 0;
            foreach (var row in bindings.EnumerateArray())
            {
                rows++;
                if (row.ValueKind != JsonValueKind.Object
                    || Value(row, "item", "uri") is not { } item || !ItemUri().IsMatch(item)
                    || Value(row, "site", "uri") is not { Length: <= MaxUrlLength } site
                    || Value(row, "tr", "literal") is not { } tr || tr is not ("true" or "false"))
                {
                    invalid++;
                    continue;
                }

                items.Add(item);
                sites.Add(new WikidataSite(site, tr == "true"));
            }

            return new WikidataParseResult(sites, rows, invalid, items.Count);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Adresten ana bilgisayar: yalnızca mutlak <c>http</c>/<c>https</c>, kullanıcı bilgisi yok, varsayılan port, IP adresi değil
    /// (yalnızca DNS adı). Uymayan adres <c>null</c> (E00 sayılır). Örnek: <c>javascript:alert(1)</c>, <c>https://1.2.3.4/</c>,
    /// <c>https://kisi:sifre@ornek.example/</c> → <c>null</c>.
    /// </summary>
    public static string? HostOf(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Length > MaxUrlLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || !uri.IsDefaultPort
            || uri.HostNameType != UriHostNameType.Dns || IPAddress.TryParse(uri.Host, out _))
        {
            return null;
        }

        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    }

    /// <summary><c>row[name]</c> beklenen türdeyse değeri.</summary>
    private static string? Value(JsonElement row, string name, string type) =>
        row.TryGetProperty(name, out var cell) && cell.ValueKind == JsonValueKind.Object
        && cell.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == type
        && cell.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    [GeneratedRegex(@"^http://www\.wikidata\.org/entity/Q[1-9][0-9]{0,11}$", RegexOptions.CultureInvariant)]
    private static partial Regex ItemUri();
}

/// <summary>Wikidata sorgu hizmetine tek istek. Testlerde sahtesi verilir; testten gerçek ağa çıkılmaz.</summary>
public interface IWikidataSparqlClient
{
    /// <summary>İstekte gönderilen User-Agent (deneme kaydına yazılır).</summary>
    string UserAgent { get; }

    Task<HttpFetchResult> QueryAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// Gerçek istemci: yalnızca <c>query.wikidata.org:443</c> (bağlantı anında denetim, <see cref="AllowedHostConnector"/>),
/// yönlendirme izlenmez, gövde <see cref="WikidataSparqlParser.MaxBytes"/> ile sınırlı, 90 saniye süre sınırı (hizmetin kendi
/// sınırı 60 sn). Otomatik yeniden deneme YOK: Her istek bir deneme olarak kayda geçer.
/// </summary>
public sealed class WikidataSparqlClient : IWikidataSparqlClient, IDisposable
{
    public const string Host = "query.wikidata.org";

    private readonly PoliteHttpClient _client;
    private readonly HttpClient _http;

    public WikidataSparqlClient(string contactUrl, HttpMessageHandler? handler = null)
    {
        if (!ControlSourcesFile.IsContactUrl(contactUrl))
        {
            throw new ArgumentException("İletişim adresi https://github.com/<sahip>/<depo> biçiminde olmalı.", nameof(contactUrl));
        }

        // Wikimedia User-Agent politikası: istemci adı, iletişim bilgisi, kütüphane. İletişim = projenin herkese açık deposu.
        UserAgent = "FraudLabG2/0.1 (" + contactUrl + ") dotnet/10";
        var options = new ControlSourceOptions(Host, TimeSpan.FromSeconds(60), WikidataSparqlParser.MaxBytes)
        {
            MaxRetryAttempts = 0,
            AttemptTimeout = TimeSpan.FromSeconds(90),
        };
        (_client, _http) = ControlHttp.Create(options, UserAgent, handler);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json");
    }

    public string UserAgent { get; }

    public Task<HttpFetchResult> QueryAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _client.GetAsync(new Uri("https://" + Host + "/sparql?query=" + Uri.EscapeDataString(query)), cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// G2'nin Wikidata ile kurulması (B seçeneği, Değişiklik kaydı 2026-11-21): <c>control-fetch-g2-wikidata</c> tek sorgu atar,
/// <c>control-build-g2-wikidata --input</c> ağa çıkmadan seçer. Ekrana yalnızca sayı ve özet (Kural 4).
/// </summary>
public static class G2WikidataCommands
{
    /// <summary>En çok deneme (istek) sayısı. Başarılı bir cevaptan sonra yeni istek atılmaz.</summary>
    public const int MaxAttempts = 3;

    /// <summary>İki deneme arasında en az bekleme. Önceki deneme 429 aldıysa <see cref="RateLimitedGap"/>.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(60);

    /// <summary>429 sonrası bekleme. Retry-After değeri kayda geçmediği için tutucu bir üst değer (PoliteHttpClient en çok 5 dk kabul eder).</summary>
    public static readonly TimeSpan RateLimitedGap = TimeSpan.FromMinutes(5);

    /// <summary>100'den az üye kalırsa grup kurulur ama "yeterli örnek yok" (önceden kayıtlı kural).</summary>
    public const int MinimumForRates = 100;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public enum AttemptStatus
    {
        /// <summary>İstekten ÖNCE yazılır. Böyle kalan kayıt yarıda kesilmiş bir denemedir ve kullanılmış sayılır (C2).</summary>
        Started = 1,
        Succeeded = 2,
        Errored = 3,
    }

    /// <summary>Deneme kaydı. Alan adı yok; cevap yalnızca dosya adı ve SHA-256 ile anılır.</summary>
    public sealed record AttemptRecord(
        int Attempt, AttemptStatus Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string QuerySha256, string UserAgent,
        int? HttpStatus, CollectionErrorKind? ErrorKind, string? ResponseFile, string? ResponseSha256, long? ResponseBytes);

    /// <summary>Deneme kayıtlarının klasörü: data/private/control/g2-wikidata.</summary>
    public static string AttemptDirectory(ControlContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Path.Combine(ctx.ControlDirectory, "g2-wikidata");
    }

    /// <summary><c>control-fetch-g2-wikidata</c>: Donmuş sorguyu TEK istekle gönderir, ham cevabı ve deneme kaydını yazar.</summary>
    public static async Task<int> FetchAsync(ControlContext ctx, IWikidataSparqlClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(client);
        if (await QueryAsync(ctx, cancellationToken).ConfigureAwait(false) is not { } query
            || await ControlCommands.RefuseIfBuiltAsync(ctx, ControlGroup.G2, ControlBuild.OneShot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        // 1) Deneme hakkı ve bekleme. Hepsi istekten ÖNCE, yalnızca yerel kayıtlara bakarak.
        var previous = await ReadAttemptsAsync(ctx, cancellationToken).ConfigureAwait(false);
        var now = ctx.Time.GetUtcNow();
        if (previous.Any(a => a.Status == AttemptStatus.Succeeded))
        {
            await ctx.Output.WriteLineAsync("Başarılı bir cevap zaten var; yeni sorgu atılmaz (sonuç beğenilmedi diye yeniden sorulmaz).").ConfigureAwait(false);
            return 2;
        }

        if (previous.Count >= MaxAttempts)
        {
            await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"Deneme hakkı bitti ({previous.Count}/{MaxAttempts}). Sorgu atılmadı; Değişiklik kaydı olmadan yeni deneme açılmaz.")).ConfigureAwait(false);
            return 2;
        }

        if (previous.Count > 0)
        {
            var last = previous[^1];
            var gap = last.ErrorKind == CollectionErrorKind.RateLimited ? RateLimitedGap : MinGap;
            var wait = (last.FinishedAt ?? last.StartedAt) + gap - now;
            if (wait > TimeSpan.Zero)
            {
                await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"Önceki denemeden bu yana yeterli süre geçmedi; {Math.Ceiling(wait.TotalSeconds)} saniye sonra yeniden çalıştırın. Sorgu atılmadı."))
                    .ConfigureAwait(false);
                return 75;
            }
        }

        // 2) "Başladı" kaydı CreateNew ile, istekten ÖNCE (C2): Yarıda kesilen ya da hatalı biten deneme de sayılır. Aynı numarayı
        // iki süreç aynı anda almaya çalışırsa ikincisi dosya var hatası alır ve istek atmaz.
        var number = previous.Count + 1;
        var recordPath = Path.Combine(AttemptDirectory(ctx), "deneme-" + number.ToString(CultureInfo.InvariantCulture) + ".json");
        var started = new AttemptRecord(number, AttemptStatus.Started, now, null, query.Sha256, client.UserAgent, null, null, null, null, null);
        try
        {
            await ControlCommands.WriteNewAsync(recordPath, JsonSerializer.SerializeToUtf8Bytes(started, Json), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await ctx.Output.WriteLineAsync("Deneme kaydı oluşturulamadı (aynı anda başka bir süreç mi çalışıyor?). Sorgu atılmadı.").ConfigureAwait(false);
            return 75;
        }

        // 3) Tek istek.
        var result = await client.QueryAsync(query.Text, cancellationToken).ConfigureAwait(false);
        var finished = ctx.Time.GetUtcNow();
        string? responseFile = null;
        string? responseSha = null;
        var status = AttemptStatus.Errored;
        var errorKind = result.ErrorKind;
        if (result.IsSuccess && result.Body is { } body)
        {
            // Ham cevap her durumda saklanır (Radar denemesindeki gözlem kaybı tekrarlanmasın). Geçersizse deneme hatalı sayılır.
            responseFile = "wikidata-g2-" + number.ToString(CultureInfo.InvariantCulture) + "-"
                + finished.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".json";
            await ControlCommands.WriteNewAsync(Path.Combine(ctx.ControlDirectory, "src", responseFile), body, cancellationToken).ConfigureAwait(false);
            responseSha = ControlCommands.Sha(body);
            if (WikidataSparqlParser.Parse(body) is not null)
            {
                status = AttemptStatus.Succeeded;
            }
            else
            {
                errorKind = CollectionErrorKind.InvalidResponse;
            }
        }

        var final = started with
        {
            Status = status, FinishedAt = finished, HttpStatus = result.HttpStatus, ErrorKind = errorKind,
            ResponseFile = responseFile, ResponseSha256 = responseSha, ResponseBytes = result.Body?.LongLength,
        };
        await File.WriteAllBytesAsync(recordPath, JsonSerializer.SerializeToUtf8Bytes(final, Json), cancellationToken).ConfigureAwait(false);

        await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"Wikidata denemesi {number}/{MaxAttempts}: durum={status}, HTTP={result.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"}, hata={errorKind}, bayt={result.Body?.LongLength ?? 0}, cevap SHA-256={responseSha ?? "-"}, sorgu SHA-256={query.Sha256}, UTC={finished.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}."))
            .ConfigureAwait(false);
        if (status == AttemptStatus.Succeeded)
        {
            await ctx.Output.WriteLineAsync("Sonraki adım: control-build-g2-wikidata --input data/private/control/src/" + responseFile).ConfigureAwait(false);
            return 0;
        }

        return 2;
    }

    /// <summary>
    /// <c>control-build-g2-wikidata --input &lt;ham cevap&gt;</c>: Ağa çıkmaz. Girdi, donmuş sorguyla alınmış BAŞARILI bir denemenin
    /// kayıtlı cevabı olmalı (SHA-256 eşleşmesi): Elle düzenlenmiş ya da başka sorguyla alınmış bir dosya seçime giremez.
    /// </summary>
    public static async Task<int> BuildAsync(ControlContext ctx, string inputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (await QueryAsync(ctx, cancellationToken).ConfigureAwait(false) is not { } query
            || await ControlCommands.RefuseIfBuiltAsync(ctx, ControlGroup.G2, ControlBuild.OneShot, cancellationToken).ConfigureAwait(false))
        {
            return 2;
        }

        if (!File.Exists(inputPath) || new FileInfo(inputPath).Length > WikidataSparqlParser.MaxBytes)
        {
            await ctx.Output.WriteLineAsync("Girdi dosyası yok ya da boyut sınırını aşıyor.").ConfigureAwait(false);
            return 2;
        }

        var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var sha = ControlCommands.Sha(bytes);
        var attempts = await ReadAttemptsAsync(ctx, cancellationToken).ConfigureAwait(false);
        var attempt = attempts.FirstOrDefault(a => a.Status == AttemptStatus.Succeeded && a.ResponseSha256 == sha);
        if (attempt is null || attempt.QuerySha256 != query.Sha256)
        {
            await ctx.Output.WriteLineAsync("Girdi, donmuş sorguyla alınmış başarılı bir denemenin cevabı değil (SHA-256 eşleşmedi). Seçim yapılmadı.")
                .ConfigureAwait(false);
            return 2;
        }

        if (WikidataSparqlParser.Parse(bytes) is not { } parsed)
        {
            await ctx.Output.WriteLineAsync("Cevap SPARQL JSON olarak okunamadı. Seçim yapılmadı.").ConfigureAwait(false);
            return 2;
        }

        // Türkiye etiketi kayıtlı alan adı başına VEYA'lanır: Aynı ada giden satırlardan biri Türkiye'ye bağlıysa ad bağlıdır.
        var turkey = new Dictionary<string, bool>(StringComparer.Ordinal);
        var hosts = new List<string>();
        var badUrls = 0;
        foreach (var site in parsed.Sites)
        {
            if (WikidataSparqlParser.HostOf(site.Url) is not { } host)
            {
                badUrls++;
                continue;
            }

            hosts.Add(host);
            if (ctx.Full.Normalize(host).Value?.RegistrableDomain is { } registrable)
            {
                turkey[registrable] = turkey.GetValueOrDefault(registrable) || site.TurkeyLinked;
            }
        }

        var eligibility = await ControlCommands.EligibilityAsync(ctx, cancellationToken).ConfigureAwait(false);
        var eligible = hosts.Select(eligibility.Check).OfType<string>().ToList();
        var selected = ControlSeeds.Order(eligible, query.Seed)
            .Take(ControlCommands.G2Size)
            .Select(n => new SelectedMember(n, null, TurkeyLinked: turkey.GetValueOrDefault(n)))
            .ToList();

        var counts = ControlCommands.Merge(eligibility.Counts, new()
        {
            ["wikidata_satir"] = parsed.Rows,
            ["wikidata_gecersiz_satir"] = parsed.InvalidRows,
            ["wikidata_oge"] = parsed.DistinctItems,
            [EliminationCodes.E00] = badUrls, // http(s) olmayan, kullanıcı bilgili, IP ya da standart dışı portlu adres
            ["uygun"] = eligible.Count,
            ["secilen_turkiye_etiketli"] = selected.Count(s => s.TurkeyLinked == true),
        });
        if (selected.Count < MinimumForRates)
        {
            await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"UYARI: Yeterli örnek yok ({selected.Count} < {MinimumForRates}). Grup kuruluyor; raporda G2 için oran verilmez.")).ConfigureAwait(false);
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "wikidata",
            ["query_sha256"] = query.Sha256,
            ["response_sha256"] = sha,
            ["fetched_at_utc"] = (attempt.FinishedAt ?? attempt.StartedAt).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["attempt"] = attempt.Attempt.ToString(CultureInfo.InvariantCulture),
            ["enough_for_rates"] = selected.Count >= MinimumForRates ? "true" : "false",
        };
        return await ControlCommands.FinishAsync(
            ctx, ControlGroup.G2, ControlBuild.OneShot, query.Seed, "wikidata-" + sha[..16], selected, counts, cancellationToken, extra)
            .ConfigureAwait(false);
    }

    /// <summary>Donmuş sorgu: test verdiyse o, değilse git denetimli dosya (C1 ile aynı kural).</summary>
    private static async Task<G2WikidataQuery?> QueryAsync(ControlContext ctx, CancellationToken cancellationToken)
    {
        var bytes = ctx.PinnedG2Query ?? await PinnedFile.ReadAsync(
            ctx.Repo, G2WikidataQuery.RelativePath, "Sorguyu ve tohumu bu dosyaya yazıp sorgudan önce commit edin.", ctx.Output, cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        var query = G2WikidataQuery.Parse(bytes);
        await ctx.Output.WriteLineAsync(query is null
            ? "Komut durdu: " + G2WikidataQuery.RelativePath + " biçimi geçersiz (ilk satır '#tohum: kontrol-g2-wikidata-yyyy-MM-dd')."
            : "Donmuş sorgu: SHA-256 " + query.Sha256 + ", tohum " + query.Seed + ".").ConfigureAwait(false);
        return query;
    }

    /// <summary>Deneme kayıtları, numara sırasıyla. Okunamayan kayıt da bir deneme sayılır (hak geri gelmez).</summary>
    private static async Task<List<AttemptRecord>> ReadAttemptsAsync(ControlContext ctx, CancellationToken cancellationToken)
    {
        var dir = AttemptDirectory(ctx);
        var records = new List<AttemptRecord>();
        if (!Directory.Exists(dir))
        {
            return records;
        }

        foreach (var path in Directory.EnumerateFiles(dir, "deneme-*.json").Order(StringComparer.Ordinal))
        {
            AttemptRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize<AttemptRecord>(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), Json);
            }
            catch (JsonException)
            {
            }

            records.Add(record ?? new AttemptRecord(0, AttemptStatus.Errored, DateTimeOffset.MinValue, null, string.Empty, string.Empty,
                null, null, null, null, null));
        }

        return [.. records.OrderBy(r => r.StartedAt)];
    }
}
