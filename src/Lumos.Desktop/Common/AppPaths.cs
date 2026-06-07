using System.IO;

namespace Lumos.Desktop.Common;

/// <summary>
/// Resolves Lumos's standard file locations on Windows. Uses %APPDATA%\Lumos
/// per spec §1.5. The folder is created on first access if missing.
/// </summary>
public static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(roaming, "Lumos");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string VaultPath => Path.Combine(AppDataDirectory, "vault.db");

    public static string LogsDirectory
    {
        get
        {
            var dir = Path.Combine(AppDataDirectory, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
