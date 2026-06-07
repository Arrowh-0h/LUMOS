using Lumos.Core.Crypto;
using Microsoft.Data.Sqlite;

namespace Lumos.Core.Vault;

/// <summary>
/// High-level facade for the UI. Implements the key-envelope pattern:
///
///   Vault DB is encrypted under a 32-byte CIPHER KEY (random, fixed for vault's lifetime).
///   The CIPHER KEY itself is encrypted (AES-256-GCM) under a WRAPPING KEY
///   derived from the user's master password via Argon2id.
///   The wrapped CIPHER KEY is stored in the vault's header file.
///
///   To unlock: Argon2id(password) -> wrapping key -> AES-GCM decrypt header -> cipher key -> open DB.
///   To change password: re-derive wrapping key, re-encrypt cipher key, rewrite header.
///                       The DB itself is untouched.
///
/// Also handles persistent failed-attempt tracking, backoff enforcement,
/// and optional self-destruct.
/// </summary>
public sealed class VaultManager
{
    private readonly string _vaultPath;
    private readonly bool _selfDestructEnabled;
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>Associated-data tag for the AES-GCM wrap. Binds ciphertext to its purpose.</summary>
    private static readonly byte[] WrapAad = System.Text.Encoding.UTF8.GetBytes("lumos-cipher-key-wrap-v1");

    public VaultManager(
        string vaultPath,
        bool selfDestructEnabled = false,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(vaultPath);
        _vaultPath = vaultPath;
        _selfDestructEnabled = selfDestructEnabled;
        _utcNow = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public string VaultPath => _vaultPath;
    public string HeaderPath => _vaultPath + ".header.json";
    public bool VaultExists => File.Exists(_vaultPath) && File.Exists(HeaderPath);
    public int CurrentFailedAttemptCount => new FailedAttemptTracker(_vaultPath).GetCount();

    /// <summary>
    /// Create a brand-new vault. Generates a random cipher key, derives a
    /// wrapping key from the master password, wraps the cipher key into the
    /// header, then opens the DB with the cipher key.
    /// </summary>
    public VaultService CreateVault(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);

        var validation = MasterPasswordPolicy.Validate(masterPassword);
        if (!validation.IsAllowed)
            throw new InvalidOperationException(validation.Message ?? "Password rejected.");

        if (VaultExists)
            throw new InvalidOperationException($"A vault already exists at {_vaultPath}.");

        // 1. Random cipher key — never derived from the password.
        var cipherKey = SecureMemory.RandomBytes(32);

        // 2. Argon2id(password, fresh salt) -> wrapping key.
        var kdf = KdfParameters.CreateDefault();
        var pwBytes = SecureMemory.Utf8ToBytes(masterPassword);
        byte[]? wrappingKey = null;
        byte[]? wrappedCipherKey = null;

        try
        {
            wrappingKey = Argon2Kdf.DeriveKey(pwBytes, kdf);

            // 3. AES-GCM wrap the cipher key under the wrapping key.
            wrappedCipherKey = AesGcmCrypto.Encrypt(wrappingKey, cipherKey, WrapAad);

            // 4. Write the header BEFORE creating the DB so a crash mid-way
            //    leaves us with either both files or neither.
            var header = VaultHeader.Build(kdf, wrappedCipherKey);
            File.WriteAllText(HeaderPath, header.ToJson());

            // 5. Open SQLite3MC with the raw cipher key.
            try
            {
                return VaultService.Create(_vaultPath, cipherKey);
            }
            catch
            {
                try { File.Delete(HeaderPath); } catch { /* ignore */ }
                throw;
            }
        }
        finally
        {
            SecureMemory.Zero(pwBytes);
            if (wrappingKey is not null) SecureMemory.Zero(wrappingKey);
            SecureMemory.Zero(cipherKey);
            // wrappedCipherKey is non-sensitive (auth-encrypted), but tidy anyway.
            if (wrappedCipherKey is not null) SecureMemory.Zero(wrappedCipherKey);
        }
    }

    /// <summary>
    /// Attempt to unlock the vault. Enforces backoff, records failures,
    /// handles self-destruct, and on success returns an open VaultService.
    /// </summary>
    public UnlockResult Unlock(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);

        if (!VaultExists)
            return new UnlockResult(UnlockStatus.VaultMissing);

        var tracker = new FailedAttemptTracker(_vaultPath);

        // Step 1: enforce any pending backoff from prior failures.
        var priorFailures = tracker.GetCount();
        if (priorFailures > 0)
        {
            var lastFailure = tracker.GetLastFailureUtc();
            var required = UnlockBackoff.GetDelayAfterFailure(priorFailures);
            if (required > TimeSpan.Zero && lastFailure.HasValue)
            {
                var elapsed = _utcNow() - lastFailure.Value;
                if (elapsed < required)
                {
                    return new UnlockResult(
                        Status: UnlockStatus.BackoffRequired,
                        RemainingBackoff: required - elapsed,
                        FailedAttemptCount: priorFailures);
                }
            }
        }

