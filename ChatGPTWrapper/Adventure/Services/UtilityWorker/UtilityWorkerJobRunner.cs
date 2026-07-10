using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>Explicit worker outbox state machine — API transport, local story context.</summary>
internal static class UtilityWorkerJobRunner
{
    public static async Task<GenerationJobResult?> RunNextAsync(
        AdventureBundle bundle,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost = null,
        CancellationToken cancellationToken = default)
    {
        var entry = UtilityOutboxService.PeekNext(bundle);
        if (entry is null)
            return null;

        return await RunClaimedAsync(
            bundle,
            entry,
            workerCore,
            conversationSend,
            turnService,
            workerHost,
            cancellationToken);
    }

    public static async Task<GenerationJobResult?> RunClaimedAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunEntryAsync(
            bundle,
            entry,
            jobContext: null,
            persistToOutbox: true,
            skipLocalLeg: false,
            workerCore,
            conversationSend,
            turnService,
            workerHost,
            cancellationToken);
        return result;
    }

    public static async Task<GenerationJobResult> RunDirectAsync(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        UtilityExecutionChannel channel,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost = null,
        bool skipLocalLeg = false,
        CancellationToken cancellationToken = default)
    {
        var entry = new UtilityOutboxEntry
        {
            RunId = Guid.NewGuid(),
            JobId = jobId,
            Channel = channel,
            State = UtilityJobRunState.Queued,
            Lane = UtilityLane.Worker,
            LinkedTurnId = context.Turn?.Id,
            TurnIndex = context.Turn?.Index,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            CardId = context.CardId,
            UserPrompt = context.UserPrompt,
            AttachmentReferenceNote = context.AttachmentReferenceNote,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        return await RunEntryAsync(
            bundle,
            entry,
            context,
            persistToOutbox: false,
            skipLocalLeg,
            workerCore,
            conversationSend,
            turnService,
            workerHost,
            cancellationToken);
    }

    private static async Task<GenerationJobResult> RunEntryAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext? jobContext,
        bool persistToOutbox,
        bool skipLocalLeg,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost,
        CancellationToken cancellationToken)
    {
        if (UtilityEphemeralWorkerPolicy.ShouldUseEphemeralLane(bundle, entry.JobId))
        {
            return await UtilityEphemeralJobRunner.RunEntryAsync(
                bundle,
                entry,
                jobContext,
                persistToOutbox,
                skipLocalLeg,
                workerCore,
                conversationSend,
                turnService,
                workerHost,
                RunLegacyProductionEntryAsync,
                cancellationToken);
        }

        return await RunLegacyProductionEntryAsync(
            bundle,
            entry,
            jobContext,
            persistToOutbox,
            skipLocalLeg,
            workerCore,
            conversationSend,
            turnService,
            workerHost,
            cancellationToken);
    }

    internal static async Task<GenerationJobResult> RunLegacyProductionEntryAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext? jobContext,
        bool persistToOutbox,
        bool skipLocalLeg,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost,
        CancellationToken cancellationToken)
    {
        if (!UtilityWorkerCapabilityGate.IsProductionReady(bundle))
        {
            return new GenerationJobResult
            {
                Success = false,
                SkippedReason = "worker_not_ready",
                Error = bundle.Metadata.UtilityWorkerCapabilities?.LastProbeError ?? "worker_api_not_ready",
                RanOnUtilityWorker = true,
            };
        }

        var context = jobContext ?? UtilityWorkerOrchestrator.BuildJobContext(bundle, entry);
        context.UtilityRunId ??= entry.RunId;
        UtilityEphemeralJobRunner.ApplySourceIoInputPath(bundle, entry, context);
        UtilityJobLoggingHooks.BeforeDispatch(bundle, entry.JobId, context);

        if (entry.State == UtilityJobRunState.Queued
            && (jobContext is null || string.IsNullOrWhiteSpace(context.StoryContextBlock)))
        {
            await UtilityWorkerStoryContextProvider.ApplyAsync(bundle, entry, context, cancellationToken);

            if (LocalUtilityInferencePolicy.IsDualRun(bundle) && context.DualRunGroupId is null)
            {
                context.DualRunGroupId = Guid.NewGuid();
                context.AllowCrossSourceDuplicates = true;
            }
        }

        LocalUtilityInferenceLegResult localLeg = new();
        var hasWorkerAttachments = LocalUtilityInferencePolicy.HasStagedWorkerAttachments(context, entry);
        if (!skipLocalLeg && !hasWorkerAttachments && entry.State == UtilityJobRunState.Queued)
        {
            var workerConversationId = UtilityWorkerSession.GetConversationId(bundle);
            localLeg = await LocalUtilityInferenceLegRunner.TryRunAsync(
                bundle,
                entry.JobId,
                context,
                entry.Channel,
                workerConversationId,
                cancellationToken);

            if (localLeg.Attempted
                && LocalUtilityInferencePolicy.ShouldUseLocalExclusive(bundle, entry.JobId, context, entry)
                && localLeg.Success
                && localLeg.ApplyResult is { } exclusiveLocal)
            {
                entry.PromptHash = localLeg.PromptHash;
                entry.State = UtilityJobRunState.Complete;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                if (persistToOutbox)
                    UtilityOutboxService.RemoveCompleted(bundle, entry.RunId);
                UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
                AdventureStore.Save(bundle);
                return exclusiveLocal;
            }
        }

        if (entry.State == UtilityJobRunState.Queued)
        {
            var push = await UtilityMessagePushService.PushProductionAsync(
                workerCore,
                bundle,
                entry,
                context,
                conversationSend,
                turnService,
                workerHost,
                cancellationToken);

            entry.PromptHash = push.PromptHash;
            if (!push.Success && !IsCapturablePushFailure(push))
            {
                entry.State = UtilityJobRunState.Failed;
                entry.PushError = push.Error;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                if (persistToOutbox)
                    UtilityOutboxService.Update(bundle, entry);
                UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
                AdventureStore.Save(bundle);
                return new GenerationJobResult
                {
                    Success = false,
                    Error = push.Error,
                    RanOnUtilityWorker = true,
                };
            }

            if (!push.Success && string.IsNullOrWhiteSpace(push.SentMessageId) && turnService is not null)
            {
                push = await TryResolvePushCorrelationAsync(
                    workerCore,
                    turnService,
                    conversationSend,
                    UtilityWorkerSession.GetConversationId(bundle)!,
                    push,
                    cancellationToken);
            }

            entry.State = UtilityJobRunState.Pushed;
            entry.SentMessageId = push.SentMessageId;
            entry.AssistantMessageId = push.AssistantMessageId;
            entry.PartialAssistantText = push.AssistantText;
            entry.StreamComplete = push.StreamComplete;
            entry.PushedAt = DateTimeOffset.UtcNow;
            entry.PushError = push.Success ? null : push.Error;
            if (push.DeliveryLane != UtilityAttachmentDeliveryLane.None
                && context.UtilityContextManifest is not null)
            {
                context.UtilityContextManifest = context.UtilityContextManifest.WithAttachmentDeliveryLane(
                    UtilityAttachmentDeliveryClassifier.FormatLaneLabel(push.DeliveryLane));
            }

            if (persistToOutbox)
                UtilityOutboxService.Update(bundle, entry);

            UtilityJobLoggingHooks.LinkWorkerFlightRecord(bundle, entry, push);
        }

        entry.State = UtilityJobRunState.Pulling;
        if (persistToOutbox)
            UtilityOutboxService.Update(bundle, entry);

        var conversationId = UtilityWorkerSession.GetConversationId(bundle)!;
        var pull = await UtilityMessagePullService.PullAndApplyAsync(
            workerCore,
            bundle,
            entry,
            context,
            conversationSend,
            conversationId,
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

    private static bool IsCapturablePushFailure(UtilityPushResult push) =>
        (!string.IsNullOrWhiteSpace(push.AssistantText)
            || !string.IsNullOrWhiteSpace(push.SentMessageId))
        && (push.Success
            || GenerationJobService.IsUtilitySendError(push.Error, "capture_premature")
            || GenerationJobService.IsUtilitySendError(push.Error, "capture_timeout"));

    private static async Task<UtilityPushResult> TryResolvePushCorrelationAsync(
        CoreWebView2 workerCore,
        AdventureTurnService turnService,
        ChatGptConversationSendService conversationSend,
        string conversationId,
        UtilityPushResult push,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(push.SentMessageId))
            return push;

        ConversationParentCache.Invalidate(conversationId);
        var parentMessageId = await conversationSend.PrefetchParentAsync(
            workerCore,
            conversationId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(parentMessageId))
            return push;

        return new UtilityPushResult
        {
            Success = push.Success,
            Error = push.Error,
            SentMessageId = parentMessageId,
            AssistantMessageId = push.AssistantMessageId,
            AssistantText = push.AssistantText,
            StreamComplete = push.StreamComplete,
            PromptHash = push.PromptHash,
            PacketText = push.PacketText,
            DeliveryLane = push.DeliveryLane,
        };
    }
}
