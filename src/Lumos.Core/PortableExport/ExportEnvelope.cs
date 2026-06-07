using System.Security.Cryptography;
using System.Text;
using Lumos.Core.Crypto;

namespace Lumos.Core.PortableExport;

/// <summary>
/// Outcome of decoding an encrypted Lumos export file.
/// </summary>
public sealed record DecodeResult(byte[]? PlaintextBytes, DecodeStatus Status)
{
    public bool Success => Status == DecodeStatus.Ok;
}

public enum DecodeStatus
{
    Ok,
    BadMagic,            // Wrong magic bytes — not an LXP1 file
    Truncated,           // Header is incomplete
    WrongPassphrase,     // GCM auth tag didn't verify
    Corrupt,             // Some other unexpected error during decode
}

/// <summary>
/// File format (encrypted Lumos export, version 1):
///
///   Offset  Size  Field
///   ------  ----  -----
///        0     4  Magic       "LXP1" (ASCII)
///        4    16  Salt        random per export
///       20    12  Nonce       random per export (GCM)
///       32     n  Ciphertext  AES-256-GCM(plaintext) + 16-byte tag at end
///
/// The Argon2id parameters are baked-in (the same defaults the vault uses)
/// so we don't need to put them in the header. Future versions can use
/// "LXP2" etc. and include explicit parameters if we want to make them
/// tunable.
///
/// Why we don't include any associated data:
///   The salt and nonce are already in the file, immediately before the
///   ciphertext. They're not user data we'd want to authenticate; they're
///   already covered by the fact that any change to them produces a
///   different key or a different decrypt that won't verify the tag.
/// </summary>
public static class ExportEnvelope
{
    public static readonly byte[] Magic = { (byte)'L', (byte)'X', (byte)'P', (byte)'1' };
    public const int SaltLength = 16;
    public const int NonceLength = 12;
    public const int HeaderLength = 4 + SaltLength + NonceLength; // 32

    /// <summary>
    /// Encode the supplied JSON bytes into an encrypted export file.
    /// </summary>
    public static byte[] Encode(byte[] plaintextJson, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintextJson);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        var salt = SecureMemory.RandomBytes(SaltLength);

        // Same Argon2id parameters the vault uses for its master key. Slow
        // by design — makes brute-forcing the export passphrase as costly
        // as brute-forcing a master password.
        var kdfParams = new KdfParameters(
            MemoryKb: KdfParameters.DefaultMemoryKb,
            Iterations: KdfParameters.DefaultIterations,
            Parallelism: KdfParameters.DefaultParallelism,
            Salt: salt,
            KeyLengthBytes: KdfParameters.DefaultKeyLengthBytes);

        var key = Argon2Kdf.DeriveKey(Encoding.UTF8.GetBytes(passphrase), kdfParams);
        try
        {
            // AesGcmCrypto.Encrypt returns [nonce(12) | ciphertext | tag(16)].
            var envelope = AesGcmCrypto.Encrypt(key, plaintextJson);

            var output = new byte[HeaderLength + envelope.Length - NonceLength];
            // Magic
            Buffer.BlockCopy(Magic, 0, output, 0, 4);
            // Salt
            Buffer.BlockCopy(salt, 0, output, 4, SaltLength);
            // Nonce — the AES envelope already starts with it; reuse those 12 bytes.
            Buffer.BlockCopy(envelope, 0, output, 4 + SaltLength, NonceLength);
            // Ciphertext + tag (everything after the nonce in the envelope).
            Buffer.BlockCopy(envelope, NonceLength, output, HeaderLength, envelope.Length - NonceLength);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Decode an encrypted export file using the supplied passphrase. Wrong
    /// passphrases produce a <see cref="DecodeStatus.WrongPassphrase"/> result,
    /// not an exception, so the UI can show a clean error.
    /// </summary>
    public static DecodeResult Decode(byte[] fileBytes, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(fileBytes);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        if (fileBytes.Length < HeaderLength + 16) // header + at least the GCM tag
            return new DecodeResult(null, DecodeStatus.Truncated);

        for (var i = 0; i < Magic.Length; i++)
            if (fileBytes[i] != Magic[i])
                return new DecodeResult(null, DecodeStatus.BadMagic);

        var salt = new byte[SaltLength];
        Buffer.BlockCopy(fileBytes, 4, salt, 0, SaltLength);

        // Rebuild the envelope the way AesGcmCrypto.Decrypt expects it:
        // nonce + ciphertext + tag.
        var envelope = new byte[fileBytes.Length - HeaderLength + NonceLength];
        Buffer.BlockCopy(fileBytes, 4 + SaltLength, envelope, 0, NonceLength);
        Buffer.BlockCopy(fileBytes, HeaderLength, envelope, NonceLength, fileBytes.Length - HeaderLength);

        var kdfParams = new KdfParameters(
            MemoryKb: KdfParameters.DefaultMemoryKb,
            Iterations: KdfParameters.DefaultIterations,
            Parallelism: KdfParameters.DefaultParallelism,
            Salt: salt,
            KeyLengthBytes: KdfParameters.DefaultKeyLengthBytes);

        var key = Argon2Kdf.DeriveKey(Encoding.UTF8.GetBytes(passphrase), kdfParams);
        try
        {
            var plaintext = AesGcmCrypto.Decrypt(key, envelope);
            return new DecodeResult(plaintext, DecodeStatus.Ok);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // GCM tag mismatch — wrong passphrase or tampered file. We
            // can't tell the difference, and the user-facing message is
            // the same either way.
            return new DecodeResult(null, DecodeStatus.WrongPassphrase);
        }
        catch
        {
            return new DecodeResult(null, DecodeStatus.Corrupt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
