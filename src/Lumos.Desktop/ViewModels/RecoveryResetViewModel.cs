using System.Security;
using Lumos.Core.Crypto;
using Lumos.Core.Recovery;
using Lumos.Core.Vault;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// The "I forgot my master password" flow: enter the recovery code, choose a
/// new master password, and get back into the vault.
///
/// The vault database is never re-encrypted. The recovery code unwraps the same
/// cipher key the master password would have, and that key is simply re-wrapped
/// under the new password — so this completes instantly regardless of how much
/// is stored.
/// </summary>
public sealed class RecoveryResetViewModel : ObservableObject
{
    private string _recoveryCode = "";
    private string _errorMessage = "";
    private string _hintMessage = "";
    private bool _isBusy;
    private int _strengthScore;
    private string _strengthLabel = "";

    /// <summary>Raised when the password has been reset and the vault is open.</summary>
    public event EventHandler? ResetSucceeded;

    /// <summary>Raised when the user backs out to the normal unlock screen.</summary>
    public event EventHandler? Cancelled;

    public RecoveryResetViewModel()
    {
        ResetCommand = new RelayCommand(
            execute: param => _ = TryResetAsync(param),
            canExecute: _ => !IsBusy);

        CancelCommand = new RelayCommand(
            execute: () => Cancelled?.Invoke(this, EventArgs.Empty),
            canExecute: () => !IsBusy);
    }

    /// <summary>
    /// Bound two-way to the code textbox. Not a SecureString: the user needs to
    /// see what they are typing to transcribe 30 characters from paper without
    /// errors, and the code is about to be handed to the vault manager as a
    /// plain string regardless.
    /// </summary>
    public string RecoveryCodeText
    {
        get => _recoveryCode;
        set
        {
            if (SetField(ref _recoveryCode, value))
            {
                OnPropertyChanged(nameof(IsCodeWellFormed));
                OnPropertyChanged(nameof(CodeHint));
            }
        }
    }

    /// <summary>Live shape check. Says nothing about whether the code is correct.</summary>
    public bool IsCodeWellFormed => RecoveryCode.IsWellFormed(RecoveryCodeText);

    public string CodeHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RecoveryCodeText)) return "";
            return IsCodeWellFormed
                ? "Code format looks right."
                : $"A recovery code is {RecoveryCode.CharacterCount} characters in six groups of five.";
        }
    }

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
            {
                ResetCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
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

    public RelayCommand ResetCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>Called from the view's PasswordChanged so we can score live.</summary>
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

    private async Task TryResetAsync(object? param)
    {
        if (param is not (SecureString password, SecureString confirmation))
        {
            ErrorMessage = "Internal error: invalid parameter.";
            return;
        }

        if (string.IsNullOrWhiteSpace(RecoveryCodeText))
        {
            ErrorMessage = "Enter your recovery code.";
            return;
        }

        if (password.Length == 0)
        {
            ErrorMessage = "Choose a new master password.";
            return;
        }

        if (!SecureStringHelper.AreEqual(password, confirmation))
        {
            ErrorMessage = "The two new passwords don't match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var plain = SecureStringHelper.ToPlainText(password);
            var code = RecoveryCodeText;

            UnlockResult result = await Task.Run(
                () => AppServices.VaultManager.ResetMasterPasswordWithRecoveryCode(code, plain));
            plain = string.Empty;

            switch (result.Status)
            {
                case UnlockStatus.Success:
                    AppServices.OpenVault = result.Service;
                    RecoveryCodeText = "";
                    ResetSucceeded?.Invoke(this, EventArgs.Empty);
                    break;

                case UnlockStatus.WrongRecoveryCode:
                    ErrorMessage = result.FailedAttemptCount <= 1
                        ? "That recovery code doesn't match this vault."
                        : $"That recovery code doesn't match this vault. ({result.FailedAttemptCount} failed attempts.)";
                    break;

                case UnlockStatus.MalformedRecoveryCode:
                    ErrorMessage = result.ErrorMessage ?? "That doesn't look like a recovery code.";
                    break;

                case UnlockStatus.RecoveryNotConfigured:
                    ErrorMessage =
                        "This vault has no recovery code. It was created before recovery " +
                        "existed, or setup was never completed. Only the master password " +
                        "can open it.";
                    break;

                case UnlockStatus.BackoffRequired:
                    ErrorMessage =
                        $"Too many failed attempts. Try again in " +
                        $"{Math.Ceiling(result.RemainingBackoff.TotalSeconds)}s.";
                    break;

                case UnlockStatus.VaultMissing:
                    ErrorMessage = "Vault file is missing.";
                    break;

                case UnlockStatus.VaultCorrupted:
                    ErrorMessage = $"Vault appears corrupted: {result.ErrorMessage}";
                    break;

                default:
                    ErrorMessage = result.ErrorMessage ?? "Something went wrong.";
                    break;
            }
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
