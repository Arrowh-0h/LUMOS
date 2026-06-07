using Lumos.Core;
using Lumos.Core.Crypto;
using Lumos.Core.Entries;
using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumos.Core.Tests;

public class FolderRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VaultService _vault;
    private readonly FolderRepository _folders;

    public FolderRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-folders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LumosCoreBootstrap.Initialize();
        var key = SecureMemory.RandomBytes(32);
        _vault = VaultService.Create(Path.Combine(_tempDir, "v.db"), key);
        _folders = new FolderRepository(_vault);
    }

    public void Dispose()
    {
        _vault.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Create_and_get_round_trip()
    {
        var f = _folders.Create("Work");
        var loaded = _folders.GetById(f.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Work", loaded!.Name);
        Assert.Null(loaded.ParentId);
    }

    [Fact]
    public void ListAll_returns_alphabetical()
    {
        _folders.Create("Zeta");
        _folders.Create("alpha");
        _folders.Create("Mike");
        var list = _folders.ListAll();
        Assert.Equal(new[] { "alpha", "Mike", "Zeta" }, list.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Rename_works()
    {
        var f = _folders.Create("Old");
        _folders.Rename(f.Id, "New");
        Assert.Equal("New", _folders.GetById(f.Id)!.Name);
    }

    [Fact]
    public void Delete_removes_folder()
    {
        var f = _folders.Create("Doomed");
        _folders.Delete(f.Id);
        Assert.Null(_folders.GetById(f.Id));
    }

    [Fact]
    public void Nested_folder_one_level_allowed()
    {
        var parent = _folders.Create("Parent");
        var child = _folders.Create("Child", parent.Id);
        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public void Nesting_more_than_one_level_is_rejected()
    {
        var grandparent = _folders.Create("GP");
        var parent = _folders.Create("P", grandparent.Id);
        // Now try to create a child under parent — should fail.
        Assert.Throws<InvalidOperationException>(() => _folders.Create("C", parent.Id));
    }

    [Fact]
    public void Rename_unknown_folder_throws()
    {
        Assert.Throws<InvalidOperationException>(() => _folders.Rename("nope", "x"));
    }
}
