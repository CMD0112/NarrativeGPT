using System.Diagnostics.CodeAnalysis;
using ChatGPTWrapper.WebView;

namespace ChatGPTWrapper.WinUiBridge;

/// <summary>WinUI alias for <see cref="WebView2ManagedCoreRuntime"/>.</summary>
public static class WinUiWebView2CoreRuntime
{
    public static void Register() => WebView2ManagedCoreRuntime.Register();

    public static System.Reflection.Assembly EnsureManagedCoreLoaded() =>
        WebView2ManagedCoreRuntime.EnsureLoaded();

    public static bool TryAsCore(object? coreObj, [NotNullWhen(true)] out object? core) =>
        WebView2ManagedCoreRuntime.TryAsCore(coreObj, out core);

    public static object RequireCore(object coreObj) =>
        WebView2ManagedCoreRuntime.RequireCore(coreObj);

    public static Microsoft.Web.WebView2.Core.CoreWebView2 RequireTypedCore(object coreObj) =>
        WebView2ManagedCoreRuntime.RequireTypedCore(coreObj);
}
