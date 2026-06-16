using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ChatGptProjectHostDependencies
{
    public Func<CoreWebView2Environment?> GetEnvironment { get; init; } = () => null;

    public Func<WebView2?> FindWebView { get; init; } = () => null;

    public Func<Guid, bool, Task<WebView2?>> EnsureAdventureTabAsync { get; init; } =
        (_, _) => Task.FromResult<WebView2?>(null);

    public Func<WebView2, ChatGptApiBridgeInjection> GetOrRegisterBridge { get; init; } =
        _ => throw new InvalidOperationException("Bridge factory not configured.");

    public Action<WebView2>? SelectTab { get; init; }

    public Action<Guid?>? RequestShowBrowserPane { get; init; }

    public Action<WebView2>? WireServices { get; init; }
}
