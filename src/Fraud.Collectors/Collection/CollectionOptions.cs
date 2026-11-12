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

    public void Validate()
    {
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
