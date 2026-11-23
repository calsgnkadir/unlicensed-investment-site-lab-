using Fraud.Domain.Signals;
using Fraud.Evaluation;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Signals;

namespace Fraud.Signals;

/// <summary>Sinyallerin dış kaynakları. Dosya okumaz: Çağıran (Collectors) okuyup verir.</summary>
/// <param name="Lexicon">Geniş finans sözlüğü (donmuş). Yalnızca new-cert-lexicon için gerekir.</param>
/// <param name="Brands">Marka listesi. Yalnızca brand için gerekir.</param>
public sealed record SignalResources(FinanceLexicon? Lexicon, BrandList? Brands);

/// <summary>Yapılandırmadaki sinyal adını nesneye çevirir. Bilinmeyen ad <see cref="SignalConfig.Validate"/>'de zaten reddedilir.</summary>
public static class SignalCatalog
{
    /// <summary>Bu adların sinyali hangi kaynağa ihtiyaç duyar (çağıran yalnızca gerekeni okusun).</summary>
    public static bool NeedsLexicon(SignalConfig config) => Selected(config).Contains("new-cert-lexicon");

    public static bool NeedsBrands(SignalConfig config) => Selected(config).Contains("brand");

    /// <summary>Çalıştırılacak tek sinyal: <c>Name</c>; "sum" ise <c>Parts</c>'ın ağırlıksız toplamı.</summary>
    public static ISignal Create(SignalConfig config, SignalResources resources)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return config.Name == "sum" ? new SumSignal(Parts(config, resources)) : One(config.Name, config, resources);
    }

    /// <summary>Ablasyonun parçaları, yapılandırmadaki sırayla.</summary>
    public static IReadOnlyList<ISignal> Parts(SignalConfig config, SignalResources resources)
    {
        ArgumentNullException.ThrowIfNull(config);
        return [.. config.Parts.Select(p => One(p, config, resources))];
    }

    private static ISignal One(string name, SignalConfig config, SignalResources resources) => name switch
    {
        "random" => new RandomSignal(config.Seed),
        "baseline" => new BaselineSignal(),
        "brand" => new BrandSimilaritySignal(resources.Brands ?? throw new InvalidOperationException("brand sinyali için marka listesi yok.")),
        "new-cert-lexicon" => new NewCertificateLexiconSignal(
            resources.Lexicon ?? throw new InvalidOperationException("new-cert-lexicon sinyali için sözlük yok."), config.NewCertificateDays),
        "cert-burst" => new CertificateBurstSignal(config.BurstWindowDays, config.BurstMinCertificates),
        "numbered" => new NumberedNameSignal(),
        _ => throw new InvalidOperationException("Bilinmeyen sinyal."),
    };

    private static HashSet<string> Selected(SignalConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var names = new HashSet<string>(StringComparer.Ordinal) { config.Name };
        if (config.Name == "sum" || config.Ablation)
        {
            names.UnionWith(config.Parts);
        }

        return names;
    }
}
