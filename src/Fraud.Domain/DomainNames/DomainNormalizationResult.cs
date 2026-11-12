namespace Fraud.Domain.DomainNames;

public enum DomainNormalizationError
{
    None = 0,

    /// <summary>Girdi boş.</summary>
    Empty,

    /// <summary>Girdiden bir ana bilgisayar adı çıkarılamadı.</summary>
    Unparseable,

    /// <summary>Girdi bir IP adresi; alan adı değil.</summary>
    IpAddress,

    /// <summary>Etiket uzunluğu, karakter veya IDN kuralı ihlali.</summary>
    InvalidSyntax,

    /// <summary>Ad, kendisi bir genel sonek (ör. <c>com.tr</c>); kayıtlı alan adı yok.</summary>
    PublicSuffixOnly,
}

/// <summary>
/// Normalleştirme ya başarılı olur ya da neden başarısız olduğunu söyler. İstisna fırlatılmaz;
/// hatalı satırlar yükleyicide gerekçesiyle reddedilir.
/// </summary>
public sealed record DomainNormalizationResult
{
    private DomainNormalizationResult(NormalizedDomain? value, DomainNormalizationError error)
    {
        Value = value;
        Error = error;
    }

    public NormalizedDomain? Value { get; }

    public DomainNormalizationError Error { get; }

    public bool IsSuccess => Value is not null;

    public static DomainNormalizationResult Success(NormalizedDomain value) =>
        new(value ?? throw new ArgumentNullException(nameof(value)), DomainNormalizationError.None);

    public static DomainNormalizationResult Failure(DomainNormalizationError error) =>
        error == DomainNormalizationError.None
            ? throw new ArgumentException("Başarısız sonuç için bir hata türü gerekir.", nameof(error))
            : new(null, error);
}
