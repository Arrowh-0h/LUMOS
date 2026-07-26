using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lumos.Desktop.Views;

/// <summary>
/// A password field with a show/hide toggle.
///
/// WPF's PasswordBox has no plaintext mode and deliberately exposes no bindable
/// Password property, so the only way to offer a reveal button is to keep a
/// TextBox alongside it and swap which one is visible, mirroring the value
/// across as the user types.
///
/// SECURITY NOTE, stated plainly: while revealed, the password exists as a
/// managed string inside the TextBox. .NET strings are immutable and relocated
/// by the garbage collector, so that copy cannot be reliably overwritten and
/// may persist in memory until collected. This is the unavoidable cost of a
/// reveal feature — the same trade every password manager makes — and it is why
/// the field re-masks itself whenever it loses focus rather than staying open.
/// </summary>
public partial class RevealPasswordBox : UserControl
{
    // Guards the two-way mirroring between the masked and revealed fields so
    // that syncing one does not re-trigger a sync back from the other.
    private bool _syncing;
    private bool _isRevealed;

    /// <summary>Raised whenever the value changes, from either field.</summary>
    public event RoutedEventHandler? PasswordChanged;

    /// <summary>Raised on key press in whichever field is active (for Enter handling).</summary>
    public event KeyEventHandler? PasswordKeyDown;

    public RevealPasswordBox()
    {
        InitializeComponent();

        // Re-mask when focus leaves the control entirely. Someone who reveals a
        // password to check a typo should not discover it still on screen when
        // they walk away.
        LostKeyboardFocus += (_, e) =>
        {
            if (_isRevealed && !IsKeyboardFocusWithin) SetRevealed(false);
        };
    }

    /// <summary>The current value as a SecureString. Caller owns and should dispose it.</summary>
    public SecureString SecurePassword => _isRevealed ? BuildSecureString(Revealed.Text)
                                                      : Masked.SecurePassword;

    /// <summary>The current value as plaintext. Used for live strength scoring.</summary>
    public string Password => _isRevealed ? Revealed.Text : Masked.Password;

    /// <summary>Clear the field and re-mask it.</summary>
    public void Clear()
    {
        _syncing = true;
        Masked.Clear();
        Revealed.Clear();
        _syncing = false;
        SetRevealed(false);
    }

    /// <summary>Focus whichever field is currently visible.</summary>
    public void FocusField()
    {
        if (_isRevealed) Revealed.Focus();
        else Masked.Focus();
    }

    private void Masked_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Revealed.Text = Masked.Password;
        _syncing = false;
        PasswordChanged?.Invoke(this, e);
    }

    private void Revealed_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        Masked.Password = Revealed.Text;
        _syncing = false;
        PasswordChanged?.Invoke(this, e);
    }

    private void Field_KeyDown(object sender, KeyEventArgs e) => PasswordKeyDown?.Invoke(this, e);

    private void ToggleButton_Click(object sender, RoutedEventArgs e) => SetRevealed(!_isRevealed);

    private void SetRevealed(bool revealed)
    {
        _isRevealed = revealed;

        Masked.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;
        Revealed.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;

        // E7B3 = RedEye, EF19 = eye with a slash (Segoe MDL2 Assets).
        ToggleGlyph.Text = revealed ? "\uEF19" : "\uE7B3";
        ToggleButton.ToolTip = revealed ? "Hide password" : "Show password";

        if (revealed)
        {
            Revealed.CaretIndex = Revealed.Text.Length;
            Revealed.Focus();
        }
        else
        {
            Masked.Focus();
        }
    }

    private static SecureString BuildSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var ch in value) secure.AppendChar(ch);
        secure.MakeReadOnly();
        return secure;
    }
}