        // Step 2: read header.
        VaultHeader header;
        try
        {
            header = VaultHeader.FromJson(File.ReadAllText(HeaderPath));
        }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.VaultCorrupted, ErrorMessage: ex.Message);
        }

        // Step 3: derive wrapping key, try to unwrap cipher key.
        var kdf = header.ToKdfParameters();
        try { kdf.Validate(); }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.VaultCorrupted, ErrorMessage: ex.Message);
        }

        var pwBytes = SecureMemory.Utf8ToBytes(masterPassword);
        byte[]? wrappingKey = null;
        byte[]? cipherKey = null;

        try
        {
            wrappingKey = Argon2Kdf.DeriveKey(pwBytes, kdf);
            try
            {
                cipherKey = AesGcmCrypto.Decrypt(wrappingKey, header.GetWrappedCipherKey(), WrapAad);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Wrong password: AES-GCM tag verification failed.
                return RecordFailure(tracker);
            }

            // Step 4: open the DB with the unwrapped cipher key.
            var service = VaultService.Open(_vaultPath, cipherKey);
            if (service is null)
            {
                // Should be unreachable given the AES-GCM verification above,
                // but defense-in-depth.
                return RecordFailure(tracker);
            }

            tracker.Reset();
            return new UnlockResult(UnlockStatus.Success, Service: service);
        }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.UnexpectedError, ErrorMessage: ex.Message);
        }
        finally
        {
            SecureMemory.Zero(pwBytes);
            if (wrappingKey is not null) SecureMemory.Zero(wrappingKey);
            if (cipherKey is not null) SecureMemory.Zero(cipherKey);
        }
    }

    private UnlockResult RecordFailure(FailedAttemptTracker tracker)
    {
        tracker.RecordFailure();
        var newCount = tracker.GetCount();

        if (_selfDestructEnabled && UnlockBackoff.ShouldTriggerSelfDestruct(newCount))
        {
            TryDeleteVaultFiles();
            return new UnlockResult(
                UnlockStatus.SelfDestructed,
                FailedAttemptCount: newCount);
        }

        return new UnlockResult(
            UnlockStatus.WrongPassword,
            Backoff: UnlockBackoff.GetDelayAfterFailure(newCount),
            FailedAttemptCount: newCount);
    }

    /// <summary>
    /// Change the master password. Verifies the old password, derives a new
    /// wrapping key from the new password (with a fresh salt), re-wraps the
    /// existing cipher key, and rewrites the header. The DB file itself
    /// is untouched — instant operation.
    ///
    /// On success the existing connection remains valid (same cipher key).
    /// Caller still owns the returned service and must dispose it.
    /// </summary>
    public UnlockResult ChangeMasterPassword(string oldPassword, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(oldPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        var validation = MasterPasswordPolicy.Validate(newPassword);
        if (!validation.IsAllowed)
        {
            return new UnlockResult(
                UnlockStatus.UnexpectedError,
                ErrorMessage: validation.Message);
        }

        // First, verify the old password the normal way. This also gives us
        // back the cipher key (indirectly, via an opened service).
        var unlock = Unlock(oldPassword);
        if (unlock.Status != UnlockStatus.Success || unlock.Service is null)
            return unlock;

        // We need the cipher key itself, not just the open connection.
        // Re-derive it from the header using the old password since
        // VaultService doesn't expose its key.
        var oldHeader = VaultHeader.FromJson(File.ReadAllText(HeaderPath));
        var oldKdf = oldHeader.ToKdfParameters();

        var oldPwBytes = SecureMemory.Utf8ToBytes(oldPassword);
        var newPwBytes = SecureMemory.Utf8ToBytes(newPassword);
        byte[]? oldWrapping = null;
        byte[]? newWrapping = null;
        byte[]? cipherKey = null;
        byte[]? newWrapped = null;

        try
        {
            oldWrapping = Argon2Kdf.DeriveKey(oldPwBytes, oldKdf);
            cipherKey = AesGcmCrypto.Decrypt(oldWrapping, oldHeader.GetWrappedCipherKey(), WrapAad);

            var newKdf = KdfParameters.CreateDefault();
            newWrapping = Argon2Kdf.DeriveKey(newPwBytes, newKdf);
            newWrapped = AesGcmCrypto.Encrypt(newWrapping, cipherKey, WrapAad);

            // Write the new header. We write to a temp file then move,
            // so a crash mid-write leaves the old header intact.
            var newHeader = VaultHeader.Build(newKdf, newWrapped);
            var tempHeader = HeaderPath + ".tmp";
            File.WriteAllText(tempHeader, newHeader.ToJson());
            File.Move(tempHeader, HeaderPath, overwrite: true);

            return new UnlockResult(UnlockStatus.Success, Service: unlock.Service);
        }
        catch (Exception ex)
        {
            unlock.Service.Dispose();
            return new UnlockResult(UnlockStatus.UnexpectedError, ErrorMessage: ex.Message);
        }
        finally
        {
            SecureMemory.Zero(oldPwBytes);
            SecureMemory.Zero(newPwBytes);
            if (oldWrapping is not null) SecureMemory.Zero(oldWrapping);
            if (newWrapping is not null) SecureMemory.Zero(newWrapping);
            if (cipherKey is not null) SecureMemory.Zero(cipherKey);
            if (newWrapped is not null) SecureMemory.Zero(newWrapped);
        }
    }

    private void TryDeleteVaultFiles()
    {
        SqliteConnection.ClearAllPools();
        var candidates = new[]
        {
            _vaultPath,
            HeaderPath,
            _vaultPath + ".attempts.json",
            _vaultPath + "-journal",
            _vaultPath + "-wal",
            _vaultPath + "-shm",
        };
        foreach (var path in candidates)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best effort */ }
        }
    }
}
