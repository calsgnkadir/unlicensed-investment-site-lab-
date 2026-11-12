using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;

namespace Fraud.Collectors.OfficialActions;

/// <summary>CSV'den okunmuş ve doğrulanmış tek satır.</summary>
/// <param name="ListedAs">
/// Normalleştirilmiş ad (<see cref="NormalizedDomain.Host"/>). Ham hücre bilerek saklanmaz: yol, sorgu metni,
/// kullanıcı bilgisi veya tablo formülü önekleri (<c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>) taşıyabilir.
/// Ham metin gerekiyorsa kaynak bültene bakılır.
/// </param>
public sealed record OfficialActionRow(
    int RowNumber,
    NormalizedDomain Domain,
    string ListedAs,
    string BulletinNumber,
    DateOnly DecisionDate,
    OfficialActionCategory Category,
    Uri SourceUrl);

public enum RowRejectionReason
{
    MissingDomain,
    InvalidDomain,
    PlatformDomain,
    MissingBulletinNumber,
    InvalidBulletinNumber,
    MissingDecisionDate,
    InvalidDecisionDate,
    MissingCategory,
    UnknownCategory,
    MissingSourceUrl,
    InvalidSourceUrl,
    SourceHostNotAllowed,
}

/// <summary>
/// Reddedilen satır. Alan adı bilerek tutulmaz; rapor ve log yalnızca satır numarası ve nedeni gösterir.
/// </summary>
public sealed record RowRejection(int RowNumber, RowRejectionReason Reason, string Detail);

public sealed record OfficialActionCsvParseResult(
    IReadOnlyList<OfficialActionRow> Rows,
    IReadOnlyList<RowRejection> Rejections);

