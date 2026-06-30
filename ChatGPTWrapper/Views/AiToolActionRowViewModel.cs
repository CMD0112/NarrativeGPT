using System.Windows.Input;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public sealed class AiToolActionRowViewModel
{
    private readonly Action _run;

    public AiToolActionRowViewModel(AiToolActionState state, Action run)
    {
        ActionKey = state.ActionKey;
        Title = state.Title;
        Hint = state.Hint;
        IsEnabled = state.IsEnabled;
        DisabledReason = state.DisabledReason ?? string.Empty;
        _run = run;
        RunCommand = new RelayCommand(_ => _run(), _ => IsEnabled);
    }

    public string ActionKey { get; }

    public string Title { get; }

    public string Hint { get; }

    public bool IsEnabled { get; private set; }

    public string DisabledReason { get; private set; }

    public ICommand RunCommand { get; }

    public void Apply(AiToolActionState state)
    {
        IsEnabled = state.IsEnabled;
        DisabledReason = state.DisabledReason ?? string.Empty;
        if (RunCommand is RelayCommand relay)
            relay.RaiseCanExecuteChanged();
    }

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
