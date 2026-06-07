using Lumos.Core.Vault;
using Xunit;

namespace Lumos.Core.Tests;

public class FailedAttemptTrackerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _vaultPath;

    public FailedAttemptTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-tracker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Initial_count_is_zero()
    {
        var t = new FailedAttemptTracker(_vaultPath);
        Assert.Equal(0, t.GetCount());
        Assert.Null(t.GetLastFailureUtc());
    }

    [Fact]
    public void RecordFailure_increments_count()
    {
        var t = new FailedAttemptTracker(_vaultPath);
        t.RecordFailure();
        Assert.Equal(1, t.GetCount());
        t.RecordFailure();
        Assert.Equal(2, t.GetCount());
        t.RecordFailure();
        Assert.Equal(3, t.GetCount());
    }

    [Fact]
    public void RecordFailure_sets_last_failure_time()
    {
        var t = new FailedAttemptTracker(_vaultPath);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        t.RecordFailure();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var last = t.GetLastFailureUtc();
        Assert.NotNull(last);
        Assert.InRange(last!.Value, before, after);
    }

    [Fact]
    public void Reset_clears_count()
    {
        var t = new FailedAttemptTracker(_vaultPath);
        t.RecordFailure();
        t.RecordFailure();
        Assert.Equal(2, t.GetCount());

        t.Reset();
        Assert.Equal(0, t.GetCount());
        Assert.Null(t.GetLastFailureUtc());
    }

    [Fact]
    public void Count_persists_across_tracker_instances()
    {
        var t1 = new FailedAttemptTracker(_vaultPath);
        t1.RecordFailure();
        t1.RecordFailure();

        // Simulate app restart.
        var t2 = new FailedAttemptTracker(_vaultPath);
        Assert.Equal(2, t2.GetCount());
    }

    [Fact]
    public void Corrupt_file_is_treated_as_zero()
    {
        File.WriteAllText(_vaultPath + ".attempts.json", "this is not json");
        var t = new FailedAttemptTracker(_vaultPath);
        Assert.Equal(0, t.GetCount());
    }
}
