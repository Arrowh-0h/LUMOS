using Lumos.Core.Security;
using Lumos.Core.Tests.Fakes;
using Xunit;

namespace Lumos.Core.Tests;

public class AutoLockServiceTests
{
    private static AutoLockService Build(
        AutoLockSettings settings,
        FakeIdleMonitor idle,
        FakeSystemEventSource src,
        TimeSpan? pollInterval = null)
    {
        return new AutoLockService(
            settings,
            idle,
            src,
            utcNowProvider: null,
            pollInterval: pollInterval ?? TimeSpan.FromMilliseconds(20));
    }

    // ---------- Idle ----------

    [Fact]
    public async Task Idle_fires_when_threshold_reached()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { IdleTimeout = TimeSpan.FromMilliseconds(50) };
        using var svc = Build(settings, idle, src);

        LockReason? captured = null;
        svc.LockRequested += (_, e) => captured = e.Reason;

        svc.Start();
        idle.IdleTime = TimeSpan.FromSeconds(10);  // way past threshold

        // Poll interval is 20ms; give it ample time.
        await WaitForLock(() => captured);

        Assert.Equal(LockReason.Idle, captured);
    }

    [Fact]
    public async Task Idle_does_not_fire_when_below_threshold()
    {
        var idle = new FakeIdleMonitor { IdleTime = TimeSpan.FromMilliseconds(10) };
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { IdleTimeout = TimeSpan.FromSeconds(60) };
        using var svc = Build(settings, idle, src);

        bool fired = false;
        svc.LockRequested += (_, _) => fired = true;

        svc.Start();
        await Task.Delay(100);

        Assert.False(fired);
    }

    [Fact]
    public async Task Null_idle_timeout_means_never_lock_on_idle()
    {
        var idle = new FakeIdleMonitor { IdleTime = TimeSpan.FromHours(1) };
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { IdleTimeout = null };
        using var svc = Build(settings, idle, src);

        bool fired = false;
        svc.LockRequested += (_, _) => fired = true;

        svc.Start();
        await Task.Delay(100);

        Assert.False(fired);
    }

    // ---------- System events ----------

    [Fact]
    public async Task System_suspending_fires_when_enabled()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { LockOnSystemSleep = true };
        using var svc = Build(settings, idle, src);

        LockReason? captured = null;
        svc.LockRequested += (_, e) => captured = e.Reason;

        svc.Start();
        src.FireSuspending();
        await Task.Yield();

        Assert.Equal(LockReason.SystemSuspending, captured);
    }

    [Fact]
    public async Task System_suspending_does_not_fire_when_disabled()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { LockOnSystemSleep = false };
        using var svc = Build(settings, idle, src);

        bool fired = false;
        svc.LockRequested += (_, _) => fired = true;

        svc.Start();
        src.FireSuspending();
        await Task.Delay(20);

        Assert.False(fired);
    }

    [Fact]
    public async Task Screen_lock_fires_when_enabled()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { LockOnScreenLock = true };
        using var svc = Build(settings, idle, src);

        LockReason? captured = null;
        svc.LockRequested += (_, e) => captured = e.Reason;

        svc.Start();
        src.FireScreenLocked();
        await Task.Yield();

        Assert.Equal(LockReason.ScreenLocked, captured);
    }

    [Fact]
    public async Task Minimize_threshold_fires_when_enabled()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        var settings = AutoLockSettings.Default with { LockOnMinimize = true };
        using var svc = Build(settings, idle, src);

        LockReason? captured = null;
        svc.LockRequested += (_, e) => captured = e.Reason;

        svc.Start();
        src.FireMinimized();
        await Task.Yield();

        Assert.Equal(LockReason.Minimized, captured);
    }

    // ---------- Manual + single-fire ----------

    [Fact]
    public void Manual_request_fires_immediately()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        using var svc = Build(AutoLockSettings.Default, idle, src);

        LockReason? captured = null;
        svc.LockRequested += (_, e) => captured = e.Reason;

        svc.Start();
        svc.RequestLock();

        Assert.Equal(LockReason.Manual, captured);
    }

    [Fact]
    public async Task Service_fires_only_once_per_Start()
    {
        // If multiple triggers fire, only the first should raise.
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        using var svc = Build(AutoLockSettings.Default, idle, src);

        var fires = 0;
        svc.LockRequested += (_, _) => fires++;

        svc.Start();
        svc.RequestLock();
        src.FireScreenLocked();
        src.FireSuspending();
        await Task.Delay(20);

        Assert.Equal(1, fires);
    }

    [Fact]
    public async Task Stop_unsubscribes_from_events()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        using var svc = Build(AutoLockSettings.Default, idle, src);

        bool fired = false;
        svc.LockRequested += (_, _) => fired = true;

        svc.Start();
        svc.Stop();
        src.FireScreenLocked();
        src.FireSuspending();
        src.FireMinimized();
        idle.IdleTime = TimeSpan.FromHours(1);
        await Task.Delay(100);

        Assert.False(fired);
    }

    [Fact]
    public async Task Restart_after_stop_works()
    {
        var idle = new FakeIdleMonitor();
        var src = new FakeSystemEventSource();
        using var svc = Build(AutoLockSettings.Default, idle, src);

        int fires = 0;
        svc.LockRequested += (_, _) => fires++;

        svc.Start();
        svc.RequestLock();
        Assert.Equal(1, fires);

        svc.Stop();
        svc.Start();
        svc.RequestLock();
        await Task.Yield();

        Assert.Equal(2, fires);
    }

    // ---------- Helpers ----------

    private static async Task WaitForLock(Func<LockReason?> probe, int timeoutMs = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (probe() is not null) return;
            await Task.Delay(10);
        }
    }
}
