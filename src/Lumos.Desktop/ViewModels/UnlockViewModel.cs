using System.Security;
using Lumos.Core.Vault;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

public sealed class UnlockViewModel : ObservableObject
{
    private string _errorMessage = "";
    private bool _isBusy;
    private int _remainingBackoffSeconds;

    /// <summary>Raised when the vault is successfully unlocked.</summary>
    public event EventHandler? VaultUnlocked;

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

    public RelayCommand UnlockCommand { get; }

    public UnlockViewModel()
    {
        UnlockCommand = new RelayCommand(
            execute: param => _ = TryUnlockAsync(param as SecureString),
            canExecute: _ => !IsBusy && !IsBackoffActive);
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
            var plain = SecureStringToString(secureString);
            UnlockResult result = await Task.Run(() => AppServices.VaultManager.Unlock(plain));
            // Best-effort scrub of the plain string — it's still on the managed
            // heap until GC, but we shorten its life as much as we reasonably can.
            plain = string.Empty;

            switch (result.Status)
            {
                case UnlockStatus.Success:
                    AppServices.OpenVault = result.Service;
                    VaultUnlocked?.Invoke(this, EventArgs.Empty);
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

    private static string SecureStringToString(SecureString secure)
    {
        var bstr = IntPtr.Zero;
        try
        {
            bstr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(secure);
            return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            if (bstr != IntPtr.Zero)
                System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstr);
        }
    }
}
