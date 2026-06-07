using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumos.Core.Vault;

/// <summary>
/// Sidecar file written next to the SQLite3MC vault DB. Now stores:
///   - Argon2id parameters + salt (for deriving the WRAPPING key)
///   - The CIPHER key (32 random bytes that SQLite3MC actually uses),
///     wrapped in AES-256-GCM with the Argon2id-derived wrapping key.
///
/// This "key envelope" pattern means a master-password change just rewrites
/// the wrapped cipher key — no need to re-encrypt the whole database.
/// Same pattern as Bitwarden, 1Password, KeePass.
///
/// Filename: vault.db -> vault.db.header.json
/// Not secret. Knowing the salt + ciphertext does not help an attacker;
/// they still need the master password to derive the wrapping key.
/// </summary>
public sealed class VaultHeader
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 2;  // bumped: v1 didn't have the envelope

    [JsonPropertyName("kdfAlgorithm")]
    public string KdfAlgorithm { get; init; } = "argon2id";

    [JsonPropertyName("kdfMemoryKb")]
    public int KdfMemoryKb { get; init; }

    [JsonPropertyName("kdfIterations")]
    public int KdfIterations { get; init; }

    [JsonPropertyName("kdfParallelism")]
    public int KdfParallelism { get; init; }

    /// <summary>Base64 of the Argon2id salt used to derive the wrapping key.</summary>
    [JsonPropertyName("kdfSalt")]
    public string KdfSaltBase64 { get; init; } = string.Empty;

    [JsonPropertyName("kdfKeyLength")]
    public int KdfKeyLengthBytes { get; init; }

    [JsonPropertyName("cipher")]
    public string Cipher { get; init; } = "sqlite3mc-sqlcipher-v4";

    /// <summary>
    /// Base64 of [nonce | ciphertext | tag] — the cipher key (used by
    /// SQLite3MC for at-rest encryption) wrapped in AES-256-GCM under the
    /// Argon2id-derived wrapping key.
    /// </summary>
    [JsonPropertyName("wrappedCipherKey")]
    public string WrappedCipherKeyBase64 { get; init; } = string.Empty;

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public Crypto.KdfParameters ToKdfParameters()
    {
        return new Crypto.KdfParameters(
            MemoryKb: KdfMemoryKb,
            Iterations: KdfIterations,
            Parallelism: KdfParallelism,
            Salt: Convert.FromBase64String(KdfSaltBase64),
            KeyLengthBytes: KdfKeyLengthBytes);
    }

    public byte[] GetWrappedCipherKey()
        => Convert.FromBase64String(WrappedCipherKeyBase64);

    public static VaultHeader Build(Crypto.KdfParameters kdf, byte[] wrappedCipherKey)
    {
        return new VaultHeader
        {
            SchemaVersion = 2,
            KdfAlgorithm = "argon2id",
            KdfMemoryKb = kdf.MemoryKb,
            KdfIterations = kdf.Iterations,
            KdfParallelism = kdf.Parallelism,
            KdfSaltBase64 = Convert.ToBase64String(kdf.Salt),
            KdfKeyLengthBytes = kdf.KeyLengthBytes,
            Cipher = "sqlite3mc-sqlcipher-v4",
            WrappedCipherKeyBase64 = Convert.ToBase64String(wrappedCipherKey),
            CreatedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, _jsonOptions);

    public static VaultHeader FromJson(string json)
    {
        var result = JsonSerializer.Deserialize<VaultHeader>(json)
            ?? throw new InvalidOperationException("Vault header JSON was empty or invalid.");
        return result;
    }
}
