using System;
using System.Windows.Input;

namespace CashBatch.Desktop;

public class RelayCommand : ICommand
{
    private readonly Func<object?, bool>? _canExecute;
    private readonly Func<object?, Task> _executeAsync;

    public RelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    { _executeAsync = executeAsync; _canExecute = canExecute; }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    // Hook into WPF command routing so the UI auto-updates enable/disable state
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    public async void Execute(object? parameter) => await _executeAsync(parameter);
}
