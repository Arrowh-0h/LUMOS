using Microsoft.Data.Sqlite;

namespace Lumos.Core.Vault;

/// <summary>
/// Applies versioned schema migrations to an open vault. Each migration is
/// idempotent within its own check — we read the current schema_version from
/// meta and only apply migrations newer than that.
///
/// Schema versions:
///   v1 — initial meta table (created by VaultService at vault creation)
///   v2 — entries, folders, tags, entry_tags, plus FTS5 search table
///   v3 — attachments (encrypted file blobs stored inside the vault)
/// </summary>
public static class SchemaMigrator
{
    public const int CurrentVersion = 3;

    public static void EnsureUpToDate(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var current = ReadVersion(connection);

        if (current < 2) ApplyV2(connection);
        if (current < 3) ApplyV3(connection);

        WriteVersion(connection, CurrentVersion);
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='schema_version';";
        var v = cmd.ExecuteScalar() as string;
        return int.TryParse(v, out var n) ? n : 0;
    }

    private static void WriteVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta(key, value) VALUES('schema_version', $v);";
        cmd.Parameters.AddWithValue("$v", version.ToString());
        cmd.ExecuteNonQuery();
    }

    private static void ApplyV2(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE folders (
                    id          TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    parent_id   TEXT NULL REFERENCES folders(id) ON DELETE SET NULL,
                    created_utc TEXT NOT NULL
                );

                CREATE TABLE entries (
                    id            TEXT PRIMARY KEY,
                    type          TEXT NOT NULL,
                    title         TEXT NOT NULL,
                    notes         TEXT NOT NULL DEFAULT '',
                    folder_id     TEXT NULL REFERENCES folders(id) ON DELETE SET NULL,
                    payload_json  TEXT NOT NULL,
                    created_utc   TEXT NOT NULL,
                    modified_utc  TEXT NOT NULL,
                    last_used_utc TEXT NULL
                );
                CREATE INDEX idx_entries_type      ON entries(type);
                CREATE INDEX idx_entries_folder    ON entries(folder_id);
                CREATE INDEX idx_entries_modified  ON entries(modified_utc DESC);

                CREATE TABLE tags (
                    id   TEXT PRIMARY KEY,
                    name TEXT NOT NULL
                );
                -- case-insensitive uniqueness on name
                CREATE UNIQUE INDEX idx_tags_name_nocase ON tags(name COLLATE NOCASE);

                CREATE TABLE entry_tags (
                    entry_id TEXT NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
                    tag_id   TEXT NOT NULL REFERENCES tags(id)    ON DELETE CASCADE,
                    PRIMARY KEY (entry_id, tag_id)
                );
                CREATE INDEX idx_entry_tags_tag ON entry_tags(tag_id);

                -- Full-text search. We index only the fields the user would
                -- reasonably search by; type-specific payload fields like
                -- card numbers or TOTP secrets are deliberately excluded.
                --
                -- This is a STANDALONE FTS5 table (no content='entries'),
                -- because our search_blob is a derived column computed from
                -- the JSON payload — there's no real entries.search_blob
                -- column for FTS5 to read from in external-content mode.
                -- The repository maintains this table by hand on every
                -- create/update/delete.
                CREATE VIRTUAL TABLE entries_fts USING fts5(
                    title,
                    notes,
                    search_blob
                );
                """;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void ApplyV3(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            // Attachments live INSIDE the vault DB as BLOBs, so SQLCipher
            // encrypts them transparently along with everything else. The
            // content column holds the raw file bytes; metadata (name, mime,
            // size, timestamp) sits alongside. ON DELETE CASCADE means
            // deleting an entry drops its attachments automatically.
            cmd.CommandText = """
                CREATE TABLE attachments (
                    id          TEXT PRIMARY KEY,
                    entry_id    TEXT NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
                    file_name   TEXT NOT NULL,
                    mime_type   TEXT NOT NULL DEFAULT '',
                    size_bytes  INTEGER NOT NULL,
                    content     BLOB NOT NULL,
                    created_utc TEXT NOT NULL
                );
                CREATE INDEX idx_attachments_entry ON attachments(entry_id);
                """;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
