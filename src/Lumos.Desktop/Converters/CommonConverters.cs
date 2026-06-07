using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lumos.Desktop.Converters;

/// <summary>
/// Converts a byte[] (image file bytes) into a BitmapImage for inline preview.
/// Returns null for null/empty input or if the bytes aren't a decodable image.
/// The image is fully loaded into memory (OnLoad) so we don't hold the stream.
/// </summary>
public sealed class BytesToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a zxcvbn score (0..4) to one of our theme brushes for the meter.
///   0,1 -> Danger
///   2   -> Warning
///   3,4 -> Success
/// </summary>
public sealed class StrengthScoreToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var score = value is int i ? i : 0;
        string key = score switch
        {
            <= 1 => "DangerBrush",
            2 => "WarningBrush",
            _ => "SuccessBrush",
        };
        if (Application.Current?.Resources[key] is Brush b) return b;
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Multiplies a strength score (0..4) by a width-per-step so the meter
/// bar can be sized proportionally. Returns 0 for score 0.
/// </summary>
public sealed class StrengthScoreToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var score = value is int i ? i : 0;
        // Each step covers 25% of the meter; clamp to 0..1.
        return (double)score * 0.25;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a non-empty string to Visibility.Visible, empty/null to Collapsed.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverts a bool. Useful for "show this when NOT busy".
/// </summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>True → Visible, False → Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>False → Visible, True → Collapsed.</summary>
public sealed class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>
/// Two-way converter for radio-button binding to a string property.
/// Convert: returns true if value equals the parameter (one is selected).
/// ConvertBack: returns parameter when bool is true (radio just got selected),
/// returns Binding.DoNothing when false so deselected radios don't clobber the value.
/// </summary>
public sealed class StringEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? (parameter ?? "") : System.Windows.Data.Binding.DoNothing;
}
