using System.Security.Cryptography;
using Lumos.Core.Crypto;
using Lumos.Core.Recovery;
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
/// Format v3 adds a SECOND, independent wrap of the same cipher key under a key
/// derived from an app-generated recovery code:
///
///     cipher key
///        |-- wrapped by Argon2id(master password, masterSalt)
///        '-- wrapped by Argon2id(recovery code,   recoverySalt)
///
/// Either secret opens the vault. Neither can produce the other. Nothing here
/// ever contacts a network, and no third party — including the Lumos authors —
/// holds any key material. If the user loses both secrets the data is gone, and
/// that is the intended, honest behaviour of a local-first password manager.
///
/// Also handles persistent failed-attempt tracking, backoff enforcement,
/// and optional self-destruct.
/// </summary>
public sealed class VaultManager
{
    private readonly string _vaultPath;
    private readonly bool _selfDestructEnabled;
    private readonly Func<DateTimeOffset> _utcNow;

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
    /// True if this vault has a recovery code issued. False for every vault
    /// created before format v3, and for v3 vaults where the user deferred the
    /// setup prompt. Returns false rather than throwing if the header is
    /// missing or unreadable — callers treat "unknown" as "not configured".
    /// </summary>
    public bool HasRecovery
    {
        get
        {
            var header = TryReadHeader();
            return header?.HasRecovery == true;
        }
    }

    // ---------------------------------------------------------------- create

    /// <summary>
    /// Create a brand-new vault. Generates a random cipher key, derives a
    /// wrapping key from the master password, wraps the cipher key into the
    /// header, then opens the DB with the cipher key.
    ///
    /// Recovery is NOT set up here. The caller should follow a successful
    /// create with <see cref="SetUpRecovery"/> so that new vaults and migrating
    /// vaults go through exactly the same code path and the same UI.
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

            // 3. AES-GCM wrap the cipher key, with the tag bound to these exact
            //    KDF parameters (format v3).
            wrappedCipherKey = AesGcmCrypto.Encrypt(
                wrappingKey, cipherKey, VaultHeader.BuildMasterWrapAad(kdf));

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

    // ---------------------------------------------------------------- unlock

    /// <summary>
    /// Attempt to unlock the vault. Enforces backoff, records failures,
    /// handles self-destruct, and on success returns an open VaultService.
    ///
    /// On success this also opportunistically upgrades a pre-v3 header to the
    /// current format. Unlock is the only moment the master password is
    /// available, so it is the only moment that rewrite is possible.
    /// </summary>
    public UnlockResult Unlock(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);

        if (!VaultExists)
            return new UnlockResult(UnlockStatus.VaultMissing);

        var tracker = new FailedAttemptTracker(_vaultPath);

        var backoff = CheckBackoff(tracker);
        if (backoff is not null) return backoff;

