namespace Fraud.Domain.DomainNames;

/// <summary>
/// Ham metni (alan adı, tam URL, büyük harfli ya da IDN ad) tek bir standart biçime çevirir.
/// Ağ isteği yapmaz; yalnızca metin işler.
/// </summary>
public interface IDomainNameNormalizer
{
    DomainNormalizationResult Normalize(string? input);
}
