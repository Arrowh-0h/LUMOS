using System.Text;
using Lumos.Core;
using Lumos.Core.Crypto;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

/// <summary>
/// Tests for the VaultService layer in isolation — given a 32-byte cipher key,
/// it should create or open an encrypted SQLite3MC database. End-to-end tests
/// (password -> wrapping -> cipher key -> DB) live in VaultManagerTests.
/// </summary>
public class VaultServiceTests : IDisposable
{
    private readonly string _tempDir;

    public VaultServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private string VaultPath() => Path.Combine(_tempDir, "vault.db");

    [Fact]
    public void Encryption_layer_is_loaded()
    {
        var key = SecureMemory.RandomBytes(32);
        using var v = VaultService.Create(VaultPath(), key);
        using var cmd = v.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT sqlite3mc_version();";
        var version = cmd.ExecuteScalar() as string;
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public void Create_then_open_with_same_key_succeeds()
    {
        var key = SecureMemory.RandomBytes(32);
        var path = VaultPath();

        using (var v = VaultService.Create(path, key))
        {
            Assert.True(v.IsOpen);
            Assert.True(File.Exists(path));
        }

        using var v2 = VaultService.Open(path, key);
        Assert.NotNull(v2);
        Assert.True(v2!.IsOpen);
    }

    [Fact]
    public void Open_with_wrong_key_returns_null()
    {
        var key = SecureMemory.RandomBytes(32);
        var wrongKey = SecureMemory.RandomBytes(32);
        var path = VaultPath();

        using (var _ = VaultService.Create(path, key)) { }

        var v = VaultService.Open(path, wrongKey);
        Assert.Null(v);
    }

    [Fact]
    public void Create_refuses_to_overwrite()
    {
        var key = SecureMemory.RandomBytes(32);
        var path = VaultPath();
        using (var _ = VaultService.Create(path, key)) { }

        Assert.Throws<InvalidOperationException>(() => VaultService.Create(path, key));
    }

    [Fact]
    public void Create_rejects_wrong_size_key()
    {
        Assert.Throws<ArgumentException>(() => VaultService.Create(VaultPath(), new byte[16]));
    }

    [Fact]
    public void Vault_file_does_not_contain_plaintext()
    {
        var key = SecureMemory.RandomBytes(32);
        var path = VaultPath();
        const string marker = "DUMBLEDORE-ASKED-CALMLY";

        using (var v = VaultService.Create(path, key))
        {
            using var cmd = v.RequireConnection().CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO meta(key, value) VALUES('marker', $m);";
            cmd.Parameters.AddWithValue("$m", marker);
            cmd.ExecuteNonQuery();
        }

        var bytes = File.ReadAllBytes(path);
        var asLatin1 = Encoding.Latin1.GetString(bytes);
        var asUtf8 = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(marker, asLatin1);
        Assert.DoesNotContain(marker, asUtf8);
    }

    [Fact]
    public void RequireConnection_throws_after_dispose()
    {
        var key = SecureMemory.RandomBytes(32);
        var v = VaultService.Create(VaultPath(), key);
        v.Dispose();
        Assert.Throws<InvalidOperationException>(() => v.RequireConnection());
    }

    [Fact]
    public void Caller_can_zero_its_key_after_create()
    {
        // The service must clone the cipher key internally so the caller
        // can safely zero theirs immediately after Create returns.
        var key = SecureMemory.RandomBytes(32);
        var path = VaultPath();

        using var v = VaultService.Create(path, key);
        SecureMemory.Zero(key);  // caller's copy is gone

        // Vault should still be operational.
        using var cmd = v.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
        var n = cmd.ExecuteScalar();
        Assert.NotNull(n);
    }
}
