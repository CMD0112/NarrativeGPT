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

    public UtilityAttachmentDeliveryLane DeliveryLane { get; init; } = UtilityAttachmentDeliveryLane.None;
}

/// <summary>Utility packet push on the worker WebView.</summary>
internal static class UtilityMessagePushService
{
    public static async Task<UtilityPushResult> PushProductionAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost = null,
        CancellationToken cancellationToken = default)
    {
        var conversationId = UtilityWorkerSession.GetConversationId(bundle);
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(gizmoId))
        {
            return new UtilityPushResult { Success = false, Error = "worker_not_configured" };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (!context.UtilityContextAssembled)
        {
            ApplyLegacyReferenceFirstDefaults(bundle, context, context.StoryContextHasTranscript);
        }

        var domAttachments = UtilityJobAttachmentStaging.LoadDomPayloads(bundle.Metadata.Id, entry.Attachments);
        var deliveryLane = UtilityAttachmentDeliveryClassifier.ResolveLane(domAttachments);
        var useDomLane = deliveryLane is UtilityAttachmentDeliveryLane.DomComposer
            or UtilityAttachmentDeliveryLane.Mixed;

        if (useDomLane)
        {
            if (!UtilityWorkerCapabilityGate.IsDomAttachReady(bundle))
            {
                return new UtilityPushResult
                {
                    Success = false,
                    Error = "worker_host_not_ready",
                    DeliveryLane = deliveryLane,
                };
            }

            if (workerHost is null)
            {
                return new UtilityPushResult
                {
                    Success = false,
                    Error = "worker_host_required_for_dom_attach",
                    DeliveryLane = deliveryLane,
                };
            }
        }
        else if (!UtilityWorkerCapabilityGate.IsProductionReady(bundle))
        {
            return new UtilityPushResult { Success = false, Error = "worker_api_not_ready" };
        }

        var jobBody = UtilityJobPromptBuilder.BuildCoreJobBody(bundle, entry.JobId, context);
        jobBody = UtilityJobPacketAttachmentEnricher.Append(
            bundle,
            jobBody,
            context.JobAttachments,
            context.AttachmentReferenceNote,
            deliveryLane);

        if (domAttachments is { Count: > 0 } && !useDomLane)
        {
            if (!UtilityReferenceAttachmentPolicy.CanEmbedInPacket(domAttachments, out var attachError))
            {
                return new UtilityPushResult
                {
                    Success = false,
                    Error = attachError ?? "utility_reference_files_unsupported",
                    DeliveryLane = deliveryLane,
                };
            }

            jobBody = UtilityReferenceAttachmentPolicy.EmbedInPacket(jobBody, domAttachments);
        }
        else if (useDomLane)
        {
            var domWorkerHost = workerHost!;
            UtilityAttachmentDeliveryClassifier.Partition(domAttachments, out var embeddable, out var domRequired);
            if (embeddable.Count > 0)
                jobBody = UtilityReferenceAttachmentPolicy.EmbedInPacket(jobBody, embeddable);

            if (domRequired.Count == 0)
            {
                return new UtilityPushResult
                {
                    Success = false,
                    Error = "utility_dom_attach_missing_files",
                    DeliveryLane = deliveryLane,
                };
            }

            jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, entry.JobId);
            var domWrapped = ContextTagFormat.WrapUtilityJob(entry.JobId, jobBody, "worker", entry.RunId);
            var domPromptHash = ComputeHash(domWrapped);

            var domPush = await PushDomAttachAsync(
                workerCore,
                bundle,
                conversationId,
                gizmoId,
                domWrapped,
                entry.JobId,
                domRequired,
                turnService,
                domWorkerHost,
                cancellationToken);

            if (!domPush.Success && embeddable.Count > 0)
            {
                var fallbackBody = UtilityJobPromptBuilder.BuildCoreJobBody(bundle, entry.JobId, context);
                fallbackBody = UtilityJobPacketAttachmentEnricher.Append(
                    bundle,
                    fallbackBody,
                    context.JobAttachments,
                    context.AttachmentReferenceNote,
                    UtilityAttachmentDeliveryLane.PacketEmbed);
                fallbackBody = UtilityReferenceAttachmentPolicy.EmbedInPacket(fallbackBody, embeddable);
                fallbackBody = UtilityResponseSchemaRegistry.AppendResponseContract(fallbackBody, entry.JobId);
                var fallbackWrapped = ContextTagFormat.WrapUtilityJob(entry.JobId, fallbackBody, "worker", entry.RunId);
                var fallbackPush = await UtilityWorkerTransportService.SendProductionPacketAsync(
                    workerCore,
                    bundle,
                    conversationId,
                    gizmoId,
                    fallbackWrapped,
                    entry.JobId,
                    conversationSend,
                    turnService,
                    cancellationToken);

                if (fallbackPush.Success)
                {
                    return ToPushResult(
                        fallbackPush,
                        ComputeHash(fallbackWrapped),
                        fallbackWrapped,
                        UtilityAttachmentDeliveryLane.PacketEmbed);
                }
            }

            return ToPushResult(domPush.Push, domPromptHash, domWrapped, domPush.DeliveryLane);
        }

        jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, entry.JobId);
        var wrapped = ContextTagFormat.WrapUtilityJob(entry.JobId, jobBody, "worker", entry.RunId);
        var promptHash = ComputeHash(wrapped);

        var push = await UtilityWorkerTransportService.SendProductionPacketAsync(
            workerCore,
            bundle,
            conversationId,
            gizmoId,
            wrapped,
            entry.JobId,
            conversationSend,
            turnService,
            cancellationToken);

        return ToPushResult(push, promptHash, wrapped, deliveryLane);
    }

    private static async Task<DomAttachPushResult> PushDomAttachAsync(
        CoreWebView2 workerCore,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string domWrapped,
        string jobId,
        IReadOnlyList<DomAttachmentPayload> domRequired,
        AdventureTurnService turnService,
        IUtilityWorkerHost workerHost,
        CancellationToken cancellationToken)
    {
        var lane = UtilityAttachmentDeliveryLane.DomComposer;
        var push = await UtilityWorkerTransportService.SendProductionPacketWithAttachmentsAsync(
            workerCore,
            bundle,
            conversationId,
            gizmoId,
            domWrapped,
            jobId,
            domRequired,
            turnService,
            workerHost,
            cancellationToken);

        if (push.Success)
            return new DomAttachPushResult(push, lane);

        var workerPush = await UtilityAttachWorkerService.TryDomAttachAsync(
            workerCore,
            conversationId,
            gizmoId,
            domWrapped,
            domRequired,
            cancellationToken);

        return new DomAttachPushResult(workerPush, UtilityAttachmentDeliveryLane.AttachWorker);
    }

    private sealed record DomAttachPushResult(ConversationSendResult Push, UtilityAttachmentDeliveryLane DeliveryLane)
    {
        public bool Success => Push.Success;
        public string? Error => Push.Error;
    }

    private static UtilityPushResult ToPushResult(
        ConversationSendResult push,
        string promptHash,
        string wrapped,
        UtilityAttachmentDeliveryLane deliveryLane) =>
        new()
        {
            Success = push.Success,
            Error = push.Success ? null : push.Error ?? "push_failed",
            SentMessageId = push.ParentMessageId,
            AssistantMessageId = push.AssistantMessageId,
            AssistantText = push.AssistantText,
            StreamComplete = push.StreamComplete,
            PromptHash = promptHash,
            PacketText = wrapped,
            DeliveryLane = deliveryLane,
        };

    internal static void ApplyLegacyReferenceFirstDefaults(
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
