using Fraud.Domain.Network;

namespace Fraud.Collectors.Dns;

/// <summary>
/// Ayrıştırılmış bir cevabın gerçekten bizim sorgumuzun cevabı olup olmadığına bakar.
/// </summary>
/// <remarks>
/// Biçimce doğru bir paket yine de sahte olabilir. RFC 5452 Bölüm 3, bir cevabın kabul edilmesi için
/// şunların tutmasını ister: aynı işlem kimliği, aynı soru (ad, tür, sınıf) ve doğru adres/port.
/// Adres ve port denetimi taşıma katmanındadır (UDP soketi çözücüye bağlanır); burada geri kalanı denetlenir.
/// </remarks>
public static class DnsResponseValidator
{
    /// <returns>Uygunsa <see cref="DnsErrorKind.None"/>, değilse hatanın türü.</returns>
    public static DnsErrorKind Validate(DnsQuery query, DnsMessage response)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(response);

        // 1) İşlem kimliği aynı mı? DoH'ta sorgu kimliği 0'dır ve cevap da 0 döner (RFC 8484 Bölüm 4.1);
        //    eşitlik denetimi bu durumu da kapsar.
        if (response.Header.Id != query.Id)
        {
            return DnsErrorKind.IdMismatch;
        }

        // 2) Bu bir cevap mı (QR = 1) ve bizim gönderdiğimiz türde bir işlem mi (OPCODE = 0, standart sorgu)?
        if (!response.Header.IsResponse || response.Header.Opcode != 0)
        {
            return DnsErrorKind.Malformed;
        }

        // 3) Soru bölümü bizim sorumuzu tekrar etmeli (RFC 1035 Bölüm 4.1.1: "copied into the response").
        //    Ad karşılaştırması büyük-küçük harf duyarsızdır (RFC 4343); okuyucu zaten küçük harfe çevirir.
        if (response.Questions.Count != 1)
        {
            return DnsErrorKind.QuestionMismatch;
        }

        var question = response.Questions[0];
        if (!string.Equals(question.Name, query.Name, StringComparison.OrdinalIgnoreCase)
            || question.Type != (int)query.Type
            || question.Class != DnsWire.ClassIn)
        {
            return DnsErrorKind.QuestionMismatch;
        }

        return DnsErrorKind.None;
    }
}
