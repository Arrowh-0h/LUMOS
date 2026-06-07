using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;

namespace Lumos.Core.Entries;

/// <summary>
/// Read-only-ish operations on tags. Tags are created lazily by the entry
/// repository when an entry references a previously-unseen tag name.
/// </summary>
public sealed class TagRepository
{
    private readonly VaultService _vault;

    public TagRepository(VaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    public IReadOnlyList<Tag> ListAll()
    {
        var list = new List<Tag>();
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = "SELECT id, name FROM tags ORDER BY name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Tag { Id = reader.GetString(0), Name = reader.GetString(1) });
        }
        return list;
    }

    /// <summary>
    /// Autocomplete: returns up to <paramref name="limit"/> tags whose name
    /// starts with the prefix (case-insensitive).
    /// </summary>
    public IReadOnlyList<Tag> StartingWith(string prefix, int limit = 10)
    {
        if (prefix is null) prefix = "";
        var list = new List<Tag>();
        using var cmd = _vault.RequireConnection().CreateCommand();
        cmd.CommandText = """
            SELECT id, name FROM tags
            WHERE name LIKE $p COLLATE NOCASE
            ORDER BY name COLLATE NOCASE
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$p", prefix + "%");
        cmd.Parameters.AddWithValue("$lim", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Tag { Id = reader.GetString(0), Name = reader.GetString(1) });
        }
        return list;
    }
}
