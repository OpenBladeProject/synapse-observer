using System.Windows.Input;

namespace SynapseObserver.App;

internal sealed class AsyncCommand(Func<Task> action, Func<bool>? canExecute = null) : ICommand
{
    private bool running;
    public bool IsRunning => running;
    public Task CurrentExecution { get; private set; } = Task.CompletedTask;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        running = true;
        Refresh();
        try
        {
            CurrentExecution = action();
            await CurrentExecution;
        }
        finally
        {
            running = false;
            Refresh();
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
