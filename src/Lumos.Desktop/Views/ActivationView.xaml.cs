using System.Windows.Controls;
using System.Windows.Input;
using Lumos.Desktop.ViewModels;

namespace Lumos.Desktop.Views;

public partial class ActivationView : UserControl
{
    public ActivationView()
    {
        InitializeComponent();
        Loaded += (_, _) => KeyField.Focus();
    }

    private void KeyField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ActivationViewModel vm)
        {
            if (vm.ActivateCommand.CanExecute(null))
                vm.ActivateCommand.Execute(null);
        }
    }
}
