using System.IO;
using Lumos.Core.Entries;
using Lumos.Core.PortableExport;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// What screen the Backup pane is currently showing.
/// </summary>
public enum BackupMode
{
    /// <summary>Default landing — three big action buttons.</summary>
    Idle,
    /// <summary>Encrypted export — user is entering a passphrase.</summary>
    ExportEncryptedPrompt,
    /// <summary>Plaintext export — first confirmation ("are you sure?").</summary>
    PlaintextWarning,
    /// <summary>Plaintext export — second confirmation.</summary>
    PlaintextFinal,
    /// <summary>Import — user is entering passphrase for an LXP1 file.</summary>
    ImportPassphrasePrompt,
    /// <summary>Import — preview shown, awaiting commit or cancel.</summary>
    ImportPreview,
}

/// <summary>
/// The Backup pane state machine. Holds enough state to drive the UI in any
/// of the modes above without leaking file paths or passphrases between
/// runs of a flow.
/// </summary>
public sealed class BackupViewModel : ObservableObject
{
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;
    private readonly IFileDialogService _dialogs;

    private BackupMode _mode = BackupMode.Idle;
    private string _statusMessage = "";
    private string _errorMessage = "";
    private bool _isBusy;

    // Per-flow scratch state — cleared on Cancel or completion.
    private string _passphrase = "";
    private string _passphraseConfirm = "";
    private ExportFormat _pendingExportFormat;        // for encrypted flow
    private ExportFormat _pendingPlaintextFormat;     // for plaintext flow
    private byte[]? _pendingImportBytes;
    private ImportPreview? _importPreview;

    public BackupViewModel(
        EntryRepository entries,
        FolderRepository folders,
        IFileDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(dialogs);
        _entries = entries;
        _folders = folders;
        _dialogs = dialogs;

        // Idle-mode actions
        StartExportLumosEncryptedCommand = new RelayCommand(() => BeginEncryptedExport(ExportFormat.LumosEncrypted),
            () => !IsBusy && Mode == BackupMode.Idle);
        StartExportBitwardenEncryptedCommand = new RelayCommand(() => BeginEncryptedExport(ExportFormat.BitwardenEncrypted),
            () => !IsBusy && Mode == BackupMode.Idle);
        StartExportPlaintextCommand = new RelayCommand(BeginPlaintextWarning,
            () => !IsBusy && Mode == BackupMode.Idle);
        StartImportCommand = new RelayCommand(() => _ = BeginImportAsync(),
            () => !IsBusy && Mode == BackupMode.Idle);

        // Encrypted-export flow
        ConfirmEncryptedExportCommand = new RelayCommand(() => _ = ConfirmEncryptedExportAsync(),
            () => Mode == BackupMode.ExportEncryptedPrompt
                  && Passphrase.Length >= 8
                  && Passphrase == PassphraseConfirm
                  && !IsBusy);
        CancelEncryptedExportCommand = new RelayCommand(ResetAll,
            () => Mode == BackupMode.ExportEncryptedPrompt);

        // Plaintext-export flow (two steps)
        AcknowledgePlaintextWarningCommand = new RelayCommand(() => Mode = BackupMode.PlaintextFinal,
            () => Mode == BackupMode.PlaintextWarning);
        CancelPlaintextExportCommand = new RelayCommand(ResetAll,
            () => Mode == BackupMode.PlaintextWarning || Mode == BackupMode.PlaintextFinal);
        ConfirmPlaintextExportLumosCommand = new RelayCommand(() => _ = WritePlaintextExportAsync(ExportFormat.LumosJson),
            () => Mode == BackupMode.PlaintextFinal && !IsBusy);
        ConfirmPlaintextExportBitwardenCommand = new RelayCommand(() => _ = WritePlaintextExportAsync(ExportFormat.BitwardenJson),
            () => Mode == BackupMode.PlaintextFinal && !IsBusy);
        ConfirmPlaintextExportCsvCommand = new RelayCommand(() => _ = WritePlaintextExportAsync(ExportFormat.Csv),
            () => Mode == BackupMode.PlaintextFinal && !IsBusy);

        // Import flow
        SubmitImportPassphraseCommand = new RelayCommand(() => _ = SubmitImportPassphraseAsync(),
            () => Mode == BackupMode.ImportPassphrasePrompt && !IsBusy);
        CancelImportCommand = new RelayCommand(ResetAll,
            () => Mode == BackupMode.ImportPassphrasePrompt || Mode == BackupMode.ImportPreview);
        ConfirmImportCommand = new RelayCommand(CommitImport,
            () => Mode == BackupMode.ImportPreview && _importPreview is not null && !IsBusy);
    }

    // -------- bindable state --------

