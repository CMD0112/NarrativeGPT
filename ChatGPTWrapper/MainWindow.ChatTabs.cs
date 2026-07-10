using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private CoreWebView2Environment? _chatWebViewEnvironment;
    private Task? _browserTabsReadyTask;
    private readonly Dictionary<TabItem, string> _chatTabTitles = new();

    internal void ConfigureChatTabsChrome()
    {
        ChatTabs.ItemContainerStyle = (Style)FindResource("ShellChatTabItemStyle");
        ChatTabs.AddHandler(
            UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnChatTabsPreviewMouseDown),
            handledEventsToo: true);
    }

    private void OnChatTabsPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        for (var i = 0; i < ChatTabs.Items.Count; i++)
        {
            if (ChatTabs.ItemContainerGenerator.ContainerFromIndex(i) is not TabItem tab || !tab.IsMouseOver)
                continue;

            if (ChatTabs.Items.Count > 1)
            {
                CloseChatTab(tab);
                e.Handled = true;
            }

            break;
        }
    }

    internal Task BrowserTabsReadyTask => _browserTabsReadyTask ?? Task.CompletedTask;

    internal async Task EnsureChatWebViewEnvironmentReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_chatWebViewEnvironment is not null)
            return;

        if (_browserTabsReadyTask is not null)
        {
            await _browserTabsReadyTask.WaitAsync(cancellationToken);
            if (_chatWebViewEnvironment is not null)
                return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (_chatWebViewEnvironment is null && DateTime.UtcNow < deadline)
            await Task.Delay(100, cancellationToken);
    }

    internal void StartBrowserTabsInitialization(Func<Task> initializeAsync) =>
        _browserTabsReadyTask ??= initializeAsync();

    internal WebView2? GetActiveWebView()
    {
        if (ChatTabs.SelectedItem is TabItem { Content: WebView2 wv })
            return wv;

        return null;
    }

    private async Task InitializeBrowserTabsAsync()
    {
        AppDirectories.EnsureCreated();

        _chatWebViewEnvironment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppDirectories.WebView2UserDataDirectory);

        await AddChatTabAsync("ChatGPT");
    }

    private async Task<WebView2> AddChatTabAsync(
        string initialHeader = "New chat",
        Uri? initialNavigateUri = null,
        bool selectNewTab = true)
    {
        if (_chatWebViewEnvironment is null)
            throw new InvalidOperationException("WebView2 environment not ready.");

        var wv = new WebView2();
        var tab = new TabItem
        {
            Header = initialHeader,
            Content = wv,
        };
        PlayTabPinService.GetOrAssignTabKey(tab);

        ChatTabs.Items.Add(tab);
        if (selectNewTab)
            ChatTabs.SelectedItem = tab;

        UiEventLogger.Debug(
            "chat_tab_added",
            "ChatGPT tab opened",
            new
            {
                tabKey = PlayTabPinService.GetOrAssignTabKey(tab),
                header = initialHeader,
                uri = initialNavigateUri?.ToString(),
            });

        await InitializeChatWebViewAsync(wv, tab);

        wv.Source = initialNavigateUri ?? new Uri("https://chatgpt.com");

        return wv;
    }

    private async Task InitializeChatWebViewAsync(WebView2 wv, TabItem? tabForChrome = null)
    {
        if (_chatWebViewEnvironment is null)
            throw new InvalidOperationException("WebView2 environment not ready.");

        await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        var pageHost = GetOrCreatePageHost(wv);

        GetOrRegisterApiBridge(wv);

        new ChatGptStyleInjection(wv)
            .Register(pageHost);

        new ChatGptContinuousViewInjection(
            wv,
            () => _chrome.TranscriptViewMode,
            () => _chrome.HideAssistantEditArtifacts,
            () => _chrome.PhraseHighlightsEnabled,
            () => _chrome.PhraseHighlightRules,
            () => _chrome.ContinuousViewFormat).Register(pageHost);

        pageHost.Wire();

        if (tabForChrome is not null)
            WireChatTabChrome(tabForChrome, wv);

        WireAdventureNavigationGuard(wv);
    }

    private void WireChatTabChrome(TabItem tab, WebView2 wv)
    {
        if (wv.CoreWebView2 is not { } core)
            return;

        core.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
            {
                args.Cancel = true;
                return;
            }

            if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
                args.Cancel = true;
        };

        core.DocumentTitleChanged += (_, _) =>
            _ = Dispatcher.InvokeAsync(() => UpdateChatTabHeader(tab, wv));

        core.NavigationCompleted += async (_, e) =>
        {
            if (!e.IsSuccess)
                return;

            if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            {
                try
                {
                    await GetOrRegisterApiBridge(wv).InjectAsync(core);
                }
                catch
                {
                    /* ignore inject errors on navigation */
                }
            }

            _ = Dispatcher.InvokeAsync(() => UpdateChatTabHeader(tab, wv));

            if (Uri.TryCreate(core.Source, UriKind.Absolute, out var completedUri)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(completedUri))
            {
                ScheduleStaleInjectionComposerCleanup(wv);
            }
        };

        UpdateChatTabHeader(tab, wv);
    }

    private void UpdateChatTabHeader(TabItem tab, WebView2 wv)
    {
        var title = wv.CoreWebView2?.DocumentTitle;
        var display = string.IsNullOrWhiteSpace(title)
            ? "Chat"
            : TruncateTabTitle(title.Trim(), 42);
        _chatTabTitles[tab] = display;
        ApplyChatTabHeader(tab);
    }

    private void ApplyChatTabHeader(TabItem tab)
    {
        var title = _chatTabTitles.TryGetValue(tab, out var stored) ? stored : "Chat";
        var pinned = IsTabPinnedForActiveAdventure(tab);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (pinned)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "\uE718",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("AccentLinkBrush"),
                ToolTip = "Pinned play/design tab",
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 160,
        });

        if (ChatTabs.Items.Count > 1)
        {
            var close = new Button
            {
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10,
                Padding = new Thickness(4, 0, 2, 0),
                Margin = new Thickness(4, 0, 0, 0),
                MinWidth = 20,
                MinHeight = 20,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Close tab",
            };
            close.Click += (_, _) =>
            {
                CloseChatTab(tab);
            };
            panel.Children.Add(close);
        }

        tab.Header = panel;
    }

    private bool IsTabPinnedForActiveAdventure(TabItem tab)
    {
        if (_activeAdventureId is not { } id)
            return false;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return false;

        var key = PlayTabPinService.GetOrAssignTabKey(tab);
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var playPinKey = PlayTabPinService.GetPlayPinKey(bundle);
        return !string.IsNullOrWhiteSpace(playPinKey)
               && string.Equals(key, playPinKey, StringComparison.OrdinalIgnoreCase);
    }

    internal void RefreshAllChatTabHeaders()
    {
        foreach (TabItem tab in ChatTabs.Items)
            ApplyChatTabHeader(tab);
    }

    private static string TruncateTabTitle(string title, int maxChars)
    {
        if (title.Length <= maxChars)
            return title;

        return title[..maxChars].TrimEnd() + "\u2026";
    }

    internal void CloseActiveChatTab()
    {
        if (ChatTabs.SelectedItem is not TabItem tab)
            return;

        CloseChatTab(tab);
    }

    internal void CloseChatTab(TabItem tab)
    {
        if (ChatTabs.Items.Count <= 1)
            return;

        if (tab.Content is WebView2 wv)
        {
            UiEventLogger.Debug(
                "chat_tab_closed",
                "ChatGPT tab closed",
                new { tabKey = PlayTabPinService.GetTabKey(wv, ChatTabs) });
            _pageHosts.Remove(wv);
        }
        else if (_parkedUtilityWorkerTab == tab && _utilityWorkerWebView is { } parkedWv)
        {
            ClearUtilityWorkerBackgroundHosting(parkedWv);
            _pageHosts.Remove(parkedWv);
            if (ReferenceEquals(_utilityWorkerWebView, parkedWv))
                _utilityWorkerWebView = null;
        }

        _chatTabTitles.Remove(tab);
        ChatTabs.Items.Remove(tab);

        if (ChatTabs.Items.Count > 0 && ChatTabs.SelectedItem is null)
            ChatTabs.SelectedIndex = ChatTabs.Items.Count - 1;

        RefreshAllChatTabHeaders();
        UpdateTranscriptSettingsVisibility();
    }
}