        // Read header.
        VaultHeader header;
        try
        {
            header = VaultHeader.FromJson(File.ReadAllText(HeaderPath));
        }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.VaultCorrupted, ErrorMessage: ex.Message);
        }

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
                cipherKey = AesGcmCrypto.Decrypt(
                    wrappingKey, header.GetWrappedCipherKey(), header.GetMasterWrapAad());
            }
            catch (CryptographicException)
            {
                // Wrong password: AES-GCM tag verification failed.
                return RecordFailure(tracker);
            }

            var service = VaultService.Open(_vaultPath, cipherKey);
            if (service is null)
            {
                // Should be unreachable given the AES-GCM verification above,
                // but defense-in-depth.
                return RecordFailure(tracker);
            }

            tracker.Reset();

            // Opportunistic format upgrade: re-wrap under the same wrapping key
            // with the bound AAD and rewrite the header. Best-effort — a failure
            // here must never cost the user a successful unlock, because the
            // old-format header still works fine.
            var upgraded = false;
            if (header.NeedsFormatUpgrade)
            {
                upgraded = TryUpgradeHeaderFormat(header, wrappingKey, cipherKey);
            }

            return new UnlockResult(
                UnlockStatus.Success,
                Service: service,
                RequiresRecoverySetup: !header.HasRecovery,
                HeaderUpgraded: upgraded);
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

    // -------------------------------------------------------------- recovery

    /// <summary>
    /// Issue a recovery code for this vault, verifying the master password
    /// first. Works both for a vault just created and for an existing vault
    /// being migrated — identical path, so there is only one thing to test and
    /// one screen to build.
    ///
    /// Calling this on a vault that already has recovery REPLACES the existing
    /// code: the old one stops working immediately. That is the "I lost my
    /// recovery code" flow, and it is safe because it requires the master
    /// password.
    ///
    /// The returned code exists only in the returned record. It is never
    /// written to disk in plaintext and cannot be recovered later.
    /// </summary>
    public RecoverySetupResult SetUpRecovery(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);

        if (!VaultExists)
            return new RecoverySetupResult(false, ErrorMessage: "No vault found.");

        VaultHeader header;
        try
        {
            header = VaultHeader.FromJson(File.ReadAllText(HeaderPath));
        }
        catch (Exception ex)
        {
            return new RecoverySetupResult(false, ErrorMessage: $"Vault header unreadable: {ex.Message}");
        }

        var kdf = header.ToKdfParameters();
        var pwBytes = SecureMemory.Utf8ToBytes(masterPassword);
        byte[]? wrappingKey = null;
        byte[]? cipherKey = null;
        byte[]? recoveryKey = null;
        byte[]? recoveryWrapped = null;
        byte[]? codeBytes = null;

        try
        {
            wrappingKey = Argon2Kdf.DeriveKey(pwBytes, kdf);
            try
            {
                cipherKey = AesGcmCrypto.Decrypt(
                    wrappingKey, header.GetWrappedCipherKey(), header.GetMasterWrapAad());
            }
            catch (CryptographicException)
            {
                return new RecoverySetupResult(false, ErrorMessage: "Incorrect master password.");
            }

            // Fresh code, fresh salt, its own KDF parameters.
            var code = RecoveryCode.Generate();
            var canonical = RecoveryCode.Normalize(code)
                ?? throw new InvalidOperationException("Generated recovery code failed its own validation.");

            var recoveryKdf = KdfParameters.CreateDefault();
            codeBytes = SecureMemory.Utf8ToBytes(canonical);
            recoveryKey = Argon2Kdf.DeriveKey(codeBytes, recoveryKdf);
            recoveryWrapped = AesGcmCrypto.Encrypt(
                recoveryKey, cipherKey, VaultHeader.BuildRecoveryWrapAad(recoveryKdf));

            // The master wrap may still be in the old format; rebuilding the
            // header here upgrades it in the same write.
            byte[]? masterWrapped = null;
            try
            {
                masterWrapped = header.NeedsFormatUpgrade
                    ? AesGcmCrypto.Encrypt(wrappingKey, cipherKey, VaultHeader.BuildMasterWrapAad(kdf))
                    : header.GetWrappedCipherKey();

                var newHeader = VaultHeader.Build(
                    kdf,
                    masterWrapped,
                    RecoveryEnvelope.Build(recoveryKdf, recoveryWrapped),
                    header.CreatedUtc);

                WriteHeaderAtomic(newHeader);
            }
            finally
            {
                if (masterWrapped is not null) SecureMemory.Zero(masterWrapped);
            }

            return new RecoverySetupResult(true, Code: code);
        }
        catch (Exception ex)
        {
            return new RecoverySetupResult(false, ErrorMessage: ex.Message);
        }
        finally
        {
            SecureMemory.Zero(pwBytes);
            if (codeBytes is not null) SecureMemory.Zero(codeBytes);
            if (wrappingKey is not null) SecureMemory.Zero(wrappingKey);
            if (recoveryKey is not null) SecureMemory.Zero(recoveryKey);
            if (cipherKey is not null) SecureMemory.Zero(cipherKey);
            if (recoveryWrapped is not null) SecureMemory.Zero(recoveryWrapped);
        }
    }

    /// <summary>
    /// Use a recovery code to set a NEW master password, without knowing the
    /// old one. Unwraps the cipher key via the recovery envelope, re-wraps it
    /// under the new password with a fresh salt, and rewrites the header.
    ///
    /// The database is never re-encrypted — the cipher key does not change, so
    /// this is instant regardless of vault size.
    ///
    /// The recovery envelope is left INTACT and the same recovery code keeps
    /// working. Rotating it here would silently invalidate the copy the user
    /// just successfully used, which is the worst possible moment to hand
    /// someone a new secret to write down. Offer regeneration separately, once
    /// they are safely back in.
    ///
    /// Attempts are rate-limited by the same backoff as password attempts.
    /// </summary>
    public UnlockResult ResetMasterPasswordWithRecoveryCode(string recoveryCode, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(recoveryCode);
        ArgumentNullException.ThrowIfNull(newPassword);

        if (!VaultExists)
            return new UnlockResult(UnlockStatus.VaultMissing);

        var canonical = RecoveryCode.Normalize(recoveryCode);
        if (canonical is null)
        {
            // Malformed input is not a guess attempt — do not burn an attempt
            // or trigger backoff for a typo that could never have been right.
            return new UnlockResult(
                UnlockStatus.MalformedRecoveryCode,
                ErrorMessage: $"A recovery code is {RecoveryCode.CharacterCount} characters " +
                              "from the Lumos alphabet, in six groups of five.");
        }

        var validation = MasterPasswordPolicy.Validate(newPassword);
        if (!validation.IsAllowed)
            return new UnlockResult(UnlockStatus.UnexpectedError, ErrorMessage: validation.Message);

        var tracker = new FailedAttemptTracker(_vaultPath);
        var backoff = CheckBackoff(tracker);
        if (backoff is not null) return backoff;

        VaultHeader header;
        try
        {
            header = VaultHeader.FromJson(File.ReadAllText(HeaderPath));
        }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.VaultCorrupted, ErrorMessage: ex.Message);
        }

        if (header.Recovery is null)
            return new UnlockResult(UnlockStatus.RecoveryNotConfigured);

        var recoveryKdf = header.Recovery.ToKdfParameters();
        try { recoveryKdf.Validate(); }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.VaultCorrupted, ErrorMessage: ex.Message);
        }

        var codeBytes = SecureMemory.Utf8ToBytes(canonical);
        var newPwBytes = SecureMemory.Utf8ToBytes(newPassword);
        byte[]? recoveryKey = null;
        byte[]? cipherKey = null;
        byte[]? newWrapping = null;
        byte[]? newWrapped = null;

        try
        {
            recoveryKey = Argon2Kdf.DeriveKey(codeBytes, recoveryKdf);
            try
            {
                cipherKey = AesGcmCrypto.Decrypt(
                    recoveryKey,
                    header.Recovery.GetWrappedCipherKey(),
                    VaultHeader.BuildRecoveryWrapAad(recoveryKdf));
            }
            catch (CryptographicException)
            {
                tracker.RecordFailure();
                var count = tracker.GetCount();
                return new UnlockResult(
                    UnlockStatus.WrongRecoveryCode,
                    Backoff: UnlockBackoff.GetDelayAfterFailure(count),
                    FailedAttemptCount: count);
            }

            // Re-wrap the same cipher key under the new master password.
            var newKdf = KdfParameters.CreateDefault();
            newWrapping = Argon2Kdf.DeriveKey(newPwBytes, newKdf);
            newWrapped = AesGcmCrypto.Encrypt(
                newWrapping, cipherKey, VaultHeader.BuildMasterWrapAad(newKdf));

            WriteHeaderAtomic(VaultHeader.Build(
                newKdf, newWrapped, header.Recovery, header.CreatedUtc));

            tracker.Reset();

            var service = VaultService.Open(_vaultPath, cipherKey);
            if (service is null)
                return new UnlockResult(UnlockStatus.VaultCorrupted,
                    ErrorMessage: "Recovery succeeded but the database could not be opened.");

            return new UnlockResult(UnlockStatus.Success, Service: service);
        }
        catch (Exception ex)
        {
            return new UnlockResult(UnlockStatus.UnexpectedError, ErrorMessage: ex.Message);
        }
        finally
        {
            SecureMemory.Zero(codeBytes);
            SecureMemory.Zero(newPwBytes);
            if (recoveryKey is not null) SecureMemory.Zero(recoveryKey);
            if (cipherKey is not null) SecureMemory.Zero(cipherKey);
            if (newWrapping is not null) SecureMemory.Zero(newWrapping);
            if (newWrapped is not null) SecureMemory.Zero(newWrapped);
        }
    }

    // ------------------------------------------------------- change password

    /// <summary>
    /// Change the master password. Verifies the old password, derives a new
    /// wrapping key from the new password (with a fresh salt), re-wraps the
    /// existing cipher key, and rewrites the header. The DB file itself
    /// is untouched — instant operation.
    ///
    /// The recovery envelope is carried across unchanged: it wraps the cipher
    /// key, not the password, so the user's existing recovery code keeps
    /// working after a password change. Dropping it here would silently revoke
    /// a code the user believes is still valid.
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

        // Re-read: Unlock may have upgraded the header format in place.
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
            cipherKey = AesGcmCrypto.Decrypt(
                oldWrapping, oldHeader.GetWrappedCipherKey(), oldHeader.GetMasterWrapAad());

            var newKdf = KdfParameters.CreateDefault();
            newWrapping = Argon2Kdf.DeriveKey(newPwBytes, newKdf);
            newWrapped = AesGcmCrypto.Encrypt(
                newWrapping, cipherKey, VaultHeader.BuildMasterWrapAad(newKdf));

            WriteHeaderAtomic(VaultHeader.Build(
                newKdf, newWrapped, oldHeader.Recovery, oldHeader.CreatedUtc));

            return new UnlockResult(
                UnlockStatus.Success,
                Service: unlock.Service,
                RequiresRecoverySetup: !oldHeader.HasRecovery);
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

    // --------------------------------------------------------------- helpers

    /// <summary>
    /// Enforce any pending backoff from prior failures. Returns a
    /// BackoffRequired result if the caller must wait, otherwise null.
    /// </summary>
    private UnlockResult? CheckBackoff(FailedAttemptTracker tracker)
    {
        var priorFailures = tracker.GetCount();
        if (priorFailures <= 0) return null;

        var lastFailure = tracker.GetLastFailureUtc();
        var required = UnlockBackoff.GetDelayAfterFailure(priorFailures);
        if (required <= TimeSpan.Zero || !lastFailure.HasValue) return null;

        var elapsed = _utcNow() - lastFailure.Value;
        if (elapsed >= required) return null;

        return new UnlockResult(
            Status: UnlockStatus.BackoffRequired,
            RemainingBackoff: required - elapsed,
            FailedAttemptCount: priorFailures);
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

    private VaultHeader? TryReadHeader()
    {
        try
        {
            if (!File.Exists(HeaderPath)) return null;
            return VaultHeader.FromJson(File.ReadAllText(HeaderPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Re-wrap the master envelope with the bound AAD and rewrite the header in
    /// the current format, preserving any recovery envelope. Best-effort: an
    /// old-format header still unlocks perfectly well, so a failure here is not
    /// worth failing the user's unlock over.
    /// </summary>
    private bool TryUpgradeHeaderFormat(VaultHeader oldHeader, byte[] wrappingKey, byte[] cipherKey)
    {
        byte[]? rewrapped = null;
        try
        {
            var kdf = oldHeader.ToKdfParameters();
            rewrapped = AesGcmCrypto.Encrypt(
                wrappingKey, cipherKey, VaultHeader.BuildMasterWrapAad(kdf));

            WriteHeaderAtomic(VaultHeader.Build(
                kdf, rewrapped, oldHeader.Recovery, oldHeader.CreatedUtc));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (rewrapped is not null) SecureMemory.Zero(rewrapped);
        }
    }

    /// <summary>
    /// Write the header via a temp file and an atomic move, so a crash or power
    /// loss mid-write leaves the previous header intact. A truncated header
    /// means an unopenable vault, so this must never be a partial write.
    /// </summary>
    private void WriteHeaderAtomic(VaultHeader header)
    {
        var tempHeader = HeaderPath + ".tmp";
        File.WriteAllText(tempHeader, header.ToJson());
        File.Move(tempHeader, HeaderPath, overwrite: true);
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
