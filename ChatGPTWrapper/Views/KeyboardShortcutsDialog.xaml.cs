using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.Views;

public partial class KeyboardShortcutsDialog : ShellDialogWindow
{
    private readonly UiChromeSettings _chrome;
    private readonly Action _onShortcutsChanged;
    private readonly List<ShortcutRowViewModel> _rows = [];
    private ShortcutRowViewModel? _recordingRow;

    public KeyboardShortcutsDialog(UiChromeSettings chrome, Action onShortcutsChanged)
    {
        _chrome = chrome;
        _onShortcutsChanged = onShortcutsChanged;
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        LoadRows();
    }

    private void LoadRows()
    {
        _rows.Clear();
        foreach (var group in ShellShortcutCatalog.GroupedForDisplay(_chrome.ShellShortcutOverrides))
        {
            foreach (var shortcut in group)
                _rows.Add(new ShortcutRowViewModel(shortcut, _chrome.ShellShortcutOverrides));
        }

        ShortcutGroups.ItemsSource = ShellShortcutCatalog
            .GroupedForDisplay(_chrome.ShellShortcutOverrides)
            .Select(group => new ShortcutGroupViewModel(
                group.Key,
                _rows.Where(row => row.Category == group.Key).ToList()))
            .ToList();

        ResetAllButton.IsEnabled = _chrome.ShellShortcutOverrides.Count > 0;
    }

    private void ChordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ShortcutRowViewModel row })
            return;

        BeginRecording(row);
    }

    private void ResetShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ShortcutRowViewModel row })
            return;

        if (_recordingRow == row)
            EndRecording();

        if (!_chrome.ShellShortcutOverrides.Remove(row.Id))
            return;

        PersistAndRefresh(row);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (_chrome.ShellShortcutOverrides.Count == 0)
            return;

        EndRecording();
        _chrome.ShellShortcutOverrides.Clear();
        PersistAndRefresh();
    }

    private void BeginRecording(ShortcutRowViewModel row)
    {
        foreach (var candidate in _rows)
        {
            candidate.IsRecording = false;
            candidate.ClearStatus();
        }

        _recordingRow = row;
        row.IsRecording = true;
        row.ClearStatus();
        RecordingBanner.Visibility = Visibility.Visible;
        Focus();
    }

    private void EndRecording()
    {
        if (_recordingRow is not null)
            _recordingRow.IsRecording = false;

        _recordingRow = null;
        RecordingBanner.Visibility = Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingRow is null)
            return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _recordingRow.ClearStatus();
            EndRecording();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (ShellShortcutCatalog.IsModifierOnlyKey(key))
            return;

        var validationError = ShellShortcutCatalog.ValidateBinding(key, modifiers);
        if (validationError is not null)
        {
            _recordingRow.SetError(validationError);
            return;
        }

        var conflict = ShellShortcutCatalog.FindBindingConflict(
            _recordingRow.Id,
            key,
            modifiers,
            PreviewOverrides(key, modifiers));
        if (conflict is not null)
        {
            _recordingRow.SetError($"Conflicts with {conflict.DisplayName}.");
            return;
        }

        var defaultDefinition = ShellShortcutCatalog.TryGetDefault(_recordingRow.Id);
        if (defaultDefinition is not null
            && defaultDefinition.Key == key
            && defaultDefinition.Modifiers == modifiers)
        {
            _chrome.ShellShortcutOverrides.Remove(_recordingRow.Id);
        }
        else
        {
            _chrome.ShellShortcutOverrides[_recordingRow.Id] = ShellShortcutBinding.From(key, modifiers);
        }

        var row = _recordingRow;
        EndRecording();
        PersistAndRefresh(row);
    }

    private Dictionary<string, ShellShortcutBinding> PreviewOverrides(Key key, ModifierKeys modifiers)
    {
        var preview = new Dictionary<string, ShellShortcutBinding>(_chrome.ShellShortcutOverrides);
        preview[_recordingRow!.Id] = ShellShortcutBinding.From(key, modifiers);
        return preview;
    }

    private void PersistAndRefresh(ShortcutRowViewModel? focusRow = null)
    {
        ShellShortcutCatalog.NormalizeOverrides(_chrome.ShellShortcutOverrides);
        UiChromeStore.Save(_chrome);
        _onShortcutsChanged();

        var focusId = focusRow?.Id;
        LoadRows();

        if (focusId is not null)
            _rows.FirstOrDefault(row => row.Id == focusId)?.SetInfo("Shortcut updated.");
    }

    private sealed class ShortcutGroupViewModel(string name, IReadOnlyList<ShortcutRowViewModel> shortcuts)
    {
        public string Name { get; } = name;

        public IReadOnlyList<ShortcutRowViewModel> Shortcuts { get; } = shortcuts;
    }

    private sealed class ShortcutRowViewModel : INotifyPropertyChanged
    {
        private readonly Dictionary<string, ShellShortcutBinding> _overrides;
        private bool _isRecording;
        private string? _statusMessage;
        private bool _isErrorStatus;
        private bool _isWarningStatus;

        public ShortcutRowViewModel(ShellShortcutDefinition shortcut, Dictionary<string, ShellShortcutBinding> overrides)
        {
            _overrides = overrides;
            Id = shortcut.Id;
            DisplayName = shortcut.DisplayName;
            Category = shortcut.Category;
            Key = shortcut.Key;
            Modifiers = shortcut.Modifiers;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public Key Key { get; private set; }

        public ModifierKeys Modifiers { get; private set; }

        public bool IsCustomized => _overrides.ContainsKey(Id);

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (_isRecording == value)
                    return;

                _isRecording = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChordLabel));
            }
        }

        public string ChordLabel =>
            IsRecording
                ? "Press keys…"
                : ShellShortcutCatalog.FormatGesture(Key, Modifiers);

        public string? StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

        public bool IsErrorStatus
        {
            get => _isErrorStatus;
            private set
            {
                _isErrorStatus = value;
                OnPropertyChanged();
            }
        }

        public bool IsWarningStatus
        {
            get => _isWarningStatus;
            private set
            {
                _isWarningStatus = value;
                OnPropertyChanged();
            }
        }

        public void SetError(string message)
        {
            IsErrorStatus = true;
            IsWarningStatus = false;
            StatusMessage = message;
        }

        public void SetInfo(string message)
        {
            IsErrorStatus = false;
            IsWarningStatus = false;
            StatusMessage = message;
        }

        public void ClearStatus()
        {
            IsErrorStatus = false;
            IsWarningStatus = false;
            StatusMessage = null;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
