namespace Fraud.Domain.CertificateTransparency;

/// <summary>
/// Sertifika logundan saklama filtresine takılan bir kayıt (tablo: ct_candidates).
/// Anahtar (log adı, indeks): Aynı kayıt ikinci kez okunursa yeni satır açılmaz.
/// </summary>
/// <remarks>
/// <para>
/// Bu tablo SPK veya USOM listesinde OLMAYAN alan adları içerir (Kural 4). Hiçbir log, metrik, rapor
/// veya API ucu bu adları basmaz; yalnızca sayılar dışarı çıkar.
/// </para>
/// <para>
/// Tarihler ayrıdır: <see cref="LoggedAt"/> kaydın loga girdiği an ve <see cref="NotBefore"/> sertifikanın
/// başlangıcı kaynağın tarihleridir; <see cref="ObservedAt"/> bu sistemin kaydı okuduğu andır.
/// Kişi verisi saklanmaz: Adlar yalnızca DNS adı biçimindeyse, issuer yalnızca C, O, OU, CN alanlarıyla kalır.
/// </para>
/// </remarks>
public sealed class CtCandidate
{
    public const int MaxNames = 1000;

    // EF Core için.
    private CtCandidate()
    {
        LogName = string.Empty;
        Issuer = string.Empty;
        SanNames = [];
        MatchedTerms = [];
    }

    public CtCandidate(
        string logName,
        long entryIndex,
        CtEntryType entryType,
        DateTimeOffset loggedAt,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        string issuer,
        IReadOnlyList<string> sanNames,
        IReadOnlyList<string> matchedTerms,
        DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(sanNames);
        ArgumentNullException.ThrowIfNull(matchedTerms);
        if (sanNames.Count is 0 or > MaxNames)
        {
            throw new ArgumentException("Aday en az bir, en fazla " + MaxNames + " DNS adı taşır.", nameof(sanNames));
        }

        if (matchedTerms.Count == 0)
        {
            throw new ArgumentException("Aday en az bir filtre terimiyle eşleşmiş olmalı.", nameof(matchedTerms));
        }

        if (issuer.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Issuer e-posta içeremez; önce temizlenmeli.", nameof(issuer));
        }

        LogName = logName;
        EntryIndex = entryIndex;
        EntryType = entryType;
        LoggedAt = loggedAt;
        NotBefore = notBefore;
        NotAfter = notAfter;
        Issuer = issuer;
        SanNames = [.. sanNames];
        MatchedTerms = [.. matchedTerms];
        ObservedAt = observedAt;
    }

    public long Id { get; private set; }

    /// <summary>Logun yapılandırmadaki adı (ör. <c>sycamore2026h2</c>).</summary>
    public string LogName { get; private set; }

    /// <summary>Kaydın logdaki 0 tabanlı sırası.</summary>
    public long EntryIndex { get; private set; }

    public CtEntryType EntryType { get; private set; }

    /// <summary>Kaydın zaman damgası (<c>TimestampedEntry.timestamp</c>): logun kaydı kabul ettiği an.</summary>
    public DateTimeOffset LoggedAt { get; private set; }

    /// <summary>Sertifikanın geçerlilik başlangıcı.</summary>
    public DateTimeOffset? NotBefore { get; private set; }

    public DateTimeOffset? NotAfter { get; private set; }

    /// <summary>Temizlenmiş issuer adı. Örnek: <c>C=US, O=Let's Encrypt, CN=R13</c>.</summary>
    public string Issuer { get; private set; }

    /// <summary>Sertifikanın SAN dNSName listesi (temizlenmiş, küçük harf, tekrarsız, sıralı).</summary>
    public string[] SanNames { get; private set; }

    /// <summary>Kaydı saklatan filtre terimleri. Bir yargı değil, yalnızca saklama nedeni.</summary>
    public string[] MatchedTerms { get; private set; }

    /// <summary>Bu sistemin kaydı okuduğu an.</summary>
    public DateTimeOffset ObservedAt { get; private set; }
}
