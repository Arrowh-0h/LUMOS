namespace Lumos.Core.Security;

/// <summary>
/// All auto-lock triggers the user can configure. These live in app settings
/// (NOT inside the vault — they're needed before the vault is unlocked).
/// </summary>
public sealed record AutoLockSettings
{
    /// <summary>
    /// Idle timeout. Null means "never lock on idle". Spec preset choices:
    /// 1 / 2 / 5 / 10 / 15 minutes.
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>If true, lock when the window has been minimized for the threshold.</summary>
    public bool LockOnMinimize { get; init; } = true;

    /// <summary>How long a minimize has to last before we lock.</summary>
    public TimeSpan MinimizeThreshold { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Lock when the system is going to sleep or hibernate.</summary>
    public bool LockOnSystemSleep { get; init; } = true;

    /// <summary>Lock when the user locks their Windows session (Win+L).</summary>
    public bool LockOnScreenLock { get; init; } = true;

    /// <summary>Clipboard auto-clear timeout. Spec preset choices: 10 / 30 / 60s.</summary>
    public TimeSpan ClipboardClearTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static AutoLockSettings Default => new();
}
