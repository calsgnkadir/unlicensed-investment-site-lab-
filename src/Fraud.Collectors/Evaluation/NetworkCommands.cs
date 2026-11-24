using System.Globalization;
using System.Net;
using System.Text;
using Fraud.Domain.DomainNames;
using Fraud.Evaluation;
using Fraud.Evaluation.Clustering;
using Fraud.Evaluation.Model;
using Fraud.Evaluation.Network;
using Fraud.Evaluation.Running;
using Fraud.Evaluation.Splitting;
using Fraud.Infrastructure.Persistence;

namespace Fraud.Collectors.Evaluation;

/// <summary>
/// Hafta 8 ağ betimlemesi ve pozitif küme analizi: <c>net-describe --config &lt;değerlendirme yapılandırması&gt;</c>.
/// </summary>
/// <remarks>
/// <para>Ağa istek atmaz; yalnızca veritabanındaki kayıtları okur. Ekrana yalnızca toplu sayı basar: alan adı, IP, NS adı ve
/// kontrol grubu için ASN yok.</para>
/// <para>Yapılandırma, ölçüm komutunun (<c>eval-run</c>) dosyasıyla aynıdır. Neden: Kontrollerin referans zamanı, bölme ve
/// bootstrap tohumu aynı kuralla ve aynı tohumla hesaplansın; betimleme ölçümden ayrı bir "referans" uydurmasın.</para>
/// <para>Kilitli test, taahhüt ve eşik seçimi bu komutta HİÇ çalışmaz. Kilit dosyasına dokunulmaz.</para>
/// </remarks>
public static class NetworkCommands
{
    public static async Task<int> DescribeAsync(
        FraudDbContext db, IDomainNameNormalizer normalizer, IDictionary<long, string> cdnAsns, IReadOnlyDictionary<string, string?> args,
        TextWriter output, RepoContext repo, Func<IPAddress, bool> isAllowedIp, CancellationToken cancellationToken, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(cdnAsns);
        if (!args.TryGetValue("config", out var configPath) || string.IsNullOrWhiteSpace(configPath))
        {
            await output.WriteLineAsync("Kullanım: net-describe --config <değerlendirme yapılandırması>").ConfigureAwait(false);
            return 64;
        }

        if (new FileInfo(configPath).Length > EvaluationConfig.MaxFileBytes)
        {
            await output.WriteLineAsync("Yapılandırma dosyası çok büyük.").ConfigureAwait(false);
            return 64;
        }

        var config = EvaluationConfig.Parse(await File.ReadAllTextAsync(configPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false));
        config.Validate();
        var at = now ?? DateTimeOffset.UtcNow;

        // 1) Birimler, sertifika ve ad gözlemleri: ölçüm komutuyla aynı yükleyici ve aynı kimlik anahtarı.
        var loader = new EvaluationDataLoader(db, normalizer, EvaluationDataLoader.LoadOrCreateKey(repo.PrivatePath(EvaluationCommands.SubjectKeyFile)));
        var data = await loader.LoadAsync(null, config.ArchiveKnownDelay, prospective: false, cancellationToken).ConfigureAwait(false);

        // 2) Kontrollerin referans zamanı: ölçümdeki kural (eşleşmiş gecikme, "şimdi"yi aşmadan).
        var assignment = ReferenceTimeAssigner.Assign(data.Subjects, config, at);
        var subjects = assignment.Subjects;

        // 3) Ağ kayıtları ve her birimin "o gün bilinen" özeti.
        var network = await new NetworkDataLoader(db, normalizer, cdnAsns, isAllowedIp)
            .LoadAsync(NetworkDataLoader.SubjectIdsByName(data.Observations), cancellationToken).ConfigureAwait(false);
        var index = new NetworkIndex(network);

        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("Ağ betimlemesi (Hafta 8). Yalnızca toplu sayılar: alan adı, IP, NS adı ve kontrol grubu için ASN yazılmaz.");
        sb.AppendLine("Bu bir tespit sonucu değildir: eşik, alarm ve yanlış alarm oranı yok. Etiket 'SPK erişim engeli listesinde' demektir, 'dolandırıcı' demek değildir.");
        sb.AppendLine(string.Create(c, $"Değerlendirme anı: {at:u}; kontrol referansı: {config.ControlReference}, gecikme havuzu (ayar pozitifi)={assignment.LagPoolSize}"));
        sb.AppendLine("Görünüm kuralı: Her birim yalnızca referans gününden önceki son TR gününün sonuna kadar sorgulanmış kaydı görür (DNS, RIPEstat, InternetDB: bilinme anı = sorgu anı). Pozitifin referansı SPK karar günüdür.");
        sb.AppendLine("Veri: DNS yalnız DoH; ölçüm konumu=" + (network.Locations.Count == 0 ? "-" : string.Join(",", network.Locations))
            + string.Create(c, $"; CDN/bulut listesi={cdnAsns.Count} ASN (RipeStat:CdnAsns); kullanılmayan hata satırı ('kayıt yok' sayılmadı): RIPEstat={network.IpAsnErrors}, InternetDB={network.PortErrors}"));
        sb.AppendLine("Dışarıda kalan: referansı olmayan kontrol=" + EvaluationReport.Cell(subjects.Count(s => !s.IsPositive && s.ReferenceTime is null))
            + " (ilk sertifikası bilinmeyen ya da referansı şimdiden sonra), pozitife bağlı kontrol (E05)=" + EvaluationReport.Cell(subjects.Count(s => s.LinkedToPositive)));
        sb.AppendLine("Payda: Her oranın paydası grubun tabanıdır (DNS kaydı bilinen birim). Yalnız port satırları iç içedir; üst satır küçük hücreyse alt satır yazılmaz. 5'ten küçük sayı ve tümleyeni yazılmaz; 10'dan az birimde oran yok.");
        sb.AppendLine("Aralık: %95 yüzdelik bootstrap, " + config.BootstrapIterations.ToString(c) + " tekrar, küme ve (pozitifte) bülten düzeyinde.");
        sb.AppendLine();

        var rows = NetworkDescription.Compute(subjects, index.ProfileOf, config);
        sb.Append(NetworkDescription.ToText(rows, config.MinCountForRates));
        sb.AppendLine();

        var positives = subjects.Where(s => s.IsPositive).ToList();
        var clusters = PositiveClusters.Analyze(positives, data.Observations, index.ProfileOf, config);
        sb.Append(PositiveClusters.ToText(clusters, config.MinCountForRates));
        sb.AppendLine();
        sb.AppendLine("Bu bir yatırım tavsiyesi değildir.");

        await output.WriteAsync(sb.ToString()).ConfigureAwait(false);
        return 0;
    }
}
