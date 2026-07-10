using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WebView;

/// <summary>Creates page hosts without requiring WinUI callers to reference CoreWebView2.</summary>
public static class ChatGptPageHostFactory
{
    public static ChatGptPageHost Create(object coreWebView2) =>
        new(WebView2ManagedCoreRuntime.RequireTypedCore(coreWebView2));
}
