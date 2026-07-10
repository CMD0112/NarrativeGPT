namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Host-neutral access to WebView2 instances from WPF or WinUI.</summary>
internal static class UtilityWebViewBridge
{
    public static object? GetCore(object? webView)
    {
        if (webView is null)
            return null;

        try
        {
            return ((dynamic)webView).CoreWebView2;
        }
        catch
        {
            return null;
        }
    }

    public static Microsoft.Web.WebView2.Core.CoreWebView2 RequireCore(object? webView) =>
        (Microsoft.Web.WebView2.Core.CoreWebView2)(GetCore(webView)
            ?? throw new InvalidOperationException("Utility worker WebView core is not ready."));

    public static Microsoft.Web.WebView2.Core.CoreWebView2? AsCoreWebView2(object? core) =>
        core as Microsoft.Web.WebView2.Core.CoreWebView2;
}
