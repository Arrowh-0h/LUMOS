using Lumos.Core;
using Lumos.Core.Crypto;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class SchemaMigratorTests : IDisposable
{
    private readonly string _tempDir;

    public SchemaMigratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Newly_created_vault_is_at_current_schema_version()
    {
        var key = SecureMemory.RandomBytes(32);
        var path = Path.Combine(_tempDir, "v.db");
        using var v = VaultService.Create(path, key);

        using var cmd = v.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='schema_version';";
        var version = cmd.ExecuteScalar() as string;
        Assert.Equal(SchemaMigrator.CurrentVersion.ToString(), version);
    }

    [Fact]
    public void All_expected_tables_exist_after_migration()
    {
        var key = SecureMemory.RandomBytes(32);
        var path = Path.Combine(_tempDir, "v.db");
        using var v = VaultService.Create(path, key);

        var expectedTables = new[] { "meta", "folders", "entries", "tags", "entry_tags", "entries_fts" };
        foreach (var t in expectedTables)
        {
            using var cmd = v.RequireConnection().CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE name = $n;";
            cmd.Parameters.AddWithValue("$n", t);
            var name = cmd.ExecuteScalar() as string;
            Assert.Equal(t, name);
        }
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        var key = SecureMemory.RandomBytes(32);
        var path = Path.Combine(_tempDir, "v.db");
        using (var _ = VaultService.Create(path, key)) { }

        // Re-open (which triggers EnsureUpToDate again). Should not error.
        using var v2 = VaultService.Open(path, key);
        Assert.NotNull(v2);

        // Calling the migrator explicitly a third time should also be safe.
        SchemaMigrator.EnsureUpToDate(v2!.RequireConnection());
    }
}