/// <summary>
/// Aşama 0 CSV dosyasını (ROADMAP 2.1) okur. Zorunlu sütunlar: alan_adi, bulten_no, karar_tarihi,
/// kategori, kaynak_url. ilk_sertifika, sertifika_sayisi ve arsiv_kaydi okunmaz; bu bilgiler elle
/// toplanmıştır ve sonraki haftalarda toplayıcılar tarafından kayıtlı biçimde yeniden üretilecektir.
/// </summary>
public sealed partial class OfficialActionCsvParser(
    IDomainNameNormalizer normalizer,
    OfficialActionImportOptions options)
{
    public static readonly IReadOnlyList<string> RequiredHeaders =
        ["alan_adi", "bulten_no", "karar_tarihi", "kategori", "kaynak_url"];

    public OfficialActionCsvParseResult Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = options.Delimiter,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            DetectColumnCountChanges = false,
            BadDataFound = null,
        };

        using var csv = new CsvReader(reader, config);
        if (!csv.Read() || !csv.ReadHeader())
        {
            throw new InvalidDataException("CSV dosyası boş ya da başlık satırı yok.");
        }

        var headers = (csv.HeaderRecord ?? []).Select(h => h.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var missing = RequiredHeaders.Where(h => !headers.Contains(h)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException($"Eksik sütun: {string.Join(", ", missing)}");
        }

        var rows = new List<OfficialActionRow>();
        var rejections = new List<RowRejection>();

        while (csv.Read())
        {
            // Başlık 1. satırdır; veri satırları 2'den başlar (elle tablodan bakarken aynı numara görünür).
            var rowNumber = csv.Parser.Row;
            var outcome = ParseRow(rowNumber, name => csv.GetField(name));
            if (outcome.Row is not null)
            {
                rows.Add(outcome.Row);
            }
            else if (outcome.Rejection is not null)
            {
                rejections.Add(outcome.Rejection);
            }
        }

        return new OfficialActionCsvParseResult(rows, rejections);
    }

    private (OfficialActionRow? Row, RowRejection? Rejection) ParseRow(int rowNumber, Func<string, string?> field)
    {
        var rawDomain = field("alan_adi")?.Trim();
        var bulletin = field("bulten_no")?.Trim();
        var rawDate = field("karar_tarihi")?.Trim();
        var rawCategory = field("kategori")?.Trim();
        var rawUrl = field("kaynak_url")?.Trim();

        // Tamamen boş satırlar sessizce atlanır (tablo programlarının eklediği son satırlar).
        if (string.IsNullOrEmpty(rawDomain) && string.IsNullOrEmpty(bulletin) && string.IsNullOrEmpty(rawDate)
            && string.IsNullOrEmpty(rawCategory) && string.IsNullOrEmpty(rawUrl))
        {
            return (null, null);
        }

        RowRejection Reject(RowRejectionReason reason, string detail) => new(rowNumber, reason, detail);

        if (string.IsNullOrEmpty(rawDomain))
        {
            return (null, Reject(RowRejectionReason.MissingDomain, "alan_adi boş"));
        }

        var normalized = normalizer.Normalize(rawDomain);
        if (!normalized.IsSuccess)
        {
            return (null, Reject(RowRejectionReason.InvalidDomain, normalized.Error.ToString()));
        }

        var domain = normalized.Value!;
        if (options.PlatformDomains.Any(p => string.Equals(p, domain.RegistrableDomain, StringComparison.Ordinal)))
        {
            return (null, Reject(RowRejectionReason.PlatformDomain, "sosyal medya veya kısaltma servisi; elle ayrılmalı"));
        }

        if (string.IsNullOrEmpty(bulletin))
        {
            return (null, Reject(RowRejectionReason.MissingBulletinNumber, "bulten_no boş"));
        }

        if (!BulletinNumberPattern().IsMatch(bulletin))
        {
            return (null, Reject(RowRejectionReason.InvalidBulletinNumber, "beklenen biçim: YYYY/NN"));
        }

        if (string.IsNullOrEmpty(rawDate))
        {
            return (null, Reject(RowRejectionReason.MissingDecisionDate, "karar_tarihi boş"));
        }

        if (!DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (null, Reject(RowRejectionReason.InvalidDecisionDate, "beklenen biçim: YYYY-AA-GG"));
        }

        if (string.IsNullOrEmpty(rawCategory))
        {
            return (null, Reject(RowRejectionReason.MissingCategory, "kategori boş"));
        }

        var category = ParseCategory(rawCategory);
        if (category is null)
        {
            return (null, Reject(RowRejectionReason.UnknownCategory, "beklenen: kripto, kaldıraçlı, diğer"));
        }

        if (string.IsNullOrEmpty(rawUrl))
        {
            return (null, Reject(RowRejectionReason.MissingSourceUrl, "kaynak_url boş"));
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            return (null, Reject(RowRejectionReason.InvalidSourceUrl, "mutlak http(s) bağlantısı olmalı"));
        }

        if (!IsAllowedSourceHost(url.IdnHost))
        {
            return (null, Reject(RowRejectionReason.SourceHostNotAllowed, "kaynak bağlantısı izin listesinde değil"));
        }

        return (new OfficialActionRow(rowNumber, domain, domain.Host, bulletin, date, category.Value, url), null);
    }

    private bool IsAllowedSourceHost(string host)
    {
        host = host.ToLowerInvariant();
        return options.AllowedSourceHosts.Any(allowed =>
        {
            var a = allowed.Trim().ToLowerInvariant();
            return host == a || host.EndsWith("." + a, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Kategori Türkçe yazılır. Büyük/küçük harf ve Türkçe karakter farkları yok sayılır:
    /// "KALDIRAÇLI", "kaldıraçlı" ve "kaldiracli" aynı kabul edilir.
    /// </summary>
    public static OfficialActionCategory? ParseCategory(string value)
    {
        var folded = value.Trim().Replace('\u0130', 'i').ToLowerInvariant()
            .Replace('ı', 'i').Replace('ç', 'c').Replace('ğ', 'g')
            .Replace('ş', 's').Replace('ö', 'o').Replace('ü', 'u');

        return folded switch
        {
            "kripto" => OfficialActionCategory.Crypto,
            "kaldiracli" => OfficialActionCategory.Leveraged,
            "diger" => OfficialActionCategory.Other,
            _ => null,
        };
    }

    // "\d" .NET'te Unicode rakamlarını da (ör. Arapça-Hint, tam genişlik) kabul eder; aynı bülten iki ayrı
    // anahtar olmasın diye yalnızca ASCII rakam. "$" sondaki satır sonunu da kabul ettiği için "\z".
    [GeneratedRegex(@"^[0-9]{4}/[0-9]{1,4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex BulletinNumberPattern();
}
