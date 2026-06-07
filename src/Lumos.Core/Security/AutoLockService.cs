namespace Lumos.Core.Security;

/// <summary>
/// Reasons the vault should be locked. Exposed on the LockRequestedEventArgs
/// so the UI can show "Vault locked because you stepped away" vs "...because
/// you closed the lid", etc.
/// </summary>
public enum LockReason
{
    Idle,
    Minimized,
    SystemSuspending,
    ScreenLocked,
    Manual,
}

public sealed class LockRequestedEventArgs : EventArgs
{
    public LockReason Reason { get; }
    public LockRequestedEventArgs(LockReason reason) { Reason = reason; }
}

/// <summary>
/// Watches idle time and system events, raises <see cref="LockRequested"/>
/// when any configured trigger fires.
///
/// Lifecycle: construct after the vault is unlocked, call Start, and Stop
/// (or Dispose) when the vault locks. The service does NOT lock the vault
/// directly — it just signals; the UI/app layer owns the vault reference
/// and decides what locking means.
///
/// Idle polling: every PollInterval (1 second by default), we ask the
/// IIdleMonitor for the current idle time. When it crosses the threshold,
/// we raise once and stop polling. Restarting requires Start() again,
/// which happens naturally when the vault is unlocked again.
/// </summary>
public sealed class AutoLockService : IDisposable
{
    private readonly IIdleMonitor _idleMonitor;
    private readonly ISystemEventSource _eventSource;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly AutoLockSettings _settings;
    private readonly TimeSpan _pollInterval;

    private CancellationTokenSource? _idleCts;
    private CancellationTokenSource? _minimizeCts;
    private bool _started;
    private bool _fired;
    private readonly object _gate = new();

    public event EventHandler<LockRequestedEventArgs>? LockRequested;

    public AutoLockService(
        AutoLockSettings settings,
        IIdleMonitor idleMonitor,
        ISystemEventSource eventSource,
        Func<DateTimeOffset>? utcNowProvider = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(idleMonitor);
        ArgumentNullException.ThrowIfNull(eventSource);
        _settings = settings;
        _idleMonitor = idleMonitor;
        _eventSource = eventSource;
        _utcNow = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _fired = false;

            _eventSource.SystemSuspending += OnSystemSuspending;
            _eventSource.ScreenLocked += OnScreenLocked;
            _eventSource.WindowMinimizedThresholdReached += OnWindowMinimized;
            _eventSource.WindowRestored += OnWindowRestored;

            if (_settings.IdleTimeout is not null)
            {
                _idleCts = new CancellationTokenSource();
                _ = PollIdleAsync(_idleCts.Token);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;

            _eventSource.SystemSuspending -= OnSystemSuspending;
            _eventSource.ScreenLocked -= OnScreenLocked;
            _eventSource.WindowMinimizedThresholdReached -= OnWindowMinimized;
            _eventSource.WindowRestored -= OnWindowRestored;

            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = null;
            _minimizeCts?.Cancel();
            _minimizeCts?.Dispose();
            _minimizeCts = null;
        }
    }

    /// <summary>
    /// Manually request a lock — used by Ctrl+L (Nox) and the Lock button.
    /// </summary>
    public void RequestLock(LockReason reason = LockReason.Manual)
    {
        Fire(reason);
    }

    private async Task PollIdleAsync(CancellationToken token)
    {
        var timeout = _settings.IdleTimeout!.Value;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_pollInterval, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                var idle = _idleMonitor.GetIdleTime();
                if (idle >= timeout)
                {
                    Fire(LockReason.Idle);
                    return;  // single-fire; Start() resets
                }
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    private void OnSystemSuspending(object? sender, EventArgs e)
    {
        if (_settings.LockOnSystemSleep) Fire(LockReason.SystemSuspending);
    }

    private void OnScreenLocked(object? sender, EventArgs e)
    {
        if (_settings.LockOnScreenLock) Fire(LockReason.ScreenLocked);
    }

    /// <summary>
    /// The system event source fires WindowMinimizedThresholdReached only
    /// after the user has kept the window minimized for the configured
    /// threshold. The desktop adapter is responsible for that timing because
    /// it owns the actual Window object.
    /// </summary>
    private void OnWindowMinimized(object? sender, EventArgs e)
    {
        if (_settings.LockOnMinimize) Fire(LockReason.Minimized);
    }

    private void OnWindowRestored(object? sender, EventArgs e)
    {
        // No-op for now. Could reset _fired here if we wanted multi-fire.
    }

    private void Fire(LockReason reason)
    {
        lock (_gate)
        {
            if (_fired) return;
            _fired = true;
        }
        LockRequested?.Invoke(this, new LockRequestedEventArgs(reason));
    }

    public void Dispose() => Stop();
}
