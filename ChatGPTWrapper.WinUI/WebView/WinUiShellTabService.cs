using ChatGPTWrapper;
using ChatGPTWrapper.WebView;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WinUI.WebView;

/// <summary>
/// Shell chat-tab lifecycle: one WinRT core per tab, chrome injection, transcript view modes.
/// Replaces the old managed-Core <c>ChatGptPageHost</c> path for WinUI.
/// </summary>
internal static class WinUiShellTabService
{
    private static readonly Dictionary<WebView2, CoreWebView2> Tabs = new();

    public static async Task ApplyWhenReadyAsync(WebView2 webView)
    {
        var core = WinUiWebViewCore.RequireCore(webView);
        WinUiWebViewCore.EnableWebMessages(core);

        if (!ChatGptPageGate.IsInjectable(WinUiWebViewCore.GetSource(core)))
            return;

        Tabs[webView] = core;
        await WinUiShellChromeApplier.ApplyAsync(core, includeLibraries: true);
    }

    public static async Task ApplyChromeToAllAsync(
        bool includeLibraries = false,
        UiChromeSettings? settings = null,
        int? revisionOverride = null)
    {
        foreach (var core in Tabs.Values.ToList())
        {
            await WinUiShellChromeApplier.ApplyAsync(
                core,
                includeLibraries,
                settings,
                revisionOverride);
        }
    }

    public static CoreWebView2? TryGetCore(WebView2 webView) =>
        Tabs.TryGetValue(webView, out var core) ? core : WinUiWebViewCore.TryGetCore(webView);

    public static void Unregister(WebView2 webView) => Tabs.Remove(webView);
}
