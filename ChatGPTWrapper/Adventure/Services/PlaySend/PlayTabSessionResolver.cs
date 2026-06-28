using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Resolves pinned WebViews and capabilities for a <see cref="PlayTabSession"/>.
/// </summary>
internal static class PlayTabSessionResolver
{
    public static WebView2? ResolvePinnedWebView(TabControl tabs, PlayTabSession session)
    {
        if (!session.HasPin || string.IsNullOrWhiteSpace(session.PinTabKey))
            return null;

        return PlayTabPinService.FindWebViewByPinKey(tabs, session.PinTabKey);
    }

    public static WebView2? ResolvePlayWebView(
        TabControl tabs,
        AdventureBundle bundle,
        WebView2? stalePlayWebView = null)
    {
        var session = PlayTabSessionFactory.FromBundle(bundle);
        if (ResolvePinnedWebView(tabs, session) is { } pinned)
            return pinned;

        if (PlayTabPinService.TryFindWebViewForPlaySession(tabs, bundle) is { } sessionTab)
            return sessionTab;

        if (stalePlayWebView is not null
            && PlayTabPinService.GetTabKey(stalePlayWebView, tabs) is not null)
        {
            return stalePlayWebView;
        }

        return null;
    }

    public static PlayTabCapabilities ResolveCapabilities(
        AdventureBundle bundle,
        WebView2? webView,
        TabControl? tabs,
        string? source = null)
    {
        var ctx = PlayTabCapabilityContext.From(bundle, webView, tabs, source);
        var session = PlayTabSessionFactory.FromBundle(bundle);
        return PlayTabCapabilityResolver.Resolve(ctx, session);
    }
}
