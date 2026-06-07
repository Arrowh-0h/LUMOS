using Microsoft.Win32;

namespace Lumos.Desktop.Platform;

/// <summary>
/// Concrete <see cref="IFileDialogService"/> backed by the standard WPF
/// dialogs. WPF doesn't ship a native folder picker in .NET 8 (the WinUI
/// one needs extra work), but file pickers are first-class.
/// </summary>
public sealed class WindowsFileDialogService : IFileDialogService
{
    public string? ShowSaveDialog(string title, string defaultFileName, IReadOnlyList<FileFilter> filters)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = BuildFilterString(filters),
            OverwritePrompt = true,
            AddExtension = true,
            // Inferring the default extension from the first filter pattern.
            DefaultExt = ExtractExtension(filters.FirstOrDefault()?.Pattern ?? ""),
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowOpenDialog(string title, IReadOnlyList<FileFilter> filters)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = BuildFilterString(filters),
            CheckFileExists = true,
            CheckPathExists = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>
    /// Build the Win32 filter string: "Label|*.ext|Label2|*.ext2".
    /// </summary>
    private static string BuildFilterString(IReadOnlyList<FileFilter> filters)
    {
        return string.Join("|", filters.SelectMany(f => new[] { f.Label, f.Pattern }));
    }

    private static string ExtractExtension(string pattern)
    {
        // Pattern looks like "*.lumosx" — strip the asterisk.
        var dot = pattern.IndexOf('.');
        return dot >= 0 ? pattern.Substring(dot + 1) : "";
    }
}
