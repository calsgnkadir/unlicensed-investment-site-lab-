using Fraud.Domain.Collection;

namespace Fraud.Collectors.Collection;

/// <summary>
/// Bir kaynağın bir alan adı için döndürdüğü sonuç. Sonuç türü yanıttan türetilir:
/// hata varsa <see cref="CollectionOutcome.Error"/>, kayıt varsa <see cref="CollectionOutcome.Success"/>,
/// yalnızca başarılı ve tamamen boş yanıt <see cref="CollectionOutcome.Empty"/> olur.
/// </summary>
public sealed record SourceResult<T>(
    CollectionOutcome Outcome,
    CollectionErrorKind ErrorKind,
    IReadOnlyList<T> Items,
    int SkippedItems,
    int? HttpStatus,
    int Attempts,
    bool Truncated);

/// <summary><see cref="SourceResult{T}"/> üreten yardımcılar. Sonuç türünü elle seçmek yerine buradan türetilir.</summary>
public static class SourceResult
{
    /// <summary>Yanıt okundu ve ayrıştırıldı. Hiç geçerli satır yoksa ama bozuk satır varsa bu bir hatadır, "boş" değil.</summary>
    public static SourceResult<T> FromParsed<T>(IReadOnlyList<T> items, int skipped, int? httpStatus, int attempts, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > 0)
        {
            return new SourceResult<T>(CollectionOutcome.Success, CollectionErrorKind.None, items, skipped, httpStatus, attempts, truncated);
        }

        return skipped > 0
            ? new SourceResult<T>(CollectionOutcome.Error, CollectionErrorKind.InvalidResponse, items, skipped, httpStatus, attempts, truncated)
            : new SourceResult<T>(CollectionOutcome.Empty, CollectionErrorKind.None, items, 0, httpStatus, attempts, truncated);
    }

    /// <summary>Hata. Daha önce okunmuş sayfalar varsa <paramref name="partialItems"/> ile korunur.</summary>
    public static SourceResult<T> Failed<T>(
        CollectionErrorKind kind, int? httpStatus, int attempts, IReadOnlyList<T>? partialItems = null, int skipped = 0) =>
        new SourceResult<T>(CollectionOutcome.Error, kind, partialItems ?? [], skipped, httpStatus, attempts, false);
}
