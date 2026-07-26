using System.Security;
using Lumos.Core.Vault;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// The one-time prompt shown to EXISTING users after they update to a build
/// with recovery codes. Explains what changed, then issues a code.
///
/// Why it asks for the master password again even though the user just typed
/// it to unlock: writing the recovery wrap needs the vault's cipher key, which
/// only the master password can produce. Rather than keeping the plaintext
/// password alive in memory across screen transitions purely for convenience,
/// we ask for it at the moment it is needed and drop it immediately. Re-
/// authenticating before a security-sensitive change is also just good
/// practice.
///
/// The user can decline. A nag screen that locks someone out of their own vault
/// is a worse outcome than one they postpone, so "Not now" is a real option —
/// the prompt simply returns on the next unlock.
/// </summary>
public sealed class RecoverySetupPromptViewModel : ObservableObject
{
    private string _errorMessage = "";
    private bool _isBusy;

    /// <summary>Raised with the newly issued code once setup succeeds.</summary>
    public event EventHandler<RecoveryIssuedEventArgs>? RecoveryIssued;

    /// <summary>Raised when the user chooses to be reminded later.</summary>
    public event EventHandler? Deferred;

    public RecoverySetupPromptViewModel()
    {
        SetUpCommand = new RelayCommand(
            execute: param => _ = TrySetUpAsync(param as SecureString),
            canExecute: _ => !IsBusy);

        DeferCommand = new RelayCommand(
            execute: () => Deferred?.Invoke(this, EventArgs.Empty),
            canExecute: () => !IsBusy);
    }

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
                SetUpCommand.RaiseCanExecuteChanged();
                DeferCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand SetUpCommand { get; }
    public RelayCommand DeferCommand { get; }

    private async Task TrySetUpAsync(SecureString? secure)
    {
        if (secure is null || secure.Length == 0)
        {
            ErrorMessage = "Enter your master password to continue.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var plain = SecureStringHelper.ToPlainText(secure);
            RecoverySetupResult result =
                await Task.Run(() => AppServices.VaultManager.SetUpRecovery(plain));
            plain = string.Empty;

            if (!result.Success || result.Code is null)
            {
                ErrorMessage = result.ErrorMessage ?? "Could not set up recovery.";
                return;
            }

            RecoveryIssued?.Invoke(this, new RecoveryIssuedEventArgs(result.Code));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class RecoveryIssuedEventArgs : EventArgs
{
    public RecoveryIssuedEventArgs(string code) => Code = code;

    /// <summary>The plaintext recovery code. Display it, then let it go.</summary>
    public string Code { get; }
}
