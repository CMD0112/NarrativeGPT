using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlayTabPinService
{
    public static bool PreferPinnedPlayWebView(bool isPlayMode, AdventureBundle? bundle) =>
        isPlayMode && !string.IsNullOrWhiteSpace(bundle?.Metadata.PinnedPlayTabKey);

    public static bool PreferPinnedUtilityWebView(AdventureBundle? bundle) =>
        !string.IsNullOrWhiteSpace(bundle?.Metadata.PinnedUtilityTabKey);

    public static bool HasUtilityPin(AdventureBundle? bundle) =>
        !string.IsNullOrWhiteSpace(bundle?.Metadata.PinnedUtilityTabKey);

    public static string GetOrAssignTabKey(TabItem tab)
    {
        if (tab.Tag is string existing && !string.IsNullOrWhiteSpace(existing))
            return existing;

        var key = Guid.NewGuid().ToString("N");
        tab.Tag = key;
        return key;
    }

    public static string? GetTabKey(WebView2 webView, TabControl tabs)
    {
        if (FindTabItem(webView, tabs) is not { } tab)
            return null;

        return GetOrAssignTabKey(tab);
    }

    public static string? GetTabTitle(WebView2 webView, TabControl tabs)
    {
        if (FindTabItem(webView, tabs) is not { } tab)
            return null;

        return tab.Header?.ToString();
    }

    public static bool HasPersistedPlaySession(AdventureBundle? bundle) =>
        bundle is not null
        && (!string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata))
            || !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
            || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabUrl)
            || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey));

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
    internal static bool HasPlayTabOrConversationBinding(AdventureBundle? bundle) =>
        bundle is not null
        && (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
            || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabUrl)
            || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey));

    public static string? GetPlayTargetUrl(AdventureBundle bundle)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var conversationId = bundle.Metadata.LinkedConversationId;

        if (!string.IsNullOrWhiteSpace(conversationId) && !string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectConversationUrl(conversationId, gizmoId);

        if (!string.IsNullOrWhiteSpace(conversationId))
            return ChatGptUrls.BuildConversationUrl(conversationId);

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabUrl)
            && ChatGptUrls.TryCreateTrustedNavigationUri(bundle.Metadata.PinnedPlayTabUrl, out _)
            && !AdventureNavigationService.IsGenericHomepage(bundle.Metadata.PinnedPlayTabUrl))
        {
            return bundle.Metadata.PinnedPlayTabUrl;
        }

        if (!string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectUrl(gizmoId);

        return null;
    }

    public static WebView2? TryFindWebViewForPlaySession(TabControl tabs, AdventureBundle bundle)
    {
        if (FindWebViewByPinKey(tabs, bundle.Metadata.PinnedPlayTabKey) is { } pinned)
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

            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.TryParseConversationId(uri, out var sourceConversationId))
            {
                continue;
            }

            if (string.Equals(sourceConversationId, targetConversationId, StringComparison.OrdinalIgnoreCase))
                return wv;
        }

        return null;
    }

    public static bool IsOnPlayTarget(string? source, AdventureBundle bundle)
    {
        var targetConversationId = GetTargetConversationId(bundle);
        if (targetConversationId is null)
            return false;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var sourceConversationId))
        {
            return false;
        }

        if (!string.Equals(sourceConversationId, targetConversationId, StringComparison.OrdinalIgnoreCase))
            return false;

        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
            return true;

        if (AdventurePlayContextService.IsOnConversationPage(source, targetConversationId))
            return true;

        return AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                   source,
                   gizmoId,
                   out _)
               || ChatGptUrls.TryParseGizmoId(uri, out _);
    }

    public static WebView2? FindWebViewByPinKey(TabControl tabs, string? pinKey)
    {
        if (string.IsNullOrWhiteSpace(pinKey))
            return null;

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem tab || tab.Content is not WebView2 wv)
                continue;

            if (string.Equals(GetOrAssignTabKey(tab), pinKey, StringComparison.OrdinalIgnoreCase))
                return wv;
        }

        return null;
    }

    public static TabItem? FindTabItem(WebView2 webView, TabControl tabs)
    {
        foreach (var item in tabs.Items)
        {
            if (item is TabItem tab && ReferenceEquals(tab.Content, webView))
                return tab;
        }

        return null;
    }

    public static void PinTab(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        var key = GetTabKey(webView, tabs)
                  ?? throw new InvalidOperationException("Could not resolve tab key for WebView.");
        TryBindProjectSessionFromWebView(bundle, webView);
        bundle.Metadata.PinnedPlayTabKey = key;
        bundle.Metadata.PinnedPlayTabTitle = GetTabTitle(webView, tabs);
        bundle.Metadata.PinnedPlayTabUrl = webView.CoreWebView2?.Source;
        AdventureStore.Save(bundle);
    }

    public static void ClearPin(AdventureBundle bundle)
    {
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

        if (string.Equals(bundle.Metadata.LinkedConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            return changed;

        var previous = bundle.Metadata.LinkedConversationId;
        PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);
        bundle.Metadata.LinkedConversationId = conversationId;
        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.PlayConversationId = conversationId;

        return true;
    }

    private static string? GetTargetConversationId(AdventureBundle bundle)
    {
        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            return bundle.Metadata.LinkedConversationId;

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
        if (string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey))
            return false;

        var key = GetTabKey(webView, tabs);
        return key is not null
               && string.Equals(key, bundle.Metadata.PinnedPlayTabKey, StringComparison.OrdinalIgnoreCase);
    }

    public static void PinUtilityTab(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        var key = GetTabKey(webView, tabs)
                  ?? throw new InvalidOperationException("Could not resolve tab key for WebView.");
        if (IsSameTabAsPlayPin(bundle, webView, tabs))
            throw new InvalidOperationException("Utility tab cannot be the same tab as the play tab.");

        bundle.Metadata.PinnedUtilityTabKey = key;
        bundle.Metadata.PinnedUtilityTabTitle = GetTabTitle(webView, tabs);
        AdventureStore.Save(bundle);
    }

    public static void ClearUtilityPin(AdventureBundle bundle)
    {
        bundle.Metadata.PinnedUtilityTabKey = null;
        bundle.Metadata.PinnedUtilityTabTitle = null;
        AdventureStore.Save(bundle);
    }

    public static WebView2? FindWebViewByUtilityPinKey(TabControl tabs, string? pinKey) =>
        FindWebViewByPinKey(tabs, pinKey);

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

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
            && string.Equals(
                conversationId,
                bundle.Metadata.LinkedConversationId,
                StringComparison.OrdinalIgnoreCase))
            return false;

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
