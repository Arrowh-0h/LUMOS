using System.Text.Json.Serialization;

namespace Lumos.Core.PortableExport;

/// <summary>
/// Subset of Bitwarden's export format we read and write. Field names match
/// what Bitwarden emits (camelCase) so we can interop. Bitwarden's full
/// format has more — attachments, sends, password-history — that we don't
/// touch because we don't model them.
///
/// Bitwarden's type integers (kept as our enum here so the JSON converter
/// emits them):
///   1 = Login
///   2 = SecureNote
///   3 = Card
///   4 = Identity
/// </summary>
public sealed class BitwardenExport
{
    public bool Encrypted { get; set; }
    public List<BitwardenFolder> Folders { get; set; } = new();
    public List<BitwardenItem> Items { get; set; } = new();
}

public sealed class BitwardenFolder
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public enum BitwardenItemType
{
    Login = 1,
    SecureNote = 2,
    Card = 3,
    Identity = 4,
}

public sealed class BitwardenItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? FolderId { get; set; }
    public BitwardenItemType Type { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public DateTimeOffset? RevisionDate { get; set; }

    public BitwardenLogin? Login { get; set; }
    public BitwardenCard? Card { get; set; }
    public BitwardenIdentity? Identity { get; set; }
    public BitwardenSecureNote? SecureNote { get; set; }
}

public sealed class BitwardenLogin
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Totp { get; set; }
    public List<BitwardenUri> Uris { get; set; } = new();
}

public sealed class BitwardenUri
{
    public string Uri { get; set; } = "";
}

public sealed class BitwardenCard
{
    public string? CardholderName { get; set; }
    public string? Brand { get; set; }
    public string? Number { get; set; }
    public string? ExpMonth { get; set; }
    public string? ExpYear { get; set; }
    public string? Code { get; set; }   // Bitwarden's CVV field
}

public sealed class BitwardenIdentity
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address1 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}

public sealed class BitwardenSecureNote
{
    /// <summary>Bitwarden's secure-note "type" is always 0 (generic).</summary>
    public int Type { get; set; }
}
