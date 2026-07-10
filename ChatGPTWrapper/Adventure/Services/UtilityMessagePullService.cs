using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class UtilityPullResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? RawResponse { get; init; }

    public GenerationJobResult? ApplyResult { get; init; }
}

/// <summary>Correlated API pull for utility worker jobs.</summary>
internal static class UtilityMessagePullService
{
    private static readonly TimeSpan[] PullBackoff = [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(10),
    ];

    public static async Task<UtilityPullResult> PullAndApplyAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        ChatGptConversationSendService conversationSend,
        string conversationId,
        string? sentMessageId,
        string? initialText,
        bool streamComplete,
        CancellationToken cancellationToken = default)
    {
        var responseText = initialText;
        string? captureError = null;

        if (!GenerationJobHandlers.IsSettledJobResponse(entry.JobId, responseText, streamComplete))
        {
            if (!string.IsNullOrWhiteSpace(sentMessageId))
            {
                var polled = await PollByMessageIdAsync(
                    conversationSend,
                    workerCore,
                    conversationId,
                    sentMessageId,
                    entry.JobId,
                    cancellationToken);
                responseText = polled.Text ?? responseText;
                captureError = polled.Error;
            }
            else if (string.IsNullOrWhiteSpace(responseText))
            {
                captureError = "missing_sent_message_id";
            }
        }

        if (LocalUtilityInferencePolicy.IsDualRun(bundle))
        {
            context.InferenceSource = UtilityLane.Worker;
            context.UtilityRunId = entry.RunId;
        }

        var validation = UtilityResponseSchemaRegistry.Validate(entry.JobId, responseText);
        var applyPayload = validation.Payload ?? ContextTagFormat.UnwrapUtilityJobResponse(responseText);
        var applyError = validation.Ok ? captureError : validation.Error;
        var applyResult = GenerationJobHandlers.ApplyResponse(
            bundle,
            entry.JobId,
            applyPayload,
            applyError,
            context);

        var pending = ToPendingInjection(entry);
        UtilityJobResultStore.SaveRun(
            bundle,
            pending,
            responseText,
            validation,
            applyResult,
            conversationId,
            entry.PromptHash,
            entry.SentMessageId,
            entry.AssistantMessageId,
            UtilityLane.Worker,
            entry.StreamComplete,
            entry.PushedAt,
            context.UtilityContextManifest?.ToRecord(),
            context.DualRunGroupId,
            context);

        UtilityParseLogService.Append(
            bundle,
            entry.JobId,
            applyPayload,
            applyResult.ProposalCount,
            applyResult.Error ?? validation.Error,
            applyResult.ProposalIds);

        UtilityWorkerSessionService.RecordJobCompleted(bundle.Metadata, applyResult.Success);

        bundle.Metadata.UtilityJobLastErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var utilityJobId = GenerationJobHandlers.GetUtilityJobId(entry.JobId);
        if (!applyResult.Success || (applyResult.ProposalCount == 0 && applyResult.Error is not null))
            bundle.Metadata.UtilityJobLastErrors[utilityJobId] =
                applyResult.Error ?? applyResult.SkippedReason ?? validation.Error ?? "failed";
        else
            bundle.Metadata.UtilityJobLastErrors.Remove(utilityJobId);

        return new UtilityPullResult
        {
            Success = applyResult.Success,
            Error = applyResult.Error ?? validation.Error ?? captureError,
            RawResponse = responseText,
            ApplyResult = new GenerationJobResult
            {
                Success = applyResult.Success,
                ProposalCount = applyResult.ProposalCount,
                Error = applyResult.Error,
                SkippedReason = applyResult.SkippedReason,
                DisplayText = applyResult.DisplayText,
                ProposalIds = applyResult.ProposalIds,
                RanOnUtilityWorker = true,
            },
        };
    }

    private static async Task<(string? Text, string? Error)> PollByMessageIdAsync(
        ChatGptConversationSendService conversationSend,
        CoreWebView2 core,
        string conversationId,
        string sentMessageId,
        string jobId,
        CancellationToken cancellationToken)
    {
        foreach (var delay in PullBackoff)
        {
            await Task.Delay(delay, cancellationToken);
            var capture = await conversationSend.CaptureAssistantViaApiAsync(
                core,
                conversationId,
                sentMessageId,
                cancellationToken);

            if (capture.Success
                && !string.IsNullOrWhiteSpace(capture.Text)
                && GenerationJobHandlers.IsSettledJobResponse(jobId, capture.Text, streamComplete: true))
            {
                return (capture.Text, null);
            }
        }

        return (null, "pull_timeout");
    }

    private static PendingUtilityInjection ToPendingInjection(UtilityOutboxEntry entry) =>
        new()
        {
            RunId = entry.RunId,
            JobId = entry.JobId,
            Channel = entry.Channel,
            LinkedTurnId = entry.LinkedTurnId,
            TurnIndex = entry.TurnIndex,
            EntityId = entry.EntityId,
            EntityKind = entry.EntityKind,
            CardId = entry.CardId,
            QueuedAt = entry.QueuedAt,
        };
}
