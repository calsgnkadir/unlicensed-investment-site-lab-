using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Fraud.Evaluation.Model;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Evaluation;

/// <summary>Yüklenen verinin sayıları. Alan adı yok.</summary>
public sealed record EvaluationLoadCounts(
    int Positives, int Controls, int ControlsRejected, int ControlsOverlappingPositives, int Certificates, int Archives, int DnsRows);

/// <summary>Yüklenen birimler ve gözlemler. Alan adları bu nesneye girmez; yalnızca opak kimlikler.</summary>
public sealed record EvaluationData(IReadOnlyList<Subject> Subjects, IReadOnlyList<Observation> Observations, EvaluationLoadCounts Counts);

/// <summary>
/// Ölçüm verisini veritabanından ve gizli CSV dosyalarından yükler. Alan adları yalnızca burada, bellekte görünür;
/// dışarıya opak kimliklerle çıkar.
/// </summary>
/// <remarks>
/// <para>Pozitifler: official_actions (SPK bülteni). Kayıtlı alan adı başına EN ERKEN karar tarihli bülten
/// kullanılır (KONTROL_GRUBU_YONTEMI.md Bölüm 7).</para>
/// <para>Kontroller: data/private/control altındaki CSV. Başlık:
/// <c>registrable_domain,group,subgroup,cluster,linked_to_positive,first_certificate_at,reference_time</c>.
/// Son üç sütun boş olabilir. Bir pozitifle aynı kayıtlı alan adı elenir (E01).</para>
/// <para>Kümeler: İsteğe bağlı CSV, başlık <c>registrable_domain,cluster</c>. Kümesi olmayan birim kendi kümesidir.</para>
/// <para>Opak kimlik: <c>HMAC-SHA256(anahtar, kayıtlı alan adı)</c>. Anahtar data/private altındadır. Anahtarsız bir özet
/// kullanılsaydı, kimliği gören biri tahmin ettiği adı özetleyip listede olup olmadığını deneyebilirdi.</para>
/// </remarks>
public sealed class EvaluationDataLoader(FraudDbContext db, IDomainNameNormalizer normalizer, byte[] idKey)
{
    /// <summary>
    /// Karar tarihi Türkiye saatiyle (UTC+3) gün başı. Neden: Bülten Türkiye'de yayımlanır; gün başını UTC'de almak
    /// referansı 3 saat geç yapar ve o gece bilinen bir kaydı "karardan önce" sayabilirdi. Erken almak tutucudur.
    /// </summary>
    public static DateTimeOffset DecisionTime(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3));

    public async Task<EvaluationData> LoadAsync(
        string? clustersCsv, TimeSpan archiveDelay, bool prospective, CancellationToken cancellationToken)
    {
        var clusters = clustersCsv is null ? new Dictionary<string, string>(StringComparer.Ordinal) : ReadClusters(clustersCsv);

        // 1) Pozitifler.
        var actions = await db.OfficialActions
            .Where(a => a.Source == OfficialActionSource.SpkBulletin)
            .Select(a => new { a.Domain.RegistrableDomain, a.BulletinNumber, a.DecisionDate })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var earliest = actions
            .GroupBy(a => a.RegistrableDomain, StringComparer.Ordinal)
            .Select(g => g.OrderBy(a => a.DecisionDate).ThenBy(a => BulletinNumber.Parse(a.BulletinNumber)).First())
            .ToList();
        var positiveNames = earliest.Select(a => a.RegistrableDomain).ToHashSet(StringComparer.Ordinal);

        // 2) Kontroller: control_members tablosundan (taahhüt edilmiş, kurma komutlarıyla seçilmiş üyeler). CSV yolu kaldırıldı:
        // Tek kaynak, seçimi kayda geçiren tablo ve taahhüttür; elle yazılmış bir liste değerlendirmeye giremez.
        var rejected = 0;
        var overlapping = 0;
        var memberRows = await db.ControlMembers
            .Select(m => new { m.Domain.RegistrableDomain, m.Group, m.Frame, m.Week, m.CommitmentId, m.FirstCertificateAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var controlRows = new List<ControlRow>();
        foreach (var m in memberRows)
        {
            if (positiveNames.Contains(m.RegistrableDomain))
            {
                overlapping++; // E01: sonradan SPK listesine girdi; pozitif olarak sayılır, kontrol olmaz.
                continue;
            }

            controlRows.Add(new ControlRow(m.RegistrableDomain, GroupOf(m.Group), FrameOf(m.Frame), false, m.FirstCertificateAt, null, m.Week, m.CommitmentId));
        }

        // 3) Gözlemler: kayıtlı alan adı → domains satırları.
        var allNames = positiveNames.Concat(controlRows.Select(c => c.Domain)).ToHashSet(StringComparer.Ordinal);
        var domainRows = await db.Domains
            .Where(d => allNames.Contains(d.RegistrableDomain))
            .Select(d => new { d.Id, d.RegistrableDomain })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var nameOfDomainId = domainRows.ToDictionary(d => d.Id, d => d.RegistrableDomain);
        var domainIds = nameOfDomainId.Keys.ToList();

        var certificates = await db.CertificateDomains
            .Where(cd => domainIds.Contains(cd.DomainId))
            .Select(cd => new { cd.DomainId, cd.Certificate.NotBefore, cd.Certificate.ObservedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var archives = await db.ArchiveSnapshots
            .Where(a => domainIds.Contains(a.DomainId))
            .Select(a => new { a.DomainId, a.CapturedAt, a.ObservedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var dns = await db.DnsSnapshots
            .Where(s => domainIds.Contains(s.DomainId))
            .Select(s => new { s.DomainId, s.QueriedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var observations = new List<Observation>();
        observations.AddRange(certificates.Select(c => new Observation(
            IdOf(nameOfDomainId[c.DomainId]), ObservationKinds.Certificate, KnownAtPolicy.Certificate(c.NotBefore, c.ObservedAt, prospective), c.ObservedAt)));
        observations.AddRange(archives.Select(a => new Observation(
            IdOf(nameOfDomainId[a.DomainId]), ObservationKinds.Archive,
            KnownAtPolicy.Archive(a.CapturedAt, a.ObservedAt, archiveDelay, prospective), a.ObservedAt)));
        observations.AddRange(dns.Select(s => new Observation(
            IdOf(nameOfDomainId[s.DomainId]), ObservationKinds.Dns, KnownAtPolicy.Network(s.QueriedAt), s.QueriedAt)));
        // RIPEstat ve InternetDB IP'ye bağlıdır; bir ağ sinyali onları istediğinde DNS cevabı üzerinden eklenir.
        // KnownAt kuralı hazırdır (KnownAtPolicy.Network): geçmiş veri testinde zaten görünmezler.

        var firstCertificate = certificates
            .GroupBy(c => nameOfDomainId[c.DomainId], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(c => c.NotBefore), StringComparer.Ordinal);

        // 4) Birimler.
        var subjects = new List<Subject>();
        foreach (var a in earliest)
        {
            var decided = DecisionTime(a.DecisionDate);
            DateTimeOffset? first = firstCertificate.TryGetValue(a.RegistrableDomain, out var f) ? f : null;
            // Yaş sınıfı: yeni (≤ 365 gün), eski (> 365 gün), bilinmiyor (ilk sertifika karardan sonra ya da hiç yok).
            // İlk sertifikası hiç bilinmeyen pozitif bölmede dışarıda kalır (mentör maddesi 8; kontrolle aynı kural).
            var subgroup = first is { } fc ? Subject.PositiveAge(decided, fc) : Subgroup.PositiveUnknownAge;
            subjects.Add(new Subject(
                IdOf(a.RegistrableDomain), SubjectGroup.Positive, subgroup, decided, BulletinNumber.Parse(a.BulletinNumber),
                ClusterOf(a.RegistrableDomain, clusters), firstCertificateAt: first));
        }

        // Aynı ad iki kez yazıldıysa ilk satır geçerli (aynı birim iki kez sayılmasın).
        foreach (var c in controlRows.DistinctBy(c => c.Domain, StringComparer.Ordinal))
        {
            DateTimeOffset? first = c.FirstCertificateAt ?? (firstCertificate.TryGetValue(c.Domain, out var f) ? f : null);
            subjects.Add(new Subject(
                IdOf(c.Domain), c.Group, c.Subgroup, c.ReferenceTime, bulletin: null, ClusterOf(c.Domain, clusters), c.Linked, first)
            {
                Week = c.Week,
                CommitmentId = c.CommitmentId,
            });
        }

        var counts = new EvaluationLoadCounts(earliest.Count, controlRows.Count, rejected, overlapping, certificates.Count, archives.Count, dns.Count);
        return new EvaluationData(subjects, observations, counts);
    }

    /// <summary>Kimlik anahtarı: yoksa 32 rastgele bayt üretilir. Yalnızca data/private altında tutulur.</summary>
    public static byte[] LoadOrCreateKey(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, key);
        return key;
    }

    /// <summary>Kayıtlı alan adının opak kimliği (ileriye dönük testte taahhüt kayıtlarını birimlere bağlamak için).</summary>
    public string SubjectIdOf(string registrableDomain) => IdOf(registrableDomain);

    private string IdOf(string registrableDomain) =>
        "s-" + Convert.ToHexStringLower(HMACSHA256.HashData(idKey, Encoding.UTF8.GetBytes(registrableDomain)))[..16];

    private string ClusterOf(string registrableDomain, Dictionary<string, string> clusters) =>
        clusters.TryGetValue(registrableDomain, out var label)
            ? "k-" + Convert.ToHexStringLower(HMACSHA256.HashData(idKey, Encoding.UTF8.GetBytes("cluster|" + label)))[..16]
            : IdOf(registrableDomain);

    private string? Normalize(string value) => normalizer.Normalize(value).Value?.RegistrableDomain;

    private Dictionary<string, string> ReadClusters(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cells in ReadRows(path, "registrable_domain,cluster"))
        {
            if (cells.Length >= 2 && Normalize(cells[0]) is { } name && cells[1].Trim().Length > 0)
            {
                map[name] = cells[1].Trim();
            }
        }

        return map;
    }

    /// <summary>Kontrol grubu → ölçüm grubu. G4a ve G4b ikisi de "rastgele"; G4b'yi çerçevesi (N) ayırır.</summary>
    public static SubjectGroup GroupOf(Fraud.Domain.Control.ControlGroup group) => group switch
    {
        Fraud.Domain.Control.ControlGroup.G1 => SubjectGroup.ControlLicensed,
        Fraud.Domain.Control.ControlGroup.G2 => SubjectGroup.ControlFinanceNews,
        Fraud.Domain.Control.ControlGroup.G3 => SubjectGroup.ControlHardNegative,
        Fraud.Domain.Control.ControlGroup.CryptoActive => SubjectGroup.ControlCryptoActive,
        _ => SubjectGroup.ControlRandom,
    };

    public static Subgroup FrameOf(Fraud.Domain.Control.SamplingFrame? frame) => frame switch
    {
        Fraud.Domain.Control.SamplingFrame.K => Subgroup.FrameTermConditioned,
        Fraud.Domain.Control.SamplingFrame.N => Subgroup.FrameNeutral,
        _ => Subgroup.None,
    };

    /// <summary>Basit CSV: virgülle ayrılmış, tırnaksız. Alan adlarında virgül olmaz. İlk satır beklenen başlık olmalı.</summary>
    private static IEnumerable<string[]> ReadRows(string path, string header)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        if (!string.Equals(reader.ReadLine()?.Trim(), header, StringComparison.Ordinal))
        {
            throw new InvalidDataException("CSV başlığı beklenen biçimde değil: " + header);
        }

        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length > 0)
            {
                yield return line.Split(',').Select(c => c.Trim()).ToArray();
            }
        }
    }

    private sealed record ControlRow(
        string Domain, SubjectGroup Group, Subgroup Subgroup, bool Linked, DateTimeOffset? FirstCertificateAt, DateTimeOffset? ReferenceTime,
        string? Week, string? CommitmentId);
}
