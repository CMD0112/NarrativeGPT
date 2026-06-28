using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityWorkerPinService
{
    public const string WorkerPinRequiredError =
        "worker_pin_required: Threads → Utility worker → Set up utility worker";

    public static bool HasWorkerPin(AdventureBundle? bundle) =>
        bundle is not null && !string.IsNullOrWhiteSpace(GetWorkerConversationId(bundle));

    /// <summary>
    /// Restores thread registry + utility session pin from verified capabilities when a stale save dropped binding.
    /// </summary>
    public static bool TryReconcilePinFromCapabilities(AdventureBundle bundle)
    {
        if (HasWorkerPin(bundle))
            return false;

        var caps = bundle.Metadata.UtilityWorkerCapabilities;
        if (caps?.IsGreen != true || string.IsNullOrWhiteSpace(caps.WorkerConversationId))
            return false;

        return TryBindWorkerConversation(bundle, caps.WorkerConversationId, persist: false);
    }

    public static string? GetWorkerConversationId(AdventureBundle bundle) =>
        UtilityWorkerSessionService.GetWorkerConversationId(bundle);

    public static string? GetWorkerTargetUrl(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var fromRegistry = AdventureThreadRegistryService.GetTargetUrl(bundle, AdventureThreadKind.UtilityWorker);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
            return fromRegistry;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var conversationId = GetWorkerConversationId(bundle);
        if (!string.IsNullOrWhiteSpace(conversationId) && !string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectConversationUrl(conversationId, gizmoId);

        return null;
    }

    public static WebView2? TryFindWebViewForWorkerSession(TabControl tabs, AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var pinKey = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker)?.PinnedTabKey;
        if (PlayTabPinService.FindWebViewByPinKey(tabs, pinKey) is { } pinned)
            return pinned;

        if (TryFindWebViewByWorkerHeader(tabs) is { } byHeader)
            return byHeader;

        var targetConversationId = GetWorkerConversationId(bundle);
        if (string.IsNullOrWhiteSpace(targetConversationId))
            return null;

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            if (wv.CoreWebView2?.Source is not { } source)
                continue;

            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
                continue;

            if (ChatGptUrls.TryParseConversationId(uri, out var conv)
                && string.Equals(conv, targetConversationId, StringComparison.OrdinalIgnoreCase)
                && !IsReservedConversation(bundle, conv))
            {
                return wv;
            }
        }

        return null;
    }

    public static bool BindWorkerPinFromWebView(AdventureBundle bundle, WebView2 webView, string? tabKey, string? tabTitle)
    {
        if (webView.CoreWebView2?.Source is not { } source
            || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var conversationId)
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        return TryBindWorkerConversation(
            bundle,
            conversationId,
            tabKey,
            tabTitle,
            source,
            clearCapabilities: true);
    }

    internal static bool TryBindWorkerConversation(
        AdventureBundle bundle,
        string conversationId,
        string? tabKey = null,
        string? tabTitle = null,
        string? tabUrl = null,
        bool clearCapabilities = false,
        bool persist = true)
    {
        if (IsReservedConversation(bundle, conversationId))
            return false;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.UtilityWorker,
            "Utility worker");
        entry.ConversationId = conversationId;
        if (tabKey is not null)
            entry.PinnedTabKey = tabKey;
        if (tabTitle is not null)
            entry.PinnedTabTitle = tabTitle;
        if (tabUrl is not null)
            entry.PinnedTabUrl = tabUrl;

        var session = UtilityWorkerSessionService.GetSession(bundle.Metadata)
                      ?? new GenerationUtilitySession
                      {
                          Sequence = 1,
                          SeedVersion = 1,
                          CreatedAt = DateTimeOffset.UtcNow,
                      };
        session.ConversationId = conversationId;
        UtilityWorkerSessionService.BindSession(bundle, session);

        if (clearCapabilities)
            bundle.Metadata.UtilityWorkerCapabilities = null;

        if (persist)
            AdventureStore.Save(bundle);

        return true;
    }

    private static bool IsReservedConversation(AdventureBundle bundle, string conversationId) =>
        !PlayTabPinService.IsAcceptableUtilityConversationId(bundle, conversationId);

    internal static WebView2? TryFindWebViewByWorkerHeader(TabControl tabs)
    {
        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv, Header: string title })
                continue;

            if (title.Contains("Utility worker", StringComparison.OrdinalIgnoreCase))
                return wv;
        }

        return null;
    }
}
