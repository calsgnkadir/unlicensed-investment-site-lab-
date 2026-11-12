using Fraud.Domain.Collection;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.StaticCt;

/// <summary>
/// Static CT log mesajları. Yalnızca sayılar ve hata türü yazılır. Log, yapılandırmadaki sırasıyla
/// (<c>#1</c>, <c>#2</c> ...) anılır; alan adı, sertifika alanı ve adres hiçbir mesaja girmez (Kural 4).
/// </summary>
internal static partial class StaticCtLog
{
    [LoggerMessage(EventId = 200, Level = LogLevel.Information,
        Message = "CT log #{LogNumber}: izleme başladı. Sıradaki indeks {NextIndex}, checkpoint boyutu {TreeSize}")]
    public static partial void FollowStarted(ILogger logger, int logNumber, long nextIndex, long treeSize);

    [LoggerMessage(EventId = 201, Level = LogLevel.Information,
        Message = "CT log #{LogNumber}: [{RangeStart}, {RangeEnd}) doğrulandı ve işlendi. İncelenen {Examined}, okunamayan {Unreadable}, yeni aday {NewCandidates}, geride kalan {Backlog}")]
    public static partial void RangeCommitted(ILogger logger, int logNumber, long rangeStart, long rangeEnd, int examined, int unreadable, int newCandidates, long backlog);

    [LoggerMessage(EventId = 202, Level = LogLevel.Warning,
        Message = "CT log #{LogNumber}: [{RangeStart}, {RangeEnd}) işlenemedi: {ErrorKind}, HTTP {HttpStatus}, üst üste {Failures}. kez")]
    public static partial void RangeFailed(ILogger logger, int logNumber, long rangeStart, long rangeEnd, CollectionErrorKind errorKind, int? httpStatus, int failures);

    [LoggerMessage(EventId = 203, Level = LogLevel.Error,
        Message = "CT log #{LogNumber}: [{RangeStart}, {RangeEnd}) ATLANDI ({ErrorKind}). Bu kayıtlar okunmadı; kapsama düştü.")]
    public static partial void RangeSkipped(ILogger logger, int logNumber, long rangeStart, long rangeEnd, CollectionErrorKind errorKind);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning,
        Message = "CT log #{LogNumber}: checkpoint kabul edilmedi: {ErrorKind}, HTTP {HttpStatus}")]
    public static partial void CheckpointRejected(ILogger logger, int logNumber, CollectionErrorKind errorKind, int? httpStatus);

    [LoggerMessage(EventId = 205, Level = LogLevel.Error,
        Message = "CT log #{LogNumber}: checkpoint geriye gitti veya önceki checkpoint ile tutarsız. Görülen {PreviousSize}, yeni {NewSize}")]
    public static partial void CheckpointInconsistent(ILogger logger, int logNumber, long previousSize, long newSize);

    [LoggerMessage(EventId = 206, Level = LogLevel.Warning,
        Message = "CT log #{LogNumber}: başka bir süreç bu logu izliyor (veritabanı kilidi dolu). Bu süreç bekliyor, kilit boşalınca devam eder.")]
    public static partial void LockBusy(ILogger logger, int logNumber);

    [LoggerMessage(EventId = 207, Level = LogLevel.Information,
        Message = "CT log #{LogNumber}: veritabanı kilidi alındı; izleme bu süreçte.")]
    public static partial void LockAcquired(ILogger logger, int logNumber);

    [LoggerMessage(EventId = 208, Level = LogLevel.Warning,
        Message = "CT log #{LogNumber}: veritabanı kilidi kaybedildi (bağlantı koptu). Tur yapılmadan kilit yeniden istenecek.")]
    public static partial void LockLost(ILogger logger, int logNumber);

    [LoggerMessage(EventId = 209, Level = LogLevel.Warning,
        Message = "CT izleme: sağlık dosyası yazılamadı (HResult {HResult}). Konteyner sağlık kontrolü başarısız görünecek.")]
    public static partial void HeartbeatFailed(ILogger logger, int hResult);
}
