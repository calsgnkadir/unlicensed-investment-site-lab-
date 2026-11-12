namespace Fraud.Domain.Collection;

/// <summary>Verinin geldiği dış kaynak.</summary>
public enum CollectionSource
{
    CrtSh = 1,
    WaybackCdx = 2,
}

/// <summary>
/// Bir toplama denemesinin sonucu. Üç durum bilerek ayrıdır:
/// <see cref="Empty"/> yalnızca kaynak başarılı yanıt verip "kayıt yok" dediğinde yazılır.
/// Zaman aşımı, 429, 5xx veya okunamayan yanıt her zaman <see cref="Error"/> olur.
/// </summary>
public enum CollectionOutcome
{
    /// <summary>Kaynak yanıt verdi ve en az bir geçerli kayıt döndü.</summary>
    Success = 1,

    /// <summary>Kaynak başarılı yanıt verdi ve boş liste döndü.</summary>
    Empty = 2,

    /// <summary>Sonuç bilinmiyor. "Kayıt yok" anlamına gelmez.</summary>
    Error = 3,
}

/// <summary>Hatanın türü. Başarılı ve boş sonuçta <see cref="None"/> olur.</summary>
public enum CollectionErrorKind
{
    None = 0,

    /// <summary>HTTP 429. Kaynak bizi yavaşlattı.</summary>
    RateLimited = 1,

    /// <summary>HTTP 5xx.</summary>
    ServerError = 2,

    /// <summary>Tekrar denemeye değmeyen 4xx veya beklenmeyen yönlendirme.</summary>
    ClientError = 3,

    /// <summary>Deneme süresi doldu (HTTP 408 dahil).</summary>
    Timeout = 4,

    /// <summary>Bağlantı kurulamadı veya yarıda kesildi.</summary>
    NetworkError = 5,

    /// <summary>Yanıt boyut sınırını aştı; içerik okunmadı.</summary>
    ResponseTooLarge = 6,

    /// <summary>Yanıt beklenen biçimde değil (ör. JSON yerine HTML).</summary>
    InvalidResponse = 7,

    /// <summary>İş durduruldu (uygulama kapanıyor).</summary>
    Cancelled = 8,

    /// <summary>Kodda beklenmeyen bir istisna.</summary>
    Unexpected = 9,
}
