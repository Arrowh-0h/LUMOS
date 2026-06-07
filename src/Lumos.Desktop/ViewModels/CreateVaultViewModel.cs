using System.Security;
using Lumos.Core.Crypto;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

public sealed class CreateVaultViewModel : ObservableObject
{
    private string _errorMessage = "";
    private string _hintMessage = "";
    private bool _isBusy;
    private int _strengthScore;       // 0..4 from zxcvbn
    private string _strengthLabel = "";

    public event EventHandler? VaultCreated;

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

        if (!SecureStringsEqual(password, confirmation))
        {
            ErrorMessage = "The two master passwords don't match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var plain = SecureStringToString(password);
            var validation = MasterPasswordPolicy.Validate(plain);
            if (!validation.IsAllowed)
            {
                ErrorMessage = validation.Message ?? "Password rejected.";
                return;
            }

            await Task.Run(() =>
            {
                var service = AppServices.VaultManager.CreateVault(plain);
                AppServices.OpenVault = service;
            });
            plain = string.Empty;

            VaultCreated?.Invoke(this, EventArgs.Empty);
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

    private static bool SecureStringsEqual(SecureString a, SecureString b)
    {
        if (a.Length != b.Length) return false;
        var bstrA = IntPtr.Zero;
        var bstrB = IntPtr.Zero;
        try
        {
            bstrA = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(a);
            bstrB = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(b);
            for (int i = 0; i < a.Length; i++)
            {
                var charA = System.Runtime.InteropServices.Marshal.ReadInt16(bstrA, i * 2);
                var charB = System.Runtime.InteropServices.Marshal.ReadInt16(bstrB, i * 2);
                if (charA != charB) return false;
            }
            return true;
        }
        finally
        {
            if (bstrA != IntPtr.Zero) System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstrA);
            if (bstrB != IntPtr.Zero) System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstrB);
        }
    }
}
