namespace Lumos.Core.Entries;

/// <summary>
/// A vault entry. Combines common fields with a type-specific payload.
/// IDs are UUIDs (string form) so import/export and any future sync work
/// without identifier collisions.
/// </summary>
public sealed record Entry
{
    public required string Id { get; init; }
    public required EntryType Type { get; init; }
    public required string Title { get; init; }
    public string Notes { get; init; } = "";
    public string? FolderId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public required EntryPayload Payload { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset ModifiedUtc { get; init; }
    public DateTimeOffset? LastUsedUtc { get; init; }

    /// <summary>
    /// Helper to build a brand-new entry with a fresh UUID and current timestamps.
    /// </summary>
    public static Entry NewLogin(string title, LoginPayload payload, string? folderId = null, IEnumerable<string>? tags = null)
        => NewEntry(EntryType.Login, title, payload, folderId, tags);

    public static Entry NewSecureNote(string title, SecureNotePayload payload, string? folderId = null, IEnumerable<string>? tags = null)
        => NewEntry(EntryType.SecureNote, title, payload, folderId, tags);

    public static Entry NewCard(string title, CardPayload payload, string? folderId = null, IEnumerable<string>? tags = null)
        => NewEntry(EntryType.Card, title, payload, folderId, tags);

    public static Entry NewIdentity(string title, IdentityPayload payload, string? folderId = null, IEnumerable<string>? tags = null)
        => NewEntry(EntryType.Identity, title, payload, folderId, tags);

    private static Entry NewEntry(EntryType type, string title, EntryPayload payload, string? folderId, IEnumerable<string>? tags)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        if (payload.Type != type)
            throw new ArgumentException($"Payload type {payload.Type} does not match entry type {type}.", nameof(payload));

        var now = DateTimeOffset.UtcNow;
        return new Entry
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Title = title,
            Payload = payload,
            FolderId = folderId,
            Tags = tags?.ToList() ?? new List<string>(),
            CreatedUtc = now,
            ModifiedUtc = now,
        };
    }
}
