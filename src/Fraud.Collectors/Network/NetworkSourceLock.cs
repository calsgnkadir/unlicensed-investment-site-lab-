using Fraud.Domain.Collection;
using Npgsql;

namespace Fraud.Collectors.Network;

/// <summary>Alınmış bir ağ kaynağı kilidi. Bırakmak için dispose edilir.</summary>
public interface INetworkSourceLockHandle : IAsyncDisposable
{
    /// <summary>
    /// Kilit hâlâ bu süreçte mi? Kilit bağlantısı koptuysa PostgreSQL kilidi bırakmıştır; o zaman <c>false</c> döner,
    /// istisna fırlatmaz. Çalıştırıcılar bunu her hedeften önce çağırır (THREAT_MODEL N9).
    /// </summary>
    Task<bool> IsHeldAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Ağ kaynağı (DNS, RIPEstat, InternetDB) başına süreçler arası kilit: Aynı kaynağı aynı anda tek süreç toplar.
/// </summary>
/// <remarks>
/// <para>
/// Neden: Hız sınırlayıcı (<c>RequestPacer</c>) süreç içidir. <c>network</c> servisi çalışırken elle bir
/// <c>collect-dns</c> başlatılırsa iki süreç aynı çözücüye saniyede 2 paket gönderir (THREAT_MODEL N8).
/// </para>
/// <para>
/// Yöntem <c>CtLogLock</c> ile aynıdır: Oturum düzeyinde PostgreSQL advisory lock (<c>pg_try_advisory_lock</c>),
/// havuza girmeyen ayrı bir bağlantı üzerinde tutulur. Süreç çökerse veya bağlantı koparsa PostgreSQL kilidi kendisi
/// bırakır. Anahtar: sabit ad alanı (<see cref="Namespace"/>) ve kaynağın sayısal değeri.
/// </para>
/// <para>
/// Veritabanı bağlantı metni yoksa (ör. yalnızca birim testinde) <see cref="Unlocked"/> kullanılabilir.
/// </para>
/// </remarks>
public class NetworkSourceLock
{
    /// <summary>"NETW" baytları. Ağ kaynakları kilitleri için ayrılmış ad alanı (CT kilitlerinden ayrı).</summary>
    public const int Namespace = 0x4E455457;

    public const string ApplicationName = "fraud-network-lock";

    private readonly string? _connectionString;

    public NetworkSourceLock(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // Kilit bağlantıya bağlıdır; havuza dönen bağlantıda asılı kalmasın.
            Pooling = false,
            ApplicationName = ApplicationName,
            KeepAlive = 30,
        }.ConnectionString;
    }

    private NetworkSourceLock()
    {
    }

    /// <summary>Kilit tutmayan sürüm: Her zaman alınır. Yalnızca testler ve veritabanısız kurulum için.</summary>
    public static NetworkSourceLock Unlocked { get; } = new();

    /// <summary>Kilidi beklemeden almaya çalışır. Başka süreç tutuyorsa <c>null</c>. Bırakmak için dispose edilir.</summary>
    public virtual async Task<INetworkSourceLockHandle?> TryAcquireAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        if (_connectionString is null)
        {
            return new Held(null, (int)source);
        }

        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@ns, @key)", connection);
            command.Parameters.AddWithValue("ns", Namespace);
            command.Parameters.AddWithValue("key", (int)source);
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (!acquired)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return null;
            }

            return new Held(connection, (int)source);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Held(NpgsqlConnection? connection, int key) : INetworkSourceLockHandle
    {
        private int _disposed;

        public async Task<bool> IsHeldAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (connection is null)
            {
                return true; // Unlocked: kilit tutulmuyor, hep "var" sayılır
            }

            try
            {
                // İki sayılı advisory lock pg_locks içinde classid = ad alanı, objid = anahtar, objsubid = 2 olarak görünür.
                // Sorgu kilidin kendi bağlantısından gider: pg_backend_pid() o oturumdur.
                await using var command = new NpgsqlCommand(
                    """
                    SELECT EXISTS (
                        SELECT 1 FROM pg_locks
                        WHERE locktype = 'advisory' AND pid = pg_backend_pid() AND granted
                          AND classid = @ns::oid AND objid = @key::oid AND objsubid = 2)
                    """,
                    connection);
                command.Parameters.AddWithValue("ns", (long)Namespace);
                command.Parameters.AddWithValue("key", (long)key);
                return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                // Bağlantı kopmuş veya kapatılmış: Kilit sunucu tarafında bırakıldı.
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || connection is null)
            {
                return;
            }

            try
            {
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@ns, @key)", connection);
                    command.Parameters.AddWithValue("ns", Namespace);
                    command.Parameters.AddWithValue("key", key);
                    await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
            {
                // Bağlantı kopmuşsa kilit sunucu tarafında zaten bırakılmıştır.
            }
            finally
            {
                // Havuz kapalı: Bağlantıyı kapatmak oturumu ve kilidi her durumda bırakır.
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
