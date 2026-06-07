using Lumos.Core.Attachments;
using Lumos.Core.Entries;
using Lumos.Core.Security;
using Lumos.Core.Vault;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.ViewModels;

public enum ShellPane
{
    Vault,
    Generator,
    Backup,
}

/// <summary>
/// The shell that runs while the vault is unlocked. Owns:
///   - The active pane (Vault / Generator / Backup)
///   - The child VMs
///   - The AutoLockService lifecycle (start on construct, stop on Dispose)
///   - The LockRequested handler that signals MainWindow to lock and re-route
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly AutoLockService _autoLock;
    private readonly WindowsIdleMonitor _idleMonitor;
    private readonly WindowsSystemEventSource _systemEvents;
    private ShellPane _activePane = ShellPane.Vault;
    private bool _disposed;

    public VaultViewModel Vault { get; }
    public GeneratorViewModel Generator { get; }
    public BackupViewModel Backup { get; }
    public UpdateViewModel Update { get; }

    /// <summary>Raised when any lock trigger fires. MainWindow listens and routes to UnlockView.</summary>
    public event EventHandler<LockRequestedEventArgs>? LockRequested;

    public ShellPane ActivePane
    {
        get => _activePane;
        set
        {
            if (SetField(ref _activePane, value))
            {
                OnPropertyChanged(nameof(IsVaultActive));
                OnPropertyChanged(nameof(IsGeneratorActive));
                OnPropertyChanged(nameof(IsBackupActive));
                // Stop the per-second TOTP timer on the detail VM when the
                // user navigates away from the vault pane — no need to burn
                // CPU recomputing for an invisible UI.
                Vault.Detail.TimerActive = IsVaultActive;
            }
        }
    }

    public bool IsVaultActive => ActivePane == ShellPane.Vault;
    public bool IsGeneratorActive => ActivePane == ShellPane.Generator;
    public bool IsBackupActive => ActivePane == ShellPane.Backup;

    public RelayCommand ShowVaultCommand { get; }
    public RelayCommand ShowGeneratorCommand { get; }
    public RelayCommand ShowBackupCommand { get; }
    public RelayCommand LockCommand { get; }

    public ShellViewModel(
        EntryRepository entries,
        FolderRepository folders,
        AttachmentRepository attachments,
        WindowsIdleMonitor idleMonitor,
        WindowsSystemEventSource systemEvents,
        AutoLockSettings settings,
        IFileDialogService fileDialogs)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(idleMonitor);
        ArgumentNullException.ThrowIfNull(systemEvents);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fileDialogs);

        _idleMonitor = idleMonitor;
        _systemEvents = systemEvents;

        // v2: no breach checker, no backend. VaultViewModel runs purely local.
        Vault = new VaultViewModel(entries, attachments, fileDialogs);
        Generator = new GeneratorViewModel();
        Backup = new BackupViewModel(entries, folders, fileDialogs);
        Update = new UpdateViewModel(new UpdateService());

        ShowVaultCommand = new RelayCommand(() => ActivePane = ShellPane.Vault);
        ShowGeneratorCommand = new RelayCommand(() => ActivePane = ShellPane.Generator);
        ShowBackupCommand = new RelayCommand(() => ActivePane = ShellPane.Backup);
        LockCommand = new RelayCommand(() => _autoLock!.RequestLock(LockReason.Manual));

        _autoLock = new AutoLockService(settings, idleMonitor, systemEvents);
        _autoLock.LockRequested += OnLockRequested;
        _autoLock.Start();
    }

    private void OnLockRequested(object? sender, LockRequestedEventArgs e)
    {
        // We hop to the UI thread because system events may arrive on
        // background threads (SystemEvents.PowerModeChanged in particular).
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LockRequested?.Invoke(this, e);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Vault.Detail.Dispose();
        Vault.Dispose();
        _autoLock.LockRequested -= OnLockRequested;
        _autoLock.Dispose();
        _systemEvents.Dispose();
    }
}
