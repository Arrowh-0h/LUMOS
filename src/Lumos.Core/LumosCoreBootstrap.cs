using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Lumos.Core;

/// <summary>
/// Result of <see cref="LumosCoreBootstrap.SelfTest"/>.
/// </summary>
/// <param name="Success">True if the native SQLite3MC library loaded AND a real
/// encrypted database could be created, written, read back, and confirmed
/// unreadable without the key.</param>
/// <param name="Stage">Which step failed (or "ok"). Useful in crash logs.</param>
/// <param name="Detail">Version info on success, or a short explanation on a
/// non-exception failure. May be null.</param>
/// <param name="Failure">The exception that caused the failure, else null.</param>
public sealed record NativeSelfTestResult(
    bool Success,
    string Stage,
    string? Detail,
    Exception? Failure);

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

    /// <summary>
    /// Verify that the native encryption stack actually works, and report
    /// *where* it broke rather than throwing an opaque exception.
    ///
    /// Why this exists: on some machines the Velopack install completes with a
    /// warning and e_sqlite3mc.dll never lands on disk (antivirus quarantine is
    /// the usual cause for an unsigned installer). The failure then surfaces as
    /// a DllNotFoundException deep inside SQLitePCL at the moment the first
    /// vault is opened — far from the real cause, and invisible to the user.
    ///
    /// The test deliberately mirrors VaultService.OpenEncryptedConnection: a
    /// real file-backed database, PRAGMA cipher before PRAGMA key, same order,
    /// same SQLCipher-v4 settings. An in-memory database CANNOT be used here —
    /// SQLite3MC rejects keying on in-memory and temporary databases, so that
    /// would test nothing the app actually does.
    ///
    /// NEVER throws: inspect the returned record instead.
    /// </summary>
    public static NativeSelfTestResult SelfTest()
    {
        // Stage 1: load and register the native provider.
        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            return new NativeSelfTestResult(false, "native-provider-load", null, ex);
        }

        // Stage 2: create a throwaway encrypted database and round-trip a row
        // through it. Init() can succeed while the P/Invoke surface is still
        // broken (wrong architecture, partially extracted file), so a real
        // encrypted open is the only honest test.
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"lumos-selftest-{Guid.NewGuid():N}.db");

        try
        {
            // Throwaway key — this database is deleted moments from now and
            // never contains anything but the literal string "ok".
            var key = RandomNumberGenerator.GetBytes(32);

            string? mcVersion;

            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = tempPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();

                // ORDER MATTERS — identical to VaultService: cipher, then
                // legacy flag, then key. Setting the key first would lock in
                // SQLite3MC's default cipher instead of SQLCipher v4.
                Exec(conn, "PRAGMA cipher='sqlcipher';");
                Exec(conn, "PRAGMA legacy=0;");
                Exec(conn, $"PRAGMA key=\"x'{Convert.ToHexString(key)}'\";");

                // Confirm we're talking to the multiple-ciphers build and not
                // a plain SQLite that silently ignored the PRAGMAs.
                using (var vcmd = conn.CreateCommand())
                {
                    vcmd.CommandText = "SELECT sqlite3mc_version();";
                    mcVersion = vcmd.ExecuteScalar() as string;
                }

                if (string.IsNullOrWhiteSpace(mcVersion))
                {
                    return new NativeSelfTestResult(
                        false, "encryption-not-engaged",
                        "sqlite3mc_version() returned nothing — the loaded SQLite " +
                        "build does not support encryption.",
                        null);
                }

                Exec(conn, "CREATE TABLE t(v TEXT);");
                Exec(conn, "INSERT INTO t(v) VALUES('ok');");

                using var rcmd = conn.CreateCommand();
                rcmd.CommandText = "SELECT v FROM t;";
                var readBack = rcmd.ExecuteScalar() as string;

                if (readBack != "ok")
                {
                    return new NativeSelfTestResult(
                        false, "roundtrip-mismatch",
                        $"expected 'ok', got '{readBack ?? "<null>"}'",
                        null);
                }
            }

            // Stage 3: the file on disk must actually be encrypted. Reopening
            // WITHOUT the key must fail — if it succeeds, encryption silently
            // did not apply and vaults would be written in the clear.
            var plainOpenSucceeded = false;
            try
            {
                using var plain = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = tempPath,
                        Mode = SqliteOpenMode.ReadOnly,
                    }.ToString());
                plain.Open();
                using var cmd = plain.CreateCommand();
                cmd.CommandText = "SELECT v FROM t;";
                plainOpenSucceeded = (cmd.ExecuteScalar() as string) == "ok";
            }
            catch
            {
                // Expected — an encrypted file is unreadable without the key.
            }

            if (plainOpenSucceeded)
            {
                return new NativeSelfTestResult(
                    false, "encryption-not-applied",
                    "the test database was readable without a key — data would " +
                    "NOT be encrypted at rest.",
                    null);
            }

            return new NativeSelfTestResult(
                true, "ok", $"sqlite3mc {mcVersion}", null);
        }
        catch (Exception ex)
        {
            return new NativeSelfTestResult(false, "encrypted-open", null, ex);
        }
        finally
        {
            TryDelete(tempPath);
            TryDelete(tempPath + "-journal");
            TryDelete(tempPath + "-wal");
            TryDelete(tempPath + "-shm");
        }
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temp file cleanup is best-effort; never fail a diagnostic on it.
        }
    }
}
