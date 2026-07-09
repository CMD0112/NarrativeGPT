using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

internal static class WinUiUtilityWorkerPin
{
    public static bool BindFromWebView(
        AdventureBundle bundle,
        WebView2 webView,
        string? tabKey,
        string? tabTitle)
    {
        if (webView.CoreWebView2?.Source is not { } source
            || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var conversationId)
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        return UtilityWorkerPinService.TryBindWorkerConversation(
            bundle,
            conversationId,
            tabKey,
            tabTitle,
            source,
            clearCapabilities: true);
    }
}
