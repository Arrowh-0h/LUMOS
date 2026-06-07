using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Lumos.Desktop.ViewModels;

namespace Lumos.Desktop.Views;

public partial class CreateVaultView : UserControl
{
    public CreateVaultView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordField.Focus();
    }

    private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateVaultViewModel vm)
        {
            // Live strength meter. We do pass the plaintext to the VM here —
            // there's no way around it for live scoring. The string is short-lived
            // (one VM method call) and not retained.
            vm.UpdateStrength(PasswordField.Password);

            // Animate the meter width to reflect the score (0..4 -> 0..1).
            var targetScale = vm.StrengthScore * 0.25;
            // Find the strength bar's ScaleTransform.
            if (StrengthBar.RenderTransform is System.Windows.Media.ScaleTransform st)
            {
                var anim = new DoubleAnimation
                {
                    To = targetScale,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
            }

            // Make the bar span the full visual width so the ScaleTransform takes effect.
            StrengthBar.Width = ((FrameworkElement)StrengthBar.Parent).ActualWidth;
        }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateVaultViewModel vm)
        {
            vm.CreateCommand.Execute(
                (PasswordField.SecurePassword, ConfirmField.SecurePassword));
        }
    }

    private void ConfirmField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateButton_Click(sender, new RoutedEventArgs());
        }
    }
}
