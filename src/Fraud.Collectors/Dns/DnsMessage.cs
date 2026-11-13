namespace Fraud.Collectors.Dns;

/// <summary>Ayrıştırılmış DNS mesajı. Bölümler paketteki sırayla tutulur.</summary>
public sealed record DnsMessage(
    DnsHeader Header,
    IReadOnlyList<DnsQuestion> Questions,
    IReadOnlyList<DnsResourceRecord> Answers,
    IReadOnlyList<DnsResourceRecord> Authority,
    IReadOnlyList<DnsResourceRecord> Additional);

/// <summary>12 baytlık başlık (RFC 1035 Bölüm 4.1.1). Bayrak bitlerinin anlamı <see cref="DnsWire"/> içinde.</summary>
public sealed record DnsHeader(ushort Id, ushort Flags, int QuestionCount, int AnswerCount, int AuthorityCount, int AdditionalCount)
{
    /// <summary>QR biti: Bu paket bir cevap mı.</summary>
    public bool IsResponse => (Flags & DnsWire.FlagQr) != 0;

    /// <summary>OPCODE, 4 bit. Bayrak alanında 11. bitten başlar; sağa kaydırıp okuruz.</summary>
    public int Opcode => (Flags & DnsWire.OpcodeMask) >> 11;

    /// <summary>TC biti: Cevap UDP'ye sığmadı ve kesildi.</summary>
    public bool IsTruncated => (Flags & DnsWire.FlagTc) != 0;

    /// <summary>RCODE, alt 4 bit.</summary>
    public int Rcode => Flags & DnsWire.RcodeMask;
}

/// <summary>Soru bölümündeki tek soru (RFC 1035 Bölüm 4.1.2).</summary>
/// <param name="Name">Küçük harfli ad, sonda nokta yok.</param>
public sealed record DnsQuestion(string Name, int Type, int Class);

/// <summary>
/// Bir kaynak kaydı (RFC 1035 Bölüm 4.1.3).
/// </summary>
/// <param name="Name">Kaydın sahibi olan ad (küçük harf).</param>
/// <param name="Ttl">Saniye. RFC 2181 Bölüm 8'e göre en üst biti 1 olan değer 0 sayılmıştır.</param>
/// <param name="Value">
/// A ve AAAA için IP adresi metni, CNAME ve NS için hedef ad. SOA ve bilinmeyen türlerde boş.
/// </param>
/// <param name="SoaMinimum">Yalnızca SOA kaydında: MINIMUM alanı (olumsuz önbellek süresi için, RFC 2308 Bölüm 4).</param>
public sealed record DnsResourceRecord(string Name, int Type, int Class, uint Ttl, string? Value, uint? SoaMinimum);
