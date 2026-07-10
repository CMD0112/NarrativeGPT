using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Maps WPF tab items to thread registry pin keys (no conversation semantics).
/// </summary>
internal static class ThreadTabBindingService
{
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

    public static WebView2? SelectFirstWebViewTab(TabControl tabs)
    {
        foreach (var item in tabs.Items)
        {
            if (item is TabItem { Content: WebView2 wv })
                return wv;
        }

        return null;
    }

    public static IReadOnlyList<BrowserTabSnapshot> ListWebViewTabs(TabControl tabs)
    {
        var list = new List<BrowserTabSnapshot>();
        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv } tab)
                continue;

            list.Add(new BrowserTabSnapshot(
                GetOrAssignTabKey(tab),
                tab.Header?.ToString() ?? "Tab",
                wv.CoreWebView2?.Source,
                wv));
        }

        return list;
    }
}

public sealed record BrowserTabSnapshot(
    string TabKey,
    string Title,
    string? SourceUrl,
    WebView2 WebView);
