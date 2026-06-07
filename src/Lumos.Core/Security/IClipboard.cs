namespace Lumos.Core.Security;

/// <summary>
/// Platform clipboard abstraction. Implemented in Lumos.Desktop by
/// WindowsClipboard (System.Windows.Clipboard). Tests use a fake.
/// </summary>
public interface IClipboard
{
    /// <summary>Set the clipboard to <paramref name="text"/>.</summary>
    void SetText(string text);

    /// <summary>Get the current clipboard text, or empty if not text.</summary>
    string GetText();

    /// <summary>
    /// Clear the clipboard. Implementations should clear all formats,
    /// not just text, so a paste afterwards yields nothing.
    /// </summary>
    void Clear();
}
