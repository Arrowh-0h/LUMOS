namespace Lumos.Core.Entries;

/// <summary>
/// The kind of entry stored in the vault. The payload schema varies per type.
/// Stored as the lowercase name in the database for forward compatibility.
/// </summary>
public enum EntryType
{
    Login,
    SecureNote,
    Card,
    Identity,
}
