using System.Windows.Threading;
using Lumos.Core.Attachments;
using Lumos.Core.Entries;
using Lumos.Core.Totp;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// The right-side detail pane. Holds the currently-selected entry and the
/// transient UI state around it (reveal, edit, delete confirmation).
/// </summary>
public sealed class EntryDetailViewModel : ObservableObject, IDisposable
{
    private readonly EntryRepository _entries;
    private Entry? _entry;
    private bool _isPasswordRevealed;
    private bool _isEditing;
    private bool _isDeleteArmed;
    private string _statusMessage = "";

    // TOTP refresh — runs while the detail has an entry with a TOTP secret
    // AND the pane is "active" (the shell view tells us via TimerActive).
    private DispatcherTimer? _totpTimer;
    private string _totpCode = "";
    private double _totpSecondsRemaining;
    private double _totpFractionRemaining = 1.0;
    private bool _timerActive = true;

    // Editable fields (used in edit mode)
    private string _editTitle = "";
    private string _editUsername = "";
    private string _editPassword = "";
    private string _editUrl = "";
    private string _editNotes = "";
    private string _editTotpSecret = "";
    private string _editTotpError = "";

    public event EventHandler? EntryChanged;       // entry was saved
    public event EventHandler? EntryDeleted;       // entry was deleted

    public EntryDetailViewModel(EntryRepository entries,
                                AttachmentRepository attachments,
                                IFileDialogService fileDialogs)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(fileDialogs);
        _entries = entries;

        Attachments = new AttachmentsPanelViewModel(attachments, fileDialogs);

        RevealCommand = new RelayCommand(() => IsPasswordRevealed = !IsPasswordRevealed,
            () => HasEntry && CurrentLogin is not null);

        CopyUsernameCommand = new RelayCommand(CopyUsername,
            () => HasEntry && !string.IsNullOrEmpty(CurrentLogin?.Username));

        CopyPasswordCommand = new RelayCommand(CopyPassword,
            () => HasEntry && !string.IsNullOrEmpty(CurrentLogin?.Password));

        CopyTotpCommand = new RelayCommand(CopyTotp,
            () => HasTotp && !string.IsNullOrEmpty(TotpCode));

        EditCommand = new RelayCommand(EnterEditMode,
            () => HasEntry && !IsEditing);

        SaveCommand = new RelayCommand(SaveEdits, () => IsEditing);

        CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditing);

        DeleteCommand = new RelayCommand(HandleDelete,
            () => HasEntry && !IsEditing);
    }

    public Entry? Entry
    {
        get => _entry;
        set
        {
            if (SetField(ref _entry, value))
            {
                // Reset all transient state when the selected entry changes.
                IsPasswordRevealed = false;
                IsEditing = false;
                IsDeleteArmed = false;
                StatusMessage = "";
                OnPropertyChanged(nameof(HasEntry));
                OnPropertyChanged(nameof(CurrentLogin));
                OnPropertyChanged(nameof(DisplayPassword));
                OnPropertyChanged(nameof(HasTotp));
                RefreshTotpSnapshot();
                UpdateTotpTimer();
                Attachments.SetEntry(value?.Id);
                RaiseAllCommands();
            }
        }
    }

    /// <summary>The attachments section for the current entry.</summary>
    public AttachmentsPanelViewModel Attachments { get; }

    public bool HasEntry => _entry is not null;

    public LoginPayload? CurrentLogin => _entry?.Payload as LoginPayload;

    /// <summary>True when the current login has a TOTP secret stored.</summary>
    public bool HasTotp => CurrentLogin is { TotpSecret: not null } login
                          && !string.IsNullOrWhiteSpace(login.TotpSecret);

    public string TotpCode
    {
        get => _totpCode;
        private set
        {
            if (SetField(ref _totpCode, value))
            {
                OnPropertyChanged(nameof(TotpCodeDisplay));
                CopyTotpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Code with a space in the middle for easier reading: "123 456".</summary>
    public string TotpCodeDisplay =>
        _totpCode.Length == 6 ? $"{_totpCode.Substring(0, 3)} {_totpCode.Substring(3, 3)}" : _totpCode;

    public double TotpSecondsRemaining
    {
        get => _totpSecondsRemaining;
        private set
        {
            if (SetField(ref _totpSecondsRemaining, value))
                OnPropertyChanged(nameof(TotpIsExpiringSoon));
        }
    }

    /// <summary>0.0 = about to roll, 1.0 = just rolled. Drives the countdown ring.</summary>
    public double TotpFractionRemaining
    {
        get => _totpFractionRemaining;
        private set => SetField(ref _totpFractionRemaining, value);
    }

    /// <summary>True when &lt; 5 seconds remain — UI uses this to switch the ring to danger color.</summary>
    public bool TotpIsExpiringSoon => TotpSecondsRemaining < 5;

    /// <summary>
    /// Pause/resume the TOTP timer. The shell sets this to false when the
    /// Vault pane is hidden so we don't burn CPU recomputing while invisible.
    /// </summary>
    public bool TimerActive
    {
        get => _timerActive;
        set
        {
            if (SetField(ref _timerActive, value))
                UpdateTotpTimer();
        }
    }

    public bool IsPasswordRevealed
    {
        get => _isPasswordRevealed;
        set
        {
            if (SetField(ref _isPasswordRevealed, value))
            {
                OnPropertyChanged(nameof(DisplayPassword));
                OnPropertyChanged(nameof(RevealLabel));
            }
        }
    }

    public string DisplayPassword
        => CurrentLogin is null
            ? ""
            : IsPasswordRevealed
                ? CurrentLogin.Password
                : new string('●', Math.Min(CurrentLogin.Password.Length, 16));

    public string RevealLabel => IsPasswordRevealed ? "HIDE" : "REVEAL";

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetField(ref _isEditing, value))
            {
                RaiseAllCommands();
            }
        }
    }

    public bool IsDeleteArmed
    {
        get => _isDeleteArmed;
        private set
        {
            if (SetField(ref _isDeleteArmed, value))
            {
                OnPropertyChanged(nameof(DeleteButtonLabel));
            }
        }
    }

    public string DeleteButtonLabel => IsDeleteArmed ? "CLICK AGAIN TO CONFIRM" : "DELETE";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    // -------- Editable fields --------

    public string EditTitle    { get => _editTitle;    set => SetField(ref _editTitle, value); }
    public string EditUsername { get => _editUsername; set => SetField(ref _editUsername, value); }
    public string EditPassword { get => _editPassword; set => SetField(ref _editPassword, value); }
    public string EditUrl      { get => _editUrl;      set => SetField(ref _editUrl, value); }
    public string EditNotes    { get => _editNotes;    set => SetField(ref _editNotes, value); }

    /// <summary>
    /// User-entered TOTP secret in edit mode. Accepts either a base32 string
    /// or an otpauth:// URI; parsed at save time.
    /// </summary>
    public string EditTotpSecret
    {
        get => _editTotpSecret;
        set
        {
            if (SetField(ref _editTotpSecret, value))
                EditTotpError = "";   // clear any prior error as the user types
        }
    }

    /// <summary>Inline validation error for the TOTP field.</summary>
    public string EditTotpError
    {
        get => _editTotpError;
        private set => SetField(ref _editTotpError, value);
    }

    // -------- Commands --------

    public RelayCommand RevealCommand { get; }
    public RelayCommand CopyUsernameCommand { get; }
    public RelayCommand CopyPasswordCommand { get; }
    public RelayCommand CopyTotpCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand DeleteCommand { get; }

    private void CopyUsername()
    {
        if (CurrentLogin is null) return;
        AppServices.Clipboard.SetTextWithAutoClear(CurrentLogin.Username);
        ShowStatus($"Username copied. Clipboard clears in {AppServices.Clipboard.DefaultClearTimeout.TotalSeconds:0}s.");
        if (_entry is not null) _entries.TouchLastUsed(_entry.Id);
    }

    private void CopyTotp()
    {
        if (!HasTotp || string.IsNullOrEmpty(TotpCode)) return;
        AppServices.Clipboard.SetTextWithAutoClear(TotpCode);
        ShowStatus($"TOTP code copied. Clipboard clears in {AppServices.Clipboard.DefaultClearTimeout.TotalSeconds:0}s.");
        if (_entry is not null) _entries.TouchLastUsed(_entry.Id);
    }

    private void CopyPassword()
    {
        if (CurrentLogin is null) return;
        AppServices.Clipboard.SetTextWithAutoClear(CurrentLogin.Password);
        ShowStatus($"Password copied. Clipboard clears in {AppServices.Clipboard.DefaultClearTimeout.TotalSeconds:0}s.");
        if (_entry is not null) _entries.TouchLastUsed(_entry.Id);
    }

    private void EnterEditMode()
    {
        if (_entry is null) return;
        EditTitle = _entry.Title;
        EditNotes = _entry.Notes;
        if (CurrentLogin is { } login)
        {
            EditUsername = login.Username;
            EditPassword = login.Password;
            EditUrl = login.Url;
            EditTotpSecret = login.TotpSecret ?? "";
        }
        EditTotpError = "";
        IsDeleteArmed = false;
        IsEditing = true;
    }

    private void CancelEdit()
    {
        IsEditing = false;
        StatusMessage = "";
    }

    private void SaveEdits()
    {
        if (_entry is null) return;
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            ShowStatus("Title cannot be empty.");
            return;
        }

        // Validate and normalize the TOTP secret. An empty string clears the
        // secret; anything else must parse to a valid base32 (via either a
        // bare secret or an otpauth:// URI).
        string? totpSecret = null;
        if (!string.IsNullOrWhiteSpace(EditTotpSecret))
        {
            var parsed = Lumos.Core.Totp.TotpGenerator.TryParseSecret(EditTotpSecret);
            if (parsed is null)
            {
                EditTotpError = "Not a valid TOTP secret or otpauth:// URI.";
                return;
            }
            totpSecret = parsed;
        }

        var updatedPayload = CurrentLogin is null
            ? _entry.Payload
            : new LoginPayload(
                Username: EditUsername ?? "",
                Password: EditPassword ?? "",
                Url: EditUrl ?? "",
                TotpSecret: totpSecret);

        var updated = _entry with
        {
            Title = EditTitle.Trim(),
            Notes = EditNotes ?? "",
            Payload = updatedPayload,
        };

        var saved = _entries.Update(updated);
        Entry = saved;        // resets transient state
        ShowStatus("Saved.");
        EntryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleDelete()
    {
        if (_entry is null) return;
        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            return;
        }

        _entries.Delete(_entry.Id);
        var deletedId = _entry.Id;
        Entry = null;
        EntryDeleted?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseAllCommands()
    {
        RevealCommand.RaiseCanExecuteChanged();
        CopyUsernameCommand.RaiseCanExecuteChanged();
        CopyPasswordCommand.RaiseCanExecuteChanged();
        CopyTotpCommand.RaiseCanExecuteChanged();
        EditCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        CancelEditCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        // Status messages auto-fade after 4s. We use a fire-and-forget task
        // and re-check that the message hasn't been replaced before clearing.
        var captured = message;
        _ = Task.Run(async () =>
        {
            await Task.Delay(4000);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (StatusMessage == captured) StatusMessage = "";
            });
        });
    }

    // -------- TOTP timer --------

    /// <summary>
    /// Decide whether the per-second TOTP timer should run. We run it when:
    ///   1. There's a selected entry with a TOTP secret, AND
    ///   2. The pane is "active" (the shell hasn't paused us).
    /// </summary>
    private void UpdateTotpTimer()
    {
        var shouldRun = TimerActive && HasTotp;
        if (shouldRun && _totpTimer is null)
        {
            _totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _totpTimer.Tick += OnTotpTimerTick;
            _totpTimer.Start();
        }
        else if (!shouldRun && _totpTimer is not null)
        {
            _totpTimer.Stop();
            _totpTimer.Tick -= OnTotpTimerTick;
            _totpTimer = null;
        }
    }

    private void OnTotpTimerTick(object? sender, EventArgs e) => RefreshTotpSnapshot();

    private void RefreshTotpSnapshot()
    {
        if (!HasTotp || CurrentLogin?.TotpSecret is null)
        {
            TotpCode = "";
            TotpSecondsRemaining = 0;
            TotpFractionRemaining = 0;
            return;
        }

        try
        {
            var snap = TotpGenerator.Snapshot(CurrentLogin.TotpSecret, DateTimeOffset.UtcNow);
            TotpCode = snap.Code;
            TotpSecondsRemaining = snap.SecondsRemaining;
            TotpFractionRemaining = snap.FractionRemaining;
        }
        catch
        {
            // Stored secret is corrupt. Don't crash — just blank the display.
            TotpCode = "";
            TotpSecondsRemaining = 0;
            TotpFractionRemaining = 0;
        }
    }

    public void Dispose()
    {
        if (_totpTimer is not null)
        {
            _totpTimer.Stop();
            _totpTimer.Tick -= OnTotpTimerTick;
            _totpTimer = null;
        }
    }
}
