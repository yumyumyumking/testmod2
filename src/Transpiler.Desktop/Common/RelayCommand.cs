using System.Windows.Input;

namespace Transpiler.Desktop.Common;

/// <summary>
/// A standard delegate-based ICommand. CanExecute is re-queried through
/// <see cref="CommandManager.RequerySuggested"/>, so bound buttons enable and
/// disable automatically (e.g. Transpile activates once the file list fills).
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    /// <summary>Forces WPF to re-evaluate CanExecute for all commands.</summary>
    public static void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
