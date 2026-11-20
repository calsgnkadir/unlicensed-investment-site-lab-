using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using Fraud.Collectors.Control;
using Fraud.Infrastructure.DomainNames;
using Fraud.Security.Tests.Infrastructure;

namespace Fraud.Security.Tests.Control;

/// <summary>
/// Kontrol grubu altyapısı: Tranco ZIP, BDDK HTML ve Radar JSON güvenilmez girdidir; Radar token'ı yalnızca
/// api.cloudflare.com'a gider; önkoşul denemeleri "tutana kadar dene" yoluna izin vermemeli. Ağ yok.
/// </summary>
public sealed class ControlAttackTests
{
    private static byte[] Zip(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.SmallestSize);
            using var s = entry.Open();
            s.Write(content);
        }

        return ms.ToArray();
    }

    /// <summary>Yerel başlık ve merkezi dizindeki "açılmış boyut" alanını yalan bir değerle değiştirir.</summary>
    private static byte[] LieAboutSize(byte[] zip, uint declared)
    {
        var bytes = (byte[])zip.Clone();
        for (var i = 0; i + 4 <= bytes.Length; i++)
        {
            var sig = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
            if (sig == 0x04034b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 22), declared);
            }
            else if (sig == 0x02014b50)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24), declared);
            }
        }

        return bytes;
    }

    [Fact]
    public void Zip_entry_declaring_more_than_the_limit_is_refused()
    {
        var honest = Zip("top-1m.csv", new byte[ControlSourceOptions.Tranco().MaxResponseBytes + 1]);

        Assert.Throws<InvalidDataException>(() => TrancoClient.Unzip(honest));
    }

    [Fact]
    public void Zip_bomb_lying_about_its_size_cannot_expand_past_the_limit()
    {
        // ~1000:1 sıkıştırma: 96 MB'lık bir gövde sınırı ~96 GB'a açılabilir. Bildirilen boyut saldırganın yazdığı bir alandır.
        var limit = ControlSourceOptions.Tranco().MaxResponseBytes;
        var bomb = LieAboutSize(Zip("top-1m.csv", new byte[limit + (8 * 1024 * 1024)]), declared: 1000);
        Assert.True(bomb.Length < 1024 * 1024, "Ön koşul: sıkıştırılmış gövde küçük");

        byte[]? result = null;
        var exception = Record.Exception(() => result = TrancoClient.Unzip(bomb));

        // Kabul edilen sonuçlar: istisna (InvalidDataException) ya da sınırın altında bir çıktı.
        Assert.True(exception is InvalidDataException || (result is not null && result.LongLength <= limit),
            $"Açılan boyut {result?.LongLength}, istisna {exception?.GetType().Name}");
    }

    [Fact]
    public void Zip_entry_names_are_never_used_as_paths()
    {
        // Yol geçişi: Unzip yalnızca belleğe okur, diske yazmaz; ad hiçbir yerde kullanılmaz.
        var zip = Zip("../../../../etc/cron.d/x", "1,ornek.example\n"u8.ToArray());

        Assert.Equal("1,ornek.example\n"u8.ToArray(), TrancoClient.Unzip(zip));
        Assert.DoesNotContain("ZipFile", RepoFiles.Read("src/Fraud.Collectors/Control/ControlSourceClients.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractToDirectory", RepoFiles.Read("src/Fraud.Collectors/Control/ControlSourceClients.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("vbscript:x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://banka.example/")]
    [InlineData("/Kurulus/Detay/1")]
    [InlineData("https://www.bddk.org.tr/x")]
    [InlineData("https://bddk.org.tr/x")]
    public void Bddk_parser_drops_non_http_internal_and_script_links(string href)
    {
        var html = $"<html><body><a href=\"{href}\">www.banka.example</a></body></html>";

        Assert.Empty(BddkBankListParser.Parse(html).Urls);
    }

    [Theory]
    [InlineData("http://user:pass@banka.example/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.169.254/latest/")]
    [InlineData("http://banka.example@evil.example/")]
    public void Userinfo_and_ip_links_never_become_control_members(string href)
    {
        // Ayrıştırıcı bunları adres olarak geçirebilir; uygunluk denetimindeki normalleştirici reddetmeli.
        var normalizer = PublicSuffixDomainNormalizer.IcannOnly();

        var registrable = normalizer.Normalize(href).Value?.RegistrableDomain;

        Assert.Null(registrable);
    }

    [Fact]
    public void Bddk_parser_is_bounded_on_pathological_html()
    {
        var html = string.Concat(Enumerable.Repeat("<a href=\"https://x.example/\">", 60_000));
        var started = System.Diagnostics.Stopwatch.StartNew();

        var exception = Record.Exception(() => BddkBankListParser.Parse(html));

        // Kapanmayan binlerce <a>: ya hızlı biter ya da regex zaman aşımıyla durur; süreç takılmaz.
        Assert.True(exception is null or System.Text.RegularExpressions.RegexMatchTimeoutException, exception?.GetType().Name);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30), $"Süre {started.Elapsed}");
        Assert.Throws<InvalidDataException>(() => BddkBankListParser.Parse(new string('a', BddkBankListParser.MaxHtmlBytes + 1)));
    }

    [Theory]
    [InlineData("""{"success":true,"result":{"details_0":{"categories":[{"name":"News"},{"name":7},"x",{"id":1}]}}}""", 1)]
    [InlineData("""{"success":false,"result":{"details_0":{"categories":[{"name":"News"}]}}}""", -1)]
    [InlineData("""{"success":"true","result":{}}""", -1)]
    [InlineData("""[1,2]""", -1)]
    [InlineData("not json", -1)]
    [InlineData("""{"success":true,"result":{"details_0":{}}}""", 0)]
    public void Radar_json_is_read_strictly(string json, int expected)
    {
        var categories = RadarCategoryParser.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(expected, categories?.Count ?? -1);
    }

    [Fact]
    public async Task Radar_token_is_not_sent_again_after_a_redirect_and_never_appears_in_results()
    {
        const string Token = "gizli-token-deger-XYZ";
        var handler = new StubHttpHandler().EnqueueRedirect("https://evil.example/steal");
        using var radar = new RadarClient(Token, "FraudLab/test", handler);

        var lookup = await radar.CategoriesAsync("ornek.example", TestContext.Current.CancellationToken);

        Assert.True(lookup.IsError);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.cloudflare.com", request.Host);
        Assert.DoesNotContain(Token, System.Text.Json.JsonSerializer.Serialize(lookup), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ornek.example/../../zones")]
    [InlineData("ornek.example?x=1")]
    [InlineData("ornek.example#a")]
    [InlineData("@evil.example")]
    [InlineData("ornek .example")]
    public async Task Radar_lookup_rejects_names_that_could_change_the_api_path(string name)
    {
        var handler = new StubHttpHandler();
        using var radar = new RadarClient("t", "FraudLab/test", handler);

        var lookup = await radar.CategoriesAsync(name, TestContext.Current.CancellationToken);

        Assert.True(lookup.IsError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Control_outputs_and_token_are_git_ignored()
    {
        var gitignore = RepoFiles.Read(".gitignore");

        Assert.Contains("**/data/private/", gitignore, StringComparison.Ordinal);
        Assert.Contains("deploy/linux/secrets/", gitignore, StringComparison.Ordinal);
        Assert.Equal("deploy/linux/secrets/cloudflare_radar_token", ControlCommands.RadarTokenFile);
    }

    // ---- Açık bulgular ----

    [Fact(Skip = "açık: C1 G2 önkoşul denemesi ve seçim tohumu komut satırındaki --tranco-id'ye bağlı; deneme geçmezse başka bir günün listesiyle yeniden denenebilir")]
    public void G2_trial_and_seed_cannot_be_rerolled_with_another_tranco_list()
    {
        // Bulgu C1: Deneme kaydı "g2-onkosul-<listId>.json"; yalnızca AYNI liste kimliğini engelliyor. Tranco her gün yeni bir
        // liste yayımlar. Deneme %80'in altında kalırsa ertesi günün kimliğiyle yeni bir deneme açılır; "%80 tutana kadar dene"
        // mümkün. Seçim tohumu da kimlikten türüyor (ControlSeeds.For("g2", listId)). Yöntem belgesi "liste tek bir tarihe
        // sabitlenir" diyor ama kod hangi kimliğin sabitlendiğini bilmiyor. Kimlik (ve tarihi) seçimden önce commit edilmiş bir
        // dosyada sabitlenmeli ve komut yalnızca onu kabul etmeli; deneme kaydı listeden bağımsız tek bir dosya olmalı.
        var code = RepoFiles.Read("src/Fraud.Collectors/Control/ControlCommands.cs");

        Assert.DoesNotContain("\"g2-onkosul-\" + listId", code, StringComparison.Ordinal);
    }

    [Fact(Skip = "açık: C2 önkoşul denemelerinin kaydı sonuç görüldükten sonra yazılıyor; yarıda kesip ya da hata ürettirip yeniden denenebilir")]
    public void Trials_are_recorded_before_the_first_query()
    {
        // Bulgu C2: G3a denemesi her denemenin sonucunu ("Deneme 1: TooFew ...") hemen basıyor, kayıt dosyasını ise üç denemeden
        // sonra yazıyor. İlk deneme kötü görünürse Ctrl+C ile kesmek kayıt bırakmaz; crt.sh cevapları (zaman aşımı, kesik) zamanla
        // değiştiği için yeniden çalıştırma sonucu değiştirebilir. G2 denemesi de hata varsa ("token, izin ya da ağ") sayıları
        // basıp kaydetmiyor; hata ürettirmek denemeyi silmenin yolu olur. Kayıt, ilk sorgudan ÖNCE "başladı" olarak (CreateNew)
        // yazılmalı; yarıda kalan ya da hatalı deneme de "kullanıldı" sayılmalı (kilitli test ile aynı kural).
        var g3a = RepoFiles.Read("src/Fraud.Collectors/Control/G3aTrial.cs");
        Assert.True(g3a.IndexOf("FileMode.CreateNew", StringComparison.Ordinal) < g3a.IndexOf("await fetch(", StringComparison.Ordinal),
            "G3a kaydı ilk sorgudan sonra yazılıyor");

        var g2 = RepoFiles.Read("src/Fraud.Collectors/Control/ControlCommands.cs");
        Assert.True(g2.IndexOf("WriteNewAsync(trialFile", StringComparison.Ordinal) < g2.IndexOf("await G2TrialAsync(", StringComparison.Ordinal),
            "G2 deneme kaydı denemeden sonra yazılıyor");
    }
}
