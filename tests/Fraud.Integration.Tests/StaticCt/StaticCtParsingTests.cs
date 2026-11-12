using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fraud.Collectors.Http;
using Fraud.Collectors.StaticCt;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Collection;

namespace Fraud.Integration.Tests.StaticCt;

/// <summary>Checkpoint imzası, tile ayrıştırma, sertifika alanları, saklama filtresi ve HTTP istemcisi. Ağ yok.</summary>
public sealed class StaticCtParsingTests : IDisposable
{
    private readonly TestCtLog _log = new TestCtLog().AddEntries(300);
    private readonly CtLogKey _key;

    public StaticCtParsingTests()
    {
        _key = CtLogKey.Create(TestCtLog.Origin, _log.PublicKey);
    }

    public void Dispose()
    {
        _key.Dispose();
        _log.Dispose();
    }

    // ---------- Checkpoint ----------

    [Fact]
    public void Key_id_follows_the_static_ct_formula()
    {
        var logId = SHA256.HashData(Convert.FromBase64String(_log.PublicKey));

        Assert.Equal(TestCtLog.KeyId(TestCtLog.Origin, logId), _key.KeyId);
        Assert.Equal(logId, _key.LogId);
    }

    [Fact]
    public void Valid_checkpoint_is_accepted_and_its_fields_are_read()
    {
        var (checkpoint, error) = CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(_log.Checkpoint(300)), _key);

