using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lumos.Core.Licensing;

namespace Lumos.Desktop.Common;

/// <summary>
/// Stores activation state locally, encrypted with Windows DPAPI (per-user).
///
/// Why DPAPI and not the vault: activation is checked BEFORE the vault is
/// unlocked (it gates app launch), so we can't use the master-password-derived
/// key. DPAPI ties the encrypted blob to the current Windows user account,
/// which is appropriate for a "this machine/user is activated" flag.
///
/// The stored file holds the validated key. On startup we re-validate it with
/// ProductKey.IsValid — so even a copied/edited license file won't activate
/// unless it contains a genuinely valid key.
/// </summary>
public sealed class LicenseStore
{
    private readonly string _path;

    public LicenseStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.AppDataDirectory, "license.dat");
    }

    /// <summary>True if a valid key has been activated on this machine/user.</summary>
    public bool IsActivated()
    {
        var key = LoadKey();
        return key is not null && ProductKey.IsValid(key);
    }

    /// <summary>
    /// Validate and persist a key. Returns true if the key was valid and saved;
    /// false if the key is invalid (nothing is written in that case).
    /// </summary>
    public bool Activate(string key)
    {
        if (!ProductKey.IsValid(key)) return false;
        SaveKey(key.Trim().ToUpperInvariant());
        return true;
    }

    /// <summary>Remove local activation (for testing / "deactivate").</summary>
    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* best effort */ }
    }

    private string? LoadKey()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var protectedBytes = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Corrupt or unreadable (e.g. copied from another user) — treat as
            // not activated rather than throwing.
            return null;
        }
    }

    private void SaveKey(string key)
    {
        var plain = Encoding.UTF8.GetBytes(key);
        var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }
}
