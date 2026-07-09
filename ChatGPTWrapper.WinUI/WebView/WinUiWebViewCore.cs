using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WinUI.WebView;

/// <summary>
/// WinUI-only WebView2 surface. Uses the WinRT <see cref="CoreWebView2"/> projection —
/// never loads the managed lib_manual assembly (conflicts with the UAP shim at output root).
/// </summary>
internal static class WinUiWebViewCore
{
    public static CoreWebView2? TryGetCore(WebView2 webView) => webView.CoreWebView2;

    public static CoreWebView2 RequireCore(WebView2 webView) =>
        webView.CoreWebView2
        ?? throw new InvalidOperationException("CoreWebView2 is not ready.");

    public static string? GetSource(CoreWebView2? core)
    {
        try
        {
            return core?.Source;
        }
        catch
        {
            return null;
        }
    }

    public static async Task ExecuteScriptAsync(CoreWebView2 core, string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return;

        try
        {
            _ = await core.ExecuteScriptAsync(script);
        }
        catch
        {
            // Ignore transient failures during teardown or before document exists.
        }
    }

    public static void EnableWebMessages(CoreWebView2 core) =>
        core.Settings.IsWebMessageEnabled = true;
}
