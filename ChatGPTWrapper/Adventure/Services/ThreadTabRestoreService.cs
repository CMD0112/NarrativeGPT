using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Picks WebView tabs when restoring play/design pins after app restart so play and design
/// threads do not collide on a single tab when their persisted URLs differ.
/// </summary>
internal static class ThreadTabRestoreService
{
    public static bool PlayAndDesignTargetsConflict(AdventureBundle bundle)
    {
        var playUrl = PlayTabPinService.GetPlayTargetUrl(bundle);
        var designUrl = DesignTabPinService.GetDesignTargetUrl(bundle);
        if (string.IsNullOrWhiteSpace(playUrl) || string.IsNullOrWhiteSpace(designUrl))
            return false;

        return !ConversationUrlsReferToSameThread(playUrl, designUrl);
    }

    public static WebView2? SelectWebViewForPlayRestore(TabControl tabs, AdventureBundle bundle)
    {
        if (PlayTabPinService.TryFindWebViewForPlaySession(tabs, bundle) is { } pinned)
            return pinned;

        if (PlayTabPinService.TryFindWebViewOnPlayTarget(tabs, bundle) is { } onTarget)
            return onTarget;

        if (!PlayAndDesignTargetsConflict(bundle))
            return ThreadTabBindingService.SelectFirstWebViewTab(tabs);

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            if (DesignTabPinService.IsSameTabAsDesignPin(bundle, wv, tabs))
                continue;

            var source = wv.CoreWebView2?.Source;
            if (PlayTabPinService.IsOnPlayTarget(source, bundle)
                || AdventureNavigationService.IsGenericHomepage(source))
            {
                return wv;
            }
        }

        return null;
    }

    public static WebView2? SelectWebViewForDesignRestore(TabControl tabs, AdventureBundle bundle)
    {
        if (DesignTabPinService.TryFindWebViewForDesignSession(tabs, bundle) is { } pinned)
            return pinned;

        if (DesignTabPinService.TryFindWebViewOnDesignTarget(tabs, bundle) is { } onTarget)
            return onTarget;

        if (!PlayAndDesignTargetsConflict(bundle))
            return ThreadTabBindingService.SelectFirstWebViewTab(tabs);

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            if (PlayTabPinService.IsSameTabAsPlayPin(bundle, wv, tabs))
                continue;

            var source = wv.CoreWebView2?.Source;
            if (DesignTabPinService.IsOnDesignTarget(source, bundle)
                || AdventureNavigationService.IsGenericHomepage(source))
            {
                return wv;
            }
        }

        return null;
    }

    private static bool ConversationUrlsReferToSameThread(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
            || !Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
            || !ChatGptUrls.TryParseConversationId(leftUri, out var leftConversation)
            || !ChatGptUrls.TryParseConversationId(rightUri, out var rightConversation))
        {
            return false;
        }

        return string.Equals(leftConversation, rightConversation, StringComparison.OrdinalIgnoreCase);
    }
}
