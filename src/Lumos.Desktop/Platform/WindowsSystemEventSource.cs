using System.Windows;
using Microsoft.Win32;
using Lumos.Core.Security;

namespace Lumos.Desktop.Platform;

/// <summary>
/// Bridges Windows-specific events to our platform-agnostic ISystemEventSource:
///
///   SystemEvents.PowerModeChanged (Suspend)   -> SystemSuspending
///   SystemEvents.SessionSwitch (SessionLock)  -> ScreenLocked
///   Window.StateChanged (Minimized, threshold)-> WindowMinimizedThresholdReached
///   Window.StateChanged (out of Minimized)    -> WindowRestored
///
/// Construction takes the main Window so we can hook its StateChanged event.
/// Disposes detach all handlers.
/// </summary>
public sealed class WindowsSystemEventSource : ISystemEventSource, IDisposable
{
    private readonly Window _window;
    private readonly TimeSpan _minimizeThreshold;
    private System.Threading.Timer? _minimizeTimer;
    private bool _disposed;

    public event EventHandler? WindowMinimizedThresholdReached;
    public event EventHandler? WindowRestored;
    public event EventHandler? SystemSuspending;
    public event EventHandler? ScreenLocked;

    public WindowsSystemEventSource(Window window, TimeSpan minimizeThreshold)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _minimizeThreshold = minimizeThreshold;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _window.StateChanged += OnWindowStateChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            SystemSuspending?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            ScreenLocked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            // Start (or restart) the threshold timer. We don't fire
            // WindowMinimizedThresholdReached until the user has kept the
            // window minimized for the configured time.
            CancelMinimizeTimer();
            _minimizeTimer = new System.Threading.Timer(
                _ => WindowMinimizedThresholdReached?.Invoke(this, EventArgs.Empty),
                state: null,
                dueTime: _minimizeThreshold,
                period: Timeout.InfiniteTimeSpan);
        }
        else
        {
            // Window came back; cancel any pending minimize timer.
            CancelMinimizeTimer();
            WindowRestored?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CancelMinimizeTimer()
    {
        var t = _minimizeTimer;
        _minimizeTimer = null;
        t?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _window.StateChanged -= OnWindowStateChanged;
        CancelMinimizeTimer();
    }
}
