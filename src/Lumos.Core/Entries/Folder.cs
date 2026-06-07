namespace Lumos.Core.Entries;

/// <summary>
/// A folder. Spec allows one level of nesting in v1, enforced at the
/// repository layer.
/// </summary>
public sealed record Folder
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ParentId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }

    public static Folder New(string name, string? parentId = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        ParentId = parentId,
        CreatedUtc = DateTimeOffset.UtcNow,
    };
}

/// <summary>
/// A tag. Tag names are compared case-insensitively for uniqueness, but
/// the original casing is preserved for display.
/// </summary>
public sealed record Tag
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public static Tag New(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
    };
}
