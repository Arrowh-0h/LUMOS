using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumos.Core.Vault;

/// <summary>
/// The recovery half of the key envelope: the SAME cipher key, wrapped a second
/// time under a key derived from the user's recovery code.
///
/// Carries its OWN Argon2id parameters and salt rather than sharing the
/// master's. This is deliberate: ChangeMasterPassword writes fresh master KDF
/// parameters every time, so if the recovery wrap borrowed those, any future
/// change to the KDF defaults would silently derive a different recovery key
/// and permanently invalidate every recovery code already in users' hands.
/// </summary>
public sealed class RecoveryEnvelope
{
    [JsonPropertyName("kdfAlgorithm")]
    public string KdfAlgorithm { get; init; } = "argon2id";

    [JsonPropertyName("kdfMemoryKb")]
    public int KdfMemoryKb { get; init; }

    [JsonPropertyName("kdfIterations")]
    public int KdfIterations { get; init; }

    [JsonPropertyName("kdfParallelism")]
    public int KdfParallelism { get; init; }

    /// <summary>Base64 of the Argon2id salt for the recovery key. Distinct from the master salt.</summary>
    [JsonPropertyName("kdfSalt")]
    public string KdfSaltBase64 { get; init; } = string.Empty;

    [JsonPropertyName("kdfKeyLength")]
    public int KdfKeyLengthBytes { get; init; }

    /// <summary>Base64 of [nonce | ciphertext | tag] — the cipher key wrapped under the recovery key.</summary>
    [JsonPropertyName("wrappedCipherKey")]
    public string WrappedCipherKeyBase64 { get; init; } = string.Empty;

    /// <summary>When this recovery code was issued. Useful when a user regenerates one.</summary>
    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public Crypto.KdfParameters ToKdfParameters()
        => new(
            MemoryKb: KdfMemoryKb,
            Iterations: KdfIterations,
            Parallelism: KdfParallelism,
            Salt: Convert.FromBase64String(KdfSaltBase64),
            KeyLengthBytes: KdfKeyLengthBytes);

    public byte[] GetWrappedCipherKey() => Convert.FromBase64String(WrappedCipherKeyBase64);

    public static RecoveryEnvelope Build(Crypto.KdfParameters kdf, byte[] wrappedCipherKey)
        => new()
        {
            KdfAlgorithm = "argon2id",
            KdfMemoryKb = kdf.MemoryKb,
            KdfIterations = kdf.Iterations,
            KdfParallelism = kdf.Parallelism,
            KdfSaltBase64 = Convert.ToBase64String(kdf.Salt),
            KdfKeyLengthBytes = kdf.KeyLengthBytes,
            WrappedCipherKeyBase64 = Convert.ToBase64String(wrappedCipherKey),
            CreatedUtc = DateTimeOffset.UtcNow,
        };
}

/// <summary>
/// Sidecar file written next to the SQLite3MC vault DB. Stores:
///   - Argon2id parameters + salt (for deriving the WRAPPING key)
///   - The CIPHER key (32 random bytes that SQLite3MC actually uses),
///     wrapped in AES-256-GCM with the Argon2id-derived wrapping key
///   - Optionally, a SECOND wrap of the same cipher key under a key derived
///     from the user's recovery code (format version 3+)
///
/// This "key envelope" pattern means a master-password change just rewrites
/// the wrapped cipher key — no need to re-encrypt the whole database.
/// Same pattern as Bitwarden, 1Password, KeePass.
///
/// Filename: vault.db -> vault.db.header.json
/// Not secret. Knowing the salts + ciphertexts does not help an attacker;
/// they still need the master password or the recovery code.
///
/// NOTE ON VERSIONS: this SchemaVersion is the HEADER FORMAT version and is
/// unrelated to SchemaMigrator.CurrentVersion, which versions the database
/// tables inside the vault. They move independently.
///
///   v1  pre-envelope (never shipped publicly)
///   v2  master wrap only; AAD was the fixed string "lumos-cipher-key-wrap-v1"
///   v3  master wrap with AAD bound to the master KDF parameters, plus an
///       optional recovery envelope
/// </summary>
public sealed class VaultHeader
{
    /// <summary>Header format version written by this build.</summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>
    /// The fixed associated-data tag used by format v2. Retained ONLY so that
    /// existing vaults can still be unwrapped and migrated. Never used for new
    /// wraps.
    /// </summary>
    public static byte[] LegacyMasterWrapAad { get; } =
        Encoding.UTF8.GetBytes("lumos-cipher-key-wrap-v1");

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentFormatVersion;

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

