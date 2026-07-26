using System.IO;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// Displays a freshly issued recovery code — ONCE.
///
/// This screen is the only moment the plaintext code exists anywhere. Lumos
/// stores only a key derived from it, so once this view is dismissed the code
/// cannot be shown again; the user would have to generate a new one (which
/// requires the master password) instead.
///
/// The "I have saved it" gate is therefore not ceremony. It is the last point
/// at which a user can avoid permanently losing their only backup route into
/// the vault, so the Continue button stays disabled until they tick it.
/// </summary>
public sealed class RecoveryCodeViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogs;
    private bool _hasSaved;
    private string _statusMessage = "";

    /// <summary>Raised when the user has acknowledged the code and wants to move on.</summary>
    public event EventHandler? Acknowledged;

    public RecoveryCodeViewModel(string code, IFileDialogService fileDialogs, bool isNewVault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(fileDialogs);

        Code = code;
        _fileDialogs = fileDialogs;
        IsNewVault = isNewVault;

        CopyCommand = new RelayCommand(CopyToClipboard);
        SaveCommand = new RelayCommand(SaveToFile);
        ContinueCommand = new RelayCommand(
            execute: () => Acknowledged?.Invoke(this, EventArgs.Empty),
            canExecute: () => HasSaved);
    }

    /// <summary>The code, in display form with dashes.</summary>
    public string Code { get; }

    /// <summary>True when shown right after vault creation rather than during migration.</summary>
    public bool IsNewVault { get; }

    public string Headline => IsNewVault
        ? "Save your recovery code"
        : "Lumos now has recovery codes";

    public string Explanation => IsNewVault
        ? "This code can unlock your vault if you ever forget your master password. " +
          "It is shown once and cannot be retrieved later."
        : "This code can unlock your vault if you ever forget your master password. " +
          "Your existing vault and master password are unchanged — this only adds a " +
          "second way in. It is shown once and cannot be retrieved later.";

    public bool HasSaved
    {
        get => _hasSaved;
        set
        {
            if (SetField(ref _hasSaved, value))
                ContinueCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public RelayCommand CopyCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ContinueCommand { get; }

    private void CopyToClipboard()
    {
        try
        {
            // Auto-clearing copy, same as vault passwords: a recovery code left
            // sitting on the clipboard is exactly the kind of thing that ends up
            // pasted into a chat window by accident.
            AppServices.Clipboard.SetTextWithAutoClear(Code);
            StatusMessage = "Copied. The clipboard will clear itself shortly.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't copy: {ex.Message}";
        }
    }

    private void SaveToFile()
    {
        try
        {
            var path = _fileDialogs.ShowSaveDialog(
                "Save your Lumos recovery code",
                "lumos-recovery-code.txt",
                new[] { new FileFilter("Text file", "*.txt") });

            if (path is null) return;   // user cancelled

            File.WriteAllText(path, BuildRecoverySheet());
            StatusMessage = $"Saved to {path}";
            HasSaved = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save: {ex.Message}";
        }
    }

    /// <summary>
    /// The text written to the saved file. Includes the warnings, because the
    /// file will outlive this window and is the thing the user actually finds
    /// again in two years.
    /// </summary>
    private string BuildRecoverySheet()
    {
        return
            "LUMOS RECOVERY CODE\r\n" +
            "===================\r\n\r\n" +
            Code + "\r\n\r\n" +
            "What this is\r\n" +
            "------------\r\n" +
            "This code can unlock your Lumos vault if you forget your master\r\n" +
            "password. It works offline and forever. Treat it exactly like the\r\n" +
            "master password itself.\r\n\r\n" +
            "Keep it somewhere separate from your computer -- printed and filed,\r\n" +
            "or in a safe. Storing it on the same machine as the vault defeats\r\n" +
            "the purpose.\r\n\r\n" +
            "Nobody can recover your vault for you. Not the Lumos authors, not\r\n" +
            "anyone else. There is no server, no account, and no master key.\r\n" +
            "If you lose both your master password and this code, the data is\r\n" +
            "gone permanently.\r\n\r\n" +
            $"Issued: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}\r\n";
    }
}
