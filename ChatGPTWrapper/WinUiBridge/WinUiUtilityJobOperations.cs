using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper.WinUiBridge;

public static class WinUiUtilityJobOperations
{
    internal static GenerationJobContext? TryPromptForAttachments(AdventureBundle bundle, string jobId)
    {
        var label = GenerationJobGuideService.GetDisplayLabel(jobId);
        if (!UtilityJobAttachmentLaunchDialog.TryShow(
                owner: null!,
                label,
                UtilityJobAttachmentLaunchService.GetDefaultReferenceNote(jobId),
                UtilityJobAttachmentLaunchService.GetSuggestedPaths(bundle, jobId),
                out var launch)
            || launch is null)
        {
            return null;
        }

        return UtilityJobAttachmentLaunchService.ApplyLaunch(null, launch);
    }

    public static Task<UtilityStoryContextBuildResult> BuildLivePreviewAsync(
        AdventureBundle bundle,
        string jobId,
        object? playCoreObj,
        AdventureTurnService? turnService,
        ChatGptConversationSendService sendService)
    {
        if (!WinUiWebView2CoreRuntime.TryAsCore(playCoreObj, out _))
            return Task.FromResult(new UtilityStoryContextBuildResult { CaptureError = "play_webview_not_ready" });

        var domOnlyCapture = UtilityDeliveryModeService.UsesInlineDelivery(bundle);
        return UtilityJobContextPreviewService.BuildLiveAsync(
            bundle,
            jobId,
            WinUiWebView2CoreRuntime.RequireTypedCore(playCoreObj!),
            turnService,
            sendService,
            domOnlyCapture);
    }
}
