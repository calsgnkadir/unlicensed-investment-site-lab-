namespace Fraud.Collectors.Http;

/// <summary>Bir dış kaynağa nazik erişim ayarları. Varsayılanlar alt sınıflarda kaynağa göre seçilir.</summary>
public abstract class SourceHttpOptions
{
    /// <summary>İki isteğin başlangıcı arasındaki en kısa süre. Tekrar denemeler de bu sınıra uyar.</summary>
    public TimeSpan MinRequestInterval { get; set; }

    /// <summary>Sıkıştırma açıldıktan sonraki en büyük yanıt boyutu.</summary>
    public long MaxResponseBytes { get; set; }

    /// <summary>Tek bir denemenin süresi (bağlantı, başlıklar ve gövde dahil).</summary>
    public TimeSpan AttemptTimeout { get; set; }

    /// <summary>İlk denemeden sonra en fazla kaç kez tekrar denenir. 0 ise tekrar yok.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Üstel geri çekilmenin başlangıç bekleme süresi (her denemede yaklaşık iki katına çıkar).</summary>
    public TimeSpan RetryBaseDelay { get; set; }

    /// <summary>Geri çekilmede bir beklemenin üst sınırı.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Sunucunun <c>Retry-After</c> ile istediği beklemenin üst sınırı.</summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bu süre içinde başarıyla (veya kesin boş sonuçla) sorgulanmış alan adı yeniden sorulmaz.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);
}
