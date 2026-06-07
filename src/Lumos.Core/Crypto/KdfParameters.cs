namespace Lumos.Core.Crypto;

/// <summary>
/// Argon2id parameters for key derivation. These are stored in the vault header
/// so the app can derive the same key on future loads, and so future versions
/// can upgrade parameters without breaking existing vaults.
///
/// Spec defaults (v1):
///   memory     = 64 MB  (64 * 1024 KB)
///   iterations = 3
///   parallelism= 4
///   salt       = 16 random bytes per vault
///   keyLength  = 32 bytes (256 bits, for SQLCipher AES-256)
/// </summary>
public sealed record KdfParameters(
    int MemoryKb,
    int Iterations,
    int Parallelism,
    byte[] Salt,
    int KeyLengthBytes)
{
    public const int DefaultMemoryKb = 64 * 1024;     // 64 MB
    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 4;
    public const int DefaultSaltBytes = 16;
    public const int DefaultKeyLengthBytes = 32;       // 256-bit key

    /// <summary>
    /// Create a fresh KdfParameters with a new random salt and the v1 defaults.
    /// </summary>
    public static KdfParameters CreateDefault()
    {
        return new KdfParameters(
            MemoryKb: DefaultMemoryKb,
            Iterations: DefaultIterations,
            Parallelism: DefaultParallelism,
            Salt: SecureMemory.RandomBytes(DefaultSaltBytes),
            KeyLengthBytes: DefaultKeyLengthBytes);
    }

    /// <summary>
    /// Validate parameters are within sane ranges. Anything outside this is
    /// either a bug or a tampered vault header.
    /// </summary>
    public void Validate()
    {
        if (MemoryKb < 8 * 1024)
            throw new InvalidOperationException($"KDF memory too low: {MemoryKb} KB (minimum 8 MB).");
        if (MemoryKb > 1024 * 1024)
            throw new InvalidOperationException($"KDF memory too high: {MemoryKb} KB (max 1 GB).");
        if (Iterations < 1 || Iterations > 20)
            throw new InvalidOperationException($"KDF iterations out of range: {Iterations}.");
        if (Parallelism < 1 || Parallelism > 16)
            throw new InvalidOperationException($"KDF parallelism out of range: {Parallelism}.");
        if (Salt is null || Salt.Length < 8)
            throw new InvalidOperationException("KDF salt missing or too short.");
        if (KeyLengthBytes != 32)
            throw new InvalidOperationException($"KDF key length must be 32 bytes, got {KeyLengthBytes}.");
    }
}
