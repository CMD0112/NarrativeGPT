using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

public static class DomFileInputProbe
{
    public static Task<ApiBridgeMessage> ListComposerFileUiAsync(
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        CancellationToken cancellationToken = default) =>
        bridge.SendAsync(
            core,
            new { action = "listComposerFileUi" },
            timeoutMs: 15_000,
            cancellationToken: cancellationToken,
            skipReadyWait: bridge.IsWarm(core));

    public static Task<ApiBridgeMessage> ListProjectFileUiAsync(
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        CancellationToken cancellationToken = default) =>
        bridge.SendAsync(
            core,
            new { action = "listProjectFileUi" },
            timeoutMs: 15_000,
            cancellationToken: cancellationToken,
            skipReadyWait: bridge.IsWarm(core));
}
