using Fraud.Domain.DomainNames;

namespace Fraud.Domain.Collection;

/// <summary>
/// Bir kaynağa yapılan tek bir toplama işinin kaydı (tablo: collection_log).
/// Hedef alan adı adıyla değil kimlik numarasıyla tutulur.
/// </summary>
/// <remarks>
/// Kurallar hem burada hem veritabanında (CHECK kısıtı) zorlanır:
/// <list type="bullet">
/// <item><see cref="CollectionOutcome.Empty"/> yalnızca 2xx yanıt, sıfır kayıt ve sıfır atlanan satırla yazılabilir.</item>
/// <item><see cref="CollectionOutcome.Error"/> her zaman bir <see cref="CollectionErrorKind"/> taşır.</item>
/// </list>
/// Böylece bir hata hiçbir zaman "kayıt yok" gibi görünemez.
/// </remarks>
public sealed class CollectionLogEntry
{
    // EF Core için.
    private CollectionLogEntry()
    {
        MeasurementLocation = string.Empty;
    }

    public CollectionLogEntry(
        CollectionSource source,
        long? domainId,
        string measurementLocation,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        int? httpStatus,
        int attempts,
        CollectionOutcome outcome,
        CollectionErrorKind errorKind,
        int itemCount,
        int skippedItemCount,
        int newItemCount,
        bool truncated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(measurementLocation);
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(newItemCount);
        if (finishedAt < startedAt)
        {
            throw new ArgumentException("Bitiş, başlangıçtan önce olamaz.", nameof(finishedAt));
        }

        switch (outcome)
        {
            case CollectionOutcome.Success when errorKind != CollectionErrorKind.None || itemCount == 0:
                throw new ArgumentException("Başarılı sonuçta hata türü olmaz ve en az bir kayıt bulunur.", nameof(outcome));
            case CollectionOutcome.Empty when errorKind != CollectionErrorKind.None || itemCount != 0
                                              || skippedItemCount != 0 || httpStatus is not (>= 200 and <= 299):
                throw new ArgumentException("Boş sonuç yalnızca 2xx yanıt ve sıfır kayıtla yazılabilir.", nameof(outcome));
            case CollectionOutcome.Error when errorKind == CollectionErrorKind.None:
                throw new ArgumentException("Hata sonucunda hata türü zorunludur.", nameof(errorKind));
        }

        Source = source;
        DomainId = domainId;
        MeasurementLocation = measurementLocation;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        HttpStatus = httpStatus;
        Attempts = attempts;
        Outcome = outcome;
        ErrorKind = errorKind;
        ItemCount = itemCount;
        SkippedItemCount = skippedItemCount;
        NewItemCount = newItemCount;
        Truncated = truncated;
    }

    public long Id { get; private set; }

    public CollectionSource Source { get; private set; }

    /// <summary>Hedef alan adının kimliği. Alan adına bağlı olmayan işlerde (ör. log akışı) boş kalır.</summary>
    public long? DomainId { get; private set; }

    public DomainName? Domain { get; private set; }

    /// <summary>İsteğin çıktığı yer (ör. <c>lab-local</c>). Ağ kayıtlarında zorunludur.</summary>
    public string MeasurementLocation { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset FinishedAt { get; private set; }

    /// <summary>Son denemenin HTTP durum kodu. Bağlantı kurulamadıysa boştur.</summary>
    public int? HttpStatus { get; private set; }

    /// <summary>Tekrar denemeler dahil gönderilen istek sayısı.</summary>
    public int Attempts { get; private set; }

    public CollectionOutcome Outcome { get; private set; }

    public CollectionErrorKind ErrorKind { get; private set; }

    /// <summary>Yanıttan okunan geçerli kayıt sayısı.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Biçimi bozuk olduğu için atlanan satır sayısı.</summary>
    public int SkippedItemCount { get; private set; }

    /// <summary>Veritabanına ilk kez giren kayıt sayısı. Tekrar çalıştırmada sıfır olması beklenir.</summary>
    public int NewItemCount { get; private set; }

    /// <summary>Sayfa sınırına takıldığı için sonuç eksik kaldı mı.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Sertifika logu işlerinde logun yapılandırmadaki adı (ör. <c>sycamore2026h2</c>). Diğer işlerde boş.</summary>
    public string? CtLogName { get; private set; }

    /// <summary>Logdaki aralığın ilk indeksi (dahil). Aralığa bağlı olmayan işlerde boş.</summary>
    public long? RangeStart { get; private set; }

    /// <summary>Logdaki aralığın son indeksi (hariç). <c>RangeEnd - RangeStart</c> aralıktaki kayıt sayısıdır.</summary>
    public long? RangeEnd { get; private set; }

    /// <summary>
    /// Kaydı bir sertifika logunun indeks aralığına bağlar. Aralık bilinmiyorsa (ör. checkpoint okunamadı)
    /// <paramref name="start"/> ve <paramref name="end"/> boş bırakılır.
    /// </summary>
    public CollectionLogEntry ForCtRange(string ctLogName, long? start, long? end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ctLogName);
        if (start.HasValue != end.HasValue)
        {
            throw new ArgumentException("Aralığın iki ucu birlikte verilir.", nameof(end));
        }

        if (start is { } s && end is { } e && (s < 0 || e < s))
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Aralık geçersiz.");
        }

        CtLogName = ctLogName;
        RangeStart = start;
        RangeEnd = end;
        return this;
    }
}
