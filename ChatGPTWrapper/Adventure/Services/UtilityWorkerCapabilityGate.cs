using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityWorkerCapabilityGate
{
    public static bool IsGreen(AdventureBundle bundle) =>
        UtilityWorkerCapabilities.IsProductionReady(bundle.Metadata.UtilityWorkerCapabilities);

    public static bool IsProductionReady(AdventureBundle bundle) => IsGreen(bundle);

    public static async Task<UtilityWorkerCapabilities> ProbeAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        string workerConversationId,
        string gizmoId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default,
        IUtilityWorkerHost? workerHost = null,
        ChatGptProjectApiService? projectApi = null) =>
        await UtilityWorkerTransportService.ProbeCapabilitiesAsync(
            workerCore,
            bundle,
            workerConversationId,
            gizmoId,
            conversationSend,
            turnService,
            cancellationToken,
            workerHost,
            projectApi);
}
