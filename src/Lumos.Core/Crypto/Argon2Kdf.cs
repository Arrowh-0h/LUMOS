using Konscious.Security.Cryptography;

namespace Lumos.Core.Crypto;

/// <summary>
/// Wraps Konscious Argon2id with our KdfParameters.
///
/// Threading: not thread-safe — create per-call. Argon2id work is CPU- and
/// memory-intensive; call from a background task, never on the UI thread.
/// </summary>
public static class Argon2Kdf
{
    /// <summary>
    /// Derive a 32-byte key from the master password bytes.
    /// The caller owns <paramref name="passwordBytes"/> and should zero it
    /// after this returns. The returned key is also a secret — caller zeroes
    /// it when done (typically after handing it to SQLCipher).
    /// </summary>
    public static byte[] DeriveKey(byte[] passwordBytes, KdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(passwordBytes);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        if (passwordBytes.Length == 0)
            throw new ArgumentException("Master password cannot be empty.", nameof(passwordBytes));

        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = parameters.Salt,
            DegreeOfParallelism = parameters.Parallelism,
            MemorySize = parameters.MemoryKb,
            Iterations = parameters.Iterations,
        };

        return argon2.GetBytes(parameters.KeyLengthBytes);
    }
}
