using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fraud.Collectors.StaticCt;

/// <summary>İmzası doğrulanmış checkpoint (imzalı ağaç başı).</summary>
/// <param name="Timestamp">İmzadaki zaman (logun ağaç başını imzaladığı an, kaynağın tarihi).</param>
public sealed record Checkpoint(string Origin, long Size, byte[] RootHash, DateTimeOffset Timestamp);

/// <summary>
/// Logun RFC 6962 açık anahtarı ve note imzasındaki anahtar kimliği.
/// </summary>
/// <remarks>
/// static-ct-api: anahtar kimliği = <c>SHA-256(anahtar adı || 0x0A || 0x05 || LogID)[:4]</c>,
/// LogID = <c>SHA-256(SubjectPublicKeyInfo DER)</c>. Anahtar adı checkpoint'in ilk satırıyla (origin) aynıdır.
/// </remarks>
public sealed class CtLogKey : IDisposable
{
    public const byte SignatureType = 0x05;

    private readonly ECDsa _key;

    private CtLogKey(string name, ECDsa key, byte[] logId, uint keyId)
    {
        Name = name;
        _key = key;
        LogId = logId;
        KeyId = keyId;
    }

    public string Name { get; }

    public byte[] LogId { get; }

    public uint KeyId { get; }

    public static CtLogKey Create(string name, string base64Spki)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Spki);

        var spki = Convert.FromBase64String(base64Spki);
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length)
            {
                throw new CryptographicException("Anahtarın sonunda fazladan veri var.");
            }

            var logId = SHA256.HashData(spki);
            return new CtLogKey(name, key, logId, ComputeKeyId(name, logId));
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public static uint ComputeKeyId(string name, ReadOnlySpan<byte> logId)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nameBytes.Length + 2 + logId.Length];
        nameBytes.CopyTo(input, 0);
        input[nameBytes.Length] = 0x0A;
        input[nameBytes.Length + 1] = SignatureType;
        logId.CopyTo(input.AsSpan(nameBytes.Length + 2));
        return BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(input));
    }

    /// <summary>RFC 6962 <c>TreeHeadSignature</c> girdisi üzerinde DER biçimli ECDSA imzasını doğrular.</summary>
    public bool Verify(ReadOnlySpan<byte> signedData, ReadOnlySpan<byte> derSignature) =>
        _key.VerifyData(signedData, derSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    public void Dispose() => _key.Dispose();
}

public enum CheckpointError
{
    None = 0,

    /// <summary>Metin veya imza satırları biçime uymuyor.</summary>
    Malformed = 1,

    /// <summary>İlk satır bu logun origin değeri değil.</summary>
    WrongOrigin = 2,

    /// <summary>Bilinen anahtardan imza yok.</summary>
    NoKnownSignature = 3,

    /// <summary>Bilinen anahtarın imzası doğrulanmadı.</summary>
    BadSignature = 4,
}

/// <summary>Checkpoint ayrıştırma ve imza doğrulama (c2sp.org/tlog-checkpoint, signed-note, static-ct-api).</summary>
public static class CheckpointVerifier
{
    public const int MaxBytes = 64 * 1024;
    public const int MaxSignatures = 64;

    private const string SignaturePrefix = "— ";

