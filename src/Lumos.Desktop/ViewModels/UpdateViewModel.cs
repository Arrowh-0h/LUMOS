using System.Reflection;
using System.Threading.Tasks;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// Drives the "Check for updates" affordance at the bottom of the sidebar.
///
/// Flow (all user-initiated — Lumos never checks on its own):
///   idle -> [Check for updates] -> checking
///        -> UpToDate / NotInstalled / Failed  (shows a status line)
///        -> UpdateAvailable                   (shows "Update to vX" button + reassurance)
///        -> [Update now] -> downloading -> app restarts into the new version
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updates;

    private bool _isBusy;
    private string _statusMessage = "";
    private bool _updateAvailable;
    private string? _newVersion;

    public UpdateViewModel(UpdateService updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        _updates = updates;

        CheckCommand = new RelayCommand(() => _ = CheckAsync(), () => !IsBusy);
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => !IsBusy && UpdateAvailable);
    }

    public RelayCommand CheckCommand { get; }
    public RelayCommand ApplyCommand { get; }

    /// <summary>Current app version, e.g. "1.0.0", shown as a subtle label.</summary>
    public string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                CheckCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set
        {
            if (SetField(ref _updateAvailable, value))
            {
                OnPropertyChanged(nameof(UpdateButtonText));
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UpdateButtonText => _newVersion is null ? "UPDATE NOW" : $"UPDATE TO v{_newVersion}";

    private async Task CheckAsync()
    {
        IsBusy = true;
        UpdateAvailable = false;
        StatusMessage = "Checking…";
        try
        {
            var result = await _updates.CheckAsync();
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _newVersion = result.NewVersion;
                    UpdateAvailable = true;
                    StatusMessage = $"Update available: v{result.NewVersion}. Your vault and all your data stay exactly as they are.";
                    break;
                case UpdateCheckStatus.UpToDate:
                    StatusMessage = $"You're on the latest version ({CurrentVersion}).";
                    break;
                case UpdateCheckStatus.NotInstalled:
                    StatusMessage = "Updates apply to the installed app only (not dev builds).";
                    break;
                default:
                    StatusMessage = $"Couldn't check for updates: {result.Error}";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAsync()
    {
        IsBusy = true;
        StatusMessage = "Downloading update…";
        try
        {
            // On success the app restarts and this never returns.
            var error = await _updates.DownloadAndApplyAsync();
            if (error is not null)
            {
                StatusMessage = $"Update failed: {error}";
                IsBusy = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            IsBusy = false;
        }
    }
}
