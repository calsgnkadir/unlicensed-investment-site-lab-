using Fraud.Domain.DomainNames;

namespace Fraud.Domain.Archives;

/// <summary>
/// Wayback Machine CDX dizinindeki bir arşiv kaydı (tablo: archive_snapshots).
/// Sayfanın içeriği değil, yalnızca "bu adres bu zamanda arşivlendi" bilgisidir.
/// </summary>
/// <remarks>
/// <see cref="CapturedAt"/> kaynağın tarihidir (arşivleme anı), <see cref="ObservedAt"/> bu sistemin
/// kaydı ilk gördüğü an. Adresteki sorgu metni ve <c>@</c> içeren yol parçaları kişi verisi
/// taşıyabileceği için kaydedilmeden önce temizlenir.
/// </remarks>
public sealed class ArchiveSnapshot
{
    // EF Core için.
    private ArchiveSnapshot()
    {
        CaptureKey = string.Empty;
        Timestamp = string.Empty;
        OriginalUrl = string.Empty;
    }

    public long Id { get; private set; }

    /// <summary>CDX sorgusunun yapıldığı alan adı.</summary>
    public long DomainId { get; private set; }

    public DomainName Domain { get; private set; } = null!;

    /// <summary>Tekrarı önleyen anahtar: zaman damgası, temizlenmiş adres ve özetin SHA-256 değeri.</summary>
    public string CaptureKey { get; private set; }

    /// <summary>CDX'teki ham zaman damgası (14 hane, UTC). Örnek: <c>20260514093000</c>.</summary>
    public string Timestamp { get; private set; }

    /// <summary>Zaman damgasının tarih karşılığı.</summary>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Temizlenmiş özgün adres (sorgu metni ve parça yok).</summary>
    public string OriginalUrl { get; private set; }

    /// <summary>Özgün adreste sorgu metni vardı ve silindi.</summary>
    public bool QueryRemoved { get; private set; }

    /// <summary>Arşivlenen yanıtın HTTP kodu. CDX'te <c>-</c> ise (ör. revisit kaydı) boştur.</summary>
    public int? StatusCode { get; private set; }

    public string? MimeType { get; private set; }

    /// <summary>İçeriğin özeti (SHA-1, base32). Aynı içerik farklı zamanlarda aynı özeti verir.</summary>
    public string? Digest { get; private set; }

    public DateTimeOffset ObservedAt { get; private set; }
}
