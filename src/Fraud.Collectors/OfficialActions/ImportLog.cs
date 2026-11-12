using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.OfficialActions;

/// <summary>
/// Derleme anında üretilen log metotları. Mesajlarda alan adı bulunmaz; yalnızca sayı, satır numarası ve neden.
/// </summary>
internal static partial class ImportLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "CSV dosyası bulunamadı. Yapılandırılan yol: {Path}")]
    public static partial void FileNotFound(ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Yükleme bitti. Geçerli satır: {Valid}, eklenen: {Inserted}, zaten var: {Present}, yeni alan adı: {NewDomains}, çakışma: {Conflicts}, reddedilen: {Rejected}")]
    public static partial void Finished(ILogger logger, int valid, int inserted, int present, int newDomains, int conflicts, int rejected);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Satır {Row} reddedildi: {Reason} ({Detail})")]
    public static partial void Rejected(ILogger logger, int row, RowRejectionReason reason, string detail);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Satır {Row} mevcut kayıtla çakışıyor, değiştirilmedi: {Detail}")]
    public static partial void Conflict(ILogger logger, int row, string detail);
}
