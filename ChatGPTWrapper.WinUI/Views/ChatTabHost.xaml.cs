using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.WinUI.WebView;

using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Controls;

using Microsoft.Web.WebView2.Core;



namespace ChatGPTWrapper.WinUI.Views;



/// <summary>

/// Shell-owned multi-tab ChatGPT host shared across Browse, Play, and Design.

/// Each tab is wired through <see cref="WinUiShellTabService"/> (WinRT script injection).

/// </summary>

public sealed partial class ChatTabHost : UserControl

{

    private readonly Dictionary<TabViewItem, WebView2> _tabWebViews = new();

    private WinUiPlaySessionService? _session;

    private bool _initialized;



    public ChatTabHost()

    {

        InitializeComponent();

        WebViewHost.SizeChanged += (_, _) => LayoutActiveWebView();

    }



    public void Bind(WinUiPlaySessionService? session)

    {

        _session = session;

        if (session is not null)

            session.BindTabHost(this);

    }



    public WebView2? GetActiveWebView()

    {

        if (TabStrip.SelectedItem is TabViewItem tab && _tabWebViews.TryGetValue(tab, out var webView))

            return webView;



        return _tabWebViews.Values.FirstOrDefault();

    }



    public WebView2? GetFirstWebView() => _tabWebViews.Values.FirstOrDefault();



    public IEnumerable<WebView2> EnumerateWebViews() => _tabWebViews.Values;



    public WebView2? FindWebViewByPinKey(string? pinKey)

    {

        if (string.IsNullOrWhiteSpace(pinKey))

            return null;



        foreach (var (tab, webView) in _tabWebViews)

        {

            var key = tab.Tag as string;

            if (string.Equals(key, pinKey, StringComparison.OrdinalIgnoreCase))

                return webView;

        }



        return null;

    }



    public TabViewItem? FindTabForWebView(WebView2 webView) =>

        _tabWebViews.FirstOrDefault(pair => ReferenceEquals(pair.Value, webView)).Key;



    public void SelectWebView(WebView2 webView)

    {

        if (FindTabForWebView(webView) is { } tab)

            TabStrip.SelectedItem = tab;

        else

            LayoutActiveWebView();

    }



    public IReadOnlyList<(string Header, WebView2 WebView)> ListTabs() =>

        _tabWebViews

            .Select(pair => (pair.Key.Header?.ToString() ?? "Tab", pair.Value))

