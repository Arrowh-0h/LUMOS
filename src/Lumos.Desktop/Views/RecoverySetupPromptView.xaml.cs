using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lumos.Desktop.ViewModels;

namespace Lumos.Desktop.Views;

public partial class RecoverySetupPromptView : UserControl
{
    public RecoverySetupPromptView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordField.FocusField();
    }

    private void SetUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoverySetupPromptViewModel vm)
            vm.SetUpCommand.Execute(PasswordField.SecurePassword);
    }

    private void PasswordField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SetUpButton_Click(sender, new RoutedEventArgs());
    }
}
