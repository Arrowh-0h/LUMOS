using System.Windows;
using System.Windows.Controls;

namespace Lumos.Desktop.Views;

public partial class NeonCard : UserControl
{
    public static readonly DependencyProperty CardContentProperty =
        DependencyProperty.Register(
            nameof(CardContent),
            typeof(object),
            typeof(NeonCard),
            new PropertyMetadata(null));

    public object? CardContent
    {
        get => GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }

    public NeonCard()
    {
        InitializeComponent();
    }
}
