using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

/// <summary>Play companion surface only — shell owns splitter and chat column.</summary>
public sealed partial class AdventurePlayPage : UserControl
{
    private WinUiPlaySessionService? _session;
    private ShellNavigationService? _navigation;
    private bool _initialized;
    private PlayLayoutSnapshot _layoutSnapshot = new(
        PlayLayoutContext.Empty(PlayPanelSide.Left),
        PlayLayoutContext.Empty(PlayPanelSide.Right));

    public AdventurePlayPage()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? ReviewRequested;

    public event EventHandler? ManageThreadsRequested;

    public void Bind(WinUiPlaySessionService session, ShellNavigationService navigation)
    {
        _session = session;
        _navigation = navigation;
        Cockpit.Bind(session);
        Companion.Bind(session);
        Footer.Bind(session);

        Cockpit.ReviewRequested += (_, _) => ReviewRequested?.Invoke(this, EventArgs.Empty);
        Cockpit.ManageThreadsRequested += (_, _) => ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

        _navigation.FocusChatChanged += OnFocusChatChanged;
        _session.StatusChanged += (_, _) => RefreshLayout();
    }

    public async Task InitializeAsync(Guid adventureId)
    {
        if (_session is null)
            return;

        await _session.LoadAdventureAsync(adventureId);
        WinUiShellHost.ApplyPlayWorkspaceLayout();
        Companion.RestoreLastTab();
        Cockpit.RestoreSection();
        _initialized = true;
        RefreshLayout();
    }

    internal void RefreshCompanionLayout() => RefreshLayout();

    private void OnFocusChatChanged(object? sender, ShellFocusModeChangedEventArgs e) =>
        WinUiShellHost.ApplyPlayWorkspaceLayout();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized)
            return;

        RefreshLayout();
    }

    private void RefreshLayout()
    {
        var shellWidth = WinUiShellHost.GetCompanionPanelWidth();
        var snapshot = PlayLayoutCoordinator.CreateSnapshot(shellWidth, 0);
        _layoutSnapshot = snapshot;

        if (shellWidth <= 0)
            return;

        Cockpit.ApplyLayout(snapshot.Shell);
        Companion.ApplyLayout(snapshot.Shell);
        Footer.ApplyLayout(snapshot.Shell);
    }
}
