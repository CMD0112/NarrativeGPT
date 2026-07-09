using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

/// <summary>Shared post-send turn completion for WPF and WinUI hosts.</summary>
internal static class PlaySendTurnCompletionRuntime
{
    public static async Task<string> CompleteAsync(
        PlaySendTurnCompletionRequest request,
        IPlaySendHost host)
    {
        var bundle = request.Bundle;
        var turn = request.Turn;
        var sendResult = request.SendResult;
        var core = (CoreWebView2)request.Core;
        var turnService = request.TurnService;
        var composeInjection = request.ComposeInjection;
        var assistantBaselineCount = request.AssistantBaselineCount;

        var narratorText = string.IsNullOrWhiteSpace(sendResult.NarratorText)
            ? null
            : sendResult.NarratorText.Trim();
        var conversationId = sendResult.ConversationId ?? PlayThreadBindingService.GetActiveConversationId(bundle);

        if (PlayTurnScopeService.NeedsNarratorCapture(narratorText))
        {
            await host.SetComposeBusyAsync(true, "Logging response…", composeInjection);

            var gizmoId = bundle.Metadata.LinkedProjectId;

            if (PlaySendDeliveryPolicy.PreferDom(bundle)
                && !string.IsNullOrWhiteSpace(conversationId))
            {
                var stable = await turnService.CaptureStableAssistantAsync(
                    core,
                    assistantBaselineCount,
                    timeoutMs: 20_000,
                    conversationId,
                    gizmoId);
                if (stable.Success
                    && !string.IsNullOrWhiteSpace(stable.Text)
                    && !PlayTurnScopeService.IsIncompleteNarratorCapture(stable.Text))
                {
                    narratorText = stable.Text.Trim();
                }
            }

            for (var attempt = 0; attempt < 3 && PlayTurnScopeService.NeedsNarratorCapture(narratorText); attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromSeconds(1.5));

                var capture = await turnService.CaptureAssistantAsync(core, bundle);
                if (capture.Success
                    && !string.IsNullOrWhiteSpace(capture.Text)
                    && !PlayTurnScopeService.IsIncompleteNarratorCapture(capture.Text))
                {
                    narratorText = capture.Text.Trim();
                }
            }

            await host.SetComposeBusyAsync(false, null, composeInjection);
        }

        PlayTurnScopeService.AssignConversation(turn, conversationId);

        if (PlayTurnScopeService.IsIncompleteNarratorCapture(narratorText))
        {
            AdventureStore.Save(bundle);
            return string.IsNullOrWhiteSpace(narratorText)
                ? "Sent — narrator response not captured yet. Send again or use context menu Edit response when ready."
                : "Sent — narrator still generating (placeholder captured). Use context menu Edit response or retry when ready.";
        }

        var fullAssistant = sendResult.NarratorText ?? narratorText;

        if (PlayUtilityRetrievalService.ProcessAssistantResponse(bundle, fullAssistant, conversationId).AnyProcessed)
            AdventureStore.Save(bundle);

        var narratorForTurn = PlayUtilityRetrievalService.StripUtilityResponsesForNarrator(fullAssistant);
        if (string.IsNullOrWhiteSpace(narratorForTurn))
            narratorForTurn = narratorText ?? "";

        NarratorOverrideResolver.ClearTurnOverrides(bundle.Metadata.Settings);
        CanonReconciliationService.ClearNotify(bundle);
        AdventureStore.Save(bundle);

        if (!string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
            host.SchedulePostTurnJobs(bundle, turn);

        return string.IsNullOrWhiteSpace(narratorForTurn)
            ? "Sent — turn logged without narrator text. Use context menu Edit response if needed."
            : "Sent — turn logged.";
    }
}
