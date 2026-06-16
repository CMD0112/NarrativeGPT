using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private Task<WebView2>? _hiddenUtilityWebViewInitTask;

    private async Task<WebView2?> EnsureUtilityWebViewAsync()
    {
        if (_activeAdventureId is { } activeId)
        {
            var bundle = AdventureStore.Load(activeId);
            if (bundle is not null && PlayTabPinService.PreferPinnedUtilityWebView(bundle))
            {
                var pinned = PlayTabPinService.FindWebViewByUtilityPinKey(
                    ChatTabs,
                    bundle.Metadata.PinnedUtilityTabKey);
                if (pinned is not null)
                    return pinned;
            }
        }

        return await EnsureHiddenUtilityWebViewAsync();
    }

    private Task<WebView2> EnsureHiddenUtilityWebViewAsync()
    {
        _hiddenUtilityWebViewInitTask ??= InitializeHiddenUtilityWebViewAsync();
        return _hiddenUtilityWebViewInitTask;
    }

    private async Task<WebView2> InitializeHiddenUtilityWebViewAsync()
    {
        if (_chatWebViewEnvironment is null)
            throw new InvalidOperationException("WebView2 environment not ready.");

        if (HiddenUtilityWebView.CoreWebView2 is null)
            await HiddenUtilityWebView.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        var pageHost = GetOrCreatePageHost(HiddenUtilityWebView);
        GetOrRegisterApiBridge(HiddenUtilityWebView);
        GetOrRegisterAdventureBridge(HiddenUtilityWebView);

        if (HiddenUtilityWebView.CoreWebView2 is { } core)
        {
            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
                {
                    args.Cancel = true;
                    return;
                }

                if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
                {
                    args.Cancel = true;
                    return;
                }

                var resumeUrl = UtilityConversationPageService.LastUtilityConversationUrl;
                if (IsBareChatGptHomepage(uri)
                    && !string.IsNullOrWhiteSpace(resumeUrl)
                    && resumeUrl.Contains("/c/", StringComparison.OrdinalIgnoreCase)
                    && ChatGptUrls.TryCreateTrustedNavigationUri(resumeUrl, out var trustedResume))
                {
                    args.Cancel = true;
                    core.Navigate(resumeUrl);
                }
            };

            if (!IsChatGptPage(core))
            {
                var resumeUrl = UtilityConversationPageService.LastUtilityConversationUrl;
                core.Navigate(
                    !string.IsNullOrWhiteSpace(resumeUrl) && ChatGptUrls.TryCreateTrustedNavigationUri(resumeUrl, out _)
                        ? resumeUrl
                        : "https://chatgpt.com");
                await WaitForChatGptNavigationAsync(core);
            }
        }

        pageHost.Wire();
        return HiddenUtilityWebView;
    }

    private static bool IsBareChatGptHomepage(Uri uri) =>
        uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/");
}
