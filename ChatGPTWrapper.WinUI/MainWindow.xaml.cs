using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.WinUI.Controls;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.WinUI.Shell;
using ChatGPTWrapper.WinUI.Theme;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.UI;
using WinRT.Interop;

namespace ChatGPTWrapper.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly ShellNavigationService _navigation = new();
    private readonly ShellStatusService _statusService = new();
    private const double PlaySessionSplitterWidth = 14;
    private const double ExpandCompanionRailWidth = 10;
    private const double DefaultPlayCompanionWidth = 320;
    private const double MinChatColumnWidth = 360;

    private readonly WinUiPlaySessionService _playSession;
    private readonly WinUiPlaySessionBootstrap _playBootstrap;
    private BrowsePage? _browsePage;
    private AdventureDashboardPage? _dashboardPage;
    private AdventurePlayPage? _playPage;
    private AdventureDesignPage? _designPage;
    private AppWindow? _appWindow;
    private bool _suppressAppModeSegment;

    public MainWindow()
    {
        _playSession = new WinUiPlaySessionService(_navigation);
        InitializeComponent();
        WirePlayWorkspaceEvents();
        App.CurrentMainWindow = this;
        _playBootstrap = new WinUiPlaySessionBootstrap(_playSession, ShellChatHost);
        ShellChatHost.Bind(_playSession);
        ConfigureTitleBar();
        TitleBarRow.Loaded += (_, _) => UpdateTitleBarLayout();
        TitleBarRow.SizeChanged += (_, _) => UpdateTitleBarLayout();
        WorkspaceGrid.SizeChanged += (_, _) =>
        {
            if (_navigation.Mode == AppMode.Play)
                ApplyPlayWorkspaceLayout();
        };
        TryEnableMica();
        WireShell();
        WinUiShellHost.Register(this, _playSession, _navigation);
        WpfStaProjectHostBridge.EnsureAdventureTabAsync = EnsureAdventureTabForProjectHostAsync;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void WireShell()
    {
        SessionBar.Bind(_navigation);
        ViewBar.Initialize();
        InitializeAppModeSegment();
        SessionBar.BackRequested += (_, _) => _ = NavigateToAdventuresAsync();
        SessionBar.ReviewRequested += (_, _) => OnReviewRequested();
        SessionBar.LinkRequested += (_, _) => OnSourcesRequested();
        SessionBar.ThreadsRequested += (_, _) => OnThreadsRequested();
        SessionBar.PlaySettingsRequested += (_, _) => OnPlaySettingsRequested();

        ViewBar.FormatRequested += (_, _) => _ = WinUiDialogHostService.ShowFormatDialogAsync(this, _navigation.ActiveAdventureId);
        ViewBar.PreferencesRequested += (_, _) => _ = ShowPreferencesHubAsync();
        ViewBar.AppearanceRequested += (_, _) => _ = WinUiDialogHostService.ShowThemeCustomizationAsync(this);

        _navigation.AppModeChanged += (_, args) => _ = ApplyAppModeAsync(args.Current);
        _navigation.SessionChanged += (_, _) => SyncSessionChrome();
        _navigation.FocusChatChanged += (_, args) => ApplyFocusChatLayout(args.FocusChat);
        _playSession.StatusChanged += (_, _) => SyncSessionChrome();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        _ = InitializeShellAsync();
    }

    private async Task InitializeShellAsync()
    {
        try
        {
            await ShellChatHost.EnsureInitializedAsync();
            ApplyShellLayout(AppMode.Browse);

            _browsePage = new BrowsePage();
            ContentFrame.Content = _browsePage;

            _navigation.SetMode(AppMode.Browse);
            SyncAppModeSegmentSelection();
            ViewBar.ApplyShellMode(AppMode.Browse);
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("shell_init", ex);
            WinUiEventLogger.Error("shell_init_failed", ex.Message, new { exceptionType = ex.GetType().Name });
        }
    }

    private void ApplyFocusChatLayout(bool focusChat)
    {
        if (_navigation.Mode is not (AppMode.Play or AppMode.Design))
            return;

        if (focusChat)
            ApplyPlayWorkspaceLayout();
        else
            ApplyShellLayout(_navigation.Mode);
    }

    private void ApplyShellLayout(AppMode mode)
    {
        HidePlayWorkspaceChrome();

        switch (mode)
        {
            case AppMode.Adventures:
                CompanionColumn.Width = new GridLength(1, GridUnitType.Star);
                CompanionColumn.MinWidth = 0;
                CompanionColumn.MaxWidth = double.PositiveInfinity;
                WorkspaceSplitterColumn.Width = new GridLength(0);
                ChatColumn.Width = new GridLength(0);
                ChatColumn.MinWidth = 0;
                ShellChatHost.Visibility = Visibility.Collapsed;
                ContentFrame.Visibility = Visibility.Visible;
                break;
            case AppMode.Browse:
                CompanionColumn.Width = new GridLength(0);
                CompanionColumn.MinWidth = 0;
                WorkspaceSplitterColumn.Width = new GridLength(0);
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                ChatColumn.MinWidth = MinChatColumnWidth;
                ShellChatHost.Visibility = Visibility.Visible;
                ContentFrame.Visibility = Visibility.Collapsed;
                break;
            case AppMode.Play:
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                ChatColumn.MinWidth = MinChatColumnWidth;
                ShellChatHost.Visibility = Visibility.Visible;
                ApplyPlayWorkspaceLayout();
                break;
            default:
                CompanionColumn.Width = new GridLength(420, GridUnitType.Pixel);
                CompanionColumn.MinWidth = 280;
                CompanionColumn.MaxWidth = double.PositiveInfinity;
                WorkspaceSplitterColumn.Width = new GridLength(0);
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                ChatColumn.MinWidth = MinChatColumnWidth;
                ShellChatHost.Visibility = Visibility.Visible;
                ContentFrame.Visibility = Visibility.Visible;
                break;
        }
    }

    internal double GetShellBodyWidth()
    {
        var width = WorkspaceGrid.ActualWidth;
        if (width > 0)
            return width;

        width = RootGrid.ActualWidth - 24;
        return width > 0 ? width : MinChatColumnWidth + DefaultPlayCompanionWidth;
    }

    private async Task ApplyAppModeAsync(AppMode mode)
    {
        if (Content.XamlRoot is { } root
            && _navigation.Mode == AppMode.Play
            && mode != AppMode.Play
            && !await WinUiPlayNavigationGuard.ConfirmLeavePlayAsync(_navigation, _playSession, root))
        {
            return;
        }

        SessionBar.Visibility = mode is AppMode.Play or AppMode.Design
            ? Visibility.Visible
            : Visibility.Collapsed;

        AppModeSegment.IsEnabled = mode is AppMode.Browse or AppMode.Adventures;
        SyncAppModeSegmentSelection();
        ViewBar.ApplyShellMode(mode);
        ShellStatusText.Text = mode switch
        {
            AppMode.Adventures => "Adventure library",
            AppMode.Play => "Play session",
            AppMode.Design => "Design session",
            _ => "Browse chat",
        };

        ApplyShellLayout(mode);

        if (mode is AppMode.Play && _navigation.ActiveAdventureId is { } playId)
        {
            _playPage ??= CreatePlayPage();
            ContentFrame.Content = _playPage;
            await _playPage.InitializeAsync(playId);
            ApplyPlayWorkspaceLayout();
            await _playBootstrap.EnterPlayAsync(playId);
        }
        else if (mode is AppMode.Design && _navigation.ActiveAdventureId is { } designId)
        {
            _designPage ??= CreateDesignPage();
            ContentFrame.Content = _designPage;
            await _designPage.InitializeAsync(designId);
        }
        else if (mode is AppMode.Adventures)
        {
            _dashboardPage ??= CreateDashboardPage();
            ContentFrame.Content = _dashboardPage;
            await _dashboardPage.RefreshAsync();
        }
        else if (mode is AppMode.Browse)
        {
            _browsePage ??= new BrowsePage();
            ContentFrame.Content = _browsePage;
        }

        SyncSessionChrome();
    }

    internal async Task NavigateToAdventuresAsync()
    {
        if (Content.XamlRoot is { } root
            && !await WinUiPlayNavigationGuard.ConfirmLeavePlayAsync(_navigation, _playSession, root))
        {
            return;
        }

        _navigation.LeaveSession();
        await ApplyAppModeAsync(AppMode.Adventures);
    }

    internal ChatTabHost ShellChatHostControl => ShellChatHost;

    internal ChatTabHost? GetSessionChatHost() => ShellChatHost;

    internal void RefreshSessionChromeFromHost() => SyncSessionChrome();

    internal void SetUtilityJobCount(int count)
    {
        _statusService.SetActiveJobCount(count);
        SyncSessionChrome();
    }

    private static async Task EnsureAdventureTabForProjectHostAsync(Guid adventureId, bool selectTab)
    {
        await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var host = WinUiShellHost.GetSessionChatHost();
            var webView = host?.GetActiveWebView() ?? host?.GetFirstWebView();
            if (webView is null)
                return;

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
            var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
            if (string.IsNullOrWhiteSpace(gizmoId))
                return;

            var url = ChatGptUrls.BuildProjectUrl(ChatGptUrls.NormalizeGizmoId(gizmoId));
            await host!.NavigateAsync(webView, new Uri(url));
            if (selectTab)
                host.SelectWebView(webView);
        });
    }

    private void SyncSessionChrome()
    {
        SessionBar.ApplyStatus(_statusService.BuildSnapshot(_navigation.ActiveAdventureId));
    }

    private void InitializeAppModeSegment()
    {
        AppModeSegment.ItemsSource = new List<object>
        {
            new SegmentedItemModel { Content = "Browse", Tag = AppMode.Browse },
            new SegmentedItemModel { Content = "Adventures", Tag = AppMode.Adventures },
        };
    }

    private void SyncAppModeSegmentSelection()
    {
        _suppressAppModeSegment = true;
        try
        {
            AppModeSegment.SelectedIndex = _navigation.Mode switch
            {
                AppMode.Browse => 0,
                AppMode.Adventures => 1,
                _ => -1,
            };
        }
        finally
        {
            _suppressAppModeSegment = false;
        }
    }

    private async void AppModeSegment_SelectionChanged(object sender, EventArgs e)
    {
        if (_suppressAppModeSegment || AppModeSegment.SelectedTag is not AppMode mode)
            return;

        switch (mode)
        {
            case AppMode.Browse:
                _navigation.SetMode(AppMode.Browse);
                _browsePage ??= new BrowsePage();
                ContentFrame.Content = _browsePage;
                ApplyShellLayout(AppMode.Browse);
                ViewBar.ApplyShellMode(AppMode.Browse);
                break;
            case AppMode.Adventures:
                _navigation.SetMode(AppMode.Adventures);
                _dashboardPage ??= CreateDashboardPage();
                ContentFrame.Content = _dashboardPage;
                await _dashboardPage.RefreshAsync();
                ApplyShellLayout(AppMode.Adventures);
                ViewBar.ApplyShellMode(AppMode.Adventures);
                break;
        }

        ShellStatusText.Text = mode == AppMode.Adventures ? "Adventure library" : "Browse chat";
    }

    private AdventureDashboardPage CreateDashboardPage()
    {
        var page = new AdventureDashboardPage();
        page.PlayRequested += (_, id) =>
        {
            _navigation.SetMode(AppMode.Play, id, "Adventure");
        };
        page.DesignWithAiRequested += async (_, _) =>
            await WpfDialogHostService.ShowDesignWizardAsync(this);
        page.ContinueDesignRequested += async (_, id) =>
        {
            await WpfDialogHostService.ShowDesignWizardAsync(this, id);
            if (_navigation.Mode == AppMode.Adventures)
                await _dashboardPage!.RefreshAsync();
        };
        return page;
    }

    private AdventurePlayPage CreatePlayPage()
    {
        var page = new AdventurePlayPage();
        page.Bind(_playSession, _navigation);
        page.ReviewRequested += (_, _) => OnReviewRequested();
        page.ManageThreadsRequested += (_, _) => OnThreadsRequested();
        return page;
    }

    private AdventureDesignPage CreateDesignPage()
    {
        var page = new AdventureDesignPage();
        page.Bind(_playSession);
        page.LaunchPlayRequested += (_, _) =>
        {
            if (_navigation.ActiveAdventureId is { } id)
                _navigation.SetMode(AppMode.Play, id, _navigation.SessionTitle);
        };
        page.ManageThreadsRequested += (_, _) => OnThreadsRequested();
        return page;
    }

    private void OnReviewRequested()
    {
        if (_navigation.ActiveAdventureId is { } id)
            _ = WinUiDialogHostService.ShowProposalReviewAsync(this, id);
    }

    private void OnSourcesRequested()
    {
        if (_navigation.ActiveAdventureId is { } id)
            _ = WinUiDialogHostService.ShowSourceManagerAsync(this, id);
    }

    private void OnThreadsRequested()
    {
        if (_navigation.ActiveAdventureId is { } id)
            _ = WinUiDialogHostService.ShowThreadManagerAsync(this, id);
    }

    private void OnPlaySettingsRequested()
    {
        if (_navigation.ActiveAdventureId is { } id)
            _ = PlaySettingsDialog.ShowAsync(id);
    }

    public async Task ApplyShellRefreshAsync(bool refreshWebView = true)
    {
        ThemeApplicationService.InvalidateApplyCache();
        WinUiThemeApplication.ApplyStartupTheme();
        RefreshShellChromeFromThemeChange();

        if (!refreshWebView)
            return;

        await RefreshWebViewThemesAsync();
    }

    internal void RefreshShellChromeFromThemeChange()
    {
        ApplyShellChromeResources();
        RefreshChromeSegments();
        ViewBar.ResyncFromStore();
        SyncSessionChrome();
    }

    private void ApplyShellChromeResources()
    {
        var resources = Application.Current.Resources;

        RootGrid.Background = GetThemeBrush(resources, "BgBaseBrush");
        ShellChromeHost.Background = GetThemeBrush(resources, "BgChromeBrush");
        ShellChromeHost.BorderBrush = GetThemeBrush(resources, "BorderSubtleBrush");
        ShellStatusHost.Background = GetThemeBrush(resources, "BgChromeBrush");
        ShellStatusHost.BorderBrush = GetThemeBrush(resources, "BorderSubtleBrush");
        ShellStatusText.Foreground = GetThemeBrush(resources, "TextMutedBrush");

        if (resources.TryGetValue("FontSizeHint", out var fontSize) && fontSize is double hintSize)
            ShellStatusText.FontSize = hintSize;
    }

    private static Brush GetThemeBrush(ResourceDictionary resources, string key) =>
        resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.Transparent);

    internal async Task RefreshWebViewThemesAsync()
    {
        await ShellChatHost.RefreshThemeOnAllTabsAsync();
    }

    internal async Task ApplyTranscriptViewModeAsync()
    {
        await ShellChatHost.ApplyTranscriptViewModeAsync();
        SyncSessionChrome();
    }

    private void RefreshChromeSegments()
    {
        AppModeSegment.RefreshVisuals();
        ViewBar.RefreshVisuals();
        SessionBar.RefreshVisuals();
    }

    private async Task ShowPreferencesHubAsync()
    {
        var isDesignMode = _navigation.Mode == AppMode.Design;
        var hub = new PreferencesHubPage(_navigation.ActiveAdventureId, isDesignMode);
        WinUiShellDialogHostWindow? hostWindow = null;

        hub.ConfigureSubDialogOpener(async openTask =>
        {
            hostWindow?.CloseDialog(null);
            await openTask;
            await ApplyShellRefreshAsync();
        });

        await WinUiDialogService.ShowWorkbenchAsync(
            this,
            "Preferences",
            hub,
            layoutKey: "PreferencesHubDialog",
            designWidth: 640,
            designHeight: 720,
            configure: window =>
            {
                hostWindow = window;
                WinUiDialogService.AddCloseFooter(window);
            });

        await ApplyShellRefreshAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args) =>
        DiagnosticsSession.WriteExtendedShutdown();

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "ChatGPT Wrapper";
        TrySetWindowIcon();
        ApplyTitleBarColors();
    }

    private void ApplyTitleBarColors()
    {
        if (_appWindow is null || !AppWindowTitleBar.IsCustomizationSupported())
            return;

        var titleBar = _appWindow.TitleBar;
        var chrome = Color.FromArgb(255, 0x23, 0x23, 0x28);
        var hover = Color.FromArgb(255, 0x32, 0x32, 0x3A);
        var pressed = Color.FromArgb(255, 0x3A, 0x3A, 0x44);
        var muted = Color.FromArgb(255, 0x98, 0x98, 0xA4);
        var primary = Color.FromArgb(255, 0xED, 0xED, 0xF0);

        titleBar.BackgroundColor = chrome;
        titleBar.ForegroundColor = muted;
        titleBar.InactiveBackgroundColor = chrome;
        titleBar.InactiveForegroundColor = muted;
        titleBar.ButtonBackgroundColor = chrome;
        titleBar.ButtonInactiveBackgroundColor = chrome;
        titleBar.ButtonForegroundColor = muted;
        titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = primary;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = primary;
    }

    private void UpdateTitleBarLayout()
    {
        if (_appWindow is null || !ExtendsContentIntoTitleBar)
            return;

        var scale = GetWindowScaleAdjustment();
        var titleBar = _appWindow.TitleBar;
        TitleBarLeftPaddingColumn.Width = new GridLength(titleBar.LeftInset / scale);
        TitleBarRightPaddingColumn.Width = new GridLength(titleBar.RightInset / scale);
    }

    private double GetWindowScaleAdjustment()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96.0;
    }

    private void TrySetWindowIcon()
    {
        if (_appWindow is null)
            return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath))
            return;

        try
        {
            _appWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("window_icon", ex);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(nint hWnd);

    private void TryEnableMica()
    {
        if (!Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            return;

        SystemBackdrop = new MicaBackdrop();
        RootGrid.Background = new SolidColorBrush(Colors.Transparent);
    }

    private void WirePlayWorkspaceEvents()
    {
        PlaySplitterHost.PointerPressed += PlaySplitterHost_PointerPressed;
        PlaySplitterHost.PointerMoved += PlaySplitterHost_PointerMoved;
        PlaySplitterHost.PointerReleased += PlaySplitterHost_PointerReleased;
        PlaySplitterHost.PointerCanceled += PlaySplitterHost_PointerReleased;
        PlaySplitterHost.DoubleTapped += PlaySplitterHost_DoubleTapped;
        ExpandCompanionRail.Click += ExpandCompanionRail_Click;
        ExpandCompanionRail.DoubleTapped += ExpandCompanionRail_DoubleTapped;
    }

    private bool _companionSplitterDragging;
    private double _companionSplitterLastX;

    internal void ApplyPlayWorkspaceLayout()
    {
        if (_navigation.Mode != AppMode.Play || _playSession.CurrentBundle is null)
        {
            HidePlayWorkspaceChrome();
            return;
        }

        if (_navigation.FocusChat)
        {
            CompanionColumn.Width = new GridLength(0);
            CompanionColumn.MinWidth = 0;
            CompanionColumn.MaxWidth = double.PositiveInfinity;
            WorkspaceSplitterColumn.Width = new GridLength(0);
            PlaySplitterHost.Visibility = Visibility.Collapsed;
            ExpandCompanionRail.Visibility = Visibility.Collapsed;
            ContentFrame.Visibility = Visibility.Collapsed;
            return;
        }

        var collapsed = _playSession.ResolveSidePanelCollapsed();
        if (collapsed)
        {
            CompanionColumn.Width = new GridLength(0);
            CompanionColumn.MinWidth = 0;
            CompanionColumn.MaxWidth = double.PositiveInfinity;
            WorkspaceSplitterColumn.Width = new GridLength(ExpandCompanionRailWidth);
            PlaySplitterHost.Visibility = Visibility.Collapsed;
            ExpandCompanionRail.Visibility = Visibility.Visible;
            ContentFrame.Visibility = Visibility.Collapsed;
        }
        else
        {
            var width = ClampCompanionWidth(_playSession.ResolveSidePanelWidth(DefaultPlayCompanionWidth));
            CompanionColumn.Width = new GridLength(width, GridUnitType.Pixel);
            CompanionColumn.MinWidth = PlayPanelOptimalWidthCalculator.MinLeftWidth;
            CompanionColumn.MaxWidth = PlayPanelOptimalWidthCalculator.MaxLeftWidth;
            WorkspaceSplitterColumn.Width = new GridLength(PlaySessionSplitterWidth);
            PlaySplitterHost.Visibility = Visibility.Visible;
            ExpandCompanionRail.Visibility = Visibility.Collapsed;
            ContentFrame.Visibility = Visibility.Visible;
        }

        _playPage?.RefreshCompanionLayout();
    }

    internal double GetCompanionPanelWidth()
    {
        if (_navigation.Mode != AppMode.Play || _navigation.FocusChat)
            return 0;

        if (_playSession.ResolveSidePanelCollapsed())
            return 0;

        if (CompanionColumn.Width.IsAbsolute)
            return CompanionColumn.Width.Value;

        return DefaultPlayCompanionWidth;
    }

    internal void SyncPlayCompanionWidth(double companionWidth, bool collapsed)
    {
        if (_navigation.Mode != AppMode.Play)
            return;

        if (collapsed)
            _playSession.SetSidePanelCollapsed(true);
        else
            _playSession.SaveSidePanelWidth(ClampCompanionWidth(companionWidth));

        ApplyPlayWorkspaceLayout();
    }

    private double ClampCompanionWidth(double width)
    {
        var available = WorkspaceGrid.ActualWidth;
        if (available <= 0)
            available = GetShellBodyWidth();

        if (available <= 0)
            return Math.Clamp(width, PlayPanelOptimalWidthCalculator.MinLeftWidth, PlayPanelOptimalWidthCalculator.MaxLeftWidth);

        var reserved = MinChatColumnWidth + PlaySessionSplitterWidth + ExpandCompanionRailWidth + 16;
        var max = Math.Clamp(
            Math.Min(PlayPanelOptimalWidthCalculator.MaxLeftWidth, available - reserved),
            PlayPanelOptimalWidthCalculator.MinLeftWidth,
            PlayPanelOptimalWidthCalculator.MaxLeftWidth);
        return Math.Clamp(width, PlayPanelOptimalWidthCalculator.MinLeftWidth, max);
    }

    private void HidePlayWorkspaceChrome()
    {
        PlaySplitterHost.Visibility = Visibility.Collapsed;
        ExpandCompanionRail.Visibility = Visibility.Collapsed;
        WorkspaceSplitterColumn.Width = new GridLength(0);
    }

    private void ExpandCompanionRail_Click(object sender, RoutedEventArgs e)
    {
        _playSession.SetSidePanelCollapsed(false);
        ApplyPlayWorkspaceLayout();
    }

    private void ExpandCompanionRail_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        SnapCompanionToOptimalWidth();

    private void CollapseCompanionButton_Click(object sender, RoutedEventArgs e) =>
        SyncPlayCompanionWidth(GetCompanionPanelWidth(), collapsed: true);

    private void PlaySplitterHost_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Brush)Application.Current.Resources["AccentSubtleBrush"];
    }

    private void PlaySplitterHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_companionSplitterDragging)
            return;
        if (sender is Border border)
            border.Background = (Brush)Application.Current.Resources["BgElevatedBrush"];
    }

    private void PlaySplitterHost_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        SnapCompanionToOptimalWidth();

    private void SnapCompanionToOptimalWidth()
    {
        if (_playSession.CurrentBundle is not { } bundle)
            return;

        if (bundle.Metadata.Settings.PlaySidePanelCollapsed)
            _playSession.SetSidePanelCollapsed(false);

        var optimal = PlayPanelOptimalWidthCalculator.Resolve(
            bundle.Metadata.Settings,
            ClampCompanionWidth(PlayPanelOptimalWidthCalculator.MaxLeftWidth),
            PlayPanelOptimalWidthCalculator.MaxRightWidth);

        _playSession.SaveSidePanelWidth(ClampCompanionWidth(optimal.LeftWidth));
        ApplyPlayWorkspaceLayout();
    }

    private void PlaySplitterHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_playSession.ResolveSidePanelCollapsed())
            return;

        _companionSplitterDragging = true;
        _companionSplitterLastX = e.GetCurrentPoint(WorkspaceGrid).Position.X;
        PlaySplitterHost.CapturePointer(e.Pointer);
        WorkspaceGrid.PointerMoved += WorkspaceGrid_CompanionSplitterPointerMoved;
        WorkspaceGrid.PointerReleased += WorkspaceGrid_CompanionSplitterPointerReleased;
        WorkspaceGrid.PointerCanceled += WorkspaceGrid_CompanionSplitterPointerReleased;
        e.Handled = true;
    }

    private void WorkspaceGrid_CompanionSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_companionSplitterDragging || !e.Pointer.IsInContact)
            return;

        var x = e.GetCurrentPoint(WorkspaceGrid).Position.X;
        var delta = x - _companionSplitterLastX;
        if (Math.Abs(delta) < 0.01)
            return;

        _companionSplitterLastX = x;
        var current = CompanionColumn.Width.IsAbsolute
            ? CompanionColumn.Width.Value
            : DefaultPlayCompanionWidth;
        var next = ClampCompanionWidth(current + delta);
        if (Math.Abs(next - current) < 0.01)
            return;

        CompanionColumn.Width = new GridLength(next, GridUnitType.Pixel);
        _playPage?.RefreshCompanionLayout();
        e.Handled = true;
    }

    private void WorkspaceGrid_CompanionSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_companionSplitterDragging)
            return;

        EndCompanionSplitterDrag(e.Pointer);
        e.Handled = true;
    }

    private void PlaySplitterHost_PointerMoved(object sender, PointerRoutedEventArgs e) =>
        WorkspaceGrid_CompanionSplitterPointerMoved(sender, e);

    private void PlaySplitterHost_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        WorkspaceGrid_CompanionSplitterPointerReleased(sender, e);

    private void EndCompanionSplitterDrag(Pointer pointer)
    {
        _companionSplitterDragging = false;
        WorkspaceGrid.PointerMoved -= WorkspaceGrid_CompanionSplitterPointerMoved;
        WorkspaceGrid.PointerReleased -= WorkspaceGrid_CompanionSplitterPointerReleased;
        WorkspaceGrid.PointerCanceled -= WorkspaceGrid_CompanionSplitterPointerReleased;
        PlaySplitterHost.ReleasePointerCapture(pointer);

        if (_playSession.ResolveSidePanelCollapsed())
            return;

        var width = CompanionColumn.Width.IsAbsolute
            ? CompanionColumn.Width.Value
            : DefaultPlayCompanionWidth;
        _playSession.SaveSidePanelWidth(ClampCompanionWidth(width));
        ApplyPlayWorkspaceLayout();
    }
}
