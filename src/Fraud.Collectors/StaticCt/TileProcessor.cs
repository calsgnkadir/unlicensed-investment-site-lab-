using Fraud.Domain.CertificateTransparency;

namespace Fraud.Collectors.StaticCt;

/// <summary>Saklama filtresine takılan, henüz yazılmamış kayıt. Yalnızca bellekte; loglanmaz.</summary>
public sealed record CandidateDraft(
    long EntryIndex, CtEntryType EntryType, DateTimeOffset LoggedAt, CertificateSummary Certificate, IReadOnlyList<string> MatchedTerms)
{
    public CtCandidate ToEntity(string logName, DateTimeOffset observedAt) => new(
        logName, EntryIndex, EntryType, LoggedAt, Certificate.NotBefore, Certificate.NotAfter, Certificate.Issuer,
        Certificate.DnsNames, MatchedTerms, observedAt);
}

/// <summary>
/// Tarafsız örneğe giren bir kayıtlı alan adı ve onu taşıyan log kaydı. Yalnızca bellekte; loglanmaz (Kural 4).
/// </summary>
public sealed record NeutralSampleDraft(string RegistrableDomain, long EntryIndex, DateTimeOffset LoggedAt, DateTimeOffset? NotBefore)
{
    public CtNeutralSample ToEntity(string logName, DateTimeOffset observedAt) =>
        new(RegistrableDomain, logName, EntryIndex, LoggedAt, NotBefore, observedAt);
}

/// <summary>İşlenmiş bir tam veri tile'ı: Merkle için yaprak özetleri, aday taslakları ve sayılar.</summary>
public sealed record ProcessedTile(
    long TileIndex,
    IReadOnlyList<byte[]> LeafHashes,
    IReadOnlyList<CandidateDraft> Candidates,
    int X509Entries,
    int PrecertEntries,
    int UnreadableCertificates,
    int CertificatesWithoutDnsNames,
    DateTimeOffset NewestEntry,
    IReadOnlyList<TimeSpan> Lifetimes)
{
    public int Entries => LeafHashes.Count;

    /// <summary>Tarafsız örneğe giren (kayıtlı alan adı, kayıt) çiftleri. Örnekleyici yoksa boş.</summary>
    public IReadOnlyList<NeutralSampleDraft> NeutralSamples { get; init; } = [];

    /// <summary>Sertifikası okunup filtreden geçirilen kayıt sayısı.</summary>
    public int Examined => Entries - UnreadableCertificates;
}

/// <summary>
/// Bir veri tile'ını ayrıştırır, her kaydın yaprak özetini hesaplar ve saklama filtresini uygular.
/// Ham tile burada biter; dışarıya yalnızca özetler, sayılar ve filtreden geçen taslaklar çıkar.
/// </summary>
/// <remarks>
/// İki kural birbirinden bağımsızdır: Kelime filtresi (<paramref name="filter"/>) ct_candidates'ı, tarafsız örnekleyici
/// (<paramref name="neutralSampler"/>) ct_neutral_sample'ı besler. Örnekleyici kelime filtresinin sonucuna bakmaz;
/// böylece örneklem kelimeden bağımsız kalır (Çerçeve N).
/// </remarks>
public sealed class TileProcessor(ICandidateFilter filter, NeutralSampler? neutralSampler = null)
{
    /// <exception cref="InvalidDataException">Tile biçimi bozuk; tile'ın tamamı reddedilir.</exception>
    public ProcessedTile Process(ReadOnlyMemory<byte> tile, long tileIndex, bool collectLifetimes = false)
    {
        var entries = DataTileParser.Parse(tile, tileIndex * TilePath.Width, TilePath.Width);
        var hashes = new byte[entries.Count][];
        var candidates = new List<CandidateDraft>();
        var neutral = new List<NeutralSampleDraft>();
        var lifetimes = new List<TimeSpan>();
        int x509 = 0, precert = 0, unreadable = 0, noNames = 0;
        var newest = DateTimeOffset.MinValue;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            hashes[i] = entry.LeafHash;
            if (entry.Type == CtEntryType.X509)
            {
                x509++;
            }
            else
            {
                precert++;
            }

            if (entry.Timestamp > newest)
            {
                newest = entry.Timestamp;
            }

            var summary = CertificateSummaryReader.TryRead(entry);
            if (summary is null)
            {
                unreadable++;
                continue;
            }

            if (collectLifetimes)
            {
                lifetimes.Add(summary.NotAfter - summary.NotBefore);
            }

            if (summary.DnsNames.Count == 0)
            {
                noNames++;
                continue;
            }

            if (neutralSampler is not null)
            {
                foreach (var domain in neutralSampler.Select(summary.DnsNames))
                {
                    neutral.Add(new NeutralSampleDraft(domain, entry.Index, entry.Timestamp, summary.NotBefore));
                }
            }

            var matched = filter.Match(summary.DnsNames);
            if (matched.Count > 0)
            {
                candidates.Add(new CandidateDraft(entry.Index, entry.Type, entry.Timestamp, summary, matched));
            }
        }

        return new ProcessedTile(tileIndex, hashes, candidates, x509, precert, unreadable, noNames, newest, lifetimes)
        {
            NeutralSamples = neutral,
        };
    }
}
