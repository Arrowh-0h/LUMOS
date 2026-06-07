namespace Lumos.Core.Security;

/// <summary>
/// Reports how long the user has been idle (no keyboard / mouse input).
/// On Windows, implemented via GetLastInputInfo P/Invoke.
/// </summary>
public interface IIdleMonitor
{
    /// <summary>Time elapsed since the last user input system-wide.</summary>
    TimeSpan GetIdleTime();
}
