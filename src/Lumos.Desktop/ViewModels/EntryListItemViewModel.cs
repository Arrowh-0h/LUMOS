using Lumos.Core.Entries;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// One row in the entry list. Display-only; the full entry is loaded into
/// the detail VM when selected.
/// </summary>
public sealed class EntryListItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public EntryType Type { get; }

    /// <summary>
    /// One-character type marker shown on the left. We use simple ASCII
    /// glyphs that read clearly in our monospace fonts and translate well
    /// across systems without needing icon fonts.
    /// </summary>
    public string TypeGlyph => Type switch
    {
        EntryType.Login => "◆",
        EntryType.SecureNote => "▤",
        EntryType.Card => "■",
        EntryType.Identity => "◉",
        _ => "·",
    };

    public EntryListItemViewModel(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Id = entry.Id;
        Title = entry.Title;
        Type = entry.Type;
        Subtitle = entry.Payload switch
        {
            LoginPayload p => string.IsNullOrEmpty(p.Username) ? p.Url : p.Username,
            CardPayload p => p.CardholderName,
            IdentityPayload p => p.Email,
            SecureNotePayload _ => "Secure note",
            _ => "",
        };
    }
}
