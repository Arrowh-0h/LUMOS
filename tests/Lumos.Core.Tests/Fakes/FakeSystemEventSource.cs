using Lumos.Core.Security;

namespace Lumos.Core.Tests.Fakes;

internal sealed class FakeSystemEventSource : ISystemEventSource
{
    public event EventHandler? WindowMinimizedThresholdReached;
    public event EventHandler? WindowRestored;
    public event EventHandler? SystemSuspending;
    public event EventHandler? ScreenLocked;

    public void FireMinimized() => WindowMinimizedThresholdReached?.Invoke(this, EventArgs.Empty);
    public void FireRestored() => WindowRestored?.Invoke(this, EventArgs.Empty);
    public void FireSuspending() => SystemSuspending?.Invoke(this, EventArgs.Empty);
    public void FireScreenLocked() => ScreenLocked?.Invoke(this, EventArgs.Empty);
}
