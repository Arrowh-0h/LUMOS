namespace Lumos.Core.Attachments;

/// <summary>
/// Metadata for one stored attachment, WITHOUT its bytes. The list view and
/// detail pane work with this lightweight record; the actual file content is
/// fetched on demand (it can be up to 25 MB, so we don't want it loaded for
/// every entry just to show a filename).
/// </summary>
public sealed record AttachmentInfo
{
    public required string Id { get; init; }
    public required string EntryId { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// True when the MIME type or file extension indicates an image we can
    /// preview inline (png/jpg/gif/bmp/webp).
    /// </summary>
    public bool IsImage
    {
        get
        {
            if (MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return true;
            var ext = System.IO.Path.GetExtension(FileName).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
        }
    }

    /// <summary>Human-readable size, e.g. "1.4 MB" or "812 KB".</summary>
    public string SizeDisplay => FormatSize(SizeBytes);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        return $"{mb:0.#} MB";
    }
}

/// <summary>An attachment together with its raw bytes (used on add / save-out).</summary>
public sealed record AttachmentContent
{
    public required AttachmentInfo Info { get; init; }
    public required byte[] Bytes { get; init; }
}
