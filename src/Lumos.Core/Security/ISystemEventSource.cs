namespace Lumos.Core.Security;

/// <summary>
/// Signals that should trigger an auto-lock when they fire. The Windows
/// implementation in Lumos.Desktop subscribes to:
///   - Microsoft.Win32.SystemEvents.PowerModeChanged (Suspend → Suspended)
///   - Microsoft.Win32.SystemEvents.SessionSwitch (SessionLock → ScreenLocked)
///   - WPF Window.StateChanged (Minimized → WindowMinimized after threshold)
///
/// The desktop layer fans these into our event without making us depend on
/// any of those types.
/// </summary>
public interface ISystemEventSource
{
    /// <summary>Raised when the user minimizes the window for the configured threshold.</summary>
    event EventHandler? WindowMinimizedThresholdReached;

    /// <summary>Raised when the user restores / focuses the window.</summary>
    event EventHandler? WindowRestored;

    /// <summary>Raised when the OS is going to sleep / hibernate.</summary>
    event EventHandler? SystemSuspending;

    /// <summary>Raised when the user locks their session (Win+L).</summary>
    event EventHandler? ScreenLocked;
}
