using System.Security;
using Lumos.Core.Vault;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>Carries post-unlock state the shell router needs to act on.</summary>
public sealed class VaultUnlockedEventArgs : EventArgs
{
    public VaultUnlockedEventArgs(bool requiresRecoverySetup)
        => RequiresRecoverySetup = requiresRecoverySetup;

    /// <summary>
    /// True when this vault has no recovery code yet — every vault created
    /// before recovery existed, plus any where the user deferred the prompt.
    /// </summary>
    public bool RequiresRecoverySetup { get; }
}

public sealed class UnlockViewModel : ObservableObject
{
    private string _errorMessage = "";
    private bool _isBusy;
    private int _remainingBackoffSeconds;

    /// <summary>Raised when the vault is successfully unlocked.</summary>
    public event EventHandler<VaultUnlockedEventArgs>? VaultUnlocked;

    /// <summary>Raised when the user wants the recovery-code reset flow.</summary>
    public event EventHandler? ForgotPasswordRequested;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                UnlockCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int RemainingBackoffSeconds
    {
        get => _remainingBackoffSeconds;
        private set
        {
            if (SetField(ref _remainingBackoffSeconds, value))
            {
                OnPropertyChanged(nameof(IsBackoffActive));
                OnPropertyChanged(nameof(BackoffMessage));
                UnlockCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBackoffActive => RemainingBackoffSeconds > 0;

    public string BackoffMessage =>
        RemainingBackoffSeconds > 0
            ? $"Too many failed attempts. Try again in {RemainingBackoffSeconds}s."
            : "";

    /// <summary>
    /// Only offer the recovery route when this vault actually has a recovery
    /// code. Showing "forgot your password?" on a vault that cannot be
    /// recovered would be a cruel thing to dangle in front of someone who has
    /// just locked themselves out.
    /// </summary>
    public bool CanRecover => AppServices.VaultManager.HasRecovery;

    public RelayCommand UnlockCommand { get; }
    public RelayCommand ForgotPasswordCommand { get; }

    public UnlockViewModel()
    {
        UnlockCommand = new RelayCommand(
            execute: param => _ = TryUnlockAsync(param as SecureString),
            canExecute: _ => !IsBusy && !IsBackoffActive);

        ForgotPasswordCommand = new RelayCommand(
            execute: () => ForgotPasswordRequested?.Invoke(this, EventArgs.Empty),
            canExecute: () => !IsBusy);
    }

    private async Task TryUnlockAsync(SecureString? secureString)
    {
        if (secureString is null || secureString.Length == 0)
        {
            ErrorMessage = "Enter your master password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var plain = SecureStringHelper.ToPlainText(secureString);
            UnlockResult result = await Task.Run(() => AppServices.VaultManager.Unlock(plain));
            // Best-effort scrub of the plain string — it's still on the managed
            // heap until GC, but we shorten its life as much as we reasonably can.
            plain = string.Empty;

            switch (result.Status)
            {
                case UnlockStatus.Success:
                    AppServices.OpenVault = result.Service;
                    VaultUnlocked?.Invoke(this,
                        new VaultUnlockedEventArgs(result.RequiresRecoverySetup));
                    break;

                case UnlockStatus.WrongPassword:
                    ErrorMessage = result.FailedAttemptCount == 1
                        ? "Incorrect master password."
                        : $"Incorrect master password. ({result.FailedAttemptCount} failed attempts.)";
                    if (result.Backoff > TimeSpan.Zero)
                    {
                        await StartBackoffCountdownAsync(result.Backoff);
                    }
                    break;

                case UnlockStatus.BackoffRequired:
                    await StartBackoffCountdownAsync(result.RemainingBackoff);
                    break;

                case UnlockStatus.VaultMissing:
                    ErrorMessage = "Vault file is missing.";
                    break;

                case UnlockStatus.VaultCorrupted:
                    ErrorMessage = $"Vault appears corrupted: {result.ErrorMessage}";
                    break;

                case UnlockStatus.SelfDestructed:
                    ErrorMessage = "Self-destruct triggered — vault has been deleted.";
                    break;

                default:
                    ErrorMessage = result.ErrorMessage ?? "Something went wrong.";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartBackoffCountdownAsync(TimeSpan total)
    {
        var remaining = (int)Math.Ceiling(total.TotalSeconds);
        RemainingBackoffSeconds = remaining;
        while (remaining > 0)
        {
            await Task.Delay(1000);
            remaining--;
            RemainingBackoffSeconds = remaining;
        }
    }
}
