using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;

namespace Lumos.Core.Entries;

/// <summary>
/// CRUD for folders. Spec allows one level of nesting in v1 — a folder may
/// have a parent, but that parent cannot itself have a parent.
/// </summary>
public sealed class FolderRepository
{
    private readonly VaultService _vault;

    public FolderRepository(VaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    public Folder Create(string name, string? parentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (parentId is not null && IsNested(parentId))
            throw new InvalidOperationException(
                "Folders can be nested only one level deep. The chosen parent already has a parent.");

        var folder = Folder.New(name, parentId);
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = """
            INSERT INTO folders (id, name, parent_id, created_utc)
            VALUES ($id, $name, $parent, $created);
            """;
        cmd.Parameters.AddWithValue("$id", folder.Id);
        cmd.Parameters.AddWithValue("$name", folder.Name);
        cmd.Parameters.AddWithValue("$parent", (object?)folder.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", folder.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
        return folder;
    }

    public Folder? GetById(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT id, name, parent_id, created_utc FROM folders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadFolder(reader) : null;
    }

    public IReadOnlyList<Folder> ListAll()
    {
        var list = new List<Folder>();
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT id, name, parent_id, created_utc FROM folders ORDER BY name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadFolder(reader));
        return list;
    }

    public void Rename(string id, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = "UPDATE folders SET name = $n WHERE id = $id;";
        cmd.Parameters.AddWithValue("$n", newName);
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException($"Folder {id} not found.");
    }

    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = "DELETE FROM folders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        // FK ON DELETE SET NULL handles cleanup of entries.folder_id and
        // any child folders' parent_id.
    }

    private bool IsNested(string folderId)
    {
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT parent_id FROM folders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", folderId);
        var v = cmd.ExecuteScalar();
        return v is not null && v != DBNull.Value;
    }

    private static Folder ReadFolder(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        ParentId = r.IsDBNull(2) ? null : r.GetString(2),
        CreatedUtc = DateTimeOffset.Parse(r.GetString(3)),
    };
}
