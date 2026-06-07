using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// First-launch gate: the user must enter a valid product key before reaching
/// the vault. Once activated, the key is stored (DPAPI-encrypted) and this
/// screen never appears again on this machine/user.
///
/// This is a "feels official" gate, not anti-piracy (Lumos is free). See
/// docs/DECISIONS.md D-V2-07.
/// </summary>
public sealed class ActivationViewModel : ObservableObject
{
    private readonly LicenseStore _store;
    private string _keyInput = "";
    private string _errorMessage = "";

    public ActivationViewModel(LicenseStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        ActivateCommand = new RelayCommand(Activate, () => !string.IsNullOrWhiteSpace(KeyInput));
    }

    /// <summary>Raised when a valid key is accepted and stored.</summary>
    public event EventHandler? Activated;

    public RelayCommand ActivateCommand { get; }

    public string KeyInput
    {
        get => _keyInput;
        set
        {
            if (SetField(ref _keyInput, value))
            {
                ErrorMessage = "";
                ActivateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>Support email shown on the screen, for requesting a key.</summary>
    public string SupportHint => "Need a key? Email the developer to request one.";

    private void Activate()
    {
        if (_store.Activate(KeyInput))
        {
            ErrorMessage = "";
            Activated?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ErrorMessage = "That key isn't valid. Check it and try again, "
                         + "or contact the developer for a new one.";
        }
    }
}
