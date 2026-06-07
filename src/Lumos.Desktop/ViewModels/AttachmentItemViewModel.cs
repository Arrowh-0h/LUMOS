using Lumos.Core.Attachments;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// One row in the attachments list. Wraps an <see cref="AttachmentInfo"/>
/// (metadata only — no bytes) for display.
/// </summary>
public sealed class AttachmentItemViewModel : ObservableObject
{
    public AttachmentInfo Info { get; }

    public AttachmentItemViewModel(AttachmentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
    }

    public string Id => Info.Id;
    public string FileName => Info.FileName;
    public string SizeDisplay => Info.SizeDisplay;
    public bool IsImage => Info.IsImage;

    /// <summary>A short type glyph for the row, by family.</summary>
    public string Glyph
    {
        get
        {
            if (Info.IsImage) return "▦";
            var ext = System.IO.Path.GetExtension(Info.FileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "▤",
                ".doc" or ".docx" or ".txt" or ".rtf" or ".md" => "▤",
                ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "▥",
                _ => "●",
            };
        }
    }
}
