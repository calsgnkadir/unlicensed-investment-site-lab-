namespace Fraud.Domain.Fetching;

public enum SafeFetchStatus
{
    Success = 1,

    /// <summary>İstek güvenlik kuralına takıldı; hedefe bağlanılmadı.</summary>
    Blocked = 2,

    /// <summary>Sunucu 2xx dışında bir kod döndü.</summary>
    HttpError = 3,

    /// <summary>Yanıt boyut sınırını aştı (sıkıştırma açıldıktan sonra da sayılır).</summary>
    TooLarge = 4,

    /// <summary>İçerik türü veya sıkıştırma biçimi izinli değil.</summary>
    UnsupportedContent = 5,

    Timeout = 6,

    NetworkError = 7,

    TooManyRedirects = 8,

    InvalidResponse = 9,
}

public enum SafeFetchBlockReason
{
    None = 0,
    NotAbsolute,
    SchemeNotAllowed,
    UserInfoNotAllowed,
    PortNotAllowed,
    IpLiteralNotAllowed,
    HostNotAllowed,

    /// <summary>Ad çözüldü ama adres özel, yerel veya ayrılmış bir aralıkta.</summary>
    AddressNotAllowed,
}

/// <summary>
/// Çekme sonucu. <see cref="Body"/> yalnızca <see cref="SafeFetchStatus.Success"/> durumunda doludur
/// ve güvenilmez metindir.
/// </summary>
public sealed record SafeFetchResult(
    SafeFetchStatus Status,
    SafeFetchBlockReason BlockReason,
    Uri? FinalUri,
    int? HttpStatus,
    string? ContentType,
    string? Body,
    long BodyBytes,
    int Redirects)
{
    public static SafeFetchResult Blocked(SafeFetchBlockReason reason, Uri? at, int redirects) =>
        new(SafeFetchStatus.Blocked, reason, at, null, null, null, 0, redirects);

    public static SafeFetchResult Failed(SafeFetchStatus status, Uri? at, int? httpStatus, int redirects) =>
        new(status, SafeFetchBlockReason.None, at, httpStatus, null, null, 0, redirects);
}
