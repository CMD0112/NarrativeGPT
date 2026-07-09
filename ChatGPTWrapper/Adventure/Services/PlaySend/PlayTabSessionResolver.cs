using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>
/// Resolves pinned WebViews and capabilities for a <see cref="PlayTabSession"/>.
/// </summary>
internal static class PlayTabSessionResolver
{
    public static object? ResolvePinnedTabHost(IPlayTabRegistry registry, PlayTabSession session)
    {
        if (!session.HasPin || string.IsNullOrWhiteSpace(session.PinTabKey))
            return null;

        return registry.FindTabHostByPinKey(session.PinTabKey);
    }

    public static object? ResolvePlayTabHost(
        IPlayTabRegistry registry,
        AdventureBundle bundle,
        object? staleTabHost = null) =>
        registry.ResolvePlayTabHost(bundle, staleTabHost);

    public static WebView2? ResolvePinnedWebView(TabControl tabs, PlayTabSession session)
    {
        if (ResolvePinnedTabHost(new WpfPlayTabRegistry(tabs), session) is WebView2 wv)
            return wv;
        return null;
    }

    public static WebView2? ResolvePlayWebView(
        TabControl tabs,
        AdventureBundle bundle,
        WebView2? stalePlayWebView = null)
    {
        var registry = new WpfPlayTabRegistry(tabs);
        if (registry.ResolvePlayTabHost(bundle, stalePlayWebView) is WebView2 wv)
            return wv;
        return null;
    }

    public static PlayTabCapabilities ResolveCapabilities(
        AdventureBundle bundle,
        object? tabHost,
        IPlayTabRegistry registry,
        string? source = null)
    {
        source ??= tabHost is not null ? PlayWebViewCoreBridge.GetSource(registry.GetCoreWebView(tabHost)) : null;
        var ctx = PlayTabCapabilityContext.FromRegistry(bundle, tabHost, registry, source);
        var session = PlayTabSessionFactory.FromBundle(bundle);
        return PlayTabCapabilityResolver.Resolve(ctx, session);
    }

    public static PlayTabCapabilities ResolveCapabilities(
        AdventureBundle bundle,
        WebView2? webView,
        TabControl? tabs,
        string? source = null)
    {
        if (tabs is null)
        {
            var ctx = PlayTabCapabilityContext.FromUrl(
                bundle,
                source ?? webView?.CoreWebView2?.Source,
                candidateTabKey: null);
            return PlayTabCapabilityResolver.Resolve(ctx, PlayTabSessionFactory.FromBundle(bundle));
        }

        var registry = new WpfPlayTabRegistry(tabs);
        return ResolveCapabilities(
            bundle,
            webView,
            registry,
            source ?? webView?.CoreWebView2?.Source);
    }
}
