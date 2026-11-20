using System.Globalization;
using Fraud.Collectors.Collection;
using Fraud.Collectors.CrtSh;
using Fraud.Collectors.Evaluation;
using Fraud.Domain.Control;
using Fraud.Domain.DomainNames;
using Fraud.Infrastructure.DomainNames;
using Fraud.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Fraud.Collectors.Control;

/// <summary>Kontrol komutlarını komut satırından kurar: gerçek istemciler, depo kökü, gizli dosyalar.</summary>
public static class ControlCommandLine
{
    public static async Task<int> RunAsync(
        string command, IReadOnlyDictionary<string, string?> args, IServiceProvider services, string? gitPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        var repo = RepoContext.Find(gitPath: gitPath);
        var userAgent = services.GetRequiredService<CollectionOptions>().UserAgent;
        var output = Console.Out;

        using var radar = command == "control-build-g2" ? RadarFromTokenFile(repo, args) : null;
        using var tranco = command is "control-build-g2" or "control-build-g4a" ? new TrancoClient(userAgent) : null;
        var ctx = new ControlContext
        {
            Db = services.GetRequiredService<FraudDbContext>(),
            Full = services.GetRequiredService<IDomainNameNormalizer>(),
            IcannOnly = PublicSuffixDomainNormalizer.IcannOnly(),
            Repo = repo,
            Output = output,
            Time = services.GetRequiredService<TimeProvider>(),
            FirstCertificate = ControlCommands.CrtShFirstCertificate(services.GetRequiredService<ICrtShClient>()),
            Radar = radar,
            Tranco = tranco,
        };

        // crt.sh kullanan komutlar (G3, G4b, G3a) crt.sh kaynak kilidini alır: network servisi ya da elle başlatılmış
        // collect-crtsh aynı anda çalışıyorsa sorgu atılmaz (dakikada 5 istek sınırı süreçler arasında korunur).
        await using var crtShLock = command is "control-build-g3" or "control-build-g4b" or "control-g3a-trial"
            ? await services.GetRequiredService<Fraud.Collectors.Network.NetworkSourceLock>()
                .TryAcquireAsync(Fraud.Domain.Collection.CollectionSource.CrtSh, cancellationToken).ConfigureAwait(false)
            : null;
        if (command is "control-build-g3" or "control-build-g4b" or "control-g3a-trial" && crtShLock is null)
        {
            await output.WriteLineAsync("crt.sh başka bir süreç tarafından kullanılıyor (network servisi?). Önce durdurun; sorgu atılmadı.")
                .ConfigureAwait(false);
            return 75;
        }

        var maxQueries = int.TryParse(Arg(args, "max-queries"), NumberStyles.None, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 600;
        switch (command)
        {
            case "control-fetch-g1":
            {
                var (client, http) = ControlHttp.Create(ControlSourceOptions.Bddk(), userAgent);
                using (http)
                {
                    return await ControlCommands.FetchG1Async(
                        ctx, token => client.GetAsync(new Uri("https://www.bddk.org.tr/Kurulus/Liste/77"), token), cancellationToken).ConfigureAwait(false);
                }
            }

            case "control-build-g1":
                return Arg(args, "input") is { } input
                    ? await ControlCommands.BuildG1Async(ctx, input, cancellationToken).ConfigureAwait(false)
                    : await UsageAsync(output, "control-build-g1 --input <kaydedilmiş BDDK HTML>").ConfigureAwait(false);

            case "control-build-g2":
                // Liste kimliği komut satırından alınmaz (C1): docs/evaluation/control_sources.json.
                return await ControlCommands.BuildG2Async(ctx, cancellationToken).ConfigureAwait(false);

            case "control-build-g4a":
                return await ControlCommands.BuildG4aAsync(ctx, cancellationToken).ConfigureAwait(false);

            case "control-build-g3":
            case "control-build-g4b":
                return await ControlCommands.BuildWeeklyAsync(
                    ctx, command == "control-build-g3" ? ControlGroup.G3 : ControlGroup.G4b, Arg(args, "week") ?? string.Empty, maxQueries,
                    cancellationToken).ConfigureAwait(false);

            default:
            {
                var (client, http) = G3aTrial.CreateClient(userAgent);
                using (http)
                {
                    return await G3aTrial.RunAsync(ctx, client.GetAsync, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Radar token'ı dosyadan okunur (varsayılan deploy/linux/secrets/cloudflare_radar_token). Token yalnızca istemcinin
    /// Authorization başlığına gider; ekrana, loga ve hata mesajına yazılmaz. Dosya yoksa <c>null</c>: komut net bir
    /// mesajla durur.
    /// </summary>
    public static RadarClient? RadarFromTokenFile(RepoContext repo, IReadOnlyDictionary<string, string?> args)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(args);
        var path = Arg(args, "token-file") ?? Path.Combine(repo.PrivateRootBase, ControlCommands.RadarTokenFile);
        if (!File.Exists(path) || new FileInfo(path).Length is 0 or > 4096)
        {
            return null;
        }

        var token = File.ReadAllText(path).Trim();
        return token.Length == 0 || token.Any(char.IsWhiteSpace) ? null : new RadarClient(token, "FraudLab/0.1 (passive research)");
    }

    private static async Task<int> UsageAsync(TextWriter output, string usage)
    {
        await output.WriteLineAsync("Kullanım: " + usage).ConfigureAwait(false);
        return 64;
    }

    private static string? Arg(IReadOnlyDictionary<string, string?> args, string name) =>
        args.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
