using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Fraud.Collectors.Http;

namespace Fraud.Collectors.StaticCt;

/// <summary>Static CT izleme ayarları (bölüm: <c>StaticCt</c>).</summary>
/// <remarks>
/// Static CT belgesi bir hız sınırı önermiyor (DATA_SOURCES Bölüm 4.1). Ölçülen hacim günde yaklaşık 40 bin tile
/// (saniyede ~0,5). Varsayılanlar bunun birkaç katı hız verir ama logu zorlamaz: iki isteğin başlangıcı arasında
/// en az 0,5 sn ve aynı anda en fazla 2 istek.
/// </remarks>
public sealed class StaticCtOptions : SourceHttpOptions
{
    public const string SectionName = "StaticCt";

    public StaticCtOptions()
    {
        MinRequestInterval = TimeSpan.FromMilliseconds(500);
        // Ölçülen en büyük açılmış tile ~640 KB. Boyut sınırı açılmış bayt üzerinden sayılır.
        MaxResponseBytes = 16 * 1024 * 1024;
        AttemptTimeout = TimeSpan.FromSeconds(60);
        MaxRetryAttempts = 3;
        RetryBaseDelay = TimeSpan.FromSeconds(5);
        MaxRetryDelay = TimeSpan.FromMinutes(1);
        MaxRetryAfter = TimeSpan.FromMinutes(5);
    }

    /// <summary>İzlenecek loglar. Anahtarlar Chrome log listesinden (log_list.json, <c>key</c> alanı) kopyalanır.</summary>
    public IList<CtLogOptions> Logs { get; } = [];

    /// <summary>Aynı log için aynı anda en fazla kaç tile isteği açık olabilir.</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>Bir turda en fazla kaç tam veri tile'ı okunur. Tur sonunda checkpoint kökü doğrulanır ve sonuç yazılır.</summary>
    public int MaxTilesPerBatch { get; set; } = 32;

    /// <summary>Loga yetişildiğinde (yeni tam tile yoksa) veya hata sonrası bekleme.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Aynı aralık üst üste bu kadar turda işlenemezse atlanır ve collection_log'a "atlandı" yazılır.</summary>
    public int MaxRangeAttempts { get; set; } = 5;

    /// <summary>
    /// Sağlık dosyası (mutlak yol). Etkin her log en az bir turu hatasız bitirdikçe bu dosya yeniden yazılır;
    /// konteyner sağlık kontrolü dosyanın yaşına bakar. Boşsa yazılmaz. İçinde yalnızca UTC zaman olur.
    /// </summary>
    public string? HeartbeatFile { get; set; }

    public IEnumerable<CtLogOptions> EnabledLogs => Logs.Where(l => l.Enabled);

    public void Validate()
    {
        if (MinRequestInterval < TimeSpan.FromMilliseconds(100))
        {
            throw new InvalidOperationException("StaticCt:MinRequestInterval en az 100 ms olmalı (nazik istemci).");
        }

        if (MaxConcurrency is < 1 or > 8)
        {
            throw new InvalidOperationException("StaticCt:MaxConcurrency 1 ile 8 arasında olmalı.");
        }

        if (MaxTilesPerBatch is < 1 or > 256)
        {
            throw new InvalidOperationException("StaticCt:MaxTilesPerBatch 1 ile 256 arasında olmalı.");
        }

        if (MaxRangeAttempts < 1)
        {
            throw new InvalidOperationException("StaticCt:MaxRangeAttempts en az 1 olmalı.");
        }

        if (PollInterval < TimeSpan.FromSeconds(1))
        {
            throw new InvalidOperationException("StaticCt:PollInterval en az 1 sn olmalı.");
        }

        if (!string.IsNullOrWhiteSpace(HeartbeatFile) && !Path.IsPathFullyQualified(HeartbeatFile))
        {
            throw new InvalidOperationException("StaticCt:HeartbeatFile mutlak bir yol olmalı.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var lockKeys = new HashSet<int>();
        foreach (var log in Logs)
        {
            log.Validate();
            if (!names.Add(log.Name))
            {
                throw new InvalidOperationException("StaticCt:Logs içinde aynı ad iki kez var.");
            }

            // İki farklı adın aynı kilit anahtarına düşmesi çok düşük olasılık; olursa biri hiç çalışmazdı.
            if (!lockKeys.Add(PostgresCtLogLockProvider.LockKey(log.Name)))
            {
                throw new InvalidOperationException("StaticCt:Logs içinde iki log aynı kilit anahtarına düşüyor; birinin adını değiştirin.");
            }
        }
    }
}

/// <summary>Tek bir Static CT logu.</summary>
public sealed partial class CtLogOptions
{
    /// <summary>Kısa ad; tablolarda ve metrik etiketinde kullanılır (ör. <c>sycamore2026h2</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>İzleme öneki (ör. <c>https://mon.sycamore.ct.letsencrypt.org/2026h2/</c>). Sonu <c>/</c> ile biter.</summary>
    public Uri? MonitoringPrefix { get; set; }

    /// <summary>Checkpoint'in ilk satırı ve imza anahtarının adı: gönderim öneki, şemasız ve sonda <c>/</c> olmadan.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Logun açık anahtarı: DER SubjectPublicKeyInfo, base64. Chrome log listesindeki <c>key</c> alanı.</summary>
    public string PublicKey { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// İlk çalıştırmada nereden başlanır: <c>head</c> (şimdiki uç; varsayılan) veya bir kayıt indeksi.
    /// İndeks tile sınırına yuvarlanır. Veritabanında kayıt varsa bu ayar yok sayılır, kalınan yerden devam edilir.
    /// </summary>
    public string StartAt { get; set; } = "head";

    public void Validate()
    {
        if (!NamePattern().IsMatch(Name))
        {
            throw new InvalidOperationException("StaticCt log adı küçük harf, rakam ve tireden oluşmalı (1-64 karakter).");
        }

        if (MonitoringPrefix is not { IsAbsoluteUri: true } prefix || prefix.Scheme != Uri.UriSchemeHttps
            || !prefix.AbsolutePath.EndsWith('/') || prefix.Query.Length > 0 || prefix.UserInfo.Length > 0)
        {
            throw new InvalidOperationException("StaticCt MonitoringPrefix https olmalı, '/' ile bitmeli, sorgu ve kullanıcı bilgisi içermemeli.");
        }

        if (string.IsNullOrWhiteSpace(Origin) || Origin.Any(char.IsWhiteSpace) || Origin.Contains('+', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("StaticCt Origin boş olamaz, boşluk ve '+' içeremez.");
        }

        if (!string.Equals(StartAt, "head", StringComparison.OrdinalIgnoreCase)
            && !(long.TryParse(StartAt, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var start) && start >= 0))
        {
            throw new InvalidOperationException("StaticCt StartAt 'head' ya da negatif olmayan bir indeks olmalı.");
        }

        try
        {
            using var key = CtLogKey.Create(Origin, PublicKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("StaticCt PublicKey geçerli bir base64 ECDSA SubjectPublicKeyInfo değil.", ex);
        }
    }

    /// <summary>İlk başlangıç indeksi; <c>null</c> ise şimdiki uç.</summary>
    public long? StartIndex =>
        long.TryParse(StartAt, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var start)
            ? start
            : null;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
