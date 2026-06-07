namespace Lumos.Core;

/// <summary>
/// Call once at app startup before opening any vault.
/// Registers the SQLitePCLRaw bundle that ships SQLCipher's native library.
/// </summary>
public static class LumosCoreBootstrap
{
    private static bool _initialized;
    private static readonly object _lock = new();

    public static void Initialize()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
        }
    }
}
