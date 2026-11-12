namespace Fraud.Domain.DomainNames;

/// <summary>
/// Normalleştirme sonucu.
/// </summary>
/// <param name="Host">Küçük harfli, sondaki noktası ve baştaki <c>www.</c> etiketi atılmış ASCII ad.</param>
/// <param name="RegistrableDomain">Kayıtlı alan adı. Örnek: <c>ornek.com.tr</c>.</param>
/// <param name="Subdomain">Kayıtlı alan adının solunda kalan kısım; yoksa <c>null</c>.</param>
/// <param name="PublicSuffix">Genel sonek. Örnek: <c>com.tr</c>.</param>
/// <param name="UnicodeHost">Gösterim için Unicode biçim.</param>
public sealed record NormalizedDomain(
    string Host,
    string RegistrableDomain,
    string? Subdomain,
    string PublicSuffix,
    string UnicodeHost)
{
    public bool IsSubdomain => Subdomain is not null;
}