    public static (Checkpoint? Checkpoint, CheckpointError Error) Verify(ReadOnlySpan<byte> body, CtLogKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (body.Length is 0 or > MaxBytes)
        {
            return (null, CheckpointError.Malformed);
        }

        string note;
        try
        {
            note = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return (null, CheckpointError.Malformed);
        }

        // signed-note: satır sonu dışında kontrol karakteri olamaz; metin ile imzalar son boş satırla ayrılır.
        if (note.Any(c => c < 0x20 && c != '\n') || !note.EndsWith('\n'))
        {
            return (null, CheckpointError.Malformed);
        }

        var split = note.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (split < 0 || split + 2 >= note.Length)
        {
            return (null, CheckpointError.Malformed);
        }

        var text = note[..(split + 1)];
        var signatureLines = note[(split + 2)..^1].Split('\n');

        // static-ct-api: RFC 6962 imzalı checkpoint uzantı satırı içeremez; tam olarak üç satır.
        var lines = text[..^1].Split('\n');
        if (lines.Length != 3)
        {
            return (null, CheckpointError.Malformed);
        }

        if (!string.Equals(lines[0], key.Name, StringComparison.Ordinal))
        {
            return (null, CheckpointError.WrongOrigin);
        }

        if (!TryParseDecimal(lines[1], out var size) || !TryDecodeCanonicalBase64(lines[2], out var root) || root.Length != Merkle.HashSize)
        {
            return (null, CheckpointError.Malformed);
        }

        if (signatureLines.Length is 0 or > MaxSignatures)
        {
            return (null, CheckpointError.Malformed);
        }

        DateTimeOffset? verifiedAt = null;
        foreach (var line in signatureLines)
        {
            if (!line.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            {
                return (null, CheckpointError.Malformed);
            }

            var rest = line[SignaturePrefix.Length..];
            var space = rest.LastIndexOf(' ');
            if (space <= 0)
            {
                return (null, CheckpointError.Malformed);
            }

            var name = rest[..space];
            if (!TryDecodeCanonicalBase64(rest[(space + 1)..], out var signature) || signature.Length < 4)
            {
                return (null, CheckpointError.Malformed);
            }

            // Adı ve kimliği birlikte bilinmeyen imzalar (ör. tanık imzaları) yok sayılır.
            if (!string.Equals(name, key.Name, StringComparison.Ordinal) || BinaryPrimitives.ReadUInt32BigEndian(signature) != key.KeyId)
            {
                continue;
            }

            // Bilinen anahtarın imzası doğrulanmazsa bütün not reddedilir.
            if (!TryVerifyRfc6962(signature.AsSpan(4), size, root, key, out var timestamp))
            {
                return (null, CheckpointError.BadSignature);
            }

            verifiedAt = timestamp;
        }

        return verifiedAt is { } at
            ? (new Checkpoint(lines[0], size, root, at), CheckpointError.None)
            : (null, CheckpointError.NoKnownSignature);
    }

    /// <summary>
    /// <c>RFC6962NoteSignature</c>: <c>uint64 timestamp</c>, ardından <c>digitally-signed</c> yapı
    /// (hash algoritması 4 = SHA-256, imza algoritması 3 = ECDSA, 2 bayt uzunluk, DER imza).
    /// İmzalanan veri RFC 6962 Bölüm 3.5: sürüm 0, tür 1 (tree_hash), zaman, ağaç boyutu, kök.
    /// </summary>
    private static bool TryVerifyRfc6962(ReadOnlySpan<byte> body, long size, byte[] root, CtLogKey key, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (body.Length < 8 + 4)
        {
            return false;
        }

        var ms = BinaryPrimitives.ReadUInt64BigEndian(body);
        if (body[8] != 4 || body[9] != 3)
        {
            return false;
        }

        var sigLength = BinaryPrimitives.ReadUInt16BigEndian(body[10..]);
        if (body.Length != 12 + sigLength || ms > (ulong)DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return false;
        }

        Span<byte> signed = stackalloc byte[2 + 8 + 8 + Merkle.HashSize];
        signed[0] = 0; // v1
        signed[1] = 1; // tree_hash
        BinaryPrimitives.WriteUInt64BigEndian(signed[2..], ms);
        BinaryPrimitives.WriteUInt64BigEndian(signed[10..], (ulong)size);
        root.CopyTo(signed[18..]);

        if (!key.Verify(signed, body.Slice(12, sigLength)))
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
        return true;
    }

    private static bool TryParseDecimal(string value, out long result)
    {
        result = 0;
        // Başında sıfır yok ("0" hariç), işaret yok, yalnızca ASCII rakam.
        if (value.Length is 0 or > 19 || (value.Length > 1 && value[0] == '0') || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>signed-note: Kanonik olmayan base64 reddedilir (yeniden kodlanınca aynı metin çıkmalı).</summary>
    private static bool TryDecodeCanonicalBase64(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length == 0 || value.Length % 4 != 0 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return false;
        }

        return string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal);
    }
}
