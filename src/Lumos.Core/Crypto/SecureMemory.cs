using System.Security.Cryptography;
using System.Text;

namespace Lumos.Core.Crypto;

/// <summary>
/// Utilities for handling secrets in memory.
/// All secret material should pass through these helpers so we keep
/// a consistent zeroing discipline.
/// </summary>
public static class SecureMemory
{
    /// <summary>
    /// Zero a byte array. Use this on every buffer that held secret material
    /// (master password bytes, derived keys, decrypted entry fields).
    /// CryptographicOperations.ZeroMemory is not optimized away by the JIT.
    /// </summary>
    public static void Zero(byte[]? buffer)
    {
        if (buffer is null || buffer.Length == 0) return;
        CryptographicOperations.ZeroMemory(buffer);
    }

    /// <summary>
    /// Encode a string to UTF-8 bytes that the caller is responsible for zeroing.
    /// Note: the source <paramref name="value"/> string itself remains in the
    /// managed heap until GC runs. For input fields, prefer SecureString or
    /// reading directly into a char[] / byte[] you control.
    /// </summary>
    public static byte[] Utf8ToBytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Encoding.UTF8.GetBytes(value);
    }

    /// <summary>
    /// Constant-time comparison of two byte arrays. Use for any equality check
    /// involving secrets (MAC verification, derived-key comparison) to avoid
    /// timing side channels.
    /// </summary>
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Generate a cryptographically random byte array of the given length.
    /// </summary>
    public static byte[] RandomBytes(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }
}
