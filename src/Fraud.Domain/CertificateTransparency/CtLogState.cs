namespace Fraud.Domain.CertificateTransparency;

/// <summary>
/// Bir sertifika logunu izlerken kalınan yer (tablo: ct_log_state). Program yeniden başlayınca buradan devam edilir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NextIndex"/> her zaman 256'nın katıdır (tam veri tile'ı sınırı). Ondan önceki bütün kayıtlar ya
/// işlenmiş ya da atlanmış ve atlanması collection_log'a hata olarak yazılmıştır.
/// </para>
/// <para>
/// <see cref="Frontier"/>, [0, NextIndex) aralığının Merkle "sağ kenarı"dır: İkinin kuvveti büyüklüğünde, büyükten
/// küçüğe sıralı alt ağaç kökleri (32 bayt, art arda). Sonraki tur kendi okuduğu kayıtları buna ekleyerek
/// checkpoint kökünü yeniden hesaplar. Boşsa (ilk başlangıç veya atlanan aralık sonrası) hash tile'larından yeniden kurulur.
/// </para>
/// <para>
/// <see cref="PendingCheckpointSize"/> ve <see cref="PendingCheckpointRoot"/>: Kökü doğrulanmış ama akışın henüz
/// içinden geçmediği en eski checkpoint. Akış o boyuta ulaşınca kök kayıtlardan yeniden hesaplanıp karşılaştırılır;
/// böylece ardışık checkpoint'lerin tutarlı olduğu (logun geçmişi değiştirmediği) kontrol edilir.
/// </para>
/// </remarks>
public sealed class CtLogState
{
    public const int HashSize = 32;
    public const int TileWidth = 256;

    // EF Core için.
    private CtLogState()
    {
        LogName = string.Empty;
        PendingCheckpointRoot = [];
    }

    public CtLogState(
        string logName, long nextIndex, long checkpointSize, byte[] checkpointRoot, DateTimeOffset checkpointTimestamp, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        ArgumentNullException.ThrowIfNull(checkpointRoot);
        if (nextIndex < 0 || nextIndex % TileWidth != 0 || nextIndex > checkpointSize)
        {
            throw new ArgumentOutOfRangeException(nameof(nextIndex), "Başlangıç indeksi tile sınırında ve checkpoint içinde olmalı.");
        }

        if (checkpointRoot.Length != HashSize)
        {
            throw new ArgumentException("Kök 32 bayt olmalı.", nameof(checkpointRoot));
        }

        LogName = logName;
        NextIndex = nextIndex;
        PendingCheckpointSize = checkpointSize;
        PendingCheckpointRoot = checkpointRoot;
        PendingCheckpointTimestamp = checkpointTimestamp;
        LatestCheckpointSize = checkpointSize;
        UpdatedAt = now;
    }

    public string LogName { get; private set; }

    /// <summary>Sıradaki işlenecek kaydın indeksi (256'nın katı).</summary>
    public long NextIndex { get; private set; }

    /// <summary>[0, NextIndex) aralığının Merkle sağ kenarı. Boşsa yeniden kurulmalı.</summary>
    public byte[]? Frontier { get; private set; }

    public long PendingCheckpointSize { get; private set; }

    public byte[] PendingCheckpointRoot { get; private set; }

    /// <summary>Bekleyen checkpoint'in imzasındaki zaman (kaynağın tarihi).</summary>
    public DateTimeOffset PendingCheckpointTimestamp { get; private set; }

    /// <summary>Görülen en büyük, imzası ve kökü doğrulanmış checkpoint boyutu. Log geriye giderse hata verilir.</summary>
    public long LatestCheckpointSize { get; private set; }

    /// <summary>İşlenen (sertifikası okunup filtreden geçirilen) kayıt sayısı.</summary>
    public long EntriesProcessed { get; private set; }

    /// <summary>Atlanan kayıt sayısı: indirilemeyen veya doğrulanamayan tile'lar ve okunamayan sertifikalar.</summary>
    public long EntriesSkipped { get; private set; }