        Assert.Equal(CheckpointError.None, error);
        Assert.NotNull(checkpoint);
        Assert.Equal(300, checkpoint.Size);
        Assert.Equal(_log.Root(300), checkpoint.RootHash);
        Assert.Equal(TestCtLog.BaseTime.AddMinutes(5), checkpoint.Timestamp);
    }

    [Fact]
    public void Changing_the_tree_size_breaks_the_signature()
    {
        var text = _log.Checkpoint(300).Replace("\n300\n", "\n299\n", StringComparison.Ordinal);

        Assert.Equal(CheckpointError.BadSignature, CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key).Error);
    }

    [Fact]
    public void Changing_the_root_breaks_the_signature()
    {
        var fakeRoot = Convert.ToBase64String(new byte[32]);
        var text = _log.Checkpoint(300).Replace(Convert.ToBase64String(_log.Root(300)), fakeRoot, StringComparison.Ordinal);

        Assert.Equal(CheckpointError.BadSignature, CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key).Error);
    }

    [Fact]
    public void Signature_from_another_key_with_our_key_id_is_rejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var text = _log.Checkpoint(300, signer: other);

        Assert.Equal(CheckpointError.BadSignature, CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key).Error);
    }

    [Fact]
    public void Checkpoint_with_only_unknown_key_ids_is_rejected()
    {
        var text = _log.Checkpoint(300, keyIdOverride: 0xDEADBEEF);

        Assert.Equal(CheckpointError.NoKnownSignature, CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key).Error);
    }

    [Fact]
    public void Unknown_witness_signatures_are_ignored()
    {
        var witness = "— witness.ornek.example " + Convert.ToBase64String(new byte[70]) + "\n";
        var text = _log.Checkpoint(300, extraSignatureLines: witness);

        Assert.Equal(CheckpointError.None, CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key).Error);
    }

    [Theory]
    [InlineData("log.baska.example/2026\n300\n")]
    public void Wrong_origin_is_rejected(string replacement)
    {
        var text = _log.Checkpoint(300).Replace(TestCtLog.Origin + "\n300\n", replacement, StringComparison.Ordinal);

        Assert.Equal(CheckpointError.WrongOrigin, CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key).Error);
    }

    [Theory]
    [InlineData("extension")]
    [InlineData("leading-zero")]
    [InlineData("no-blank-line")]
    [InlineData("control-char")]
    [InlineData("empty")]
    public void Malformed_checkpoints_are_rejected(string variant)
    {
        var good = _log.Checkpoint(300);
        var root = Convert.ToBase64String(_log.Root(300));
        var text = variant switch
        {
            "extension" => good.Replace(root + "\n", root + "\nek satir\n", StringComparison.Ordinal),
            "leading-zero" => good.Replace("\n300\n", "\n0300\n", StringComparison.Ordinal),
            "no-blank-line" => good.Replace("\n\n", "\n", StringComparison.Ordinal),
            "control-char" => good.Replace("\n300\n", "\n300\r\n", StringComparison.Ordinal),
            _ => string.Empty,
        };

        var result = CheckpointVerifier.Verify(Encoding.UTF8.GetBytes(text), _key);

        Assert.Null(result.Checkpoint);
        Assert.NotEqual(CheckpointError.None, result.Error);
    }

    // ---------- Veri tile'ı ----------

    [Fact]
    public void Data_tile_entries_hash_to_the_log_leaf_hashes()
    {
        var entries = DataTileParser.Parse(_log.DataTile(0), 0, 256);

        Assert.Equal(256, entries.Count);
        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(i, entries[i].Index);
            Assert.Equal(_log.LeafHash(i), entries[i].LeafHash);
            Assert.Equal(_log.TimestampOf(i), entries[i].Timestamp);
        }

        Assert.Contains(entries, e => e.Type == CtEntryType.Precert && e.IsTbsCertificate);
        Assert.Contains(entries, e => e.Type == CtEntryType.X509 && !e.IsTbsCertificate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(5000)]
    public void Truncated_data_tile_is_rejected_as_a_whole(int cut)
    {
        var tile = _log.DataTile(0);

        Assert.Throws<InvalidDataException>(() => DataTileParser.Parse(tile.AsMemory(0, tile.Length - cut), 0, 256));
    }

    [Fact]
    public void Tile_with_extra_bytes_or_wrong_count_is_rejected()
    {
        var tile = _log.DataTile(0);

        Assert.Throws<InvalidDataException>(() => DataTileParser.Parse(tile.Concat(new byte[] { 0 }).ToArray(), 0, 256));
        Assert.Throws<InvalidDataException>(() => DataTileParser.Parse(tile, 0, 255));
    }

    [Fact]
    public void Leaf_index_extension_must_match_the_position()
    {
        // Tile 1'in verisi tile 0'mış gibi sunulursa leaf_index uzantıları tutmaz.
        Assert.Throws<InvalidDataException>(() => DataTileParser.Parse(_log.DataTile(0), 256, 256));
    }

    [Fact]
    public void Garbage_tile_is_rejected()
    {
        var garbage = RandomNumberGenerator.GetBytes(4096);

        Assert.Throws<InvalidDataException>(() => DataTileParser.Parse(garbage, 0, 256));
    }

    // ---------- Sertifika alanları ----------

    [Fact]
    public void Certificate_and_precert_tbs_yield_dns_names_and_a_sanitized_issuer()
    {
        var entries = DataTileParser.Parse(_log.DataTile(0), 0, 256);
        var x509 = CertificateSummaryReader.TryRead(entries[0])!;   // anahtar kelimeli, x509
        var precert = CertificateSummaryReader.TryRead(entries[7])!; // anahtar kelimeli, ön sertifika

        foreach (var summary in new[] { x509, precert })
        {
            Assert.Equal(["www." + TestCtLog.KeywordName, TestCtLog.KeywordName], summary.DnsNames); // sıralı
            Assert.DoesNotContain('@', summary.Issuer);
            Assert.Equal(["C=TR", "CN=Ornek Test R1", "O=Ornek Test CA"], summary.Issuer.Split(", ").Order(StringComparer.Ordinal));
            Assert.Equal(TestCtLog.BaseTime.AddDays(-1), summary.NotBefore);
            Assert.Equal(TestCtLog.BaseTime.AddDays(89), summary.NotAfter);
        }
    }

    [Fact]
    public void Unreadable_certificate_returns_null()
    {
        Assert.Null(CertificateSummaryReader.TryReadCertificate(RandomNumberGenerator.GetBytes(300)));
        Assert.Null(CertificateSummaryReader.TryReadTbs(new byte[] { 0x30, 0x03, 0x02, 0x01 }));
    }

    // ---------- Saklama filtresi ----------

    [Fact]
    public void Default_filter_matches_terms_as_substrings_and_returns_them_sorted()
    {
        var filter = new KeywordCandidateFilter(new CandidateFilterOptions());

        Assert.Equal(["borsa", "yatirim"], filter.Match(["borsa-yatirim-ornek.example"]));
        Assert.Equal(["trade"], filter.Match(["sade-ornek.example", "*.trade-ornek.example"]));
        Assert.Empty(filter.Match(["sade-ornek.example"]));
        Assert.Empty(filter.Match([]));
    }

    [Fact]
    public void Turkish_idn_names_are_folded_before_matching()
    {
        var filter = new KeywordCandidateFilter(new CandidateFilterOptions());

        Assert.Equal(["yatirim"], filter.Match([TestCtLog.IdnKeywordName]));
    }

    [Fact]
    public void Configured_terms_replace_the_defaults_and_bad_terms_are_refused()
    {
        var options = new CandidateFilterOptions();
        options.Terms.Add("kaldirac");
        var filter = new KeywordCandidateFilter(options);

        Assert.Equal(["kaldirac"], filter.Terms);
        Assert.Empty(filter.Match(["yatirim-ornek.example"]));

        var bad = new CandidateFilterOptions();
        bad.Terms.Add("a b");
        Assert.Throws<InvalidOperationException>(() => new KeywordCandidateFilter(bad));
    }

    // ---------- HTTP istemcisi ----------

    private StaticCtClient Client(FakeCtServer server)
    {
        var options = new StaticCtOptions { MinRequestInterval = TimeSpan.Zero, RetryBaseDelay = TimeSpan.Zero, MaxRetryAttempts = 0 };
        var http = new HttpClient(server) { Timeout = Timeout.InfiniteTimeSpan };
        return new StaticCtClient(
            new PoliteHttpClient(http, new RequestPacer(TimeSpan.Zero, TimeProvider.System), options, TimeProvider.System),
            _log.Options(), _key, options.MaxResponseBytes);
    }

    [Fact]
    public async Task Partial_hash_tile_falls_back_to_the_full_tile_when_deleted()
    {
        using var server = new FakeCtServer(_log) { PublishedSize = 300, PartialTilesDeleted = true };
        var ct = TestContext.Current.CancellationToken;

        // 300 kayıtlık ağaçta tile/0/000 tamdır; 255'lik kısmi istek silinmiş, tam tile'ın ilk 255 özeti kullanılır.
        var result = await Client(server).GetHashTileAsync(0, 0, 255, ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(255 * 32, result.Value!.Length);
        Assert.Equal(_log.HashTile(0, 0, 255), result.Value);
        Assert.Equal(["tile/0/000.p/255", "tile/0/000"], server.Paths);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task Gzip_body_without_content_encoding_is_decompressed()
    {
        using var server = new FakeCtServer(_log) { PublishedSize = 300, GzipWithoutHeader = true };

        var result = await Client(server).GetDataTileAsync(0, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(_log.DataTile(0), result.Value);
    }

    [Fact]
    public async Task Hash_tile_of_the_wrong_size_is_an_invalid_response()
    {
        using var server = new FakeCtServer(_log)
        {
            PublishedSize = 300,
            Intercept = p => p.StartsWith("tile/0/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[100]) }
                : null,
        };

        var result = await Client(server).GetHashTileAsync(0, 0, 256, TestContext.Current.CancellationToken);

        Assert.Equal(CollectionErrorKind.InvalidResponse, result.ErrorKind);
    }

    [Fact]
    public async Task Checkpoint_signed_by_another_key_is_a_verification_failure()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var server = new FakeCtServer(_log) { PublishedSize = 300 };
        server.CheckpointOverride = size => _log.Checkpoint(size, signer: other);

        var result = await Client(server).GetCheckpointAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CollectionErrorKind.VerificationFailed, result.ErrorKind);
    }

    [Fact]
    public void Configured_log_options_are_validated()
    {
        var good = _log.Options();
        good.Validate();

        var http = _log.Options();
        http.MonitoringPrefix = new Uri("http://mon.test-ct.example/2026/");
        Assert.Throws<InvalidOperationException>(http.Validate);

        var badKey = _log.Options();
        badKey.PublicKey = "bm90LWEta2V5";
        Assert.Throws<InvalidOperationException>(badKey.Validate);

        var badStart = _log.Options("dun");
        Assert.Throws<InvalidOperationException>(badStart.Validate);
    }
}
