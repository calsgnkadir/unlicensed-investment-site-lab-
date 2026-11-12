using Fraud.Domain.DomainNames;

namespace Fraud.Domain.OfficialActions;

/// <summary>
/// Bir alan adı için resmi bir kurumun aldığı erişim engeli kararı (tablo: official_actions).
/// Bu kayıt "dolandırıcı" anlamına gelmez; yalnızca "bu bültende bu karar yayımlandı" demektir.
/// </summary>
/// <remarks>
/// İki tarih bilerek ayrı tutulur:
/// <see cref="DecisionDate"/> kaynağın kendi tarihidir (bültende yazan karar tarihi),
/// <see cref="ObservedAt"/> ise kaydın bu sisteme girdiği andır.
/// Böylece geçmişe dönük analizde "o tarihte ne biliniyordu" sorusu cevaplanabilir.
/// </remarks>
public sealed class OfficialAction
{
    // EF Core için.
    private OfficialAction()
    {
        BulletinNumber = string.Empty;
        SourceUrl = string.Empty;
        ListedAs = string.Empty;
    }

    public OfficialAction(
        DomainName domain,
        OfficialActionSource source,
        string bulletinNumber,
        DateOnly decisionDate,
        OfficialActionCategory category,
        Uri sourceUrl,
        string listedAs,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(bulletinNumber);
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(listedAs);
        if (!sourceUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Kaynak bağlantısı mutlak bir adres olmalı.", nameof(sourceUrl));
        }

        Domain = domain;
        Source = source;
        BulletinNumber = bulletinNumber;
        DecisionDate = decisionDate;
        Category = category;
        SourceUrl = sourceUrl.AbsoluteUri;
        ListedAs = listedAs;
        ObservedAt = observedAt;
    }

    public long Id { get; private set; }

    public long DomainId { get; private set; }

    public DomainName Domain { get; private set; } = null!;

    public OfficialActionSource Source { get; private set; }

    /// <summary>Bülten numarası. Örnek: <c>2026/46</c>.</summary>
    public string BulletinNumber { get; private set; }

    /// <summary>Kaynağın kendi tarihi: bültende yazan karar tarihi.</summary>
    public DateOnly DecisionDate { get; private set; }

    public OfficialActionCategory Category { get; private set; }

    /// <summary>Bültenin bağlantısı.</summary>
    public string SourceUrl { get; private set; }

    /// <summary>
    /// Kaynaktaki adın normalleştirilmiş hali (denetim için; eşleştirmede kullanılmaz). Ham hücre saklanmaz:
    /// kullanıcı bilgisi, sorgu metni veya tablo formülü taşıyabilir.
    /// </summary>
    public string ListedAs { get; private set; }

    /// <summary>Kaydın bu sisteme girdiği an.</summary>
    public DateTimeOffset ObservedAt { get; private set; }
}
