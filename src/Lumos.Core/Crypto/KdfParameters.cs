namespace Lumos.Core.Crypto;

/// <summary>
/// Argon2id parameters for key derivation. These are stored in the vault header
/// so the app can derive the same key on future loads, and so future versions
/// can upgrade parameters without breaking existing vaults.
///
/// Defaults (v2):
///   memory     = 256 MB  (256 * 1024 KB)
///   iterations = 4
///   parallelism= 4
///   salt       = 16 random bytes per vault
///   keyLength  = 32 bytes (256 bits, for SQLCipher AES-256)
///
/// Raised from v1's 64 MB / t=3 in v2. Rationale: the unlock backoff only slows
/// someone typing into the UI — an attacker who copies vault.db and its header
/// ignores it entirely and grinds Argon2id offline on a GPU. KDF cost is the
/// only defence that applies in that case, and it is the attacker's cost per
/// guess. Quadrupling memory quadruples their hardware bill; for the user it
/// means roughly a 1-2 second unlock, which is an acceptable trade for a vault
/// opened a handful of times a day.
///
/// PER-VAULT, NOT GLOBAL. These values are written into each vault's header, so
/// changing them here affects only newly created vaults and vaults whose
/// password is changed afterwards. Existing vaults keep deriving with the
/// parameters recorded in their own header and continue to open normally.
///
/// Upper bound on raising this further: 256 MB must be allocatable on the
/// weakest machine we support. Konscious's Argon2 is a pure managed
/// implementation, so this is a managed-heap allocation held for the duration
/// of the derivation.
/// </summary>
public sealed record KdfParameters(
    int MemoryKb,
    int Iterations,
    int Parallelism,
    byte[] Salt,
    int KeyLengthBytes)
{
    public const int DefaultMemoryKb = 256 * 1024;    // 256 MB
    public const int DefaultIterations = 4;
    public const int DefaultParallelism = 4;
    public const int DefaultSaltBytes = 16;
    public const int DefaultKeyLengthBytes = 32;       // 256-bit key

    /// <summary>
    /// Create a fresh KdfParameters with a new random salt and the current defaults.
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
