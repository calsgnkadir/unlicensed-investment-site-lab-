using System.Globalization;
using System.Text.Json;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Http;
using Fraud.Domain.Collection;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Control;

/// <summary>Bir G3a denemesinin sınıfı. Hata "sonuç yok" sayılmaz.</summary>
public enum G3aAttemptClass
{
    /// <summary>Tam cevap ve hedef ayda en az 50 kayıtlı alan adı.</summary>
    Passed = 1,

    /// <summary>Tam cevap ama hedef ayda 50'den az kayıtlı alan adı.</summary>
    TooFew = 2,

    /// <summary>Cevap kesik (yaklaşık 10.000 satır sınırı).</summary>
    Truncated = 3,

    Timeout = 4,
    RateLimited = 5,
    ServerError = 6,
    OtherError = 7,
}

/// <summary>Deneme kaydı (data/private/control/g3a-deneme.json). Alan adı yok; terim ve ay da yazılmaz, yalnızca tohum.</summary>
public sealed record G3aTrialResult(DateTimeOffset At, IReadOnlyList<G3aAttemptClass> Attempts, IReadOnlyList<int> MonthDomains, bool Passed)
{
    /// <summary>"basladi" (ilk sorgudan önce yazılır) ya da "bitti". "basladi" kalan kayıt da kullanılmış sayılır.</summary>
    public string Status { get; init; } = "bitti";
}

/// <summary>
/// G3a önkoşul denemesi (KONTROL_LISTESI_KURULUM.md 3.3; Değişiklik kaydı 2026-11-20 madde 5): Tek terim, tek ay, aynı
/// sorgu 3 kez. Başarı: 3 denemenin en az 2'si tam cevap ve hedef ayda <c>not_before</c>'u olan en az 50 kayıtlı alan adı.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Terim: baseline'ın 8 teriminden tohumlu sırayla ilki. Ay: ayar pozitiflerinin ilk sertifika aylarından tohumlu ilki.</item>
/// <item>İstek aralığı 15 sn, zaman aşımı 120 sn, tekrar deneme yok (her istek bir denemedir).</item>
/// <item>Saatin ilk 10 dakikasında başlamaz (topluluk gözlemi).</item>
/// <item>Bir kez yapılır: kayıt dosyası varsa yeniden çalışmaz (sonuca göre tekrar deneme yok).</item>
/// </list>
/// </remarks>
public static class G3aTrial
{
    public const int Attempts = 3;
    public const int MinDomains = 50;

    /// <summary>crt.sh'in kimlik aramasındaki üst sınır (operatör açıklaması): bu kadar satır gelirse cevap kesik sayılır.</summary>
    public const int TruncationRows = 9_900;

    public static readonly IReadOnlyList<string> BaselineTerms = Fraud.Domain.Signals.BaselineTerms.All;

    /// <summary>Üretim istemcisi: yalnızca crt.sh'e bağlanır, 15 sn aralık, 120 sn zaman aşımı, tekrar deneme yok.</summary>
    public static (PoliteHttpClient Client, HttpClient Http) CreateClient(string userAgent) =>
        ControlHttp.Create(new ControlSourceOptions("crt.sh", TimeSpan.FromSeconds(15), 32L * 1024 * 1024) { MaxRetryAttempts = 0 }, userAgent);

    public static Uri QueryUri(string term) => new(CrtShOptions.BaseUri, "?q=" + Uri.EscapeDataString("%" + term + "%") + "&output=json");

