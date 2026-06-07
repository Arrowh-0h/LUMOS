using Lumos.Core.Vault;
using Microsoft.Data.Sqlite;

namespace Lumos.Core.Attachments;

/// <summary>
/// CRUD for entry attachments. Attachments are stored as BLOBs in the vault
/// database, so SQLCipher encrypts them transparently — there is no separate
/// key or file. Listing returns metadata only (no bytes); the content is
/// fetched on demand because files can be large (up to the cap).
/// </summary>
public sealed class AttachmentRepository
{
    /// <summary>Per-file size cap. Files larger than this are rejected on add.</summary>
    public const long MaxFileSizeBytes = 25L * 1024 * 1024; // 25 MB

    private readonly VaultService _vault;

    public AttachmentRepository(VaultService vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    /// <summary>
    /// Add a file to an entry. Throws <see cref="AttachmentTooLargeException"/>
    /// if the content exceeds the cap. Returns the stored metadata.
    /// </summary>
    public AttachmentInfo Add(string entryId, string fileName, string mimeType, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.LongLength > MaxFileSizeBytes)
            throw new AttachmentTooLargeException(bytes.LongLength, MaxFileSizeBytes);

        var info = new AttachmentInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            EntryId = entryId,
            FileName = fileName,
            MimeType = mimeType ?? "",
            SizeBytes = bytes.LongLength,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO attachments (id, entry_id, file_name, mime_type, size_bytes, content, created_utc)
            VALUES ($id, $entry, $name, $mime, $size, $content, $created);
            """;
        cmd.Parameters.AddWithValue("$id", info.Id);
        cmd.Parameters.AddWithValue("$entry", info.EntryId);
        cmd.Parameters.AddWithValue("$name", info.FileName);
        cmd.Parameters.AddWithValue("$mime", info.MimeType);
        cmd.Parameters.AddWithValue("$size", info.SizeBytes);
        cmd.Parameters.AddWithValue("$content", bytes);
        cmd.Parameters.AddWithValue("$created", info.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();

        return info;
    }

    /// <summary>List attachment metadata for an entry (no bytes loaded).</summary>
    public IReadOnlyList<AttachmentInfo> ListForEntry(string entryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entry_id, file_name, mime_type, size_bytes, created_utc
            FROM attachments
            WHERE entry_id = $entry
            ORDER BY created_utc ASC;
            """;
        cmd.Parameters.AddWithValue("$entry", entryId);

        var list = new List<AttachmentInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadInfo(reader));
        return list;
    }

    /// <summary>Count attachments for an entry (cheap; for badge display).</summary>
    public int CountForEntry(string entryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM attachments WHERE entry_id = $entry;";
        cmd.Parameters.AddWithValue("$entry", entryId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Fetch the full content (bytes) of one attachment, or null if gone.</summary>
    public AttachmentContent? GetContent(string attachmentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachmentId);
        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entry_id, file_name, mime_type, size_bytes, created_utc, content
            FROM attachments
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", attachmentId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var info = ReadInfo(reader);
        // content is the last column (index 6)
        var bytes = (byte[])reader["content"];
        return new AttachmentContent { Info = info, Bytes = bytes };
    }

    /// <summary>Delete one attachment by id. Returns true if a row was removed.</summary>
    public bool Delete(string attachmentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(attachmentId);
        var conn = _vault.RequireConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM attachments WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", attachmentId);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static AttachmentInfo ReadInfo(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        EntryId = reader.GetString(reader.GetOrdinal("entry_id")),
        FileName = reader.GetString(reader.GetOrdinal("file_name")),
        MimeType = reader.GetString(reader.GetOrdinal("mime_type")),
        SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
        CreatedUtc = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc")),
            null, System.Globalization.DateTimeStyles.RoundtripKind),
    };
}

/// <summary>Thrown when an attachment exceeds the per-file size cap.</summary>
public sealed class AttachmentTooLargeException : Exception
{
    public long ActualBytes { get; }
    public long MaxBytes { get; }

    public AttachmentTooLargeException(long actualBytes, long maxBytes)
        : base($"Attachment is {AttachmentInfo.FormatSize(actualBytes)}, which exceeds the " +
               $"{AttachmentInfo.FormatSize(maxBytes)} limit.")
    {
        ActualBytes = actualBytes;
        MaxBytes = maxBytes;
    }
}