    public BackupMode Mode
    {
        get => _mode;
        private set
        {
            if (SetField(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsExportEncryptedPrompt));
                OnPropertyChanged(nameof(IsPlaintextWarning));
                OnPropertyChanged(nameof(IsPlaintextFinal));
                OnPropertyChanged(nameof(IsImportPassphrasePrompt));
                OnPropertyChanged(nameof(IsImportPreview));
                RaiseAllCommandsCanExecute();
            }
        }
    }

    public bool IsIdle => Mode == BackupMode.Idle;
    public bool IsExportEncryptedPrompt => Mode == BackupMode.ExportEncryptedPrompt;
    public bool IsPlaintextWarning => Mode == BackupMode.PlaintextWarning;
    public bool IsPlaintextFinal => Mode == BackupMode.PlaintextFinal;
    public bool IsImportPassphrasePrompt => Mode == BackupMode.ImportPassphrasePrompt;
    public bool IsImportPreview => Mode == BackupMode.ImportPreview;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseAllCommandsCanExecute();
        }
    }

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string ErrorMessage  { get => _errorMessage;  private set => SetField(ref _errorMessage, value); }

    public string Passphrase
    {
        get => _passphrase;
        set
        {
            if (SetField(ref _passphrase, value))
                RaiseAllCommandsCanExecute();
        }
    }

    public string PassphraseConfirm
    {
        get => _passphraseConfirm;
        set
        {
            if (SetField(ref _passphraseConfirm, value))
                RaiseAllCommandsCanExecute();
        }
    }

    /// <summary>Preview info shown after Load(), before Commit().</summary>
    public ImportPreview? ImportPreview
    {
        get => _importPreview;
        private set
        {
            if (SetField(ref _importPreview, value))
            {
                OnPropertyChanged(nameof(ImportSummary));
                OnPropertyChanged(nameof(ImportSampleText));
                RaiseAllCommandsCanExecute();
            }
        }
    }

    public string ImportSummary => _importPreview is null
        ? ""
        : $"{_importPreview.NewEntryCount} new · {_importPreview.DuplicateCount} duplicates · {_importPreview.NewFolderCount} new folders";

    public string ImportSampleText => _importPreview is null || _importPreview.PreviewTitles.Count == 0
        ? ""
        : string.Join("\n", _importPreview.PreviewTitles.Select(t => "• " + t));

    // -------- commands --------

    public RelayCommand StartExportLumosEncryptedCommand { get; }
    public RelayCommand StartExportBitwardenEncryptedCommand { get; }
    public RelayCommand StartExportPlaintextCommand { get; }
    public RelayCommand StartImportCommand { get; }

    public RelayCommand ConfirmEncryptedExportCommand { get; }
    public RelayCommand CancelEncryptedExportCommand { get; }

    public RelayCommand AcknowledgePlaintextWarningCommand { get; }
    public RelayCommand CancelPlaintextExportCommand { get; }
    public RelayCommand ConfirmPlaintextExportLumosCommand { get; }
    public RelayCommand ConfirmPlaintextExportBitwardenCommand { get; }
    public RelayCommand ConfirmPlaintextExportCsvCommand { get; }

    public RelayCommand SubmitImportPassphraseCommand { get; }
    public RelayCommand CancelImportCommand { get; }
    public RelayCommand ConfirmImportCommand { get; }

    // -------- encrypted export flow --------

    private void BeginEncryptedExport(ExportFormat format)
    {
        _pendingExportFormat = format;
        Passphrase = "";
        PassphraseConfirm = "";
        ErrorMessage = "";
        Mode = BackupMode.ExportEncryptedPrompt;
    }

    private async Task ConfirmEncryptedExportAsync()
    {
        // Capture passphrase locally so we can clear it before the dialog
        // round-trip (we don't want it sitting in the VM longer than
        // necessary).
        var passphrase = Passphrase;
        var format = _pendingExportFormat;
        var defaultName = format == ExportFormat.LumosEncrypted
            ? $"lumos-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.lumosx"
            : $"bitwarden-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.lumosx";
        var filters = new[]
        {
            new FileFilter("Lumos encrypted export (*.lumosx)", "*.lumosx"),
        };

        var path = _dialogs.ShowSaveDialog("Save encrypted export", defaultName, filters);
        if (path is null) { ResetAll(); return; }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var bytes = await Task.Run(() =>
                new VaultExporter(_entries, _folders).Export(format, passphrase));
            await File.WriteAllBytesAsync(path, bytes);
            StatusMessage = $"Exported {bytes.Length:N0} bytes to {Path.GetFileName(path)}.";
            ResetAllPreservingStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -------- plaintext export flow --------

    private void BeginPlaintextWarning()
    {
        ErrorMessage = "";
        Mode = BackupMode.PlaintextWarning;
    }

    private async Task WritePlaintextExportAsync(ExportFormat format)
    {
        _pendingPlaintextFormat = format;
        var (defaultName, filters) = format switch
        {
            ExportFormat.LumosJson => (
                $"lumos-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json",
                new[] { new FileFilter("Lumos plaintext JSON (*.json)", "*.json") }),
            ExportFormat.BitwardenJson => (
                $"bitwarden-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json",
                new[] { new FileFilter("Bitwarden JSON (*.json)", "*.json") }),
            ExportFormat.Csv => (
                $"lumos-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv",
                new[] { new FileFilter("CSV (*.csv)", "*.csv") }),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        var path = _dialogs.ShowSaveDialog("Save plaintext export", defaultName, filters);
        if (path is null) { ResetAll(); return; }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var bytes = await Task.Run(() =>
                new VaultExporter(_entries, _folders).Export(format));
            await File.WriteAllBytesAsync(path, bytes);
            StatusMessage = $"Exported {bytes.Length:N0} bytes (plaintext) to {Path.GetFileName(path)}.";
            ResetAllPreservingStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -------- import flow --------

    private async Task BeginImportAsync()
    {
        var filters = new[]
        {
            new FileFilter("Lumos / Bitwarden export (*.lumosx;*.json)", "*.lumosx;*.json"),
            new FileFilter("Lumos encrypted (*.lumosx)", "*.lumosx"),
            new FileFilter("JSON (*.json)", "*.json"),
            new FileFilter("All files (*.*)", "*.*"),
        };
        var path = _dialogs.ShowOpenDialog("Open export file", filters);
        if (path is null) return;

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            _pendingImportBytes = await File.ReadAllBytesAsync(path);

            // Try a no-passphrase Load first. If it's a plaintext file the
            // preview comes back immediately. If it's LXP1 we'll get a
            // WrongPassphrase failure and switch to the passphrase prompt.
            var importer = new VaultImporter(_entries, _folders);
            var load = importer.Load(_pendingImportBytes, passphrase: null);
            if (load.Failure == ImportFailureReason.WrongPassphrase)
            {
                Passphrase = "";
                Mode = BackupMode.ImportPassphrasePrompt;
            }
            else if (load.Failure != ImportFailureReason.None)
            {
                ErrorMessage = DescribeFailure(load.Failure);
                ResetAll();
            }
            else
            {
                ImportPreview = load.Preview;
                Mode = BackupMode.ImportPreview;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't read file: {ex.Message}";
            ResetAll();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SubmitImportPassphraseAsync()
    {
        if (_pendingImportBytes is null) { ResetAll(); return; }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var importer = new VaultImporter(_entries, _folders);
            var load = await Task.Run(() => importer.Load(_pendingImportBytes, Passphrase));
            if (load.Failure != ImportFailureReason.None)
            {
                ErrorMessage = DescribeFailure(load.Failure);
                return;   // stay on the passphrase prompt — user can retry
            }
            ImportPreview = load.Preview;
            Mode = BackupMode.ImportPreview;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CommitImport()
    {
        if (_importPreview is null) return;
        IsBusy = true;
        try
        {
            var importer = new VaultImporter(_entries, _folders);
            var inserted = importer.Commit(_importPreview);
            StatusMessage = $"Imported {inserted} new entr{(inserted == 1 ? "y" : "ies")}.";
            ResetAllPreservingStatus();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import commit failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -------- helpers --------

    private static string DescribeFailure(ImportFailureReason r) => r switch
    {
        ImportFailureReason.WrongPassphrase => "Wrong passphrase, or the file isn't a Lumos encrypted export.",
        ImportFailureReason.BadFormat => "Couldn't recognize the file format.",
        ImportFailureReason.Corrupt => "The file appears to be corrupt.",
        ImportFailureReason.EmptyFile => "The file is empty.",
        _ => "Import failed.",
    };

    /// <summary>Reset every scratch field. Called on Cancel and after success/failure.</summary>
    private void ResetAll()
    {
        Passphrase = "";
        PassphraseConfirm = "";
        _pendingImportBytes = null;
        ImportPreview = null;
        Mode = BackupMode.Idle;
    }

    /// <summary>Reset transient state but keep StatusMessage so the success banner survives.</summary>
    private void ResetAllPreservingStatus()
    {
        var status = StatusMessage;
        ResetAll();
        StatusMessage = status;
    }

    private void RaiseAllCommandsCanExecute()
    {
        StartExportLumosEncryptedCommand.RaiseCanExecuteChanged();
        StartExportBitwardenEncryptedCommand.RaiseCanExecuteChanged();
        StartExportPlaintextCommand.RaiseCanExecuteChanged();
        StartImportCommand.RaiseCanExecuteChanged();
        ConfirmEncryptedExportCommand.RaiseCanExecuteChanged();
        CancelEncryptedExportCommand.RaiseCanExecuteChanged();
        AcknowledgePlaintextWarningCommand.RaiseCanExecuteChanged();
        CancelPlaintextExportCommand.RaiseCanExecuteChanged();
        ConfirmPlaintextExportLumosCommand.RaiseCanExecuteChanged();
        ConfirmPlaintextExportBitwardenCommand.RaiseCanExecuteChanged();
        ConfirmPlaintextExportCsvCommand.RaiseCanExecuteChanged();
        SubmitImportPassphraseCommand.RaiseCanExecuteChanged();
        CancelImportCommand.RaiseCanExecuteChanged();
        ConfirmImportCommand.RaiseCanExecuteChanged();
    }
}
