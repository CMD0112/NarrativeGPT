using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;
/// <summary>CMD-412 / CMD-424: per-job ephemeral project chat for utility worker lane.</summary>
internal static class UtilityEphemeralJobRunner
{
    public static async Task<GenerationJobResult> RunEntryAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext? jobContext,
        bool persistToOutbox,
        bool skipLocalLeg,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost,
        Func<
            AdventureBundle,
            UtilityOutboxEntry,
            GenerationJobContext?,
            bool,
            bool,
            CoreWebView2,
            ChatGptConversationSendService,
            AdventureTurnService,
            IUtilityWorkerHost?,
            CancellationToken,
            Task<GenerationJobResult>> runLegacyEntryAsync,
        CancellationToken cancellationToken)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            return new GenerationJobResult
            {
                Success = false,
                Error = "no_linked_project",
                RanOnUtilityWorker = true,
            };
        }
        var context = jobContext ?? UtilityWorkerOrchestrator.BuildJobContext(bundle, entry);
        context.UtilityRunId ??= entry.RunId;
        ApplySourceIoInputPath(bundle, entry, context);
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
        var usesSourceFileIo = UtilitySourceFileIoCatalog.UsesSourceFileIo(entry.JobId);
        var hasWorkerAttachments = !usesSourceFileIo
            && LocalUtilityInferencePolicy.HasStagedWorkerAttachments(context, entry);
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
        var attachmentPacket = hasWorkerAttachments
            ? UtilityEphemeralAttachmentSendService.TryPrepare(bundle, entry, context)
            : null;
        if (hasWorkerAttachments && attachmentPacket is null)
        {
            return await RunLegacyPinnedFallbackAsync(
                bundle,
                entry,
                context,
                persistToOutbox,
                workerCore,
                conversationSend,
                turnService,
                workerHost,
                runLegacyEntryAsync,
                logEvent: "ephemeral_attachment_fallback",
                statusMessage: $"{entry.JobId}: ephemeral attach unsupported — using pinned worker…",
                cancellationToken);
        }
        if (attachmentPacket is { } packet
            && UtilityEphemeralAttachmentSendService.RequiresDomHost(packet)
            && workerHost is null)
        {
            return new GenerationJobResult
            {
                Success = false,
                SkippedReason = "worker_not_ready",
                Error = "worker_host_required_for_dom_attach",
                RanOnUtilityWorker = true,
            };
        }
        var projectApi = workerHost?.ProjectApi;
        if (projectApi is null)
        {
            return new GenerationJobResult
            {
                Success = false,
                Error = "project_api_not_ready",
                RanOnUtilityWorker = true,
            };
        }

        if (usesSourceFileIo)
        {
            var alreadyPublished = entry.SourceInputsPublishedAt is not null
                || UtilitySourceFileIoPublishService.IsPublishComplete(
                    bundle.Metadata.Id,
                    entry.RunId,
                    entry.JobId,
                    bundle);

            if (!alreadyPublished)
            {
                workerHost?.SetStatus($"{entry.JobId}: publishing job input to Project sources…");
                var publish = await UtilitySourceFileIoPublishService.PublishJobInputsAsync(
                    projectApi,
                    workerCore,
                    bundle,
                    entry.JobId,
                    entry.RunId,
                    progress: workerHost is not null ? new Progress<string>(workerHost.SetStatus) : null,
                    cancellationToken);
                if (!publish.Success)
                {
                    return new GenerationJobResult
                    {
                        Success = false,
                        Error = publish.Error ?? "source_publish_failed",
                        RanOnUtilityWorker = true,
                    };
                }

                entry.SourceInputsPublishedAt = DateTimeOffset.UtcNow;
                if (persistToOutbox)
                {
                    UtilityOutboxService.Update(bundle, entry);
                    AdventureStore.Save(bundle);
                }
            }
            else
            {
                workerHost?.SetStatus($"{entry.JobId}: project sources ready…");
            }
        }

        string wrapped;
        if (attachmentPacket is not null)
        {
            wrapped = attachmentPacket.Wrapped;
            entry.PromptHash = attachmentPacket.PromptHash;
            workerHost?.SetStatus(UtilityEphemeralAttachmentSendService.FormatAttachStatus(entry, attachmentPacket));
        }
        else
        {
            var jobBody = UtilityJobPromptBuilder.BuildCoreJobBody(bundle, entry.JobId, context);
            jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, entry.JobId);
            wrapped = ContextTagFormat.WrapUtilityJob(entry.JobId, jobBody, "worker", entry.RunId);
            entry.PromptHash = UtilityMessagePushService.ComputeHash(wrapped);
        }
        var ephemeral = new EphemeralProjectChatService(projectApi, conversationSend);
        Func<CoreWebView2, CancellationToken, Task<string?>> tryUiCreate = async (c, ct) =>
        {
            if (workerHost is not null)
                return await workerHost.TryCreateEphemeralConversationViaUiAsync(bundle, c, ct);
            return await UtilityEphemeralUiCreateService.TryOpenComposerAsync(
                bundle,
                c,
                projectApi,
                turnService,
                ct);
        };
        EphemeralUtilityRunOptions? utilityOptions = attachmentPacket?.DomRequired is { Count: > 0 }
            ? new EphemeralUtilityRunOptions
            {
                DomAttachments = attachmentPacket.DomRequired,
                JobId = entry.JobId,
                WorkerHost = workerHost,
                FallbackConversationId = UtilityWorkerSession.GetConversationId(bundle),
                Bundle = bundle,
            }
            : null;
        async Task<EphemeralProjectChatResult> RunEphemeralOnceAsync() =>
            await ephemeral.RunOnceAsync(
                new EphemeralProjectChatRequest
                {
                    Core = workerCore,
                    GizmoId = gizmoId,
                    MessageText = wrapped,
                    TurnService = turnService,
                    TryUiCreate = tryUiCreate,
                    WarmSession = true,
                    DeleteAfterCapture = true,
                    DeleteInBackground = true,
                },
                utilityOptions,
                cancellationToken);
        EphemeralProjectChatResult ephemeralResult;
        if (workerHost is not null)
        {
            ephemeralResult = await workerHost.WithUtilityWebViewActivatedAsync(
                workerCore,
                RunEphemeralOnceAsync,
                cancellationToken);
        }
        else
        {
            ephemeralResult = await RunEphemeralOnceAsync();
        }
        if (!ephemeralResult.Success
            && attachmentPacket is not null
            && TryBuildEmbedOnlyFallbackPacket(bundle, entry, context, attachmentPacket, out var embedOnly))
        {
            ProjectLinkDiagnostics.Log("ephemeral_attach_embed_fallback");
            workerHost?.SetStatus(
                $"{entry.JobId}: ephemeral DOM attach failed — retrying embed-only…");
            ephemeralResult = await RunEmbedOnlyFallbackAsync(
                workerHost,
                workerCore,
                ephemeral,
                gizmoId,
                turnService,
                tryUiCreate,
                embedOnly,
                entry.JobId,
                cancellationToken);
        }
        if (!ephemeralResult.Success
            && attachmentPacket is not null
            && UtilityWorkerCapabilityGate.IsProductionReady(bundle))
        {
            var failureError = FormatEphemeralFailure(ephemeralResult);
            ProjectLinkDiagnostics.Log($"ephemeral_attach_fallback: {failureError}");
            workerHost?.SetStatus(
                $"{entry.JobId}: ephemeral attach failed ({failureError}) — trying pinned DOM attach…");

            var pinnedAttach = await UtilityEphemeralPinnedAttachFallback.TryAsync(
                bundle,
                entry,
                context,
                attachmentPacket,
                persistToOutbox,
                workerCore,
                conversationSend,
                turnService,
                workerHost,
                localLeg,
                cancellationToken);
            if (pinnedAttach is not null)
                return pinnedAttach;
        }

        if (!ephemeralResult.Success
            && (attachmentPacket is not null
                || UtilityWorkerPinService.HasWorkerPin(bundle))
            && UtilityWorkerCapabilityGate.IsProductionReady(bundle)
            && !UtilityWorkerTransitionCatalog.BlocksPinnedWorkerFallback(entry.JobId))
        {
            var failureError = FormatEphemeralFailure(ephemeralResult);
            var logEvent = attachmentPacket is not null
                ? "ephemeral_attach_fallback"
                : "ephemeral_create_fallback";
            ProjectLinkDiagnostics.Log($"{logEvent}: {failureError}");
            var status = attachmentPacket is not null
                ? $"{entry.JobId}: ephemeral attach failed ({failureError}) — using pinned worker…"
                : $"{entry.JobId}: ephemeral create failed ({failureError}) — using pinned worker…";
            workerHost?.SetStatus(status);
            entry.State = UtilityJobRunState.Queued;
            entry.PushedAt = null;
            entry.PushError = null;
            if (persistToOutbox)
                UtilityOutboxService.Update(bundle, entry);
            return await runLegacyEntryAsync(
                bundle,
                entry,
                context,
                persistToOutbox,
                true,
                workerCore,
                conversationSend,
                turnService,
                workerHost,
                cancellationToken);
        }
        if (persistToOutbox)
        {
            entry.State = UtilityJobRunState.Pushed;
            entry.PushedAt = DateTimeOffset.UtcNow;
            UtilityOutboxService.Update(bundle, entry);
        }
        entry.State = UtilityJobRunState.Pulling;
        if (persistToOutbox)
            UtilityOutboxService.Update(bundle, entry);
        var responseText = ephemeralResult.ResponseText;
        string? captureError = null;
        if (!ephemeralResult.Success)
            captureError = FormatEphemeralFailure(ephemeralResult);
        if (LocalUtilityInferencePolicy.IsDualRun(bundle))
        {
            context.InferenceSource = UtilityLane.Worker;
            context.UtilityRunId = entry.RunId;
        }
        var validation = UtilityResponseSchemaRegistry.Validate(entry.JobId, responseText);
        var applyPayload = validation.Payload ?? ContextTagFormat.UnwrapUtilityJobResponse(responseText);
        var applyError = ResolveApplyError(validation, captureError, responseText);
        var applyResult = GenerationJobHandlers.ApplyResponse(
            bundle,
            entry.JobId,
            applyPayload,
            applyError,
            context);
        var pending = ToPendingInjection(entry);
        UtilityJobLoggingHooks.RecordEphemeralJobCapture(
            bundle,
            entry,
            context,
            ephemeralResult,
            wrapped);
        UtilityJobResultStore.SaveRun(
            bundle,
            pending,
            responseText,
            validation,
            applyResult,
            ephemeralResult.ConversationId,
            entry.PromptHash,
            sentMessageId: null,
            assistantMessageId: null,
            UtilityLane.Worker,
            ephemeralResult.StreamComplete,
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
        var remoteResult = new GenerationJobResult
        {
            Success = applyResult.Success && ephemeralResult.Success,
            ProposalCount = applyResult.ProposalCount,
            Error = applyError ?? applyResult.Error ?? captureError,
            SkippedReason = applyResult.SkippedReason,
            DisplayText = applyResult.DisplayText,
            ProposalIds = applyResult.ProposalIds,
            RanOnUtilityWorker = true,
        };
        if (remoteResult.Success)
        {
            entry.State = UtilityJobRunState.Complete;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            if (persistToOutbox)
                UtilityOutboxService.RemoveCompleted(bundle, entry.RunId);
            UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
            if (projectApi is not null)
            {
                await UtilitySourceFileLifecycleService.CompleteJobAsync(
                    projectApi,
                    workerCore,
                    bundle,
                    entry.JobId,
                    entry.RunId,
                    jobSucceeded: true,
                    cancellationToken);
            }
        }
        else
        {
            entry.State = UtilityJobRunState.Failed;
            entry.PullError = remoteResult.Error;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            if (persistToOutbox)
                UtilityOutboxService.Update(bundle, entry);
            UtilityJobAttachmentStaging.Cleanup(bundle.Metadata.Id, entry.RunId);
            if (projectApi is not null)
            {
                await UtilitySourceFileLifecycleService.CompleteJobAsync(
                    projectApi,
                    workerCore,
                    bundle,
                    entry.JobId,
                    entry.RunId,
                    jobSucceeded: false,
                    cancellationToken);
            }
        }
        AdventureStore.Save(bundle);
        if (applyResult.ProposalCount > 0)
            AdventureStore.SaveReviewDomains(bundle);
        if (localLeg.Attempted && LocalUtilityInferencePolicy.IsDualRun(bundle))
            return LocalUtilityInferenceLegRunner.MergeDualRunResults(localLeg.ApplyResult, remoteResult);
        return remoteResult;
    }
    private static async Task<GenerationJobResult> RunLegacyPinnedFallbackAsync(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        bool persistToOutbox,
        CoreWebView2 workerCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService turnService,
        IUtilityWorkerHost? workerHost,
        Func<
            AdventureBundle,
            UtilityOutboxEntry,
            GenerationJobContext?,
            bool,
            bool,
            CoreWebView2,
            ChatGptConversationSendService,
            AdventureTurnService,
            IUtilityWorkerHost?,
            CancellationToken,
            Task<GenerationJobResult>> runLegacyEntryAsync,
        string logEvent,
        string statusMessage,
        CancellationToken cancellationToken)
    {
        ProjectLinkDiagnostics.Log(logEvent);
        workerHost?.SetStatus(statusMessage);
        if (!UtilityWorkerCapabilityGate.IsProductionReady(bundle))
        {
            return new GenerationJobResult
            {
                Success = false,
                SkippedReason = "worker_not_ready",
                Error = UtilityWorkerTransitionCatalog.BlocksPinnedWorkerFallback(entry.JobId)
                    ? "ephemeral_required_for_transitioned_job"
                    : "ephemeral_attachment_requires_pinned_worker",
                RanOnUtilityWorker = true,
            };
        }

        if (UtilityWorkerTransitionCatalog.BlocksPinnedWorkerFallback(entry.JobId))
        {
            return new GenerationJobResult
            {
                Success = false,
                SkippedReason = "worker_not_ready",
                Error = "ephemeral_required_for_transitioned_job",
                RanOnUtilityWorker = true,
            };
        }

        return await runLegacyEntryAsync(
            bundle,
            entry,
            context,
            persistToOutbox,
            true,
            workerCore,
            conversationSend,
            turnService,
            workerHost,
            cancellationToken);
    }
    internal static bool TryBuildEmbedOnlyFallbackPacket(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        UtilityEphemeralAttachmentSendService.PreparedPacket failedPacket,
        out UtilityEphemeralAttachmentSendService.PreparedPacket embedOnly)
    {
        embedOnly = failedPacket;
        if (failedPacket.Lane is not UtilityAttachmentDeliveryLane.Mixed)
            return false;
        var domAttachments = UtilityJobAttachmentStaging.LoadDomPayloads(bundle.Metadata.Id, entry.Attachments);
        UtilityAttachmentDeliveryClassifier.Partition(domAttachments, out var embeddable, out _);
        if (embeddable.Count == 0)
            return false;
        if (!context.UtilityContextAssembled)
        {
            UtilityMessagePushService.ApplyLegacyReferenceFirstDefaults(
                bundle,
                context,
                context.StoryContextHasTranscript);
        }
        var jobBody = UtilityJobPromptBuilder.BuildCoreJobBody(bundle, entry.JobId, context);
        jobBody = UtilityJobPacketAttachmentEnricher.Append(
            bundle,
            jobBody,
            context.JobAttachments,
            context.AttachmentReferenceNote,
            UtilityAttachmentDeliveryLane.PacketEmbed);
        jobBody = UtilityReferenceAttachmentPolicy.EmbedInPacket(jobBody, embeddable);
        jobBody = UtilityResponseSchemaRegistry.AppendResponseContract(jobBody, entry.JobId);
        var wrapped = ContextTagFormat.WrapUtilityJob(entry.JobId, jobBody, "worker", entry.RunId);
        embedOnly = new UtilityEphemeralAttachmentSendService.PreparedPacket(
            wrapped,
            UtilityMessagePushService.ComputeHash(wrapped),
            UtilityAttachmentDeliveryLane.PacketEmbed,
            null,
            ForceDomAttach: false);
        return true;
    }
    private static async Task<EphemeralProjectChatResult> RunEmbedOnlyFallbackAsync(
        IUtilityWorkerHost? workerHost,
        CoreWebView2 workerCore,
        EphemeralProjectChatService ephemeral,
        string gizmoId,
        AdventureTurnService turnService,
        Func<CoreWebView2, CancellationToken, Task<string?>> tryUiCreate,
        UtilityEphemeralAttachmentSendService.PreparedPacket embedOnly,
        string jobId,
        CancellationToken cancellationToken)
    {
        async Task<EphemeralProjectChatResult> RunAsync() =>
            await ephemeral.RunOnceAsync(
                new EphemeralProjectChatRequest
                {
                    Core = workerCore,
                    GizmoId = gizmoId,
                    MessageText = embedOnly.Wrapped,
                    TurnService = turnService,
                    TryUiCreate = tryUiCreate,
                    WarmSession = true,
                    DeleteAfterCapture = true,
                    DeleteInBackground = true,
                },
                new EphemeralUtilityRunOptions { JobId = jobId },
                cancellationToken);
        if (workerHost is not null)
        {
            return await workerHost.WithUtilityWebViewActivatedAsync(workerCore, RunAsync, cancellationToken);
        }
        return await RunAsync();
    }
    internal static string FormatEphemeralFailure(EphemeralProjectChatResult result)
    {
        if (result.FailedPhase is { } phase && !string.IsNullOrWhiteSpace(result.Error))
            return $"{phase.ToString().ToLowerInvariant()}:{result.Error}";
        return result.Error ?? "ephemeral_failed";
    }
    internal static string? ResolveApplyError(
        UtilitySchemaValidation validation,
        string? captureError,
        string? responseText)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
            return validation.Ok ? captureError : validation.Error;
        if (!string.IsNullOrWhiteSpace(captureError))
            return captureError;
        return validation.Error ?? "empty_response";
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

    internal static void ApplySourceIoInputPath(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context)
    {
        if (!UtilitySourceFileIoCatalog.UsesSourceFileIo(entry.JobId))
            return;

        context.SourceIoInputPath = entry.JobId switch
        {
            var id when string.Equals(id, GenerationJobId.ProposeEntitiesFile, StringComparison.OrdinalIgnoreCase) =>
                EntitiesFileRevisionService.BuildCanonicalInputRemotePath(bundle, entry.RunId),
            var id when string.Equals(id, GenerationJobId.ProposeSourceEdits, StringComparison.OrdinalIgnoreCase) =>
                SourceFileRevisionService.BuildCanonicalInputRemotePath(
                    bundle,
                    entry.RunId,
                    SectionSchema.WorldFile),
            var id when string.Equals(id, GenerationJobId.ExtractEntities, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, GenerationJobId.ExpandEntity, StringComparison.OrdinalIgnoreCase) =>
                EntityExtractionService.BuildCanonicalInputRemotePath(
                    bundle,
                    entry.JobId,
                    entry.RunId,
                    SourceJsonImportService.EntitiesJsonFileName),
            _ => null,
        };
    }
}
