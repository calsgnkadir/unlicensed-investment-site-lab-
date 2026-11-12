using System.Globalization;
using System.Text;

namespace Fraud.Collectors.StaticCt;

/// <summary>İstek bütçesi doldu; istek gönderilmedi.</summary>
public sealed class RequestBudgetExceededException : Exception
{
    public RequestBudgetExceededException()
        : base("İstek bütçesi doldu.")
    {
    }

    public RequestBudgetExceededException(string message)
        : base(message)
    {
    }

    public RequestBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// HTTP katmanında istek sayısını sınırlar: Sınır dolunca istek ağa çıkmadan durdurulur.
/// Tekrar denemeler ve kısmi tile yedeği de sayılır, çünkü hepsi bu işleyiciden geçer.
/// </summary>
public sealed class RequestBudgetHandler(int maxRequests) : DelegatingHandler
{
    private int _sent;

    public int Sent => Volatile.Read(ref _sent);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _sent) > maxRequests)
        {
            Interlocked.Decrement(ref _sent);
            throw new RequestBudgetExceededException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary><c>ct-sample</c> sonucu. Yalnızca sayılar; hiçbir ad, adres veya sertifika alanı içermez.</summary>
public sealed record CtSampleReport(
    long TreeSize,
    TimeSpan CheckpointAge,
    long FirstTile,
    int Tiles,
    int Entries,
    int X509Entries,
    int PrecertEntries,
    int UnreadableCertificates,
    int CertificatesWithoutDnsNames,
    int CandidateCertificates,
    IReadOnlyDictionary<string, int> CandidatesByTerm,
    IReadOnlyDictionary<string, int> LifetimeBuckets,
    long DecompressedBytes,
    int? Level1Matched,
    string Level1Note,
    bool? RootVerified,
    string RootNote,
    int Requests,
    int RequestBudget)
{
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Static CT örnek okuma (yalnızca sayılar)");
        sb.AppendLine(c, $"  Checkpoint imzası:        doğrulandı (log anahtarı, RFC 6962)");
        sb.AppendLine(c, $"  Checkpoint boyutu:        {TreeSize}");
        sb.AppendLine(c, $"  Checkpoint yaşı:          {CheckpointAge.TotalSeconds:F0} sn");
        sb.AppendLine(c, $"  Okunan tile:              {Tiles} (tile #{FirstTile} ve sonrası)");
        sb.AppendLine(c, $"  Kayıt:                    {Entries} (x509 {X509Entries}, ön sertifika {PrecertEntries})");
        sb.AppendLine(c, $"  Okunamayan sertifika:     {UnreadableCertificates}");
        sb.AppendLine(c, $"  DNS adı olmayan:          {CertificatesWithoutDnsNames}");
        sb.AppendLine(c, $"  Filtreden geçen:          {CandidateCertificates} ({(Entries == 0 ? 0 : (double)CandidateCertificates / Entries).ToString("P2", c)})");
        foreach (var (term, count) in CandidatesByTerm.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(c, $"    {term}: {count}");
        }

        sb.AppendLine("  Geçerlilik süresi (notAfter - notBefore):");
        foreach (var (bucket, count) in LifetimeBuckets)
        {
            sb.AppendLine(c, $"    {bucket}: {count}");
        }

        sb.AppendLine(c, $"  Açılmış tile baytı:       {DecompressedBytes}");
        var level1 = Level1Matched is { } m ? m.ToString(c) + "/" + Tiles.ToString(c) + " eşleşti" : "yapılmadı";
        var root = RootVerified switch { true => "kök eşleşti", false => "KÖK UYUŞMADI", null => "yapılmadı" };
        sb.AppendLine(("  Level-1 hash karşılaştırması: " + level1 + " " + Level1Note).TrimEnd());
        sb.AppendLine(("  Merkle kök doğrulaması:   " + root + " " + RootNote).TrimEnd());
        sb.AppendLine(c, $"  HTTP isteği:              {Requests} / bütçe {RequestBudget}");
        sb.AppendLine("  Not: Örnek rastgele değil, logun ucundaki ardışık tile'lardır. Filtre bir yargı değil, saklama ölçütüdür.");
        return sb.ToString();
    }
}

/// <summary>
/// Sınırlı deneme: checkpoint, logun ucundaki <c>N</c> tam veri tile'ı ve bir level-1 hash tile'ı.
/// Hiçbir şey yazılmaz; adaylar yalnızca sayılır.
/// </summary>
/// <remarks>
/// Level-1 tile'daki her özet, bir tam veri tile'ının 256 yaprağının alt ağaç köküdür. Okunan tile'lardan hesaplanan
/// kök buna eşitse tile'lar logun kendi ağacıyla tutarlıdır. Bu, imzalı köke tam bağlama değildir; tam doğrulama
/// (<c>--verify-root true</c>) birkaç hash tile daha ister ve bütçeye sığmazsa yapılmaz.
/// </remarks>
public sealed class CtSampleRunner(IStaticCtClient client, TileProcessor processor, TimeProvider timeProvider)
{
    public const int MaxTiles = 16;

