using Fraud.Domain.Collection;
using Microsoft.Extensions.Logging;

namespace Fraud.Collectors.Collection;

/// <summary>
/// Toplayıcı log mesajları. Alan adı yazılmaz; yalnızca kaynak, alan adı kimliği, sayılar ve hata türü.
/// </summary>
internal static partial class CollectorLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "{Source} toplama başladı. Hedef alan adı: {Targets}, önbellekte taze: {Fresh}")]
    public static partial void RunStarted(ILogger logger, CollectionSource source, int targets, int fresh);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning,
        Message = "{Source} hata: alan adı #{DomainId}, tür {ErrorKind}, HTTP {HttpStatus}, deneme {Attempts}")]
    public static partial void TargetFailed(ILogger logger, CollectionSource source, long domainId, CollectionErrorKind errorKind, int? httpStatus, int attempts);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information,
        Message = "{Source} toplama bitti. Başarılı: {Succeeded}, boş: {Empty}, hata: {Failed}, atlanan (önbellek): {Skipped}, yeni kayıt: {NewItems}")]
    public static partial void RunFinished(ILogger logger, CollectionSource source, int succeeded, int empty, int failed, int skipped, int newItems);

    [LoggerMessage(EventId = 103, Level = LogLevel.Error,
        Message = "{Source} turu beklenmeyen hatayla durdu: {ExceptionType}")]
    public static partial void RunCrashed(ILogger logger, CollectionSource source, string exceptionType);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Sonraki toplama turu {Interval} sonra.")]
    public static partial void NextRun(ILogger logger, TimeSpan interval);
}
