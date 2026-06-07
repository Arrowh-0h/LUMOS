namespace Lumos.Desktop.Platform;

/// <summary>
/// One open-or-save dialog filter. The label is what the user sees in
/// the format dropdown; the pattern is the Win32 wildcard syntax that
/// SaveFileDialog/OpenFileDialog expect (e.g. "*.lumosx").
/// </summary>
public sealed record FileFilter(string Label, string Pattern);

/// <summary>
/// Abstraction over file open/save dialogs so view-models can be tested
/// without standing up a real WPF UI.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Show a Save dialog. Returns the chosen path, or null if cancelled.
    /// <paramref name="defaultFileName"/> is the name pre-filled in the box.
    /// </summary>
    string? ShowSaveDialog(string title, string defaultFileName, IReadOnlyList<FileFilter> filters);

    /// <summary>Show an Open dialog. Returns the chosen path, or null if cancelled.</summary>
    string? ShowOpenDialog(string title, IReadOnlyList<FileFilter> filters);
}