    /// <summary>ct_candidates tablosuna ilk kez yazılan kayıt sayısı.</summary>
    public long CandidatesStored { get; private set; }

    /// <summary>
    /// <see cref="NextIndex"/>'ten başlayan aralık üst üste kaç turda işlenemedi. Sınırı geçince aralık atlanır.
    /// Veritabanında durduğu için yeniden başlatma sayacı sıfırlamaz.
    /// </summary>
    public int ConsecutiveFailures { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>İşlenen ve atlanan toplamı içinde işlenenin payı. Hiç kayıt yoksa <c>null</c>.</summary>
    public double? Coverage => EntriesProcessed + EntriesSkipped == 0
        ? null
        : (double)EntriesProcessed / (EntriesProcessed + EntriesSkipped);

    /// <summary>Doğrulanmış bir aralığı işlendi olarak işaretler.</summary>
    public void Advance(long newNextIndex, byte[] frontier, long processed, long skipped, long newCandidates, DateTimeOffset now)
    {
        CheckForward(newNextIndex);
        CheckFrontier(frontier);
        ArgumentOutOfRangeException.ThrowIfNegative(processed);
        ArgumentOutOfRangeException.ThrowIfNegative(skipped);
        ArgumentOutOfRangeException.ThrowIfNegative(newCandidates);

        NextIndex = newNextIndex;
        Frontier = frontier;
        EntriesProcessed += processed;
        EntriesSkipped += skipped;
        CandidatesStored += newCandidates;
        ConsecutiveFailures = 0;
        UpdatedAt = now;
    }

    /// <summary>Sıradaki aralık bu turda işlenemedi; sayaç bir artar, indeks değişmez.</summary>
    public void RecordFailure(DateTimeOffset now)
    {
        ConsecutiveFailures++;
        UpdatedAt = now;
    }

    /// <summary>
    /// Bir aralığı atlar: Kayıtlar okunmadı, sayaçlara "atlanan" olarak girer. Merkle sağ kenarı artık bilinmediği
    /// için silinir; sonraki tur onu hash tile'larından yeniden kurar.
    /// </summary>
    public void Skip(long newNextIndex, DateTimeOffset now)
    {
        CheckForward(newNextIndex);
        EntriesSkipped += newNextIndex - NextIndex;
        NextIndex = newNextIndex;
        Frontier = null;
        ConsecutiveFailures = 0;
        UpdatedAt = now;
    }

    /// <summary>Hash tile'larından yeniden kurulan sağ kenarı kaydeder; indeks değişmez.</summary>
    public void SetFrontier(byte[] frontier)
    {
        CheckFrontier(frontier);
        Frontier = frontier;
    }

    /// <summary>Kökü doğrulanmış yeni checkpoint'i kaydeder.</summary>
    /// <param name="replacePending">Bekleyen checkpoint'in tutarlılığı kontrol edildiyse (veya hiç yoksa) yenisi bekleyen olur.</param>
    public void ObserveCheckpoint(long size, byte[] root, DateTimeOffset timestamp, bool replacePending)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (size < LatestCheckpointSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Checkpoint geriye gidemez.");
        }

        if (root.Length != HashSize)
        {
            throw new ArgumentException("Kök 32 bayt olmalı.", nameof(root));
        }

        LatestCheckpointSize = size;
        if (replacePending)
        {
            PendingCheckpointSize = size;
            PendingCheckpointRoot = root;
            PendingCheckpointTimestamp = timestamp;
        }
    }

    private void CheckForward(long newNextIndex)
    {
        if (newNextIndex <= NextIndex || newNextIndex % TileWidth != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newNextIndex), "İndeks yalnızca ileri ve tile sınırına gider.");
        }
    }

    private static void CheckFrontier(byte[] frontier)
    {
        ArgumentNullException.ThrowIfNull(frontier);
        if (frontier.Length % HashSize != 0)
        {
            throw new ArgumentException("Sağ kenar 32 baytlık özetlerden oluşur.", nameof(frontier));
        }
    }
}
