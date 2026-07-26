using System.Text.Json;
using Lumos.Core.Crypto;
using Lumos.Core.Recovery;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class VaultRecoveryTests : IDisposable
{
    private const string Password = "rabbit-trumpet-glacier-77";
    private const string NewPassword = "otter-lantern-marble-42";

    private readonly string _tempDir;
    private readonly string _vaultPath;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public VaultRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.db");
        LumosCoreBootstrap.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private VaultManager Manager() => new(_vaultPath, false, () => _now);
    private string HeaderPath => _vaultPath + ".header.json";
    private VaultHeader ReadHeader() => VaultHeader.FromJson(File.ReadAllText(HeaderPath));

    private void CreateVault()
    {
        using var v = Manager().CreateVault(Password);
    }

    // ---------- setup ----------

    [Fact]
    public void New_vault_has_no_recovery_until_set_up()
    {
        CreateVault();
        Assert.False(Manager().HasRecovery);

        var result = Manager().Unlock(Password);
        using (result.Service) { }
        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.True(result.RequiresRecoverySetup);
    }

    [Fact]
    public void SetUpRecovery_issues_a_wellformed_code_and_marks_the_vault()
    {
        CreateVault();

        var setup = Manager().SetUpRecovery(Password);

        Assert.True(setup.Success, setup.ErrorMessage);
        Assert.NotNull(setup.Code);
        Assert.True(RecoveryCode.IsWellFormed(setup.Code));
        Assert.True(Manager().HasRecovery);
    }

    [Fact]
    public void SetUpRecovery_rejects_the_wrong_master_password()
    {
        CreateVault();

        var setup = Manager().SetUpRecovery("definitely-not-the-password");

        Assert.False(setup.Success);
        Assert.Null(setup.Code);
        Assert.False(Manager().HasRecovery);
    }

    [Fact]
    public void Recovery_code_is_never_written_to_the_header()
    {
        CreateVault();
        var setup = Manager().SetUpRecovery(Password);
        var canonical = RecoveryCode.Normalize(setup.Code!)!;

        var raw = File.ReadAllText(HeaderPath);

        Assert.DoesNotContain(canonical, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(setup.Code!, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_uses_a_different_salt_from_the_master_wrap()
    {
        CreateVault();
        Manager().SetUpRecovery(Password);

        var header = ReadHeader();

        Assert.NotNull(header.Recovery);
        Assert.NotEqual(header.KdfSaltBase64, header.Recovery!.KdfSaltBase64);
        Assert.NotEqual(header.WrappedCipherKeyBase64, header.Recovery.WrappedCipherKeyBase64);
    }

    [Fact]
    public void After_setup_the_master_password_still_works()
    {
        CreateVault();
        Manager().SetUpRecovery(Password);

        var result = Manager().Unlock(Password);
        using (result.Service) { }

        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.False(result.RequiresRecoverySetup);
    }

    // ---------- reset via recovery ----------

    [Fact]
    public void Recovery_code_resets_the_master_password_and_opens_the_vault()
    {
        CreateVault();
        var code = Manager().SetUpRecovery(Password).Code!;

        var reset = Manager().ResetMasterPasswordWithRecoveryCode(code, NewPassword);
        using (reset.Service) { }
        Assert.Equal(UnlockStatus.Success, reset.Status);

        // New password works...
        var fresh = Manager().Unlock(NewPassword);
        using (fresh.Service) { }
        Assert.Equal(UnlockStatus.Success, fresh.Status);

        // ...and the old one does not.
        var stale = Manager().Unlock(Password);
        using (stale.Service) { }
        Assert.Equal(UnlockStatus.WrongPassword, stale.Status);
    }

    [Fact]
    public void Recovery_code_still_works_after_being_used_once()
    {
        CreateVault();
        var code = Manager().SetUpRecovery(Password).Code!;

        using (Manager().ResetMasterPasswordWithRecoveryCode(code, NewPassword).Service) { }

        var second = Manager().ResetMasterPasswordWithRecoveryCode(code, "third-password-here-9");
        using (second.Service) { }
        Assert.Equal(UnlockStatus.Success, second.Status);
    }

    [Fact]
    public void Recovery_code_survives_a_normal_password_change()
    {
        CreateVault();
        var code = Manager().SetUpRecovery(Password).Code!;

        using (Manager().ChangeMasterPassword(Password, NewPassword).Service) { }

        Assert.True(Manager().HasRecovery);

        var reset = Manager().ResetMasterPasswordWithRecoveryCode(code, "yet-another-password-3");
        using (reset.Service) { }
        Assert.Equal(UnlockStatus.Success, reset.Status);
    }

    [Fact]
    public void Wrong_recovery_code_is_rejected_and_reported_distinctly()
    {
        CreateVault();
        Manager().SetUpRecovery(Password);

        var wrong = RecoveryCode.Generate();   // valid shape, wrong value
        var result = Manager().ResetMasterPasswordWithRecoveryCode(wrong, NewPassword);

        Assert.Equal(UnlockStatus.WrongRecoveryCode, result.Status);

        // Original password unaffected.
        var still = Manager().Unlock(Password);
        using (still.Service) { }
        Assert.Equal(UnlockStatus.Success, still.Status);
    }

    [Fact]
    public void Malformed_recovery_code_is_reported_separately_and_costs_no_attempt()
    {
        CreateVault();
        Manager().SetUpRecovery(Password);

        var mgr = Manager();
        var result = mgr.ResetMasterPasswordWithRecoveryCode("not-a-code", NewPassword);

        Assert.Equal(UnlockStatus.MalformedRecoveryCode, result.Status);
        Assert.Equal(0, mgr.CurrentFailedAttemptCount);
    }

    [Fact]
    public void Recovery_on_a_vault_without_recovery_reports_not_configured()
    {
        CreateVault();

        var result = Manager().ResetMasterPasswordWithRecoveryCode(
            RecoveryCode.Generate(), NewPassword);

        Assert.Equal(UnlockStatus.RecoveryNotConfigured, result.Status);
    }

    [Fact]
    public void Regenerating_recovery_invalidates_the_previous_code()
    {
        CreateVault();
        var first = Manager().SetUpRecovery(Password).Code!;
        var second = Manager().SetUpRecovery(Password).Code!;

        Assert.NotEqual(first, second);

        var oldAttempt = Manager().ResetMasterPasswordWithRecoveryCode(first, NewPassword);
        Assert.Equal(UnlockStatus.WrongRecoveryCode, oldAttempt.Status);

        var newAttempt = Manager().ResetMasterPasswordWithRecoveryCode(second, NewPassword);
        using (newAttempt.Service) { }
        Assert.Equal(UnlockStatus.Success, newAttempt.Status);
    }

    // ---------- format migration ----------

    /// <summary>
    /// Rewrite the header in the legacy v2 shape: no recovery envelope, and the
    /// master wrap authenticated with the old fixed AAD string. This is exactly
    /// what a vault created by Lumos v1.0.0 looks like on disk.
    /// </summary>
    private void DowngradeHeaderToV2()
    {
        var header = ReadHeader();
        var kdf = header.ToKdfParameters();

        // Unwrap with whatever AAD the current header uses, then re-wrap with
        // the legacy AAD so the file is genuinely a v2 artifact.
        var pw = SecureMemory.Utf8ToBytes(Password);
        var wrappingKey = Argon2Kdf.DeriveKey(pw, kdf);
        var cipherKey = AesGcmCrypto.Decrypt(
            wrappingKey, header.GetWrappedCipherKey(), header.GetMasterWrapAad());
        var legacyWrapped = AesGcmCrypto.Encrypt(
            wrappingKey, cipherKey, VaultHeader.LegacyMasterWrapAad);

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            kdfAlgorithm = "argon2id",
            kdfMemoryKb = kdf.MemoryKb,
            kdfIterations = kdf.Iterations,
            kdfParallelism = kdf.Parallelism,
            kdfSalt = Convert.ToBase64String(kdf.Salt),
            kdfKeyLength = kdf.KeyLengthBytes,
            cipher = "sqlite3mc-sqlcipher-v4",
            wrappedCipherKey = Convert.ToBase64String(legacyWrapped),
            createdUtc = header.CreatedUtc,
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(HeaderPath, json);

        SecureMemory.Zero(pw);
        SecureMemory.Zero(wrappingKey);
        SecureMemory.Zero(cipherKey);
    }

    [Fact]
    public void Legacy_v2_vault_still_unlocks()
    {
        CreateVault();
        DowngradeHeaderToV2();
        Assert.Equal(2, ReadHeader().SchemaVersion);

        var result = Manager().Unlock(Password);
        using (result.Service) { }

        Assert.Equal(UnlockStatus.Success, result.Status);
    }

    [Fact]
    public void Legacy_v2_vault_is_upgraded_in_place_on_unlock()
    {
        CreateVault();
        DowngradeHeaderToV2();

        var result = Manager().Unlock(Password);
        using (result.Service) { }

        Assert.True(result.HeaderUpgraded);
        Assert.Equal(VaultHeader.CurrentFormatVersion, ReadHeader().SchemaVersion);

        // And it still opens after the rewrite.
        var again = Manager().Unlock(Password);
        using (again.Service) { }
        Assert.Equal(UnlockStatus.Success, again.Status);
    }

    [Fact]
    public void Legacy_v2_vault_asks_for_recovery_setup_on_unlock()
    {
        CreateVault();
        DowngradeHeaderToV2();

        var result = Manager().Unlock(Password);
        using (result.Service) { }

        Assert.True(result.RequiresRecoverySetup);
    }

    [Fact]
    public void Legacy_v2_vault_can_have_recovery_added_without_reencrypting()
    {
        CreateVault();
        DowngradeHeaderToV2();
        var dbBefore = File.ReadAllBytes(_vaultPath);

        var setup = Manager().SetUpRecovery(Password);
        Assert.True(setup.Success, setup.ErrorMessage);

        // The database file is byte-identical: only the header changed.
        Assert.Equal(dbBefore, File.ReadAllBytes(_vaultPath));
        Assert.Equal(VaultHeader.CurrentFormatVersion, ReadHeader().SchemaVersion);

        var reset = Manager().ResetMasterPasswordWithRecoveryCode(setup.Code!, NewPassword);
        using (reset.Service) { }
        Assert.Equal(UnlockStatus.Success, reset.Status);
    }

    // ---------- tamper resistance ----------

    [Fact]
    public void Master_and_recovery_wraps_are_not_interchangeable()
    {
        CreateVault();
        Manager().SetUpRecovery(Password);

        var header = ReadHeader();

        // Splice the recovery ciphertext into the master slot. Distinct AAD
        // purposes mean this must fail authentication rather than be accepted.
        var spliced = VaultHeader.Build(
            header.ToKdfParameters(),
            header.Recovery!.GetWrappedCipherKey(),
            header.Recovery,
            header.CreatedUtc);
        File.WriteAllText(HeaderPath, spliced.ToJson());

        var result = Manager().Unlock(Password);
        using (result.Service) { }

        Assert.NotEqual(UnlockStatus.Success, result.Status);
    }

    [Fact]
    public void Editing_the_kdf_cost_in_the_header_prevents_unlock()
    {
        CreateVault();

        var header = ReadHeader();
        var tampered = new VaultHeader
        {
            SchemaVersion = header.SchemaVersion,
            KdfAlgorithm = header.KdfAlgorithm,
            KdfMemoryKb = 8 * 1024,          // downgraded from 64 MB
            KdfIterations = header.KdfIterations,
            KdfParallelism = header.KdfParallelism,
            KdfSaltBase64 = header.KdfSaltBase64,
            KdfKeyLengthBytes = header.KdfKeyLengthBytes,
            Cipher = header.Cipher,
            WrappedCipherKeyBase64 = header.WrappedCipherKeyBase64,
            CreatedUtc = header.CreatedUtc,
        };
        File.WriteAllText(HeaderPath, tampered.ToJson());

        var result = Manager().Unlock(Password);
        using (result.Service) { }

        Assert.NotEqual(UnlockStatus.Success, result.Status);
    }
}
