using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lumos.Desktop.ViewModels;

namespace Lumos.Desktop.Views;

public partial class UnlockView : UserControl
{
    public UnlockView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordField.Focus();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is UnlockViewModel vm)
        {
            // SecurePassword is never copied to a managed string here; the VM
            // converts it inside an IntPtr block and zeros the BSTR.
            vm.UnlockCommand.Execute(PasswordField.SecurePassword);
        }
    }

    private void PasswordField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UnlockButton_Click(sender, new RoutedEventArgs());
        }
    }
}
