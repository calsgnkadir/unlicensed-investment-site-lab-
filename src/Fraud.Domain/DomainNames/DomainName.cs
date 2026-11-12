namespace Fraud.Domain.DomainNames;

/// <summary>
/// Veritabanındaki bir alan adı kaydı (tablo: domains).
/// Anahtar, normalleştirilmiş ASCII (punycode) adıdır. Aynı ad iki kez kaydedilmez.
/// </summary>
public sealed class DomainName
{
    // EF Core için.
    private DomainName()
    {
        Host = string.Empty;
        RegistrableDomain = string.Empty;
        PublicSuffix = string.Empty;
        UnicodeHost = string.Empty;
    }

    public DomainName(NormalizedDomain normalized, DateTimeOffset firstObservedAt)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        Host = normalized.Host;
        RegistrableDomain = normalized.RegistrableDomain;
        PublicSuffix = normalized.PublicSuffix;
        UnicodeHost = normalized.UnicodeHost;
        FirstObservedAt = firstObservedAt;
    }

    public long Id { get; private set; }

    /// <summary>Normalleştirilmiş tam ad, ASCII biçimde. Örnek: <c>panel.ornek.com</c>.</summary>
    public string Host { get; private set; }

    /// <summary>Kayıtlı alan adı (Public Suffix List kurallarına göre). Örnek: <c>ornek.com.tr</c>.</summary>
    public string RegistrableDomain { get; private set; }

    /// <summary>Genel sonek. Örnek: <c>com.tr</c>.</summary>
    public string PublicSuffix { get; private set; }

    /// <summary>Okunabilir (Unicode) biçim. Yalnızca gösterim içindir, eşleştirmede kullanılmaz.</summary>
    public string UnicodeHost { get; private set; }

    /// <summary>Bu adı sistemin ilk kez gördüğü an (kaynağın tarihi değil).</summary>
    public DateTimeOffset FirstObservedAt { get; private set; }
}
