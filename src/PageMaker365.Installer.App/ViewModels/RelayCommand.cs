using System.Windows.Input;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<bool>? _runningChanged;
    private bool _isRunning;

    public RelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<bool>? runningChanged = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute(), runningChanged)
    {
    }

    public RelayCommand(
        Func<object?, Task> execute,
        Func<object?, bool>? canExecute = null,
        Action<bool>? runningChanged = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _runningChanged = runningChanged;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isRunning = true;
            _runningChanged?.Invoke(true);
            RaiseCanExecuteChanged();
            await _execute(parameter);
        }
        finally
        {
            _isRunning = false;
            _runningChanged?.Invoke(false);
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
