using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private CoreWebView2Environment? _chatWebViewEnvironment;
    private Task? _browserTabsReadyTask;

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
        Uri? initialNavigateUri = null)
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
        ChatTabs.SelectedItem = tab;

        await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        var pageHost = GetOrCreatePageHost(wv);

        GetOrRegisterApiBridge(wv);

        new ChatGptStyleInjection(wv, () => _chrome.ProseEnhancementsEnabled)
            .Register(pageHost);

        new ChatGptContinuousViewInjection(
            wv,
            () => _chrome.ContinuousViewEnabled,
            () => _chrome.ProseEnhancementsEnabled,
            () => _chrome.HideAssistantEditArtifacts,
            () => _chrome.PhraseHighlightsEnabled,
            () => _chrome.PhraseHighlightRules,
            () => _chrome.ContinuousViewFormat).Register(pageHost);

        pageHost.Wire();

        WireChatTabChrome(tab, wv);
        WireAdventureNavigationGuard(wv);

        wv.Source = initialNavigateUri ?? new Uri("https://chatgpt.com");

        return wv;
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
            Dispatcher.Invoke(() => UpdateChatTabHeader(tab, wv));

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

            Dispatcher.Invoke(() => UpdateChatTabHeader(tab, wv));
        };

        UpdateChatTabHeader(tab, wv);
    }

    private void UpdateChatTabHeader(TabItem tab, WebView2 wv)
    {
        var title = wv.CoreWebView2?.DocumentTitle;
        tab.Header = string.IsNullOrWhiteSpace(title)
            ? "Chat"
            : TruncateTabTitle(title.Trim(), 42);
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

        if (ChatTabs.Items.Count <= 1)
            return;

        if (tab.Content is WebView2 wv)
            _pageHosts.Remove(wv);

        ChatTabs.Items.Remove(tab);

        if (ChatTabs.Items.Count > 0 && ChatTabs.SelectedItem is null)
            ChatTabs.SelectedIndex = ChatTabs.Items.Count - 1;

        UpdateTranscriptSettingsVisibility();
    }
}
