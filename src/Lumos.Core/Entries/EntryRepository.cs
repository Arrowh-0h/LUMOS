using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;

namespace Lumos.Core.Entries;

/// <summary>
/// CRUD for entries. Owns the tag relationship — setting tags on an entry
/// goes through here, not through TagRepository. Maintains the FTS5 index
/// in sync with entries on every write.
///
/// Search semantics:
///   - SearchAll uses FTS5 (title, notes, derived search blob).
///   - List/filtering methods use simple SELECTs.
/// </summary>
public sealed class EntryRepository
{
    private readonly VaultService _vault;

    public EntryRepository(VaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    // ---------- Create ----------

    public Entry Insert(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var conn = _vault.RequireConnection();

        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO entries (id, type, title, notes, folder_id, payload_json,
                                     created_utc, modified_utc, last_used_utc)
                VALUES ($id, $type, $title, $notes, $folder, $payload,
                        $created, $modified, $last);
                """;
            BindEntryCommon(cmd, entry);
            cmd.ExecuteNonQuery();
        }

        UpsertTags(conn, tx, entry);
        UpsertFts(conn, tx, entry);

        tx.Commit();
        return entry;
    }

    // ---------- Read ----------

    public Entry? GetById(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + " WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var partial = ReadEntry(reader);
        return partial with { Tags = LoadTags(conn, id) };
    }

    public IReadOnlyList<Entry> ListAll()
    {
        return List(filterSql: "", parameters: null);
    }

    public IReadOnlyList<Entry> ListByType(EntryType type)
    {
        return List(
            filterSql: " WHERE type = $type",
            parameters: cmd => cmd.Parameters.AddWithValue("$type", type.ToString().ToLowerInvariant()));
    }

    public IReadOnlyList<Entry> ListByFolder(string? folderId)
    {
        if (folderId is null)
        {
            return List(filterSql: " WHERE folder_id IS NULL", parameters: null);
        }
        return List(
            filterSql: " WHERE folder_id = $folder",
            parameters: cmd => cmd.Parameters.AddWithValue("$folder", folderId));
    }

    public IReadOnlyList<Entry> ListByTag(string tagName)
    {
        ArgumentException.ThrowIfNullOrEmpty(tagName);
        // Subselect tag IDs by name (case-insensitive) and join entry_tags.
        var conn = _vault.RequireConnection();
        var entries = new List<Entry>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectColumns + """
             WHERE id IN (
                SELECT et.entry_id FROM entry_tags et
                JOIN tags t ON t.id = et.tag_id
                WHERE t.name = $tag COLLATE NOCASE
             )
             ORDER BY modified_utc DESC;
            """;
        cmd.Parameters.AddWithValue("$tag", tagName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) entries.Add(ReadEntry(reader));
        // Backfill tags per entry.
        for (int i = 0; i < entries.Count; i++)
            entries[i] = entries[i] with { Tags = LoadTags(conn, entries[i].Id) };
        return entries;
    }

    /// <summary>
    /// FTS5 search across title, notes, and a derived blob (username + url for
    /// Login entries; full-name + email for Identity; cardholder for Card).
    /// Type-specific sensitive fields (password, CVV, TOTP secret) are NEVER
    /// indexed.
    /// </summary>
    public IReadOnlyList<Entry> Search(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<Entry>();

        var conn = _vault.RequireConnection();
        var entries = new List<Entry>();

        // FTS5 needs the query escaped for special characters. We wrap each
        // token in quotes and add a prefix-match operator, which gives us
        // user-friendly partial-match search out of the box.
        var ftsQuery = BuildFtsQuery(query);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                {SelectColumns}
                 WHERE rowid IN (
                    SELECT rowid FROM entries_fts WHERE entries_fts MATCH $q
                    ORDER BY rank
                    LIMIT $lim
                 );
                """;
            cmd.Parameters.AddWithValue("$q", ftsQuery);
            cmd.Parameters.AddWithValue("$lim", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) entries.Add(ReadEntry(reader));
        }

        for (int i = 0; i < entries.Count; i++)
            entries[i] = entries[i] with { Tags = LoadTags(conn, entries[i].Id) };
        return entries;
    }

