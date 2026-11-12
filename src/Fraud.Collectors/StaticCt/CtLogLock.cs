using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Fraud.Collectors.StaticCt;

/// <summary>Bir CT logunu izleme hakkı. Elde tutulduğu sürece başka süreç aynı logu ilerletemez.</summary>
public interface ICtLogLock : IAsyncDisposable
{
    /// <summary>
    /// Kilit hâlâ bu süreçte mi? Veritabanı bağlantısı koptuysa kilit sunucu tarafında bırakılmıştır; <c>false</c> döner.
    /// </summary>
    Task<bool> IsHeldAsync(CancellationToken cancellationToken);
}

/// <summary>Log başına kilit verir. Testlerde sahte uygulama kullanılabilir.</summary>
public interface ICtLogLockProvider
{
    /// <summary>Kilidi beklemeden almaya çalışır. Başka bir süreç tutuyorsa <c>null</c>.</summary>
    Task<ICtLogLock?> TryAcquireAsync(string logName, CancellationToken cancellationToken);
}

/// <summary>
/// PostgreSQL advisory lock ile log başına kilit (<c>ct_log_state</c> satırı başına bir anahtar).
/// </summary>
/// <remarks>
/// <para>
/// Oturum düzeyinde kilit (<c>pg_try_advisory_lock</c>) kullanılır: Kilit, onu alan bağlantı açık kaldıkça durur.
/// Bu yüzden her kilit kendi bağlantısını tutar ve bu bağlantı havuza girmez (<c>Pooling=false</c>). Havuza dönen
/// bir bağlantıda kilit, bağlantı bir sonraki kullanımda sıfırlanana kadar asılı kalırdı.
/// </para>
/// <para>
/// Süreç çökerse veya bağlantı koparsa PostgreSQL kilidi kendisi bırakır; elle temizlik gerekmez.
/// </para>
/// <para>
/// Anahtar iki 32 bitlik sayıdır: Sabit bir ad alanı (<see cref="Namespace"/>) ve log adının SHA-256 özetinden
/// türetilen pozitif sayı. Ad alanı, aynı veritabanında başka bir amaçla alınan advisory lock'larla çakışmayı önler.
/// </para>
/// </remarks>
public sealed class PostgresCtLogLockProvider(string connectionString) : ICtLogLockProvider
{
    /// <summary>"CTLG" baytları. Bu projede CT log kilitleri için ayrılmış ad alanı.</summary>
    public const int Namespace = 0x43544C47;

    public const string ApplicationName = "fraud-ct-lock";

    private readonly string _connectionString = BuildConnectionString(connectionString);

    /// <summary>Log adından kilit anahtarının ikinci yarısı. Kararlıdır: Aynı ad her süreçte aynı sayıyı verir.</summary>
    public static int LockKey(string logName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logName);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(logName), hash);
        return (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash) & 0x7FFF_FFFF);
    }

    public async Task<ICtLogLock?> TryAcquireAsync(string logName, CancellationToken cancellationToken)
    {
        var key = LockKey(logName);
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@ns, @key)", connection))
            {
                command.Parameters.AddWithValue("ns", Namespace);
                command.Parameters.AddWithValue("key", key);
                var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
                if (!acquired)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return null;
                }
            }

            return new Lock(connection, key);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string BuildConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = ApplicationName,
            // Sessiz kopan bağlantı (ör. ağ kesintisi) bu sürede fark edilir; kilit kontrolü de hata verir.
            KeepAlive = 30,
        }.ConnectionString;
    }

    private sealed class Lock(NpgsqlConnection connection, int key) : ICtLogLock
    {
        private int _disposed;

        public async Task<bool> IsHeldAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            try
            {
                // İki sayılı advisory lock pg_locks içinde classid = ad alanı, objid = anahtar, objsubid = 2 olarak görünür.
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
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
            {
                // Bağlantı kopmuş: Kilit sunucu tarafında bırakıldı.
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
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
                // Bağlantı zaten kopmuşsa kilit de bırakılmıştır.
            }
            finally
            {
                // Havuz kapalı: Bağlantıyı kapatmak oturumu da kapatır; kilit her durumda bırakılır.
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
