using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class VaultManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _vaultPath;

    // Mocked clock so backoff tests don't have to actually sleep.
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private DateTimeOffset NowProvider() => _now;

    public VaultManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-mgr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.db");
        LumosCoreBootstrap.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private VaultManager Manager(bool selfDestruct = false)
        => new(_vaultPath, selfDestruct, NowProvider);

    // ---------- CreateVault ----------

    [Fact]
    public void CreateVault_rejects_short_password()
    {
        var mgr = Manager();
        var ex = Assert.Throws<InvalidOperationException>(
            () => mgr.CreateVault("short"));
        Assert.Contains("at least", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateVault_rejects_empty_password()
    {
        var mgr = Manager();
        Assert.Throws<InvalidOperationException>(() => mgr.CreateVault(""));
    }

    [Fact]
    public void CreateVault_accepts_valid_password()
    {
        var mgr = Manager();
        using var v = mgr.CreateVault("rabbit-trumpet-glacier-77");
        Assert.True(v.IsOpen);
        Assert.True(File.Exists(_vaultPath));
    }

    [Fact]
    public void CreateVault_accepts_weak_long_password_with_warning_path()
    {
        // Policy says: warn but allow. We don't surface the warning through
        // CreateVault (UI gets it from MasterPasswordPolicy.Validate beforehand),
        // but CreateVault must not reject it.
        var mgr = Manager();
        using var v = mgr.CreateVault("aaaaaaaaaaaa");
        Assert.True(v.IsOpen);
    }

    // ---------- Unlock: happy path ----------

    [Fact]
    public void Unlock_with_correct_password_succeeds_and_resets_attempts()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("rabbit-trumpet-glacier-77")) { }

        // Sprinkle some failures first.
        mgr.Unlock("wrong-but-long-enough");
        mgr.Unlock("also-wrong-and-long");
        Assert.Equal(2, mgr.CurrentFailedAttemptCount);

        // Advance the clock past the backoff window so we're not blocked.
        _now = _now.AddMinutes(5);

        var result = mgr.Unlock("rabbit-trumpet-glacier-77");
        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.NotNull(result.Service);
        Assert.Equal(0, mgr.CurrentFailedAttemptCount);

        result.Service!.Dispose();
    }

    // ---------- Unlock: wrong password ----------

    [Fact]
    public void Unlock_with_wrong_password_records_failure_and_returns_backoff()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("rabbit-trumpet-glacier-77")) { }

        // Spec curve: 1st failure → no required delay before attempt 2.
        // So the first wrong attempt proceeds and returns WrongPassword,
        // with Backoff=0s (the delay applied AFTER this attempt, before next).
        var result = mgr.Unlock("wrong-password-long");
        Assert.Equal(UnlockStatus.WrongPassword, result.Status);
        Assert.Equal(1, result.FailedAttemptCount);
        Assert.Equal(TimeSpan.Zero, result.Backoff);

        // 2nd attempt also proceeds (delay before attempt 2 is 0).
        // Returns WrongPassword with Backoff=1s (the delay AFTER attempt 2).
        var second = mgr.Unlock("still-wrong-and-long");
        Assert.Equal(UnlockStatus.WrongPassword, second.Status);
        Assert.Equal(2, second.FailedAttemptCount);
        Assert.Equal(TimeSpan.FromSeconds(1), second.Backoff);

        // 3rd attempt is now blocked because of the 1s window from attempt 2.
        var third = mgr.Unlock("yet-another-wrong-one");
        Assert.Equal(UnlockStatus.BackoffRequired, third.Status);
        Assert.True(third.RemainingBackoff > TimeSpan.Zero);
    }

    [Fact]
    public void Backoff_clears_once_time_passes()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("rabbit-trumpet-glacier-77")) { }

        mgr.Unlock("wrong-password-long");
        mgr.Unlock("wrong-password-also");  // forces 1s backoff

        // Inside the window: blocked.
        var blocked = mgr.Unlock("any-password-now");
        Assert.Equal(UnlockStatus.BackoffRequired, blocked.Status);

        // After the window: allowed to actually attempt.
        _now = _now.AddSeconds(2);
        var allowed = mgr.Unlock("wrong-password-still");
        Assert.Equal(UnlockStatus.WrongPassword, allowed.Status);
    }

    [Fact]
    public void Backoff_curve_matches_spec_progression()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("rabbit-trumpet-glacier-77")) { }

        var expectedDelays = new[]
        {
            TimeSpan.Zero,                  // after 1st failure
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
        };

        for (int i = 0; i < expectedDelays.Length; i++)
        {
            // Advance past prior backoff before each attempt.
            _now = _now.AddMinutes(2);
            var r = mgr.Unlock("wrong-password-here");
            Assert.Equal(UnlockStatus.WrongPassword, r.Status);
            Assert.Equal(i + 1, r.FailedAttemptCount);
            Assert.Equal(expectedDelays[i], r.Backoff);
        }
    }

    // ---------- Self-destruct ----------

    [Fact]
    public void Self_destruct_disabled_by_default()
    {
        var mgr = Manager(selfDestruct: false);
        using (var _ = mgr.CreateVault("rabbit-trumpet-glacier-77")) { }

        for (int i = 0; i < 15; i++)
        {
            _now = _now.AddMinutes(2);
            mgr.Unlock("wrong-password-here");
        }

        // Vault file must still exist.
        Assert.True(File.Exists(_vaultPath));
    }

    [Fact]
    public void Self_destruct_when_enabled_deletes_vault_at_threshold()
    {
        var mgr = Manager(selfDestruct: true);
        using (var _ = mgr.CreateVault("rabbit-trumpet-glacier-77")) { }

        UnlockResult? lastResult = null;
        for (int i = 0; i < UnlockBackoff.SelfDestructThreshold; i++)
        {
            _now = _now.AddMinutes(2);
            lastResult = mgr.Unlock("wrong-password-here");
        }

        Assert.NotNull(lastResult);
        Assert.Equal(UnlockStatus.SelfDestructed, lastResult!.Status);
        Assert.False(File.Exists(_vaultPath));
        Assert.False(File.Exists(_vaultPath + ".header.json"));
    }

    // ---------- Missing / corrupted ----------

    [Fact]
    public void Unlock_when_vault_missing()
    {
        var mgr = Manager();
        var r = mgr.Unlock("any-password-at-all");
        Assert.Equal(UnlockStatus.VaultMissing, r.Status);
    }

    // ---------- Change master password ----------

    [Fact]
    public void ChangeMasterPassword_requires_correct_old_password()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("old-rabbit-trumpet-77")) { }

        var r = mgr.ChangeMasterPassword("wrong-old-password", "new-glacier-spaceship-88");
        Assert.Equal(UnlockStatus.WrongPassword, r.Status);
    }

    [Fact]
    public void ChangeMasterPassword_validates_new_password()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("old-rabbit-trumpet-77")) { }

        var r = mgr.ChangeMasterPassword("old-rabbit-trumpet-77", "tiny");
        Assert.Equal(UnlockStatus.UnexpectedError, r.Status);
        Assert.Contains("at least", r.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangeMasterPassword_succeeds_and_old_password_no_longer_works()
    {
        var mgr = Manager();
        using (var _ = mgr.CreateVault("old-rabbit-trumpet-77")) { }

        var change = mgr.ChangeMasterPassword("old-rabbit-trumpet-77", "new-glacier-spaceship-88");
        Assert.Equal(UnlockStatus.Success, change.Status);
        Assert.NotNull(change.Service);
        change.Service!.Dispose();

        // Old password should now be rejected.
        var oldAttempt = mgr.Unlock("old-rabbit-trumpet-77");
        Assert.Equal(UnlockStatus.WrongPassword, oldAttempt.Status);

        // New password should work. Advance clock past backoff first.
        _now = _now.AddMinutes(5);
        var newAttempt = mgr.Unlock("new-glacier-spaceship-88");
        Assert.Equal(UnlockStatus.Success, newAttempt.Status);
        newAttempt.Service!.Dispose();
    }
}
