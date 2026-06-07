using System.Text;
using Lumos.Core;
using Lumos.Core.Crypto;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Lumos.Core.Tests;

/// <summary>
/// Diagnostic tests for the rekey path. These deliberately bypass VaultManager
/// to isolate exactly what sqlite3_rekey does (or doesn't) to the file.
///
/// If ChangeMasterPassword is failing, exactly one of these will tell us why:
///   - did the rekey succeed at all?
///   - did the bytes-on-disk actually change?
///   - does the new raw key open the file, or does it need to go through PBKDF2?
/// </summary>
public class RekeyDiagnosticTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ITestOutputHelper _out;

    public RekeyDiagnosticTests(ITestOutputHelper outHelper)
    {
        _out = outHelper;
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-rekey-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Diagnose_what_sqlite3_rekey_does()
    {
        var path = Path.Combine(_tempDir, "vault.db");

        // ---- Step 1: create a vault with a known 32-byte raw key A ----
        var keyA = new byte[32];
        for (int i = 0; i < 32; i++) keyA[i] = (byte)i;

        OpenAndKey(path, keyA, isNew: true, out var connA);
        using (var cmd = connA.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t (v TEXT); INSERT INTO t VALUES ('marker-AAA');";
            cmd.ExecuteNonQuery();
        }
        connA.Close();
        connA.Dispose();
        SqliteConnection.ClearAllPools();

        var snapshotA = File.ReadAllBytes(path);
        _out.WriteLine($"After create+insert: file size = {snapshotA.Length}, first 16 bytes hex = {Convert.ToHexString(snapshotA[..16])}");

        // ---- Step 2: open with key A, rekey to key B, close ----
        var keyB = new byte[32];
        for (int i = 0; i < 32; i++) keyB[i] = (byte)(0xFF - i);

        OpenAndKey(path, keyA, isNew: false, out var connRekey);
        var nativeDb = connRekey.Handle!;
        var rc = SQLitePCL.raw.sqlite3_rekey(nativeDb, keyB);
        var rcMessage = SQLitePCL.raw.sqlite3_errmsg(nativeDb).utf8_to_string();
        _out.WriteLine($"sqlite3_rekey returned: {rc} (SQLITE_OK={SQLitePCL.raw.SQLITE_OK}), errmsg={rcMessage}");

        // Force a write so the rekey actually flushes.
        using (var cmd = connRekey.CreateCommand())
        {
            cmd.CommandText = "UPDATE t SET v = 'marker-BBB';";
            cmd.ExecuteNonQuery();
        }
        connRekey.Close();
        connRekey.Dispose();
        SqliteConnection.ClearAllPools();

        var snapshotB = File.ReadAllBytes(path);
        _out.WriteLine($"After rekey+update: file size = {snapshotB.Length}, first 16 bytes hex = {Convert.ToHexString(snapshotB[..16])}");
        _out.WriteLine($"File bytes changed: {!snapshotA.SequenceEqual(snapshotB)}");

        // ---- Step 3: try to open with key A (should now fail) ----
        bool openedWithA = TryOpen(path, keyA, out var errA);
        _out.WriteLine($"Open with original key A: success={openedWithA}, err={errA}");

        // ---- Step 4: try to open with key B (should succeed if rekey worked) ----
        bool openedWithB = TryOpen(path, keyB, out var errB);
        _out.WriteLine($"Open with new key B: success={openedWithB}, err={errB}");

        // ---- Step 5: also try passphrase forms of B in case the cipher
        //              applied PBKDF2 to our raw bytes ----
        var keyBHex = Convert.ToHexString(keyB);
        bool openedWithBAsPassphrase = TryOpenWithPragma(path, $"PRAGMA key='{keyBHex}';", out var errBP);
        _out.WriteLine($"Open with key B treated as hex passphrase: success={openedWithBAsPassphrase}, err={errBP}");

        // The verdict tells us which mode sqlite3_rekey actually used.
        if (openedWithB)
        {
            _out.WriteLine("VERDICT: sqlite3_rekey accepted raw bytes as expected. Bug is elsewhere.");
        }
        else if (openedWithBAsPassphrase)
        {
            _out.WriteLine("VERDICT: sqlite3_rekey treated our bytes as a passphrase. Need a different rekey path.");
        }
        else if (!openedWithA && !openedWithB)
        {
            _out.WriteLine("VERDICT: rekey corrupted the file or used some other key derivation. Need to investigate further.");
        }
    }

    private static void OpenAndKey(string path, byte[] key, bool isNew, out SqliteConnection conn)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = isNew ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
        }.ToString();

        conn = new SqliteConnection(connStr);
        conn.Open();
        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "PRAGMA cipher='sqlcipher';";
        cmd1.ExecuteNonQuery();
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "PRAGMA legacy=0;";
        cmd2.ExecuteNonQuery();
        using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = $"PRAGMA key=\"x'{Convert.ToHexString(key)}'\";";
        cmd3.ExecuteNonQuery();
    }

    private static bool TryOpen(string path, byte[] key, out string error)
    {
        try
        {
            OpenAndKey(path, key, isNew: false, out var conn);
            using (conn)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT v FROM t LIMIT 1;";
                var v = cmd.ExecuteScalar() as string;
                error = "";
                SqliteConnection.ClearAllPools();
                return v != null;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SqliteConnection.ClearAllPools();
            return false;
        }
    }

    private static bool TryOpenWithPragma(string path, string keyPragma, out string error)
    {
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
            }.ToString();
            using var conn = new SqliteConnection(connStr);
            conn.Open();
            using (var c1 = conn.CreateCommand()) { c1.CommandText = "PRAGMA cipher='sqlcipher';"; c1.ExecuteNonQuery(); }
            using (var c2 = conn.CreateCommand()) { c2.CommandText = "PRAGMA legacy=0;"; c2.ExecuteNonQuery(); }
            using (var c3 = conn.CreateCommand()) { c3.CommandText = keyPragma; c3.ExecuteNonQuery(); }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT v FROM t LIMIT 1;";
            var v = cmd.ExecuteScalar() as string;
            error = "";
            SqliteConnection.ClearAllPools();
            return v != null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SqliteConnection.ClearAllPools();
            return false;
        }
    }
}
