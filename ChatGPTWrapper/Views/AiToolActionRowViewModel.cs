using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public sealed class AiToolActionRowViewModel : INotifyPropertyChanged
{
    private readonly Action _run;
    private bool _isEnabled;
    private string _disabledReason = string.Empty;
    private bool _showBatchSelection;
    private bool _isSelected;

    public AiToolActionRowViewModel(AiToolActionState state, Action run)
    {
        ActionKey = state.ActionKey;
        Title = state.Title;
        Hint = state.Hint;
        _isEnabled = state.IsEnabled;
        _disabledReason = state.DisabledReason ?? string.Empty;
        _run = run;
        RunCommand = new RelayCommand(_ => _run(), _ => IsEnabled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ActionKey { get; }

    public string Title { get; }

    public string Hint { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public string DisabledReason
    {
        get => _disabledReason;
        private set
        {
            if (_disabledReason == value)
                return;

            _disabledReason = value;
            OnPropertyChanged();
        }
    }

    public bool ShowBatchSelection
    {
        get => _showBatchSelection;
        set
        {
            if (_showBatchSelection == value)
                return;

            _showBatchSelection = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public ICommand RunCommand { get; }

    public void Apply(AiToolActionState state, bool showBatchSelection)
    {
        IsEnabled = state.IsEnabled;
        DisabledReason = state.DisabledReason ?? string.Empty;
        ShowBatchSelection = showBatchSelection;
        if (!IsEnabled)
            IsSelected = false;

        if (RunCommand is RelayCommand relay)
            relay.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
