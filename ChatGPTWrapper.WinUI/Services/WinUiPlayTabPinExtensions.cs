using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

internal static class WinUiPlayTabPinExtensions
{
    public static void PinTabToEntry(
        AdventureBundle bundle,
        Guid entryId,
        WebView2 webView,
        TabViewItem? tabItem,
        bool setActive = true)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        if (entry.Kind != AdventureThreadKind.Play)
            throw new InvalidOperationException("Entry is not a play thread.");

        if (entry.Status == AdventureThreadStatus.Archived)
            throw new InvalidOperationException("Cannot pin an archived thread.");

        if (webView.CoreWebView2?.Source is { } source)
        {
            PlayTabPinService.TryBindProjectSessionFromSource(bundle, source);
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && ChatGptUrls.TryParseConversationId(uri, out var fromUrl)
                && !string.IsNullOrWhiteSpace(fromUrl)
                && PlayTabPinService.IsAcceptablePlayConversationId(bundle, fromUrl))
            {
                entry.ConversationId = fromUrl;
            }
        }
        else if (AdventureThreadRegistryService.IsActiveEntry(bundle, entryId))
        {
            var activeConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
            if (!string.IsNullOrWhiteSpace(activeConversationId))
                entry.ConversationId = activeConversationId;
        }

        var key = tabItem?.Tag as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (tabItem is not null)
                tabItem.Tag = key;
        }

        entry.PinnedTabKey = key;
        entry.PinnedTabTitle = tabItem?.Header?.ToString() ?? "Play";
        entry.PinnedTabUrl = webView.CoreWebView2?.Source;

        if (setActive)
            AdventureThreadRegistryService.SetActivePin(bundle, entry.Id);

        AdventureStore.Save(bundle);
    }
}
