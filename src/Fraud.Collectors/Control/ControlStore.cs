using System.Text.Json;
using Fraud.Domain.Control;
using Fraud.Domain.DomainNames;
using Fraud.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Collectors.Control;

/// <summary>Seçilen bir üye (kaydedilmeden önce). Alan adı yalnızca bellekte; hiçbir yere basılmaz.</summary>
public sealed record SelectedMember(
    string RegistrableDomain, SamplingFrame? Frame, DateTimeOffset? FirstLoggedAt = null, DateTimeOffset? FirstCertificateAt = null,
    string? DictionarySubset = null);

/// <summary>control_members ve control_builds tablolarının okunması ve yazılması.</summary>
public sealed class ControlStore(FraudDbContext db, IDomainNameNormalizer normalizer)
{
    /// <summary>Pozitiflerin (official_actions) kayıtlı alan adları: E01 elemesi.</summary>
    public async Task<IReadOnlySet<string>> PositiveRegistrablesAsync(CancellationToken cancellationToken) =>
        (await db.OfficialActions.Select(a => a.Domain.RegistrableDomain).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Daha önce herhangi bir gruba girmiş adlar: E02 elemesi (öncelik G1 &gt; G2 &gt; G3 &gt; G4, kurma sırasıyla).</summary>
    public async Task<IReadOnlySet<string>> ExistingMembersAsync(CancellationToken cancellationToken) =>
        (await db.ControlMembers.Select(m => m.Domain.RegistrableDomain).ToListAsync(cancellationToken).ConfigureAwait(false))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Bu grup bu hafta (ya da tek seferlik) kuruldu mu?</summary>
    public Task<bool> IsBuiltAsync(ControlGroup group, string weekKey, CancellationToken cancellationToken) =>
        db.ControlBuilds.AnyAsync(b => b.Group == group && b.WeekKey == weekKey, cancellationToken);

    /// <summary>
    /// Üyeleri ve kurma kaydını tek veritabanı işleminde yazar. (grup, hafta) tekil indeksi ikinci yazmayı reddeder; böylece
    /// iki süreç aynı haftayı aynı anda kursa bile yalnızca biri kaydolur.
    /// </summary>
    public async Task SaveAsync(
        ControlGroup group, string weekKey, string seed, string? sourceListId, string? commitmentId,
        IReadOnlyList<SelectedMember> members, IReadOnlyDictionary<string, int> counts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(counts);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var week = weekKey == ControlBuild.OneShot ? null : weekKey;
        foreach (var member in members)
        {
            var domain = await DomainRowAsync(member.RegistrableDomain, now, cancellationToken).ConfigureAwait(false);
            db.ControlMembers.Add(new ControlMember(
                domain, group, member.Frame, now, week, seed, sourceListId, commitmentId, member.FirstLoggedAt, member.FirstCertificateAt,
                member.DictionarySubset));
        }

        db.ControlBuilds.Add(new ControlBuild(group, weekKey, now, seed, sourceListId, commitmentId, members.Count, JsonSerializer.Serialize(counts)));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>domains satırı: kayıtlı alan adının kendisi. Varsa onu kullanır.</summary>
    private async Task<DomainName> DomainRowAsync(string registrable, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Domains.SingleOrDefaultAsync(d => d.Host == registrable, cancellationToken).ConfigureAwait(false)
                       ?? db.Domains.Local.SingleOrDefault(d => d.Host == registrable);
        if (existing is not null)
        {
            return existing;
        }

        var normalized = normalizer.Normalize(registrable).Value
            ?? throw new InvalidOperationException("Seçilen ad normalleştirilemedi (beklenmez: seçim normalleştirilmiş adlarla yapılır).");
        var row = new DomainName(normalized, now);
        db.Domains.Add(row);
        return row;
    }
}
