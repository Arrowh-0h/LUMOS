using System.Security.Cryptography;

namespace Lumos.Core.Crypto;

/// <summary>
/// AES-256-GCM authenticated encryption. Used outside SQLCipher for:
///   - Encrypted export/backup files (the LXP1 format)
///
/// SQLCipher handles the vault database itself (including attachments), so this
/// is auxiliary — it covers data that leaves the vault file, like encrypted
/// exports. Every call uses a fresh random 12-byte nonce and a 16-byte GCM tag.
/// </summary>
public static class AesGcmCrypto
{
    public const int KeySizeBytes = 32;          // AES-256
    public const int NonceSizeBytes = 12;        // GCM standard
    public const int TagSizeBytes = 16;          // GCM standard

    /// <summary>
    /// Encrypt plaintext with the given 32-byte key.
    /// Returns a single buffer: [nonce (12) | ciphertext | tag (16)].
    /// </summary>
    public static byte[] Encrypt(byte[] key, byte[] plaintext, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Key must be {KeySizeBytes} bytes.", nameof(key));

        var nonce = SecureMemory.RandomBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        // Concatenate: nonce | ciphertext | tag
        var output = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, output, NonceSizeBytes + ciphertext.Length, TagSizeBytes);

        SecureMemory.Zero(ciphertext);
        SecureMemory.Zero(tag);

        return output;
    }

    /// <summary>
    /// Decrypt a [nonce | ciphertext | tag] buffer produced by Encrypt.
    /// Throws CryptographicException on tag mismatch (tampered or wrong key).
    /// </summary>
    public static byte[] Decrypt(byte[] key, byte[] envelope, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(envelope);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Key must be {KeySizeBytes} bytes.", nameof(key));
        if (envelope.Length < NonceSizeBytes + TagSizeBytes)
            throw new ArgumentException("Envelope too short to be valid.", nameof(envelope));

        var ciphertextLen = envelope.Length - NonceSizeBytes - TagSizeBytes;
        var nonce = new byte[NonceSizeBytes];
        var ciphertext = new byte[ciphertextLen];
        var tag = new byte[TagSizeBytes];
        var plaintext = new byte[ciphertextLen];

        Buffer.BlockCopy(envelope, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(envelope, NonceSizeBytes, ciphertext, 0, ciphertextLen);
        Buffer.BlockCopy(envelope, NonceSizeBytes + ciphertextLen, tag, 0, TagSizeBytes);

        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }

        return plaintext;
    }
}
