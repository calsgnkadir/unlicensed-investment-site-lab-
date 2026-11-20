using Fraud.Domain.Collection;

namespace Fraud.Collectors.Collection;

public sealed class CollectionOptions
{
    public const string SectionName = "Collection";

    /// <summary>
    /// İsteklerin çıktığı yerin kısa adı (ör. <c>lab-local</c>, <c>vps-de</c>). Her collection_log satırına yazılır.
    /// </summary>
    public string MeasurementLocation { get; set; } = "lab-local";

    /// <summary>
    /// Genel istemci adı. E-posta veya kişi adı içermez; proje adresi yayına alınınca eklenecek.
    /// </summary>
    public string UserAgent { get; set; } = "FraudLab/0.1 (passive research)";

    /// <summary>
    /// Kontrol üyelerinin haftalık DNS günleri (Türkiye saatiyle), iki ardışık gün. Üyeler bu iki güne tuzlu özetin tekliğine
    /// göre karışık bölünür (Değişiklik kaydı 2026-11-20, madde 4). Boşsa cumartesi ve pazar.
    /// </summary>
    public IList<DayOfWeek> ControlDnsDays { get; } = [];

    public IReadOnlyList<DayOfWeek> EffectiveControlDnsDays =>
        ControlDnsDays.Count == 2 ? [.. ControlDnsDays] : [DayOfWeek.Saturday, DayOfWeek.Sunday];

    public void Validate()
    {
        if (ControlDnsDays.Count is not (0 or 2) || (ControlDnsDays.Count == 2 && ControlDnsDays[0] == ControlDnsDays[1]))
        {
            throw new InvalidOperationException("Collection:ControlDnsDays iki farklı gün olmalı.");
        }

        if (string.IsNullOrWhiteSpace(MeasurementLocation) || MeasurementLocation.Length > 64)
        {
            throw new InvalidOperationException("Collection:MeasurementLocation boş olamaz (en fazla 64 karakter).");
        }

        if (string.IsNullOrWhiteSpace(UserAgent) || UserAgent.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Collection:UserAgent boş olamaz ve e-posta içeremez.");
        }
    }
}

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>İki toplama turu arasındaki bekleme.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Her turda sırayla çalışan kaynaklar. Yapılandırmadan gelen değerler eklenir.</summary>
    public IList<CollectionSource> Sources { get; } = [];

    public IReadOnlyList<CollectionSource> EffectiveSources =>
        Sources.Count > 0 ? Sources.Distinct().ToList() : [CollectionSource.CrtSh, CollectionSource.WaybackCdx];
}
