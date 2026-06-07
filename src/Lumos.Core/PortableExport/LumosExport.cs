using System.Text.Json.Serialization;

namespace Lumos.Core.PortableExport;

/// <summary>
/// Top-level shape of a Lumos export file (plaintext form). Both the
/// plaintext and encrypted variants serialize this same object; the
/// encrypted form just wraps the bytes.
///
/// Versioned so we can change the schema later without breaking older files.
/// </summary>
public sealed class LumosExport
{
    public string Format { get; set; } = "lumos-export";
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<LumosExportFolder> Folders { get; set; } = new();
    public List<LumosExportEntry> Entries { get; set; } = new();
}

public sealed class LumosExportFolder
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
}

/// <summary>
/// One entry. The "type" field discriminates which of the payload-specific
/// fields are populated; the rest are null/empty. Flat instead of nested
/// because JSON-with-discriminator is awkward without custom converters.
/// </summary>
public sealed class LumosExportEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Entries.EntryType Type { get; set; }

    public string Notes { get; set; } = "";
    public string? FolderId { get; set; }
    public List<string> Tags { get; set; } = new();

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public DateTimeOffset? LastUsedUtc { get; set; }

    // Login fields
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Url { get; set; }
    public string? TotpSecret { get; set; }

    // Card fields
    public string? CardholderName { get; set; }
    public string? CardNumber { get; set; }
    public string? Cvv { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? CardBrand { get; set; }

    // Identity fields
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    // SecureNote has no payload-specific fields — just uses Notes.
}