    public static async Task<int> RunAsync(ControlContext ctx, Func<Uri, CancellationToken, Task<HttpFetchResult>> fetch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(fetch);
        var file = Path.Combine(ctx.ControlDirectory, "g3a-deneme.json");
        if (File.Exists(file))
        {
            await ctx.Output.WriteLineAsync("G3a denemesi daha önce başlatıldı (yarıda kalmış olsa da); ikinci kez yapılmaz.").ConfigureAwait(false);
            return 2;
        }

        var now = ctx.Time.GetUtcNow();
        if (now.Minute < 10)
        {
            await ctx.Output.WriteLineAsync("Saatin ilk 10 dakikası: deneme başlamadı. 10 dakika sonra yeniden çalıştırın.").ConfigureAwait(false);
            return 75;
        }

        var months = await TuningPositiveMonthsAsync(ctx, cancellationToken).ConfigureAwait(false);
        if (months.Count == 0)
        {
            await ctx.Output.WriteLineAsync("Ayar pozitiflerinin sertifika verisi yok; önce collect-crtsh çalıştırın.").ConfigureAwait(false);
            return 2;
        }

        var term = ControlSeeds.Order(BaselineTerms, "kontrol-g3a-terim")[0];
        var month = ControlSeeds.Order(months, "kontrol-g3a-ay")[0];

        // C2: Kayıt İLK sorgudan önce "başladı" olarak yazılır (CreateNew). Ctrl+C, hata ya da kötü görünen ilk cevap denemeyi
        // silmenin yolu olmaz: "başladı" kalan kayıt da kullanılmış sayılır (kilitli testteki kural).
        Directory.CreateDirectory(ctx.ControlDirectory);
        await using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, new G3aTrialResult(now, [], [], Passed: false) { Status = "basladi" }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var classes = new List<G3aAttemptClass>();
        var counts = new List<int>();
        for (var i = 0; i < Attempts; i++)
        {
            var (cls, domains) = Classify(await fetch(QueryUri(term), cancellationToken).ConfigureAwait(false), month, ctx);
            classes.Add(cls);
            counts.Add(domains);
        }

        var passed = classes.Count(c => c == G3aAttemptClass.Passed) >= 2;
        var result = new G3aTrialResult(now, classes, counts, passed) { Status = "bitti" };
        await File.WriteAllBytesAsync(file, JsonSerializer.SerializeToUtf8Bytes(result), cancellationToken).ConfigureAwait(false);

        // Sonuçlar yalnızca üç deneme bitince basılır.
        for (var i = 0; i < Attempts; i++)
        {
            await ctx.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Deneme {i + 1}: {classes[i]}, hedef ayda kayıtlı alan adı={counts[i]}."))
                .ConfigureAwait(false);
        }

        await ctx.Output.WriteLineAsync(passed
            ? "G3a denemesi GEÇTİ (3'te en az 2)."
            : "G3a denemesi GEÇMEDİ. Geçmiş veri testinde zaman eşleşmeli zor negatif kullanılmaz (karar 7).").ConfigureAwait(false);
        return 0;
    }

    /// <summary>Bir cevabı sınıflar ve hedef aydaki kayıtlı alan adı sayısını döner.</summary>
    public static (G3aAttemptClass Class, int MonthDomains) Classify(HttpFetchResult response, string month, ControlContext ctx)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(ctx);
        if (!response.IsSuccess)
        {
            return (response switch
            {
                { ErrorKind: CollectionErrorKind.Timeout } => G3aAttemptClass.Timeout,
                { HttpStatus: 429 } => G3aAttemptClass.RateLimited,
                { HttpStatus: >= 500 } => G3aAttemptClass.ServerError,
                _ => G3aAttemptClass.OtherError,
            }, 0);
        }

        var parsed = CrtShResponseParser.Parse(response.Body ?? []);
        if (!parsed.IsValid)
        {
            return (G3aAttemptClass.OtherError, 0);
        }

        if (parsed.Certificates.Count + parsed.Skipped >= TruncationRows)
        {
            return (G3aAttemptClass.Truncated, 0);
        }

        var domains = parsed.Certificates
            .Where(c => c.NotBefore.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture) == month)
            .SelectMany(c => c.Names)
            .Select(n => ctx.Full.Normalize(n.StartsWith("*.", StringComparison.Ordinal) ? n[2..] : n).Value?.RegistrableDomain)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Count();
        return (domains >= MinDomains ? G3aAttemptClass.Passed : G3aAttemptClass.TooFew, domains);
    }

    /// <summary>Ayar bölümündeki (2026/1–2026/18) pozitiflerin ilk sertifika ayları (yyyy-MM).</summary>
    private static async Task<IReadOnlyList<string>> TuningPositiveMonthsAsync(ControlContext ctx, CancellationToken cancellationToken)
    {
        var tuning = new Fraud.Evaluation.EvaluationConfig().TuningBulletins;
        var actions = await ctx.Db.OfficialActions.Select(a => new { a.DomainId, a.BulletinNumber }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = actions
            .Where(a => tuning.Contains(Fraud.Evaluation.Model.BulletinNumber.Parse(a.BulletinNumber)))
            .Select(a => a.DomainId)
            .ToHashSet();
        var firsts = await ctx.Db.CertificateDomains
            .Where(cd => ids.Contains(cd.DomainId))
            .GroupBy(cd => cd.DomainId)
            .Select(g => g.Min(cd => cd.Certificate.NotBefore))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. firsts.Select(f => f.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal)];
    }
}
