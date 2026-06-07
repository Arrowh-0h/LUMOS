using System.Windows.Input;

namespace Lumos.Desktop.Common;

/// <summary>
/// Standard ICommand implementation: wraps an Action and an optional
/// can-execute Predicate. Call RaiseCanExecuteChanged() when the
/// can-execute condition changes.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : new Predicate<object?>(_ => canExecute())) { }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => _canExecute is null || _canExecute(parameter);

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Generic ICommand variant that passes a typed CommandParameter through to
/// the execute action. Used where a list row needs to pass itself to a
/// parent VM command (e.g. selecting an attachment row).
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => _canExecute is null || _canExecute(parameter is T t ? t : default);

    public void Execute(object? parameter)
        => _execute(parameter is T t ? t : default);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
