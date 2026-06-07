using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lumos.Desktop.Common;

/// <summary>
/// Minimal INotifyPropertyChanged base. WPF doesn't ship one and we don't
/// want a heavy MVVM framework dependency yet.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Set <paramref name="field"/> to <paramref name="value"/> and raise
    /// PropertyChanged if the value actually changed. Returns true if changed.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
