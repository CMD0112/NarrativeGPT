using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityWorkerCapabilityGate
{
    public static bool IsGreen(AdventureBundle bundle) =>
        bundle.Metadata.UtilityWorkerCapabilities?.IsGreen == true;

    public static async Task<UtilityWorkerCapabilities> ProbeAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        string workerConversationId,
        string gizmoId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default) =>
        await UtilityWorkerTransportService.ProbeCapabilitiesAsync(
            workerCore,
            bundle,
            workerConversationId,
            gizmoId,
            conversationSend,
            turnService,
            cancellationToken);
}
