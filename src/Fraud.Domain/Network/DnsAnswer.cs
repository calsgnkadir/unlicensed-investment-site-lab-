namespace Fraud.Domain.Network;

/// <summary>
/// Saklanan tek bir DNS cevap kaydı. dns_snapshots.answers sütununa, cevaptaki sırayla JSON dizisi olarak yazılır.
/// </summary>
/// <param name="Type">Kayıt türü adı (<c>A</c>, <c>AAAA</c>, <c>CNAME</c>, <c>NS</c>) ya da bilinmeyen tür için <c>TYPE65</c> gibi sayı.</param>
/// <param name="Value">
/// A ve AAAA için IP adresi, CNAME ve NS için hedef ad (küçük harf, sonda nokta yok). Bilinmeyen türde boş:
/// İçeriği yorumlamadığımız veriyi saklamayız.
/// </param>
/// <param name="Ttl">Kaydın saniye cinsinden yaşam süresi (RFC 2181 Bölüm 8'e göre en fazla 2^31 - 1).</param>
public sealed record DnsAnswer(string Type, string? Value, long Ttl);
