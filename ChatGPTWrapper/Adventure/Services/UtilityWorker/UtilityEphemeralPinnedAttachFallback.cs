using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>CMD-424: pinned attach-worker / DOM attach before legacy embed fallback.</summary>
internal static class UtilityEphemeralPinnedAttachFallback
{
    internal static async Task<GenerationJobResult?> TryAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        UtilityEphemeralAttachmentSendService.PreparedPacket attachmentPacket,
        bool persistToOutbox,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost,
        LocalUtilityInferenceLegResult localLeg,
        CancellationToken cancellationToken)
    {
        var pinnedConv = UtilityWorkerSession.GetConversationId(bundle);
        var files = attachmentPacket.DomRequired;
        if (string.IsNullOrWhiteSpace(pinnedConv) || files is not { Count: > 0 })
            return null;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        entry.PromptHash = attachmentPacket.PromptHash;
        ConversationSendResult? push = null;

        ProjectLinkDiagnostics.Log("ephemeral_attach_fallback_attach_worker");
        push = await UtilityAttachWorkerService.TryDomAttachAsync(
            workerCore,
            pinnedConv,
            gizmoId,
            attachmentPacket.Wrapped,
            files,
            cancellationToken);

        if (!push.Success && workerHost is not null)
        {
            ProjectLinkDiagnostics.Log("ephemeral_attach_fallback_pinned_dom");
            push = await UtilityWorkerTransportService.SendProductionPacketWithAttachmentsAsync(
                workerCore,
                bundle,
                pinnedConv,
                gizmoId,
                attachmentPacket.Wrapped,
                entry.JobId,
                files,
                turnService,
                workerHost,
                cancellationToken);
        }

        if (!push.Success)
            return null;

        entry.State = UtilityJobRunState.Pushed;
        entry.SentMessageId = push.AssistantMessageId;
        entry.AssistantMessageId = push.AssistantMessageId;
        entry.PartialAssistantText = push.AssistantText;
        entry.StreamComplete = push.StreamComplete;
        entry.PushedAt = DateTimeOffset.UtcNow;
        entry.PushError = null;
        if (persistToOutbox)
            UtilityOutboxService.Update(bundle, entry);

        entry.State = UtilityJobRunState.Pulling;
        if (persistToOutbox)
            UtilityOutboxService.Update(bundle, entry);

        var pull = await UtilityMessagePullService.PullAndApplyAsync(
            workerCore,
            bundle,
            entry,
            context,
            conversationSend,
            push.ConversationId ?? pinnedConv,
            entry.SentMessageId,
            entry.PartialAssistantText,
            entry.StreamComplete,
            cancellationToken);

        if (pull.Success)
        {
            entry.State = UtilityJobRunState.Complete;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            if (persistToOutbox)
                UtilityOutboxService.RemoveCompleted(bundle, entry.RunId);
            UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
        }
        else
        {
            entry.State = UtilityJobRunState.Failed;
            entry.PullError = pull.Error;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            if (persistToOutbox)
                UtilityOutboxService.Update(bundle, entry);
            UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
        }

        AdventureStore.Save(bundle);
        if (pull.ApplyResult?.ProposalCount > 0)
            AdventureStore.SaveReviewDomains(bundle);

        var remoteResult = pull.ApplyResult ?? new GenerationJobResult
        {
            Success = false,
            Error = pull.Error,
            RanOnUtilityWorker = true,
        };

        if (localLeg.Attempted && LocalUtilityInferencePolicy.IsDualRun(bundle))
            return LocalUtilityInferenceLegRunner.MergeDualRunResults(localLeg.ApplyResult, remoteResult);

        return remoteResult;
    }
}
