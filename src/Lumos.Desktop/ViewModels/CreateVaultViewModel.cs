using System.Security;
using Lumos.Core.Crypto;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>Carries the recovery code issued alongside a newly created vault.</summary>
public sealed class VaultCreatedEventArgs : EventArgs
{
    public VaultCreatedEventArgs(string? recoveryCode) => RecoveryCode = recoveryCode;

    /// <summary>
    /// The one-time recovery code, or null if issuing it failed. Null is not
    /// fatal — the vault exists and works; the user is simply prompted again
    /// at their next unlock.
    /// </summary>
    public string? RecoveryCode { get; }
}

public sealed class CreateVaultViewModel : ObservableObject
{
    private string _errorMessage = "";
    private string _hintMessage = "";
    private bool _isBusy;
    private int _strengthScore;       // 0..4 from zxcvbn
    private string _strengthLabel = "";

    public event EventHandler<VaultCreatedEventArgs>? VaultCreated;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string HintMessage
    {
        get => _hintMessage;
        private set => SetField(ref _hintMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                CreateCommand.RaiseCanExecuteChanged();
        }
    }

    public int StrengthScore
    {
        get => _strengthScore;
        private set => SetField(ref _strengthScore, value);
    }

    public string StrengthLabel
    {
        get => _strengthLabel;
        private set => SetField(ref _strengthLabel, value);
    }

    public RelayCommand CreateCommand { get; }

    public CreateVaultViewModel()
    {
        CreateCommand = new RelayCommand(
            execute: param => _ = TryCreateAsync(param),
            canExecute: _ => !IsBusy);
    }

    /// <summary>
    /// Called from the view's PasswordChanged event so we can score the
    /// password live without holding a reference to a SecureString in the VM.
    /// </summary>
    public void UpdateStrength(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            StrengthScore = 0;
            StrengthLabel = "";
            HintMessage = "";
            return;
        }

        var validation = MasterPasswordPolicy.Validate(candidate);
        StrengthScore = validation.Score ?? 0;
        StrengthLabel = StrengthScore switch
        {
            0 => "Very weak",
            1 => "Weak",
            2 => "Fair",
            3 => "Strong",
            4 => "Very strong",
            _ => "",
        };

        if (!validation.IsAllowed)
            HintMessage = validation.Message ?? "";
        else if (validation.IsWeak)
            HintMessage = "Lumos will accept this, but a stronger password is recommended.";
        else
            HintMessage = "";
    }

    private async Task TryCreateAsync(object? param)
    {
        if (param is not (SecureString password, SecureString confirmation))
        {
            ErrorMessage = "Internal error: invalid parameter.";
            return;
        }

        if (password.Length == 0)
        {
            ErrorMessage = "Enter a master password.";
            return;
        }

        if (!SecureStringHelper.AreEqual(password, confirmation))
        {
            ErrorMessage = "The two master passwords don't match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var plain = SecureStringHelper.ToPlainText(password);
            var validation = MasterPasswordPolicy.Validate(plain);
            if (!validation.IsAllowed)
            {
                ErrorMessage = validation.Message ?? "Password rejected.";
                return;
            }

            // Create the vault, then immediately issue a recovery code while we
            // still hold the master password. Doing both here means the plain
            // text never has to survive a screen transition.
            string? recoveryCode = null;
            await Task.Run(() =>
            {
                var service = AppServices.VaultManager.CreateVault(plain);
                AppServices.OpenVault = service;

                // Best-effort: a vault without a recovery code is still a
                // perfectly usable vault, so a failure here must not undo a
                // successful creation. The user gets prompted again on their
                // next unlock.
                var setup = AppServices.VaultManager.SetUpRecovery(plain);
                if (setup.Success) recoveryCode = setup.Code;
            });
            plain = string.Empty;

            VaultCreated?.Invoke(this, new VaultCreatedEventArgs(recoveryCode));
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
