using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumos.Desktop.ViewModels;

namespace Lumos.Desktop.Views;

public partial class RecoveryResetView : UserControl
{
    public RecoveryResetView()
    {
        InitializeComponent();
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecoveryResetViewModel vm) return;

        vm.UpdateStrength(PasswordField.Password);

        if (StrengthBar.RenderTransform is ScaleTransform st)
        {
            var anim = new DoubleAnimation
            {
                To = vm.StrengthScore * 0.25,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        }

        StrengthBar.Width = ((FrameworkElement)StrengthBar.Parent).ActualWidth;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryResetViewModel vm)
            vm.ResetCommand.Execute((PasswordField.SecurePassword, ConfirmField.SecurePassword));
    }

    private void ConfirmField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ResetButton_Click(sender, new RoutedEventArgs());
    }
}
