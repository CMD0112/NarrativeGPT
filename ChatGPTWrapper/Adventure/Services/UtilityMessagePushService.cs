using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class UtilityPushResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? SentMessageId { get; init; }

    public string? AssistantMessageId { get; init; }

    public string? AssistantText { get; init; }

    public bool StreamComplete { get; init; }

    public string? PromptHash { get; init; }

    public string? PacketText { get; init; }
}

/// <summary>Utility packet push on the worker WebView via unified transport.</summary>
internal static class UtilityMessagePushService
{
    public static async Task<UtilityPushResult> PushAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService = null,
        IUtilityWorkerHost? workerHost = null,
        CancellationToken cancellationToken = default)
    {
        if (!UtilityWorkerCapabilityGate.IsGreen(bundle))
        {
            return new UtilityPushResult
            {
                Success = false,
                Error = "worker_capabilities_not_green",
            };
        }

        var conversationId = UtilityWorkerSessionService.GetWorkerConversationId(bundle);
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(gizmoId))
        {
            return new UtilityPushResult
            {
                Success = false,
                Error = "worker_not_configured",
            };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (!context.UtilityContextAssembled)
        {
            var storyContextHasTranscript = context.StoryContextHasTranscript;
            ApplyLegacyReferenceFirstDefaults(bundle, context, storyContextHasTranscript);
        }

        var jobBody = GenerationJobHandlers.BuildJobPrompt(bundle, entry.JobId, context);
        jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, entry.JobId);
        var wrapped = ContextTagFormat.WrapUtilityJob(
            entry.JobId,
            jobBody,
            "worker",
            entry.RunId);

        var promptHash = ComputeHash(wrapped);

        await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
            workerCore,
            conversationId,
            gizmoId,
            cancellationToken);

        var push = await UtilityWorkerTransportService.SendPacketAsync(
            workerCore,
            bundle,
            conversationId,
            gizmoId,
            wrapped,
            entry.JobId,
            conversationSend,
            turnService,
            cancellationToken,
            workerHost: workerHost);

        if (!push.Success)
        {
            if (GenerationJobService.IsUtilitySendError(push.Error, "capture_premature")
                && !string.IsNullOrWhiteSpace(push.AssistantText)
                && GenerationJobHandlers.IsSettledJobResponse(
                    entry.JobId,
                    push.AssistantText,
                    push.StreamComplete))
            {
                return new UtilityPushResult
                {
                    Success = true,
                    SentMessageId = push.ParentMessageId,
                    AssistantMessageId = push.AssistantMessageId,
                    AssistantText = push.AssistantText,
                    StreamComplete = push.StreamComplete,
                    PromptHash = promptHash,
                    PacketText = wrapped,
                };
            }

            return new UtilityPushResult
            {
                Success = false,
                Error = push.Error ?? "push_failed",
                PromptHash = promptHash,
                PacketText = wrapped,
            };
        }

        return new UtilityPushResult
        {
            Success = true,
            SentMessageId = push.ParentMessageId,
            AssistantMessageId = push.AssistantMessageId,
            AssistantText = push.AssistantText,
            StreamComplete = push.StreamComplete,
            PromptHash = promptHash,
            PacketText = wrapped,
        };
    }

    private static void ApplyLegacyReferenceFirstDefaults(
        AdventureBundle bundle,
        GenerationJobContext context,
        bool storyContextHasTranscript)
    {
        var hasPlayThreadTurns = storyContextHasTranscript
                                 || (!string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle))
                                     && bundle.Log.Turns.Any(t => t.Status == TurnStatus.Accepted));
        context.OmitRedundantJobTurnSlices = hasPlayThreadTurns;
        context.StoryContextHasTranscript = hasPlayThreadTurns;
        context.SuppressInlineGuide = true;
    }

    internal static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }
}
