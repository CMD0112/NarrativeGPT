using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

/// <summary>
/// Host-provided compositor scopes for DOM file uploads. Registered from <c>MainWindow</c> at startup.
/// </summary>
public static class DomUploadCompositor
{
    /// <summary>Shadow compositor for utility worker background host cores.</summary>
    public static Func<CoreWebView2, IDomUploadCompositorScope?>? BeginShadowScope { get; set; }

    /// <summary>Tab-select compositor for play/project tab cores.</summary>
    public static Func<CoreWebView2, IDomUploadCompositorScope?>? BeginTabSelectScope { get; set; }

    public static IDomUploadCompositorScope? TryBegin(CoreWebView2 core)
    {
        var shadow = BeginShadowScope?.Invoke(core);
        if (shadow is not null)
            return shadow;

        return BeginTabSelectScope?.Invoke(core);
    }
}

internal sealed class DomUploadCompositorScopeAdapter : IDomUploadCompositorScope
{
    private readonly IDisposable _inner;

    public DomUploadCompositorScopeAdapter(IDisposable inner) => _inner = inner;

    public void Dispose() => _inner.Dispose();
}
