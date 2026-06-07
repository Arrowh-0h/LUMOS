using Lumos.Core.Security;
using Lumos.Core.Vault;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.Common;

/// <summary>
/// Minimal service container for the singletons that view models share.
/// We're not introducing a DI framework yet — too much weight for a 2-person
/// project. If we end up needing one, this is one of the easier things to
/// refactor.
/// </summary>
public static class AppServices
{
    public static VaultManager VaultManager { get; private set; } = null!;
    public static ClipboardService Clipboard { get; private set; } = null!;

    /// <summary>The currently-open vault, or null if locked.</summary>
    public static VaultService? OpenVault { get; set; }

    public static void Initialize()
    {
        VaultManager = new VaultManager(AppPaths.VaultPath);

        // The Windows clipboard implementation needs the UI Dispatcher, which
        // is available by the time this runs (App.OnStartup is on the UI thread).
        var clipboardImpl = new WindowsClipboard();
        Clipboard = new ClipboardService(clipboardImpl,
            defaultClearTimeout: AutoLockSettings.Default.ClipboardClearTimeout);
    }

    public static void ShutDown()
    {
        Clipboard?.Dispose();
        OpenVault?.Dispose();
        OpenVault = null;
    }
}
