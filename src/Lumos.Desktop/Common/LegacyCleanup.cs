using System.IO;

namespace Lumos.Desktop.Common;

/// <summary>
/// Removes files left behind by features that no longer exist.
///
/// Right now that means one thing: license.dat, the DPAPI-encrypted blob the
/// product-key activation gate wrote. Nothing reads it any more, so it is
/// harmless — but it sits in the same folder as the vault, and an unexplained
/// file next to someone's password database is the kind of thing that
/// reasonably makes people uneasy. Cleaning it up costs nothing.
///
/// Everything here is best-effort and must never throw. A failure to delete a
/// stale file is not worth interrupting startup over, and it will simply be
/// retried on the next launch.
/// </summary>
public static class LegacyCleanup
{
    /// <summary>
    /// Files that older versions of Lumos created and current versions do not.
    /// Add to this list rather than deleting by hand elsewhere, so there is one
    /// place to audit what Lumos removes from the user's data folder.
    /// </summary>
    private static readonly string[] _obsoleteFiles =
    {
        // Product-key activation, removed in v2. Never contained vault data —
        // only the activation key itself, encrypted under the Windows user.
        "license.dat",
    };

    public static void Run()
    {
        string dir;
        try
        {
            dir = AppPaths.AppDataDirectory;
        }
        catch
        {
            return;   // no data folder yet, or unreadable — nothing to clean
        }

        foreach (var name in _obsoleteFiles)
        {
            try
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Locked, read-only, or permission-denied. Try again next launch.
            }
        }
    }
}
