using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Resolves the WinUI browser tab bound to the active design thread (CMD-555).</summary>
internal static class WinUiDesignTabRegistry
{
    public static async Task<WebView2?> ResolveAsync(
        WinUiPlaySessionService session,
        AdventureBundle bundle,
        bool selectTab,
        CancellationToken cancellationToken = default)
    {
        var host = WinUiShellHost.GetShellChatHost();
        if (host is null)
            return null;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design);
        if (!string.IsNullOrWhiteSpace(entry?.PinnedTabKey)
            && host.FindWebViewByPinKey(entry.PinnedTabKey) is { } pinned)
        {
            await host.EnsureWebViewReadyAsync(pinned, cancellationToken);
            await session.EnsurePageHostAsync(pinned);
            if (selectTab)
                host.SelectWebView(pinned);
            return pinned;
        }

        foreach (var (_, webView) in host.ListTabs())
        {
            var source = webView.CoreWebView2?.Source;
            if (!string.IsNullOrWhiteSpace(source)
                && DesignTabPinService.TryResolveDesignConversationFromSource(bundle, source, out _, out _))
            {
                await host.EnsureWebViewReadyAsync(webView, cancellationToken);
                await session.EnsurePageHostAsync(webView);
                if (selectTab)
                    host.SelectWebView(webView);
                return webView;
            }
        }

        if (host.GetActiveWebView() is { } active)
        {
            var source = active.CoreWebView2?.Source;
            if (DesignTabPinService.IsOnDesignTarget(source, bundle)
                || AdventureNavigationService.IsOnLinkedProjectPage(source, bundle))
            {
                await host.EnsureWebViewReadyAsync(active, cancellationToken);
                await session.EnsurePageHostAsync(active);
                return active;
            }
        }

        return null;
    }

    public static TabViewItem? FindTabForWebView(ChatTabHost host, WebView2 webView) =>
        host.FindTabForWebView(webView);
}