    public async Task<CtSampleReport> RunAsync(int tiles, bool verifyRoot, int requestBudget, Func<int> requestsSent, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tiles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tiles, MaxTiles);
        ArgumentNullException.ThrowIfNull(requestsSent);
        if (requestBudget < tiles + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requestBudget), "Bütçe en az checkpoint ve tile sayısı kadar olmalı.");
        }

        var cpFetch = await client.GetCheckpointAsync(cancellationToken).ConfigureAwait(false);
        if (!cpFetch.IsSuccess)
        {
            throw new InvalidOperationException("Checkpoint okunamadı veya imzası doğrulanmadı: " + cpFetch.ErrorKind + ", HTTP " + cpFetch.HttpStatus);
        }

        var checkpoint = cpFetch.Value!;
        var fullTiles = checkpoint.Size / TilePath.Width;
        if (fullTiles < tiles)
        {
            throw new InvalidOperationException("Logda yeterli tam tile yok.");
        }

        // Tile'lar tek bir level-1 tile'ının içinde kalsın (karşılaştırma tek istekle yapılsın).
        var last = fullTiles - 1;
        if ((last - tiles + 1) / TilePath.Width != last / TilePath.Width)
        {
            last = (last / TilePath.Width * TilePath.Width) - 1;
        }

        var first = last - tiles + 1;
        var processed = new List<ProcessedTile>();
        long bytes = 0;
        for (var t = first; t <= last; t++)
        {
            var fetch = await client.GetDataTileAsync(t, cancellationToken).ConfigureAwait(false);
            if (!fetch.IsSuccess)
            {
                throw new InvalidOperationException("Veri tile'ı okunamadı: " + fetch.ErrorKind + ", HTTP " + fetch.HttpStatus);
            }

            bytes += fetch.Value!.Length;
            processed.Add(processor.Process(fetch.Value, t, collectLifetimes: true));
        }

        int? level1Matched = null;
        var level1Note = string.Empty;
        try
        {
            var level1Index = first / TilePath.Width;
            var width = (int)Math.Min(TilePath.Width, fullTiles - (level1Index * TilePath.Width));
            var hashTile = await client.GetHashTileAsync(1, level1Index, width, cancellationToken).ConfigureAwait(false);
            if (hashTile.IsSuccess)
            {
                level1Matched = processed.Count(p =>
                {
                    var offset = (int)(p.TileIndex - (level1Index * TilePath.Width)) * Merkle.HashSize;
                    var computed = Merkle.SubtreeRoot(p.LeafHashes.SelectMany(h => h).ToArray(), TilePath.Width);
                    return hashTile.Value!.AsSpan(offset, Merkle.HashSize).SequenceEqual(computed);
                });
            }
            else
            {
                level1Note = "(okunamadı: " + hashTile.ErrorKind + ")";
            }
        }
        catch (RequestBudgetExceededException)
        {
            level1Note = "(istek bütçesi)";
        }

        bool? rootVerified = null;
        var rootNote = verifyRoot ? string.Empty : "(--verify-root verilmedi)";
        if (verifyRoot)
        {
            try
            {
                var reader = new TreeHashReader(client, checkpoint.Size);
                var range = await reader.BuildFrontierAsync(first * TilePath.Width, cancellationToken).ConfigureAwait(false);
                foreach (var hash in processed.SelectMany(p => p.LeafHashes))
                {
                    range.AppendLeaf(hash);
                }

                await reader.AppendAsync(range, checkpoint.Size, cancellationToken).ConfigureAwait(false);
                rootVerified = range.Root().AsSpan().SequenceEqual(checkpoint.RootHash);
            }
            catch (RequestBudgetExceededException)
            {
                rootNote = "(istek bütçesi yetmedi; tam doğrulama testlerde sınanıyor)";
            }
            catch (StaticCtFetchException ex)
            {
                rootNote = "(hash tile okunamadı: " + ex.ErrorKind + ")";
            }
        }

        var byTerm = processed.SelectMany(p => p.Candidates).SelectMany(d => d.MatchedTerms)
            .GroupBy(t => t, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new CtSampleReport(
            checkpoint.Size,
            timeProvider.GetUtcNow() - checkpoint.Timestamp,
            first,
            processed.Count,
            processed.Sum(p => p.Entries),
            processed.Sum(p => p.X509Entries),
            processed.Sum(p => p.PrecertEntries),
            processed.Sum(p => p.UnreadableCertificates),
            processed.Sum(p => p.CertificatesWithoutDnsNames),
            processed.Sum(p => p.Candidates.Count),
            byTerm,
            LifetimeBuckets(processed.SelectMany(p => p.Lifetimes)),
            bytes,
            level1Matched,
            level1Note,
            rootVerified,
            rootNote,
            requestsSent(),
            requestBudget);
    }

    private static Dictionary<string, int> LifetimeBuckets(IEnumerable<TimeSpan> lifetimes)
    {
        var buckets = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["<= 7 gün"] = 0,
            ["8-47 gün"] = 0,
            ["48-100 gün"] = 0,
            ["> 100 gün"] = 0,
        };

        foreach (var days in lifetimes.Select(l => l.TotalDays))
        {
            var key = days switch
            {
                <= 7 => "<= 7 gün",
                <= 47 => "8-47 gün",
                <= 100 => "48-100 gün",
                _ => "> 100 gün",
            };
            buckets[key]++;
        }

        return buckets;
    }
}