    /// <summary>
    /// Second wrap of the same cipher key under the recovery code. Null when
    /// the user has not set up recovery yet (all v2 vaults, and v3 vaults
    /// where the user deferred the prompt).
    /// </summary>
    [JsonPropertyName("recovery")]
    public RecoveryEnvelope? Recovery { get; init; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True if a recovery code has been issued for this vault.</summary>
    [JsonIgnore]
    public bool HasRecovery => Recovery is not null;

    /// <summary>
    /// True if this header predates the bound-AAD format and should be
    /// migrated the next time the master password is available.
    /// </summary>
    [JsonIgnore]
    public bool NeedsFormatUpgrade => SchemaVersion < CurrentFormatVersion;

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

    /// <summary>
    /// The associated data to use when unwrapping THIS header's master wrap.
    /// v2 headers used a fixed string; v3+ bind the KDF parameters.
    /// </summary>
    public byte[] GetMasterWrapAad()
        => SchemaVersion < 3
            ? LegacyMasterWrapAad
            : BuildMasterWrapAad(ToKdfParameters());

    /// <summary>
    /// Build the associated data for a master wrap, binding the AES-GCM tag to
    /// the exact Argon2id parameters recorded alongside it.
    ///
    /// What this buys us, stated precisely: simply editing the KDF cost in the
    /// header ALREADY fails without any AAD, because different parameters
    /// derive a different wrapping key and the GCM tag won't verify. Binding
    /// closes a narrower gap — SPLICING. Without it, a wrapped key lifted from
    /// an older copy of the header (a backup taken before a password change)
    /// could be pasted into the current header and would still decrypt under
    /// the old password. Binding the salt and cost into the tag makes each wrap
    /// valid only in the exact header it was created for, so wraps cannot be
    /// mixed and matched across header generations.
    /// </summary>
    public static byte[] BuildMasterWrapAad(Crypto.KdfParameters kdf)
    {
        ArgumentNullException.ThrowIfNull(kdf);
        return Canonical("lumos-cipher-key-wrap-v2", kdf);
    }

    /// <summary>
    /// Associated data for the recovery wrap. A DIFFERENT purpose string from
    /// the master wrap, so the two wraps are not interchangeable: pasting the
    /// recovery ciphertext into the master field (or vice versa) fails
    /// authentication rather than being silently accepted.
    /// </summary>
    public static byte[] BuildRecoveryWrapAad(Crypto.KdfParameters kdf)
    {
        ArgumentNullException.ThrowIfNull(kdf);
        return Canonical("lumos-recovery-key-wrap-v1", kdf);
    }

    /// <summary>
    /// Deterministic byte encoding of (purpose + KDF parameters). Must never
    /// depend on JSON formatting, property order, or culture — it is recomputed
    /// on every unlock and has to match byte-for-byte forever.
    /// </summary>
    private static byte[] Canonical(string purpose, Crypto.KdfParameters kdf)
    {
        var sb = new StringBuilder();
        sb.Append(purpose).Append('|')
          .Append("argon2id").Append('|')
          .Append(kdf.MemoryKb.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(kdf.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(kdf.Parallelism.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(kdf.KeyLengthBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
          .Append(Convert.ToBase64String(kdf.Salt));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Build a current-format header. Pass <paramref name="recovery"/> to
    /// include a recovery envelope, or null to leave recovery unconfigured.
    /// </summary>
    public static VaultHeader Build(
        Crypto.KdfParameters kdf,
        byte[] wrappedCipherKey,
        RecoveryEnvelope? recovery = null,
        DateTimeOffset? createdUtc = null)
    {
        ArgumentNullException.ThrowIfNull(kdf);
        ArgumentNullException.ThrowIfNull(wrappedCipherKey);

        return new VaultHeader
        {
            SchemaVersion = CurrentFormatVersion,
            KdfAlgorithm = "argon2id",
            KdfMemoryKb = kdf.MemoryKb,
            KdfIterations = kdf.Iterations,
            KdfParallelism = kdf.Parallelism,
            KdfSaltBase64 = Convert.ToBase64String(kdf.Salt),
            KdfKeyLengthBytes = kdf.KeyLengthBytes,
            Cipher = "sqlite3mc-sqlcipher-v4",
            WrappedCipherKeyBase64 = Convert.ToBase64String(wrappedCipherKey),
            Recovery = recovery,
            // Preserve the original creation date across rewrites so a password
            // change or recovery setup doesn't make the vault look brand new.
            CreatedUtc = createdUtc ?? DateTimeOffset.UtcNow,
        };
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, _jsonOptions);

    public static VaultHeader FromJson(string json)
    {
        var result = JsonSerializer.Deserialize<VaultHeader>(json)
            ?? throw new InvalidOperationException("Vault header JSON was empty or invalid.");
        return result;
    }
}
