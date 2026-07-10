using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlayTabPinService
{
    public static string? GetPlayPinKey(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        return AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.PinnedTabKey
               ?? bundle.Metadata.PinnedPlayTabKey;
    }

    public static string? GetPlayPinTitle(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        return AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.PinnedTabTitle
               ?? bundle.Metadata.PinnedPlayTabTitle;
    }

    public static bool PreferPinnedPlayWebView(bool isPlayMode, AdventureBundle? bundle)
    {
        if (!isPlayMode || bundle is null)
            return false;

        return !string.IsNullOrWhiteSpace(GetPlayPinKey(bundle));
    }

    public static string GetOrAssignTabKey(TabItem tab) =>
        ThreadTabBindingService.GetOrAssignTabKey(tab);

    public static string? GetTabKey(WebView2 webView, TabControl tabs) =>
        ThreadTabBindingService.GetTabKey(webView, tabs);

    public static string? GetTabTitle(WebView2 webView, TabControl tabs) =>
        ThreadTabBindingService.GetTabTitle(webView, tabs);

    public static bool HasPersistedPlaySession(AdventureBundle? bundle)
    {
        if (bundle is null)
            return false;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
        return !string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata))
               || !string.IsNullOrWhiteSpace(entry?.ConversationId)
               || !string.IsNullOrWhiteSpace(entry?.PinnedTabUrl)
               || !string.IsNullOrWhiteSpace(entry?.PinnedTabKey);
    }

    /// <summary>
    /// True when a Project is linked but play tab / conversation binding is still missing —
    /// safe to show the one-time pin prompt. Unlinked adventures defer to the Link now banner.
    /// </summary>
    public static bool ShouldOfferPinPromptOnOpen(AdventureBundle bundle)
    {
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
            return false;

        return !HasPlayTabOrConversationBinding(bundle);
    }

    /// <summary>
    /// Play session artifacts excluding project link alone (used for pin-prompt gating).
    /// </summary>
    internal static bool HasPlayTabOrConversationBinding(AdventureBundle? bundle)
    {
        if (bundle is null)
            return false;

        return PlayThreadBindingService.HasDurableBinding(bundle);
    }

    public static string? GetPlayTargetUrl(AdventureBundle bundle) =>
        PlayThreadBindingService.ResolveBrowsableUrl(bundle);

    public static WebView2? TryFindWebViewForPlaySession(TabControl tabs, AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var pinKey = GetPlayPinKey(bundle);

        if (FindWebViewByPinKey(tabs, pinKey) is { } pinned)
            return pinned;

        return TryFindWebViewOnPlayTarget(tabs, bundle);
    }

    public static WebView2? TryFindWebViewOnPlayTarget(TabControl tabs, AdventureBundle bundle)
    {
        var targetConversationId = GetTargetConversationId(bundle);
        if (targetConversationId is null)
            return null;

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            if (wv.CoreWebView2?.Source is not { } source)
                continue;

            if (IsOnPlayTarget(source, bundle))
                return wv;
        }

        return null;
    }

    public static bool IsOnPlayTarget(string? source, AdventureBundle bundle)
    {
        var targetConversationId = GetTargetConversationId(bundle);
        if (targetConversationId is null)
            return false;

        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
            return AdventurePlayContextService.IsOnConversationPage(source, targetConversationId);

        return AdventurePlayContextService.IsOnPlayConversationPage(source, targetConversationId, gizmoId);
    }

    public static WebView2? FindWebViewByPinKey(TabControl tabs, string? pinKey) =>
        ThreadTabBindingService.FindWebViewByPinKey(tabs, pinKey);

    public static TabItem? FindTabItem(WebView2 webView, TabControl tabs) =>
        ThreadTabBindingService.FindTabItem(webView, tabs);

    public static void PinTab(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)
                      ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Play);
        PinTabToEntry(bundle, entry.Id, webView, tabs, setActive: true);
    }

    public static void PinTabToEntry(
        AdventureBundle bundle,
        Guid entryId,
        WebView2 webView,
        TabControl tabs,
        bool setActive = true)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        if (entry.Kind != AdventureThreadKind.Play)
            throw new InvalidOperationException("Entry is not a play thread.");

        if (entry.Status == AdventureThreadStatus.Archived)
            throw new InvalidOperationException("Cannot pin an archived thread.");

        TryBindProjectSessionFromWebView(bundle, webView);

        if (webView.CoreWebView2?.Source is { } source
            && TryResolveConversationFromUrl(source, out var fromUrl)
            && !string.IsNullOrWhiteSpace(fromUrl)
            && IsAcceptablePlayConversationId(bundle, fromUrl))
        {
            entry.ConversationId = fromUrl;
        }
        else if (AdventureThreadRegistryService.IsActiveEntry(bundle, entryId))
        {
            var activeConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
            if (!string.IsNullOrWhiteSpace(activeConversationId))
                entry.ConversationId = activeConversationId;
        }

        AdventureThreadRegistryService.UpdatePinFromWebView(
            bundle,
            entry.Id,
            webView,
            tabs,
            webView.CoreWebView2?.Source);

        if (setActive)
            AdventureThreadRegistryService.SetActivePin(bundle, entry.Id);

        if (webView.CoreWebView2 is { } core)
            _ = PlayThreadBindingService.TryPromoteVerifiedFromPageAsync(bundle, core, turnService: null);

        AdventureStore.Save(bundle);
    }

    public static void ClearPin(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        if (AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play) is { } entry)
        {
            entry.PinnedTabKey = null;
            entry.PinnedTabTitle = null;
            entry.PinnedTabUrl = null;
        }

        bundle.Metadata.PinnedPlayTabKey = null;
        bundle.Metadata.PinnedPlayTabTitle = null;
        bundle.Metadata.PinnedPlayTabUrl = null;
        AdventureStore.Save(bundle);
    }

    public static bool TryBindProjectSessionFromWebView(AdventureBundle bundle, WebView2 webView) =>
        webView.CoreWebView2?.Source is { } source
        && TryBindProjectSessionFromSource(bundle, source);

    public static bool TryBindProjectSessionFromSource(AdventureBundle bundle, string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return false;
        }

        var changed = false;

        if (ChatGptUrls.TryParseGizmoId(uri, out var gizmoId)
            && !string.IsNullOrWhiteSpace(gizmoId)
            && string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            var normalized = ChatGptUrls.NormalizeGizmoId(gizmoId);
            bundle.Metadata.LinkedProjectId = normalized;
            bundle.Metadata.LinkedProjectHint = normalized;
            bundle.Metadata.ProjectLink = new ProjectLink
            {
                GizmoId = normalized,
                CanonicalUrl = ChatGptUrls.BuildProjectUrl(normalized),
                LinkedAt = DateTimeOffset.UtcNow,
            };
            changed = true;
        }

        if (!ChatGptUrls.TryParseConversationId(uri, out var conversationId)
            || string.IsNullOrWhiteSpace(conversationId))
        {
            return changed;
        }

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && !AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                source,
                bundle.Metadata.LinkedProjectId,
                out conversationId))
        {
            return changed;
        }

        if (string.Equals(PlayThreadBindingService.GetActiveConversationId(bundle), conversationId, StringComparison.OrdinalIgnoreCase))
            return changed;

        if (!IsAcceptablePlayConversationId(bundle, conversationId))
            return changed;

        var previous = PlayThreadBindingService.GetActiveConversationId(bundle);
        PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);
        PlayThreadBindingService.MarkPendingPin(bundle, conversationId);

        return true;
    }

    private static string? GetTargetConversationId(AdventureBundle bundle)
    {
        var fromBinding = PlayThreadBindingService.GetActiveConversationId(bundle);
        if (!string.IsNullOrWhiteSpace(fromBinding))
            return fromBinding;

        var targetUrl = GetPlayTargetUrl(bundle);
        if (targetUrl is null
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var conversationId))
        {
            return null;
        }

        return conversationId;
    }

    public static bool IsSameTabAsPlayPin(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        var key = GetTabKey(webView, tabs);
        return IsTabKeyPlayPin(bundle, key);
    }

    public static bool IsTabKeyPlayPin(AdventureBundle bundle, string? tabKey)
    {
        if (string.IsNullOrWhiteSpace(tabKey))
            return false;

        var pinKey = GetPlayPinKey(bundle);
        return !string.IsNullOrWhiteSpace(pinKey)
               && string.Equals(tabKey, pinKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When the pinned tab key drifted but this WebView is on the bound play conversation URL,
    /// re-pin so capability resolution and compose injection stay aligned.
    /// </summary>
    public static bool TryReconcileStalePlayPin(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        if (webView.CoreWebView2?.Source is not { } source)
            return false;

        if (string.IsNullOrWhiteSpace(GetPlayPinKey(bundle)))
            return false;

        if (IsSameTabAsPlayPin(bundle, webView, tabs))
            return false;

        if (!TryResolvePlayConversationFromSource(bundle, source, out _, out _))
            return false;

        if (!IsOnPlayTarget(source, bundle))
            return false;

        PinTab(bundle, webView, tabs);
        return true;
    }

    public static bool TryResolveUtilityConversationId(
        AdventureBundle bundle,
        CoreWebView2 core,
        out string? conversationId,
        out string? error) =>
        TryResolveUtilityConversationFromSource(bundle, core.Source, out conversationId, out error);

    public static bool TryResolveUtilityConversationFromSource(
        AdventureBundle bundle,
        string? source,
        out string? conversationId,
        out string? error)
    {
        conversationId = null;
        error = null;

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            error = "utility_no_project";
            return false;
        }

        var gizmoId = ChatGptUrls.NormalizeGizmoId(bundle.Metadata.LinkedProjectId);
        if (!AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                source,
                gizmoId,
                out var resolved)
            && !TryResolveConversationFromUrl(source, out resolved))
        {
            error = "utility_tab_not_on_conversation";
            return false;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            error = "utility_tab_not_on_conversation";
            return false;
        }

        if (!IsAcceptableUtilityConversationId(bundle, resolved))
        {
            error = "utility_same_as_play_thread";
            return false;
        }

        conversationId = resolved;
        return true;
    }

    public static bool IsAcceptableUtilityConversationId(AdventureBundle bundle, string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return false;

        var playConversation = PlayThreadBindingService.GetActiveConversationId(bundle);

        if (!string.IsNullOrWhiteSpace(playConversation)
            && string.Equals(conversationId, playConversation, StringComparison.OrdinalIgnoreCase))
            return false;

        var designConversation = AdventureDesignContextService.GetDesignConversationId(bundle);
        if (!string.IsNullOrWhiteSpace(designConversation)
            && string.Equals(conversationId, designConversation, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public static bool IsAcceptablePlayConversationId(AdventureBundle bundle, string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return false;

        var designConversation = AdventureDesignContextService.GetDesignConversationId(bundle);
        return string.IsNullOrWhiteSpace(designConversation)
               || !string.Equals(conversationId, designConversation, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryResolvePlayConversationFromSource(
        AdventureBundle bundle,
        string? source,
        out string? conversationId,
        out string? error)
    {
        conversationId = null;
        error = null;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            if (!TryResolveConversationFromUrl(source, out var plainConversation))
            {
                error = "play_no_project";
                return false;
            }

            conversationId = plainConversation;
            if (!IsAcceptablePlayConversationId(bundle, conversationId))
            {
                error = "play_same_as_design_thread";
                conversationId = null;
                return false;
            }

            return true;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        string? resolved = null;
        if (AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                source,
                gizmoId,
                out var fromProject))
        {
            resolved = fromProject;
        }
        else if (TryResolveConversationFromUrl(source, out var fromUri))
        {
            resolved = fromUri;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            error = "play_tab_not_on_conversation";
            return false;
        }

        if (!IsAcceptablePlayConversationId(bundle, resolved))
        {
            error = "play_same_as_design_thread";
            return false;
        }

        conversationId = resolved;
        return true;
    }

    private static bool TryResolveConversationFromUrl(string? source, out string conversationId)
    {
        conversationId = "";
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var parsed)
            || string.IsNullOrWhiteSpace(parsed))
        {
            return false;
        }

        conversationId = parsed;
        return true;
    }
}
