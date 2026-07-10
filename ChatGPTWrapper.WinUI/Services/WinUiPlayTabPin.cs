using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Play tab pin without WPF TabControl (WinUI host).</summary>
internal static class WinUiPlayTabPin
{
    public static void PinTab(AdventureBundle bundle, WebView2 webView, TabViewItem? tabItem)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)
                      ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play);

        dynamic? coreDynamic = webView.CoreWebView2;
        string? sourceUrl = coreDynamic?.Source;

        if (sourceUrl is { } url)
        {
            PlayTabPinService.TryBindProjectSessionFromSource(bundle, url);
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && ChatGptUrls.TryParseConversationId(uri, out var fromUrl)
                && !string.IsNullOrWhiteSpace(fromUrl)
                && PlayTabPinService.IsAcceptablePlayConversationId(bundle, fromUrl))
            {
                entry.ConversationId = fromUrl;
            }
        }

        var tabKey = tabItem?.Tag as string ?? Guid.NewGuid().ToString("N");
        if (tabItem is not null && tabItem.Tag is null)
            tabItem.Tag = tabKey;

        entry.PinnedTabKey = tabKey;
        entry.PinnedTabTitle = tabItem?.Header?.ToString() ?? "Play";
        entry.PinnedTabUrl = coreDynamic?.Source;

        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id);

        PlaySessionWebViewBridge.TryPromoteThreadFromSource(bundle, sourceUrl);

        AdventureStore.Save(bundle);
    }

    public static void ClearPin(AdventureBundle bundle) => PlayTabPinService.ClearPin(bundle);
}