    private static string BuildFtsQuery(string raw)
    {
        // Tokenize on whitespace; wrap each token in double quotes (which
        // FTS5 treats as a literal phrase) and append * for prefix matching.
        // Escape internal double quotes by doubling.
        var tokens = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var quoted = tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\"*");
        return string.Join(" ", quoted);
    }

    // ---------- Update ----------

    public Entry Update(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var conn = _vault.RequireConnection();

        var updated = entry with { ModifiedUtc = DateTimeOffset.UtcNow };

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE entries
                   SET title = $title,
                       notes = $notes,
                       folder_id = $folder,
                       payload_json = $payload,
                       modified_utc = $modified,
                       last_used_utc = $last
                 WHERE id = $id;
                """;
            BindEntryCommon(cmd, updated);
            if (cmd.ExecuteNonQuery() == 0)
                throw new InvalidOperationException($"Entry {entry.Id} not found.");
        }

        // Replace tags wholesale.
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM entry_tags WHERE entry_id = $id;";
            del.Parameters.AddWithValue("$id", updated.Id);
            del.ExecuteNonQuery();
        }
        UpsertTags(conn, tx, updated);
        UpsertFts(conn, tx, updated);

        tx.Commit();
        return updated;
    }

    /// <summary>
    /// Record that an entry was just used. Updates last_used_utc only,
    /// without touching modified_utc.
    /// </summary>
    public void TouchLastUsed(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE entries SET last_used_utc = $now WHERE id = $id;";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ---------- Delete ----------

    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var conn = _vault.RequireConnection();
        using var tx = conn.BeginTransaction();

        // Look up the rowid first so we can delete from FTS even after the
        // entries row is gone.
        long? rowid;
        using (var rid = conn.CreateCommand())
        {
            rid.Transaction = tx;
            rid.CommandText = "SELECT rowid FROM entries WHERE id = $id;";
            rid.Parameters.AddWithValue("$id", id);
            var raw = rid.ExecuteScalar();
            rowid = raw is null || raw is DBNull ? null : Convert.ToInt64(raw);
        }

        if (rowid.HasValue)
        {
            using var fts = conn.CreateCommand();
            fts.Transaction = tx;
            fts.CommandText = "DELETE FROM entries_fts WHERE rowid = $r;";
            fts.Parameters.AddWithValue("$r", rowid.Value);
            fts.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM entries WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        // Explicitly remove attachments too. The schema declares ON DELETE
        // CASCADE, but PRAGMA foreign_keys enforcement can be reset by
        // connection pooling, so we don't rely on it — we delete here in the
        // same transaction. (The attachments table may not exist on very old
        // vaults that haven't migrated yet, so guard with a table check.)
        using (var attCheck = conn.CreateCommand())
        {
            attCheck.Transaction = tx;
            attCheck.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='attachments';";
            var exists = attCheck.ExecuteScalar() is not null;
            if (exists)
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM attachments WHERE entry_id = $id;";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    // ---------- Helpers ----------

    private const string SelectColumns = """
        SELECT id, type, title, notes, folder_id, payload_json,
               created_utc, modified_utc, last_used_utc
          FROM entries
        """;

    private IReadOnlyList<Entry> List(string filterSql, Action<SqliteCommand>? parameters)
    {
        var conn = _vault.RequireConnection();
        var entries = new List<Entry>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = SelectColumns + filterSql + " ORDER BY modified_utc DESC;";
            parameters?.Invoke(cmd);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) entries.Add(ReadEntry(reader));
        }
        for (int i = 0; i < entries.Count; i++)
            entries[i] = entries[i] with { Tags = LoadTags(conn, entries[i].Id) };
        return entries;
    }

    private static Entry ReadEntry(SqliteDataReader r)
    {
        var typeStr = r.GetString(1);
        var type = Enum.Parse<EntryType>(typeStr, ignoreCase: true);
        var payload = PayloadJson.Deserialize(r.GetString(5));
        return new Entry
        {
            Id = r.GetString(0),
            Type = type,
            Title = r.GetString(2),
            Notes = r.GetString(3),
            FolderId = r.IsDBNull(4) ? null : r.GetString(4),
            Payload = payload,
            CreatedUtc = DateTimeOffset.Parse(r.GetString(6)),
            ModifiedUtc = DateTimeOffset.Parse(r.GetString(7)),
            LastUsedUtc = r.IsDBNull(8) ? null : DateTimeOffset.Parse(r.GetString(8)),
        };
    }

    private static void BindEntryCommon(SqliteCommand cmd, Entry entry)
    {
        cmd.Parameters.AddWithValue("$id", entry.Id);
        cmd.Parameters.AddWithValue("$type", entry.Type.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$title", entry.Title);
        cmd.Parameters.AddWithValue("$notes", entry.Notes ?? "");
        cmd.Parameters.AddWithValue("$folder", (object?)entry.FolderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", PayloadJson.Serialize(entry.Payload));
        cmd.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$modified", entry.ModifiedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$last", (object?)entry.LastUsedUtc?.ToString("O") ?? DBNull.Value);
    }

    private static IReadOnlyList<string> LoadTags(SqliteConnection conn, string entryId)
    {
        var tags = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.name FROM tags t
            JOIN entry_tags et ON et.tag_id = t.id
            WHERE et.entry_id = $id
            ORDER BY t.name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$id", entryId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tags.Add(reader.GetString(0));
        return tags;
    }

    /// <summary>
    /// Lazily create missing tags and link them to the entry. Tag names are
    /// compared case-insensitively for uniqueness.
    /// </summary>
    private static void UpsertTags(SqliteConnection conn, SqliteTransaction tx, Entry entry)
    {
        foreach (var name in entry.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            string? tagId;
            using (var find = conn.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = "SELECT id FROM tags WHERE name = $n COLLATE NOCASE;";
                find.Parameters.AddWithValue("$n", name);
                tagId = find.ExecuteScalar() as string;
            }

            if (tagId is null)
            {
                tagId = Guid.NewGuid().ToString("N");
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO tags(id, name) VALUES($id, $n);";
                ins.Parameters.AddWithValue("$id", tagId);
                ins.Parameters.AddWithValue("$n", name);
                ins.ExecuteNonQuery();
            }

            using (var link = conn.CreateCommand())
            {
                link.Transaction = tx;
                link.CommandText = """
                    INSERT OR IGNORE INTO entry_tags(entry_id, tag_id)
                    VALUES($e, $t);
                    """;
                link.Parameters.AddWithValue("$e", entry.Id);
                link.Parameters.AddWithValue("$t", tagId);
                link.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// Refresh the FTS5 row for this entry. We do a delete-then-insert via
    /// the external-content table's special command interface.
    /// </summary>
    private static void UpsertFts(SqliteConnection conn, SqliteTransaction tx, Entry entry)
    {
        // Get the rowid (FTS5 binds by rowid, not by id).
        long rowid;
        using (var rid = conn.CreateCommand())
        {
            rid.Transaction = tx;
            rid.CommandText = "SELECT rowid FROM entries WHERE id = $id;";
            rid.Parameters.AddWithValue("$id", entry.Id);
            rowid = Convert.ToInt64(rid.ExecuteScalar());
        }

        // Build the search blob from non-sensitive payload fields.
        var blob = BuildSearchBlob(entry);

        // Delete the existing FTS row for this rowid (no-op if absent).
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM entries_fts WHERE rowid = $r;";
            del.Parameters.AddWithValue("$r", rowid);
            del.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO entries_fts (rowid, title, notes, search_blob)
                VALUES ($r, $t, $n, $b);
                """;
            ins.Parameters.AddWithValue("$r", rowid);
            ins.Parameters.AddWithValue("$t", entry.Title);
            ins.Parameters.AddWithValue("$n", entry.Notes ?? "");
            ins.Parameters.AddWithValue("$b", blob);
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Build the searchable text blob for an entry. Deliberately omits
    /// sensitive fields (password, CVV, TOTP secret, national ID, etc.) —
    /// only fields a user would reasonably search by.
    /// </summary>
    private static string BuildSearchBlob(Entry entry) => entry.Payload switch
    {
        LoginPayload p => $"{p.Username} {p.Url}",
        SecureNotePayload p => p.Body,   // the body IS the entry — must be searchable
        CardPayload p => $"{p.CardholderName} {p.Brand ?? ""}",
        IdentityPayload p => $"{p.FullName} {p.Email}",
        _ => "",
    };
}