            .ToList();



    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)

    {

        if (_initialized)

            return;



        _initialized = true;

        WinUiEventLogger.Debug("chat_tab_init_start", "Initializing shell WebView2 host");

        await EnsureDefaultTabAsync(cancellationToken);

    }



    public async Task<WebView2?> EnsureDefaultTabAsync(CancellationToken cancellationToken = default)

    {

        if (_tabWebViews.Count > 0)

            return GetFirstWebView();



        return await AddTabAsync("ChatGPT", cancellationToken: cancellationToken);

    }



    public async Task<WebView2> AddTabAsync(

        string header,

        Uri? uri = null,

        CancellationToken cancellationToken = default)

    {

        var tab = new TabViewItem

        {

            Header = header,

            IsClosable = TabStrip.TabItems.Count > 0,

            Tag = Guid.NewGuid().ToString("N"),

        };

        var webView = CreateWebView();

        WireNavigationDiagnostics(webView);

        _tabWebViews[tab] = webView;



        tab.SizeChanged += (_, _) => LayoutActiveWebView();

        TabStrip.TabItems.Add(tab);

        TabStrip.SelectedItem = tab;



        WinUiEventLogger.Debug(

            "chat_tab_added",

            "ChatGPT tab opened",

            new { header, uri = uri?.ToString() ?? "https://chatgpt.com/" });



        await EnsureWebViewReadyAsync(webView, cancellationToken);

        webView.Source = uri ?? new Uri("https://chatgpt.com/");

        LayoutActiveWebView();

        return webView;

    }



    public async Task EnsureWebViewReadyAsync(WebView2 webView, CancellationToken cancellationToken = default)

    {

        if (webView.CoreWebView2 is not null)

        {

            await WinUiTranscriptViewCoordinator.OnTabReadyAsync(webView);

            LayoutActiveWebView();

            return;

        }



        await WinUiWebViewEnvironment.GetAsync(cancellationToken);

        using var initTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        initTimeout.CancelAfter(TimeSpan.FromSeconds(45));

        await webView.EnsureCoreWebView2Async().AsTask(initTimeout.Token);



        if (webView.CoreWebView2 is not null)

            await WinUiTranscriptViewCoordinator.OnTabReadyAsync(webView);



        LayoutActiveWebView();

    }



    public async Task NavigateAsync(WebView2 webView, Uri uri, CancellationToken cancellationToken = default)

    {

        await EnsureWebViewReadyAsync(webView, cancellationToken);

        if (webView.CoreWebView2 is { } core)

            WinUiChatNavigation.Navigate(core, uri.ToString());

        else

            webView.Source = uri;

    }



    public async Task<WebView2?> PickTabAsync(XamlRoot xamlRoot)

    {

        var tabs = ListTabs();

        if (tabs.Count == 0)

            return null;



        if (tabs.Count == 1)

            return tabs[0].WebView;



        var list = new ListView

        {

            ItemsSource = tabs.Select(t => t.Header).ToList(),

            SelectionMode = ListViewSelectionMode.Single,

        };

        list.SelectedIndex = 0;



        var dialog = new ContentDialog

        {

            Title = "Pick browser tab",

            Content = list,

            PrimaryButtonText = "Select",

            CloseButtonText = "Cancel",

            DefaultButton = ContentDialogButton.Primary,

            XamlRoot = xamlRoot,

        };



        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedIndex < 0)

            return null;



        return tabs[list.SelectedIndex].WebView;

    }



    public Task RefreshThemeOnAllTabsAsync() =>

        WinUiTranscriptViewCoordinator.ApplyToAllTabsAsync();



    public Task ApplyTranscriptViewModeAsync() =>

        WinUiTranscriptViewCoordinator.ApplyToAllTabsAsync();



    private static WebView2 CreateWebView() =>

        new()

        {

            HorizontalAlignment = HorizontalAlignment.Stretch,

            VerticalAlignment = VerticalAlignment.Stretch,

        };



    private void LayoutActiveWebView()

    {

        WebViewHost.Children.Clear();

        if (TabStrip.SelectedItem is not TabViewItem tab || !_tabWebViews.TryGetValue(tab, out var webView))

            return;



        WebViewHost.Children.Add(webView);

    }



    private async void TabStrip_AddTabButtonClick(TabView sender, object args) =>

        await AddTabAsync("New chat");



    private void TabStrip_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)

    {

        if (args.Tab is not TabViewItem tab)

            return;



        if (_tabWebViews.Remove(tab, out var webView))

        {

            WinUiShellTabService.Unregister(webView);

            webView.Close();

        }



        TabStrip.TabItems.Remove(tab);

        if (TabStrip.TabItems.Count == 0)

            _ = AddTabAsync("ChatGPT");

        else

            LayoutActiveWebView();

    }



    private void TabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        LayoutActiveWebView();

        WinUiEventLogger.Debug("chat_tab_selected", "Tab switched");



        if (TabStrip.SelectedItem is TabViewItem tab

            && _tabWebViews.TryGetValue(tab, out var webView)

            && _session is not null)

        {

            _session.PinActiveTab(webView);

        }

    }



    private void WireNavigationDiagnostics(WebView2 webView)

    {

        webView.NavigationStarting += OnNavigationStarting;

        webView.NavigationCompleted += OnNavigationCompleted;

    }



    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)

    {

        WinUiEventLogger.Debug("navigation_starting", args.Uri, new { uri = args.Uri });

    }



    private async void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)

    {

        var uri = sender.Source?.ToString();

        DiagnosticsLog.Write(

            DiagnosticsChannel.WebView,

            args.IsSuccess ? DiagnosticsLevel.Info : DiagnosticsLevel.Warn,

            "navigation_completed",

            uri ?? "unknown",

            source: "winui",

            outcome: args.IsSuccess ? "ok" : "failed",

            data: new { uri, httpStatus = args.HttpStatusCode });



        if (args.IsSuccess)

            await WinUiTranscriptViewCoordinator.OnTabReadyAsync(sender);

    }

}


