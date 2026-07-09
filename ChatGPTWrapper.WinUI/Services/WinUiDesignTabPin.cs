using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

internal static class WinUiDesignTabPin
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

        if (entry.Kind != AdventureThreadKind.Design)
            throw new InvalidOperationException("Entry is not a design thread.");

        if (entry.Status == AdventureThreadStatus.Archived)
            throw new InvalidOperationException("Cannot pin an archived thread.");

        var source = webView.CoreWebView2?.Source;
        if (!DesignTabPinService.TryResolveDesignConversationFromSource(bundle, source, out var conversationId, out var error))
        {
            throw new InvalidOperationException(error switch
            {
                "design_tab_not_on_conversation" =>
                    "Open a Project conversation (/c/…) in this tab, then pin it as the design tab.",
                "design_same_as_play_thread" =>
                    "Design thread cannot be the play thread — create a New chat in the Project.",
                _ => "Could not pin this tab for design. Open a Project conversation page first.",
            });
        }

        if (!string.IsNullOrWhiteSpace(conversationId))
            entry.ConversationId = conversationId;

        ApplyPinFromWebView(bundle, entryId, webView, tabItem, source);

        if (setActive)
            AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        if (ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id) == ProjectChatDraftKind.Design)
            ProjectChatDraftService.Complete(bundle);

        AdventureThreadRegistryService.SyncActiveDesignUtilitySession(bundle);
        AdventureStore.Save(bundle);
    }

    public static void PinActiveTab(AdventureBundle bundle, WebView2 webView, TabViewItem? tabItem) =>
        PinTabToEntry(
            bundle,
            (AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)
             ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design)).Id,
            webView,
            tabItem,
            setActive: true);

    private static void ApplyPinFromWebView(
        AdventureBundle bundle,
        Guid entryId,
        WebView2 webView,
        TabViewItem? tabItem,
        string? sourceUrl)
    {
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        var key = tabItem?.Tag as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Guid.NewGuid().ToString("N");
            if (tabItem is not null)
                tabItem.Tag = key;
        }

        entry.PinnedTabKey = key;
        entry.PinnedTabTitle = tabItem?.Header?.ToString() ?? "Design";
        entry.PinnedTabUrl = sourceUrl ?? webView.CoreWebView2?.Source;
    }
}
