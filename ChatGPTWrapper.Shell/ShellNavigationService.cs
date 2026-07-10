using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper.Shell;

public sealed class ShellNavigationService
{
    private AppMode _mode = AppMode.Browse;
    private Guid? _activeAdventureId;
    private string _sessionTitle = string.Empty;

    public AppMode Mode => _mode;

    public Guid? ActiveAdventureId => _activeAdventureId;

    public string SessionTitle => _sessionTitle;

    public event EventHandler<AppModeChangedEventArgs>? AppModeChanged;

    public event EventHandler? SessionChanged;

    public event EventHandler<ShellFocusModeChangedEventArgs>? FocusChatChanged;

    private bool _focusChat;

    public bool FocusChat => _focusChat;

    public bool SetMode(AppMode mode, Guid? adventureId = null, string? sessionTitle = null, string source = "winui")
    {
        if (_mode == mode && _activeAdventureId == adventureId)
            return false;

        var previous = _mode;
        _mode = mode;
        _activeAdventureId = mode is AppMode.Play or AppMode.Design ? adventureId : null;
        _sessionTitle = sessionTitle ?? _sessionTitle;

        if (mode is AppMode.Browse or AppMode.Adventures)
        {
            _sessionTitle = string.Empty;
            SetFocusChat(false, source);
        }

        DiagnosticsLog.Write(
            DiagnosticsChannel.Ui,
            DiagnosticsLevel.Info,
            "app_mode_changed",
            $"{previous} -> {mode}",
            source: source,
            data: new
            {
                previousMode = previous.ToString(),
                mode = mode.ToString(),
                adventureId = _activeAdventureId?.ToString(),
            });

        AppModeChanged?.Invoke(this, new AppModeChangedEventArgs(previous, mode, _activeAdventureId));
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetSessionTitle(string title)
    {
        if (string.Equals(_sessionTitle, title, StringComparison.Ordinal))
            return;

        _sessionTitle = title;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LeaveSession(string source = "winui") =>
        SetMode(AppMode.Adventures, source: source);

    public void SetFocusChat(bool focused, string source = "winui")
    {
        if (_focusChat == focused)
            return;

        _focusChat = focused;
        DiagnosticsLog.Write(
            DiagnosticsChannel.Ui,
            DiagnosticsLevel.Debug,
            "focus_chat_changed",
            focused ? "on" : "off",
            source: source);
        FocusChatChanged?.Invoke(this, new ShellFocusModeChangedEventArgs(focused));
    }
}

public sealed class AppModeChangedEventArgs(AppMode previous, AppMode current, Guid? adventureId) : EventArgs
{
    public AppMode Previous { get; } = previous;

    public AppMode Current { get; } = current;

    public Guid? AdventureId { get; } = adventureId;
}
