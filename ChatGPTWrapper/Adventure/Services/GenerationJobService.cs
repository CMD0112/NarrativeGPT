using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class GenerationJobService
{
    private readonly ChatGptProjectApiService _projectApi;
    private readonly ChatGptConversationSendService _conversationSend;
    private readonly Func<CoreWebView2, CancellationToken, Task<string?>>? _tryUiCreateConversation;

    public GenerationJobService(
        ChatGptProjectApiService projectApi,
        ChatGptConversationSendService conversationSend,
        Func<CoreWebView2, CancellationToken, Task<string?>>? tryUiCreateConversation = null)
    {
        _projectApi = projectApi;
        _conversationSend = conversationSend;
        _tryUiCreateConversation = tryUiCreateConversation;
    }

    public async Task<GenerationJobResult> RunJobAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        AdventureTurnService? turnService = null,
        CoreWebView2? playCore = null,
        AdventureTurnService? playTurnService = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            return new GenerationJobResult
            {
                Success = false,
                SkippedReason = "skipped_no_project",
            };
        }

        if (UtilityDeliveryModeService.UsesInlineDelivery(bundle))
        {
            if (playCore is null || playTurnService is null)
            {
                return new GenerationJobResult
                {
                    Success = false,
                    Error = "play_thread_unavailable",
                };
            }

            if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            {
                return new GenerationJobResult
                {
                    Success = false,
                    Error = "play_thread_unlinked",
                };
            }

            return await RunInlineJobAsync(
                playCore,
                bundle,
                jobId,
                context,
                playTurnService,
                cancellationToken);
        }

        var utilityJobId = GenerationJobHandlers.GetUtilityJobId(jobId);
        var session = await EnsureUtilityConversationAsync(
            core,
            bundle,
            utilityJobId,
            context.ForceRotate,
            turnService,
            cancellationToken);
        if (session is null)
        {
            return new GenerationJobResult
            {
                Success = false,
                Error = bundle.Metadata.UtilityConversationLastError ?? "utility_conversation_unavailable",
            };
        }

        var gizmoId = bundle.Metadata.LinkedProjectId;

        var transcriptService = new PlayThreadTranscriptService(_conversationSend, playTurnService);
        var storyBuilder = new UtilityStoryContextBuilder(transcriptService);
        var storyContext = await storyBuilder.BuildAsync(bundle, jobId, playCore, cancellationToken);
        context.StoryContextBlock = storyContext.Text;
        context.StoryContextHasTranscript = storyContext.HasTranscriptSection;
        var storySettings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        context.OmitRedundantJobTurnSlices =
            storySettings.OmitRedundantJobTurnSlices && storyContext.HasTranscriptSection;
        context.StoryContextIncludesSummary =
            storySettings.IncludeRollingSummary && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary);
        context.StoryContextIncludesState =
            storySettings.IncludeState
            && EntityExtractionService.BuildWorldSnapshot(bundle, includeSummary: false) != "(none)";

        var prompt = GenerationJobHandlers.BuildJobPrompt(bundle, jobId, context);
        var baselineCount = -1;
        if (turnService is not null)
        {
            await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                core,
                session.ConversationId,
                gizmoId!,
                cancellationToken);
            baselineCount = await turnService.GetAssistantTurnCountAsync(core, cancellationToken);
            PlaySendTrace.Event(
                PlaySendTraceEvents.UtilityJobPhase,
                PlaySendCategory.Bridge,
                PlaySendLevel.Debug,
                "Utility job baseline assistant turn count",
                data: new
                {
                    phase = "baseline_count",
                    jobId,
                    conversationId = session.ConversationId,
                    baselineCount,
                    pageHref = await UtilityConversationPageService.GetPageHrefAsync(core),
                });
        }

        var sendResult = await SendUtilityPacketAsync(
            core,
            bundle,
            session.ConversationId,
            gizmoId!,
            prompt,
            jobId,
            turnService,
            cancellationToken);

        if (!sendResult.Success && !IsUtilitySendError(sendResult.Error, "capture_premature"))
        {
            return new GenerationJobResult
            {
                Success = false,
                Error = sendResult.Error ?? "send_failed",
            };
        }

        var effectiveConversationId = sendResult.ConversationId ?? session.ConversationId;
        if (sendResult.Success
            && !string.IsNullOrWhiteSpace(sendResult.ConversationId)
            && !string.Equals(sendResult.ConversationId, session.ConversationId, StringComparison.OrdinalIgnoreCase))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.UtilityJobPhase,
                PlaySendCategory.Bridge,
                PlaySendLevel.Info,
                "Utility session adopted drifted conversation id",
                data: new
                {
                    phase = "conversation_drift",
                    jobId,
                    previousConversationId = session.ConversationId,
                    conversationId = sendResult.ConversationId,
                });
            session.ConversationId = sendResult.ConversationId;
        }

        var parentMessageId = sendResult.ParentMessageId;
        if (string.IsNullOrWhiteSpace(parentMessageId))
        {
            ConversationParentCache.Invalidate(effectiveConversationId);
            parentMessageId = await _conversationSend.PrefetchParentAsync(
                core,
                effectiveConversationId,
                cancellationToken);
        }

        var responseText = sendResult.AssistantText;
        string? captureError = sendResult.Success
            ? null
            : sendResult.Error;
        if (!GenerationJobHandlers.IsSettledJobResponse(jobId, responseText, sendResult.StreamComplete))
        {
            if (!sendResult.StreamComplete && !string.IsNullOrWhiteSpace(parentMessageId))
            {
                var tail = await CaptureUtilityApiTailAsync(
                    core,
                    effectiveConversationId,
                    parentMessageId,
                    jobId,
                    cancellationToken);
                responseText = tail.Text;
                captureError = tail.Error;
            }
            else if (string.IsNullOrWhiteSpace(responseText))
            {
                captureError = sendResult.Error ?? "capture_no_assistant";
            }
            else if (turnService is not null
                     && baselineCount >= 0
                     && ShouldRetryDomCapture(jobId, responseText, sendResult))
            {
                var recapture = await turnService.CaptureStableAssistantAsync(
                    core,
                    baselineCount,
                    30_000,
                    effectiveConversationId,
                    gizmoId,
                    cancellationToken);
                if (recapture.Success && !string.IsNullOrWhiteSpace(recapture.Text))
                    responseText = recapture.Text;
                else
                    captureError = recapture.Error ?? sendResult.Error ?? "capture_premature";
            }
        }

        var applyResult = GenerationJobHandlers.ApplyResponse(bundle, jobId, responseText, captureError, context);

        UtilityParseLogService.Append(
            bundle,
            jobId,
            responseText,
            applyResult.ProposalCount,
            applyResult.Error,
            applyResult.ProposalIds);

        if (string.Equals(applyResult.Error, "parse_failed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(responseText))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeSubmitResult,
                PlaySendCategory.Bridge,
                PlaySendLevel.Warn,
                "Utility job response failed JSON parse",
                outcome: "parse_failed",
                data: new
                {
                    jobId,
                    textLength = responseText.Length,
                    textPreview = responseText.Length <= 120 ? responseText : responseText[..120],
                });
        }

        session.JobCount++;
        session.LastUsedAt = DateTimeOffset.UtcNow;
        if (applyResult.ProposalCount == 0
            && applyResult.Error is not null
            && !GenerationJobHandlers.IsCaptureFailureError(applyResult.Error))
            session.ConsecutiveParseFailures++;
        else
            session.ConsecutiveParseFailures = 0;

        bundle.Metadata.UtilitySessions[utilityJobId] = session;
        AdventureStore.Save(bundle);

        return new GenerationJobResult
        {
            Success = applyResult.Success,
            ProposalCount = applyResult.ProposalCount,
            Error = applyResult.Error,
            SkippedReason = applyResult.SkippedReason,
            DisplayText = applyResult.DisplayText,
            Rotated = context.ForceRotate,
            StoryContextSource = storyContext.TranscriptSource,
            StoryContextTurnPairs = storyContext.TurnPairCount,
            StoryContextCharCount = storyContext.Text.Length,
            StoryContextStatusHint = storyContext.Text.Length > 0 ? storyContext.FormatStatusHint() : null,
        };
    }

    public Task<GenerationUtilitySession?> ForceRotateAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        AdventureTurnService? turnService = null,
        CancellationToken cancellationToken = default) =>
        EnsureUtilityConversationAsync(core, bundle, jobId, forceRotate: true, turnService, cancellationToken);

    public async Task<GenerationUtilitySession?> EnsureUtilityConversationAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        bool forceRotate = false,
        AdventureTurnService? turnService = null,
        CancellationToken cancellationToken = default)
    {
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var metadata = bundle.Metadata;
        AdventureMetadataMigration.MigrateUtilitySessions(metadata);
        metadata.UtilityConversationLastError = null;

        try
        {
            await _projectApi.PrepareForApiAsync(core, cancellationToken);
        }
        catch (Exception ex)
        {
            metadata.UtilityConversationLastError = $"utility_prepare_failed: {ex.Message}";
            return null;
        }
        if (PlayTabPinService.HasUtilityPin(bundle)
            && !string.Equals(jobId, GenerationJobId.DesignAdventure, StringComparison.OrdinalIgnoreCase))
        {
            var pinned = await EnsurePinnedUtilityConversationAsync(
                core,
                bundle,
                jobId,
                gizmoId,
                forceRotate,
                turnService,
                cancellationToken);
            if (pinned is not null)
                return pinned;

            ProjectLinkDiagnostics.Log(
                "Utility pin invalid or stale; falling back to auto-managed utility WebView");
            metadata.UtilityConversationLastError = null;
        }

        var session = GenerationUtilitySessionService.GetSession(metadata, jobId);

        if (forceRotate && session is not null)
        {
            GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "manual_rotate");
            session = null;
        }
        else if (session is not null && GenerationUtilitySessionService.ShouldRotateSession(bundle, session, jobId))
        {
            GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "saturation");
            session = null;
        }

        IReadOnlyList<GizmoConversationRef> conversations = [];
        try
        {
            conversations = await _projectApi.ListProjectConversationsAsync(core, gizmoId, cancellationToken);
        }
        catch (Exception ex)
        {
            metadata.UtilityConversationLastError = $"utility_list_failed: {ex.Message}";
            if (session is null)
                return null;
        }

        if (session is not null && conversations.Count > 0)
        {
            var reconciled = GenerationUtilitySessionService.TryReconcileSession(bundle, jobId, conversations);
            if (reconciled is not null
                && !string.Equals(reconciled.ConversationId, session.ConversationId, StringComparison.OrdinalIgnoreCase))
            {
                GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "reconciled_to_project");
                session = reconciled;
                metadata.UtilitySessions[jobId] = session;
                AdventureStore.Save(bundle);
            }
        }
        else if (session is null && conversations.Count > 0)
        {
            session = GenerationUtilitySessionService.TryReconcileSession(bundle, jobId, conversations);
        }

        if (IsDesignUtilityJob(jobId))
        {
            DesignTabPinService.PruneUnverifiedDesignSession(bundle, conversations);

            session = GenerationUtilitySessionService.GetSession(metadata, jobId);
            if (session is null)
                session = DesignTabPinService.TryResolveDesignSessionFromPin(bundle);

            if (session is null)
            {
                metadata.UtilityConversationLastError =
                    DesignTabPinService.DesignPinRequiredError;
                AdventureStore.Save(bundle);
                return null;
            }

            if (conversations.Count > 0
                && !conversations.Any(c =>
                    string.Equals(c.Id, session.ConversationId, StringComparison.OrdinalIgnoreCase))
                && !DesignTabPinService.IsTrustedPinnedDesignConversation(metadata, session.ConversationId))
            {
                GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "not_in_project");
                metadata.UtilityConversationLastError = DesignTabPinService.DesignPinRequiredError;
                AdventureStore.Save(bundle);
                return null;
            }
        }

        var createdNew = false;
        if (session is null)
        {
            var nextSequence = GenerationUtilitySessionService.GetNextSequence(metadata, jobId);
            var createOptions = BuildCreateOptions();
            var created = await _projectApi.CreateProjectConversationDetailedAsync(
                core,
                gizmoId,
                createOptions,
                cancellationToken);
            var conversationId = created.ConversationId;
            if (string.IsNullOrWhiteSpace(conversationId) && conversations.Count > 0)
            {
                var reconciled = GenerationUtilitySessionService.TryReconcileSession(bundle, jobId, conversations);
                if (reconciled is not null)
                {
                    session = reconciled;
                    metadata.UtilitySessions[jobId] = reconciled;
                    AdventureStore.Save(bundle);
                }
            }

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                metadata.UtilityConversationLastError = created.Error ?? "utility_create_failed";
                return null;
            }

            if (created.ClientBootstrapped)
            {
                metadata.UtilityConversationLastError =
                    "utility_create_failed: ChatGPT did not register a new conversation — create a Project chat manually and pin it";
                return null;
            }

            session = new GenerationUtilitySession
            {
                ConversationId = conversationId,
                Sequence = nextSequence,
                SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            metadata.UtilitySessions[jobId] = session;
            createdNew = true;
            AdventureStore.Save(bundle);
        }

        if (createdNew || session.JobCount == 0)
        {
            await EnsureUtilityConversationPageAsync(core, session.ConversationId, gizmoId, cancellationToken);

            var seed = GenerationJobHandlers.BuildSeedPrompt(bundle, jobId, session.Sequence);
            var seedResult = await SendUtilitySeedAsync(
                core,
                bundle,
                session.ConversationId,
                gizmoId,
                seed,
                jobId,
                turnService,
                cancellationToken);

            if (!seedResult.Success
                && IsUnregisteredConversationSeedFailure(seedResult.Error)
                && _tryUiCreateConversation is not null)
            {
                ProjectLinkDiagnostics.Log(
                    $"Utility seed failed ({seedResult.Error}) for {jobId}; retrying with UI new chat");
                GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "seed_send_failed");
                AdventureStore.Save(bundle);

                var uiCreated = await _projectApi.CreateProjectConversationDetailedAsync(
                    core,
                    gizmoId,
                    new ProjectConversationCreateOptions
                    {
                        TryUiCreate = _tryUiCreateConversation,
                        UiCreateOnly = true,
                    },
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(uiCreated.ConversationId))
                {
                    metadata.UtilityConversationLastError =
                        $"utility_seed_send_failed: {seedResult.Error ?? "unknown"}";
                    AdventureStore.Save(bundle);
                    return null;
                }

                session = new GenerationUtilitySession
                {
                    ConversationId = uiCreated.ConversationId,
                    Sequence = GenerationUtilitySessionService.GetNextSequence(metadata, jobId),
                    SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId),
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                metadata.UtilitySessions[jobId] = session;
                AdventureStore.Save(bundle);

                seedResult = await SendUtilitySeedAsync(
                    core,
                    bundle,
                    session.ConversationId,
                    gizmoId,
                    seed,
                    jobId,
                    turnService,
                    cancellationToken);
            }

            if (!seedResult.Success)
            {
                metadata.UtilityConversationLastError =
                    FormatSeedFailure(seedResult.Error);
                GenerationUtilitySessionService.ArchiveSession(metadata, jobId, session, "seed_send_failed");
                AdventureStore.Save(bundle);
                return null;
            }

            await WaitForUtilityComposerReadyAsync(turnService, core, cancellationToken);
        }

        metadata.UtilitySessions[jobId] = session;
        metadata.UtilityConversationLastError = null;
        AdventureStore.Save(bundle);
        return session;
    }

    private async Task<GenerationUtilitySession?> EnsurePinnedUtilityConversationAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        string gizmoId,
        bool forceRotate,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken)
    {
        var metadata = bundle.Metadata;
        if (!PlayTabPinService.TryResolveUtilityConversationId(bundle, core, out var pinnedId, out var pinError))
        {
            metadata.UtilityConversationLastError = pinError switch
            {
                "utility_tab_not_on_conversation" =>
                    "utility_pin_invalid: Pin utility tab on a Project /c/ conversation page",
                "utility_same_as_play_thread" =>
                    "utility_pin_invalid: Utility tab cannot be the play thread — create a new Project chat",
                _ => $"utility_pin_invalid: {pinError ?? "unknown"}",
            };
            AdventureStore.Save(bundle);
            return null;
        }

        await EnsureUtilityConversationPageAsync(core, pinnedId!, gizmoId, cancellationToken);

        var session = GenerationUtilitySessionService.GetSession(metadata, jobId);
        if (forceRotate && session is not null)
        {
            session.JobCount = 0;
            session.ConsecutiveParseFailures = 0;
            session.LastUsedAt = null;
        }
        else if (session is not null && GenerationUtilitySessionService.ShouldRotateSession(bundle, session, jobId))
        {
            session.JobCount = 0;
            session.ConsecutiveParseFailures = 0;
            session.SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId);
        }

        var createdNew = session is null
                         || !string.Equals(session.ConversationId, pinnedId, StringComparison.OrdinalIgnoreCase);
        if (createdNew)
        {
            session = new GenerationUtilitySession
            {
                ConversationId = pinnedId!,
                Sequence = GenerationUtilitySessionService.GetNextSequence(metadata, jobId),
                SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            metadata.UtilitySessions[jobId] = session;
            AdventureStore.Save(bundle);
        }

        var activeSession = session!;
        if (createdNew || activeSession.JobCount == 0)
        {
            await EnsureUtilityParentReadyAsync(
                core,
                activeSession.ConversationId,
                gizmoId,
                invalidateCached: true,
                cancellationToken);

            var seed = GenerationJobHandlers.BuildSeedPrompt(bundle, jobId, activeSession.Sequence);
            var seedResult = await SendUtilitySeedAsync(
                core,
                bundle,
                activeSession.ConversationId,
                gizmoId,
                seed,
                jobId,
                turnService,
                cancellationToken);

            if (!seedResult.Success)
            {
                metadata.UtilityConversationLastError = FormatSeedFailure(seedResult.Error);
                AdventureStore.Save(bundle);
                return null;
            }
        }

        metadata.UtilitySessions[jobId] = activeSession;
        metadata.UtilityConversationLastError = null;
        AdventureStore.Save(bundle);
        return activeSession;
    }

    private sealed class UtilityCaptureResult
    {
        public string? Text { get; init; }

        public string? Error { get; init; }
    }

    private async Task<UtilityCaptureResult> CaptureUtilityApiTailAsync(
        CoreWebView2 core,
        string conversationId,
        string? parentMessageId,
        string jobId,
        CancellationToken cancellationToken)
    {
        string? responseText = null;
        string? lastSeenText = null;
        var stableEmptyCount = 0;

        for (var attempt = 0; attempt < 15; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(2000, cancellationToken);

            var capture = await _conversationSend.CaptureAssistantViaApiAsync(
                core,
                conversationId,
                parentMessageId,
                cancellationToken);

            PlaySendTrace.Event(
                PlaySendTraceEvents.UtilityCaptureAttempt,
                PlaySendCategory.Bridge,
                PlaySendLevel.Debug,
                "Utility job API tail capture attempt",
                data: new
                {
                    attempt,
                    apiSuccess = capture.Success,
                    apiError = capture.Error,
                    textLength = capture.Text?.Length ?? 0,
                });

            if (capture.Success && !string.IsNullOrWhiteSpace(capture.Text))
                responseText = capture.Text;

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                if (GenerationJobHandlers.ExpectsPlainTextResponse(jobId))
                {
                    if (GenerationJobHandlers.IsSettledJobResponse(jobId, responseText, streamComplete: attempt >= 1))
                        return new UtilityCaptureResult { Text = responseText };
                }
                else if (GenerationJobHandlers.HasActionableJobProposals(jobId, responseText))
                {
                    return new UtilityCaptureResult { Text = responseText };
                }
                else if (GenerationJobHandlers.IsEmptyJsonArrayResponse(responseText))
                {
                    stableEmptyCount = string.Equals(responseText, lastSeenText, StringComparison.Ordinal)
                        ? stableEmptyCount + 1
                        : 1;
                    lastSeenText = responseText;
                    if (stableEmptyCount >= 2)
                        return new UtilityCaptureResult { Text = responseText };
                }
                else
                {
                    stableEmptyCount = 0;
                    lastSeenText = null;
                }
            }

            responseText = null;
        }

        return new UtilityCaptureResult { Error = "capture_timeout" };
    }

    private async Task EnsureUtilityConversationPageAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        await _projectApi.PrepareForApiAsync(core, cancellationToken);
        await UtilityConversationPageService.EnsureOnProjectConversationAsync(
            core,
            conversationId,
            gizmoId,
            cancellationToken);
    }

    private async Task EnsureUtilityParentReadyAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken) =>
        await EnsureUtilityParentReadyAsync(core, conversationId, gizmoId: null, invalidateCached: false, cancellationToken);

    private async Task EnsureUtilityParentReadyAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        bool invalidateCached,
        CancellationToken cancellationToken)
    {
        if (invalidateCached)
        {
            ConversationParentCache.Invalidate(conversationId);
            ConversationConduitCache.Invalidate(conversationId);
        }

        await _conversationSend.PrefetchParentAsync(core, conversationId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(gizmoId))
            await _conversationSend.PrefetchConduitAsync(core, conversationId, gizmoId, cancellationToken);

        if (!ConversationParentCache.IsCached(conversationId))
        {
            if (string.IsNullOrWhiteSpace(gizmoId))
                ChatGptConversationSendService.BootstrapNewConversationParent(conversationId);
        }
    }

    private ProjectConversationCreateOptions? BuildCreateOptions() =>
        _tryUiCreateConversation is null
            ? null
            : new ProjectConversationCreateOptions { TryUiCreate = _tryUiCreateConversation };

    private static bool IsDesignUtilityJob(string jobId) =>
        string.Equals(jobId, GenerationJobId.DesignAdventure, StringComparison.OrdinalIgnoreCase)
        || string.Equals(jobId, GenerationJobId.DesignExtractStep, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnregisteredConversationSeedFailure(string? error) =>
        string.Equals(error, "http_403", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "missing_conduit_token", StringComparison.OrdinalIgnoreCase);

    private static string FormatSeedFailure(string? error) =>
        string.Equals(error, "http_403", StringComparison.OrdinalIgnoreCase)
            ? "utility_seed_send_failed: http_403 (conversation not registered — open the linked Project tab and retry)"
            : $"utility_seed_send_failed: {error ?? "unknown"}";

    private async Task<ConversationSendResult> SendInlineUtilityPacketDomAsync(
        CoreWebView2 playCore,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        AdventureTurnService playTurnService,
        CancellationToken cancellationToken)
    {
        if (!await playTurnService.EnsureUtilityBridgeReadyAsync(playCore, cancellationToken))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "bridge_not_ready",
                ConversationId = conversationId,
            };
        }

        await playTurnService.EnsureUtilityComposerReadyAsync(
            playCore,
            cancellationToken,
            maxWaitSeconds: 30,
            conversationId,
            gizmoId);

        var health = await playTurnService.GetAdventureComposerHealthAsync(playCore, cancellationToken);
        if (!health.ComposerFound)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "utility_page_not_ready",
                ConversationId = conversationId,
            };
        }

        TraceUtilityJobPhase(
            InlineUtilityPipeline.SendPhase,
            jobId,
            conversationId,
            new
            {
                packetLength = messageText.Length,
                composerFound = health.ComposerFound,
                submitFound = health.SubmitFound,
            },
            deliveryMode: InlineUtilityPipeline.DeliveryMode);

        var timeoutMs = AdventureTurnService.ComputeUtilityJobTimeoutMs(messageText.Length);
        return await playTurnService.SubmitUtilityJobAsync(
            playCore,
            conversationId,
            gizmoId,
            messageText,
            timeoutMs,
            jobId,
            cancellationToken);
    }

    private async Task<ConversationSendResult> SendUtilityPacketAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string messageText,
        string jobId,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken)
    {
        var readiness = await UtilityConversationReadinessService.ProbeAsync(
            core,
            conversationId,
            gizmoId,
            _conversationSend,
            turnService,
            bundle,
            cancellationToken);

        TraceUtilityJobPhase(
            "readiness",
            jobId,
            conversationId,
            new
            {
                level = readiness.Level.ToString(),
                readiness.Error,
                readiness.ApiVisible,
                pageHref = readiness.PageHref,
                readiness.DomOnlyReason,
                readiness.Hint,
                readiness.ComposerFound,
                readiness.SubmitFound,
            });

        if (readiness.Level == UtilityConversationReadinessLevel.Unready)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = readiness.Error ?? "utility_page_not_ready",
                ConversationId = conversationId,
            };
        }

        if (PlaySendDeliveryPolicy.ShouldUseApiUtilitySend(bundle, readiness.Level))
        {
            TraceUtilityJobPhase("send_api", jobId, conversationId, new { packetLength = messageText.Length });
            await EnsureUtilityParentReadyAsync(
                core,
                conversationId,
                gizmoId,
                invalidateCached: false,
                cancellationToken);

            return await _conversationSend.SendUserMessageAsync(
                core,
                conversationId,
                gizmoId,
                messageText,
                cancellationToken);
        }

        TraceUtilityJobPhase("send_dom", jobId, conversationId, new
        {
            packetLength = messageText.Length,
            domOnlyReason = readiness.DomOnlyReason,
            hint = readiness.Hint,
        });

        if (turnService is null)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "utility_turn_service_required",
                ConversationId = conversationId,
            };
        }

        var timeoutMs = AdventureTurnService.ComputeUtilityJobTimeoutMs(messageText.Length);
        var result = await turnService.SubmitUtilityJobAsync(
            core,
            conversationId,
            gizmoId,
            messageText,
            timeoutMs,
            jobId,
            cancellationToken);

        if (!result.Success
            && !string.IsNullOrWhiteSpace(readiness.Hint)
            && GenerationJobHandlers.IsCaptureFailureError(result.Error))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = $"{result.Error} ({readiness.Hint})",
                ConversationId = conversationId,
            };
        }

        return result;
    }

    private async Task<ConversationSendResult> SendUtilitySeedAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string conversationId,
        string gizmoId,
        string seed,
        string jobId,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken)
    {
        await EnsureUtilityParentReadyAsync(
            core,
            conversationId,
            gizmoId,
            invalidateCached: false,
            cancellationToken);

        var seedResult = await SendUtilityPacketAsync(
            core,
            bundle,
            conversationId,
            gizmoId,
            seed,
            jobId,
            turnService,
            cancellationToken);

        if (seedResult.Success)
            return seedResult;

        await Task.Delay(400, cancellationToken);
        await EnsureUtilityParentReadyAsync(
            core,
            conversationId,
            gizmoId,
            invalidateCached: true,
            cancellationToken);

        return await SendUtilityPacketAsync(
            core,
            bundle,
            conversationId,
            gizmoId,
            seed,
            jobId,
            turnService,
            cancellationToken);
    }

    private async Task<GenerationJobResult> RunInlineJobAsync(
        CoreWebView2 playCore,
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        AdventureTurnService playTurnService,
        CancellationToken cancellationToken)
    {
        var gizmoId = bundle.Metadata.LinkedProjectId!;
        var conversationId = bundle.Metadata.LinkedConversationId!;

        var transcriptService = new PlayThreadTranscriptService(_conversationSend, playTurnService);
        var storyBuilder = new UtilityStoryContextBuilder(transcriptService);
        var storyContext = await storyBuilder.BuildAsync(
            bundle,
            jobId,
            playCore,
            cancellationToken,
            domOnlyCapture: true);
        context.StoryContextBlock = storyContext.Text;
        context.StoryContextHasTranscript = storyContext.HasTranscriptSection;
        var storySettings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        context.OmitRedundantJobTurnSlices =
            storySettings.OmitRedundantJobTurnSlices && storyContext.HasTranscriptSection;
        context.StoryContextIncludesSummary =
            storySettings.IncludeRollingSummary && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary);
        context.StoryContextIncludesState =
            storySettings.IncludeState
            && EntityExtractionService.BuildWorldSnapshot(bundle, includeSummary: false) != "(none)";

        var jobBody = GenerationJobHandlers.BuildJobPrompt(bundle, jobId, context);
        jobBody = ContextTagFormat.AppendInlineUtilityResponseContract(
            jobBody,
            jobId,
            GenerationJobHandlers.ExpectsJsonArrayResponse(jobId),
            GenerationJobHandlers.ExpectsJsonObjectResponse(jobId));
        context.SuppressInlineGuide = true;
        var prompt = ContextTagFormat.WrapUtilityJob(jobId, jobBody);
        var promptHash = ComputePromptHash(prompt);

        await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
            playCore,
            conversationId,
            gizmoId,
            cancellationToken);
        var baselineCount = await playTurnService.GetAssistantTurnCountAsync(playCore, cancellationToken);

        await ChatGptAdventureBridgeInjection.ApplyInlineUtilityPreferencesAsync(
            playCore,
            UtilityDeliveryModeService.ShouldHideInlineUtility(bundle),
            UtilityDeliveryModeService.ShouldShowInlineUtilityTraffic(bundle));
        await ChatGptAdventureBridgeInjection.RegisterUtilityHideAsync(playCore, jobId);

        var sendResult = await SendInlineUtilityPacketDomAsync(
            playCore,
            conversationId,
            gizmoId,
            prompt,
            jobId,
            playTurnService,
            cancellationToken);

        if (!sendResult.Success && !IsUtilitySendError(sendResult.Error, "capture_premature"))
        {
            return new GenerationJobResult
            {
                Success = false,
                Error = sendResult.Error ?? "send_failed",
                StoryContextSource = storyContext.TranscriptSource,
                StoryContextTurnPairs = storyContext.TurnPairCount,
                StoryContextCharCount = storyContext.Text.Length,
                StoryContextStatusHint = storyContext.Text.Length > 0 ? storyContext.FormatStatusHint() : null,
            };
        }

        var effectiveConversationId = sendResult.ConversationId ?? conversationId;
        if (sendResult.Success
            && !string.IsNullOrWhiteSpace(sendResult.ConversationId)
            && !string.Equals(sendResult.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase)
            && AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, sendResult.ConversationId))
        {
            bundle.Metadata.LinkedConversationId = sendResult.ConversationId;
            effectiveConversationId = sendResult.ConversationId;
        }

        var responseText = sendResult.AssistantText;
        string? captureError = sendResult.Success ? null : sendResult.Error;
        if (!GenerationJobHandlers.IsSettledJobResponse(jobId, responseText, sendResult.StreamComplete))
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                captureError = sendResult.Error ?? "capture_no_assistant";
            }
            else if (baselineCount >= 0 && ShouldRetryDomCapture(jobId, responseText, sendResult))
            {
                var recapture = await playTurnService.CaptureStableAssistantAsync(
                    playCore,
                    baselineCount,
                    30_000,
                    effectiveConversationId,
                    gizmoId,
                    cancellationToken);
                if (recapture.Success && !string.IsNullOrWhiteSpace(recapture.Text))
                    responseText = recapture.Text;
                else
                    captureError = recapture.Error ?? sendResult.Error ?? "capture_premature";
            }
        }

        var applyResult = GenerationJobHandlers.ApplyResponse(bundle, jobId, responseText, captureError);

        UtilityParseLogService.Append(
            bundle,
            jobId,
            responseText,
            applyResult.ProposalCount,
            applyResult.Error,
            applyResult.ProposalIds);

        bundle.UtilityExchanges.Exchanges.Add(new UtilityExchangeRecord
        {
            JobId = jobId,
            PromptHash = promptHash,
            ResponseText = responseText,
            ConversationId = effectiveConversationId,
        });

        ThreadMetadataService.RecordUtilityExchange(
            bundle,
            jobId,
            prompt,
            responseText,
            effectiveConversationId);

        bundle.Metadata.UtilityJobLastErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var utilityJobId = GenerationJobHandlers.GetUtilityJobId(jobId);
        if (!applyResult.Success || (applyResult.ProposalCount == 0 && applyResult.Error is not null))
            bundle.Metadata.UtilityJobLastErrors[utilityJobId] = applyResult.Error ?? applyResult.SkippedReason ?? "failed";
        else
            bundle.Metadata.UtilityJobLastErrors.Remove(utilityJobId);

        AdventureStore.Save(bundle);

        return new GenerationJobResult
        {
            Success = applyResult.Success,
            ProposalCount = applyResult.ProposalCount,
            Error = applyResult.Error,
            SkippedReason = applyResult.SkippedReason,
            DisplayText = applyResult.DisplayText,
            StoryContextSource = storyContext.TranscriptSource,
            StoryContextTurnPairs = storyContext.TurnPairCount,
            StoryContextCharCount = storyContext.Text.Length,
            StoryContextStatusHint = storyContext.Text.Length > 0 ? storyContext.FormatStatusHint() : null,
        };
    }

    private static string ComputePromptHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }

    internal static bool IsUtilitySendError(string? error, string code) =>
        error?.StartsWith(code, StringComparison.OrdinalIgnoreCase) == true;

    private static bool ShouldRetryDomCapture(
        string jobId,
        string? responseText,
        ConversationSendResult sendResult) =>
        sendResult.StreamComplete
        && (IsUtilitySendError(sendResult.Error, "capture_premature")
            || AdventureTurnService.IsUtilityCapturePremature(jobId, responseText ?? ""));

    private static void TraceUtilityJobPhase(
        string phase,
        string jobId,
        string conversationId,
        object? data = null,
        string? deliveryMode = null)
    {
        PlaySendTrace.Event(
            PlaySendTraceEvents.UtilityJobPhase,
            PlaySendCategory.Bridge,
            PlaySendLevel.Debug,
            $"Utility job phase: {phase}",
            data: new { phase, jobId, conversationId, deliveryMode, detail = data });
    }

    private static async Task WaitForUtilityComposerReadyAsync(
        AdventureTurnService? turnService,
        CoreWebView2 core,
        CancellationToken cancellationToken,
        int maxWaitSeconds = 90)
    {
        if (turnService is null)
            return;

        var deadline = DateTimeOffset.UtcNow.AddSeconds(maxWaitSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var health = await turnService.GetHealthAsync(core);
            if (health.BridgeReachable && health.ComposerFound && health.SubmitFound)
                return;

            await Task.Delay(750, cancellationToken);
        }
    }

}
