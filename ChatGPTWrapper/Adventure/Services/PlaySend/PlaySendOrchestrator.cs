using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

using ChatGPTWrapper;

internal enum PlaySendOrchestratorState
{
    Idle,
    ValidateSession,
    ResolveCapabilities,
    LoadArtifact,
    Preflight,
    Deliver,
    Verify,
    RecordTurn,
    CaptureAssistant,
    Blocked,
    Failed,
}

internal sealed class PlaySendOrchestrator
{
    public async Task<PlaySendResult> RequestSendAsync(
        PlayComposeSendEventArgs? sendRequest,
        ChatGptPlayComposeInjection? composeInjection,
        IPlaySendHost host,
        CancellationToken cancellationToken = default)
    {
        var composeText = sendRequest?.Text;
        var pendingAttachments = sendRequest?.Attachments ?? [];
        var attachmentsPreStaged = sendRequest?.AttachmentsPreStaged == true;
        composeInjection ??= host.GetActiveComposeInjection();
        PlaySendScope? traceScope = null;

        async Task ReleaseComposeAsync(string? status = null)
        {
            var state = new PlayComposeUiState { Busy = false, Focus = true };
            if (status is not null)
                state = new PlayComposeUiState { Busy = false, Focus = true, Status = status };

            await host.SyncComposeUiAsync(state, composeInjection);
        }

        if (host.ActiveAdventureId is not { } adventureId)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.SendGate,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                "Send aborted: no active adventure",
                outcome: "no_adventure");
            host.SetComposeStatus("No active adventure.", composeInjection);
            await ReleaseComposeAsync();
            return new PlaySendResult(PlaySendOutcome.Blocked, "no_adventure");
        }

        traceScope = PlaySendTrace.BeginSend(
            adventureId,
            composeText,
            composeInjection?.CoreWebView is not null
                ? PlayWebViewCoreBridge.GetSource(composeInjection.CoreWebView)
                : null);

        if (!await host.TryAcquireSendGateAsync())
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.SendGate,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                "Send aborted: send gate already held",
                outcome: "already_sending");
            host.SetComposeStatus("Already sending…", composeInjection);
            await ReleaseComposeAsync();
            traceScope.Complete("blocked", "already_sending");
            return new PlaySendResult(PlaySendOutcome.Blocked, "already_sending");
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.SendGate,
            PlaySendCategory.Host,
            PlaySendLevel.Debug,
            "Send gate acquired");

        string? playerLine = null;
        host.IncrementActiveSendCount();
        try
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
            {
                host.SetComposeStatus("Could not load adventure.", composeInjection);
                traceScope.Complete("failed", "bundle_missing");
                await ReleaseComposeAsync();
                return new PlaySendResult(PlaySendOutcome.Failed, "bundle_missing");
            }

            var sendTabHost = composeInjection?.TabHost ?? host.ActivePlayTabHost;
            PlayTabCapabilities capabilities;
            if (sendTabHost is not null)
            {
                capabilities = host.ResolveCapabilities(bundle, sendTabHost);
                PlaySendTraceMapper.LogCapabilities(
                    capabilities,
                    PlayWebViewCoreBridge.GetSource(host.TabRegistry.GetCoreWebView(sendTabHost)));

                var preflight = PlaySendPreflight.Evaluate(capabilities, host.ArtifactStore);
                if (!preflight.CanProceed)
                {
                    host.SetComposeStatus(preflight.UserMessage, composeInjection);
                    traceScope.Complete("blocked", preflight.ReasonCode);
                    await ReleaseComposeAsync();
                    return new PlaySendResult(PlaySendOutcome.Blocked, preflight.ReasonCode);
                }
            }

            playerLine = host.ResolvePlayerInput(bundle, consumeQueue: true, composeText);
            var attachmentContext = host.BuildAttachmentContext(sendRequest, pendingAttachments);
            playerLine = PlaySurfaceActionSendHelper.ApplyInjectedOnly(bundle, playerLine);
            PlaySendTrace.Event(
                PlaySendTraceEvents.PlayerLineResolved,
                PlaySendCategory.Host,
                PlaySendLevel.Info,
                string.IsNullOrWhiteSpace(playerLine)
                    ? "Player line resolved empty"
                    : "Player line resolved",
                outcome: string.IsNullOrWhiteSpace(playerLine) ? "empty" : "ok",
                data: new
                {
                    composeTextLength = composeText?.Length ?? 0,
                    playerLineLength = playerLine.Length,
                    preview = TruncateForLog(playerLine, 120),
                    queueCount = bundle.ContinuationQueue.Count,
                });

            if (string.IsNullOrWhiteSpace(playerLine)
                && attachmentContext is not { HasAttachments: true }
                && pendingAttachments.Count == 0
                && !attachmentsPreStaged)
            {
                host.SetComposeStatus(
                    "Enter a prompt in the composer, set a fallback line in Play settings, add lines to the continuation queue, or attach a file.",
                    composeInjection);
                traceScope.Complete("failed", "empty_player_line");
                await ReleaseComposeAsync();
                return new PlaySendResult(PlaySendOutcome.Failed, "empty_player_line");
            }

            ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(bundle);
            var sourceReadiness = ProjectSourceInjectionService.Evaluate(bundle);
            PlaySendTraceMapper.LogSourceReadiness(sourceReadiness);
            var userChoseInlineFallback = false;
            if (sourceReadiness.HasLinkedProject && !sourceReadiness.CanDelegateStaticContent)
            {
                var onlyNeedsRepublish = sourceReadiness.LoreEntryCount > 0
                    && sourceReadiness.NeedsRepublishCount > 0
                    && sourceReadiness.BlockingReason?.Contains("manual publish", StringComparison.OrdinalIgnoreCase) == true;

                if (onlyNeedsRepublish)
                {
                    userChoseInlineFallback = true;
                    host.SetComposeStatus(
                        "Sources need re-publish for Project delegation — sending with inline lore. "
                        + "After uploading to ChatGPT Project, use Mark all published in Sources.",
                        composeInjection);
                }
                else
                {
                    var warnMessage =
                        "Project sources are not ready for this send."
                        + (string.IsNullOrWhiteSpace(sourceReadiness.BlockingReason)
                            ? ""
                            : $"\n\nReason: {sourceReadiness.BlockingReason}")
                        + (string.IsNullOrWhiteSpace(sourceReadiness.SuggestedAction)
                            ? ""
                            : $"\n\n{sourceReadiness.SuggestedAction}")
                        + "\n\nChoose No to send now with inline lore fallback, or Yes to cancel and open Sources.";

                    if (host.PromptSourcesInlineFallback(warnMessage) == PlaySendSourcesPromptResult.CancelSend)
                    {
                        host.SetComposeStatus(
                            "Publish sources in Play settings → Sources, then send again.",
                            composeInjection);
                        traceScope.Complete("blocked", "sources_not_published");
                        await ReleaseComposeAsync();
                        return new PlaySendResult(PlaySendOutcome.Blocked, "sources_not_published");
                    }

                    userChoseInlineFallback = true;
                }
            }

            host.SyncPlayThreadScopeForPacket(bundle);
            await host.SetComposeBusyAsync(true, "Preparing…", composeInjection);

            TurnRecord? turn = null;
            try
            {
                if (composeInjection?.TabHost is { } composeTab)
                    host.ActivePlayTabHost = composeTab;

                await host.EnsurePlayWebViewReadyAsync(
                    adventureId,
                    selectTab: false,
                    prepareContext: false,
                    navigateToBrowseTarget: false);

                var playTabHost = composeInjection?.TabHost ?? host.ActivePlayTabHost;
                var coreObj = playTabHost is not null
                    ? host.TabRegistry.GetCoreWebView(playTabHost)
                    : null;
                if (coreObj is null)
                {
                    host.SetComposeStatus("Pin a ChatGPT tab for this adventure first.", composeInjection);
                    host.ShowSendError(
                        PlaySendTrace.FormatRunContextForError("Pin a ChatGPT tab for this adventure first."));
                    await host.RestoreComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", "no_webview");
                    return new PlaySendResult(PlaySendOutcome.Failed, "no_webview");
                }

                var core = (CoreWebView2)coreObj;
                capabilities = host.ResolveCapabilities(bundle, playTabHost!);
                PlaySendTraceMapper.LogCapabilities(capabilities, PlayWebViewCoreBridge.GetSource(coreObj));

                var turnService = host.GetOrCreateTurnService(playTabHost!);
                if (turnService is null)
                {
                    host.SetComposeStatus("Adventure bridge is not ready.", composeInjection);
                    host.ShowSendError(
                        PlaySendTrace.FormatRunContextForError("Adventure bridge is not ready."));
                    await host.RestoreComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", "turn_service_missing");
                    return new PlaySendResult(PlaySendOutcome.Failed, "turn_service_missing");
                }

                host.ActivePlayTabHost = playTabHost;
                host.SetComposeStatus("Sending to ChatGPT…", composeInjection);

                bundle = AdventureStore.Load(adventureId) ?? bundle;
                var linkedProject = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);
                if (linkedProject)
                {
                    var threadReady = await host.RequireLinkedPlayThreadForSendAsync(bundle, core);
                    if (threadReady is not null && !threadReady.IsReady)
                    {
                        var threadError = AdventureNavigationService.FormatPlaySessionError(threadReady);
                        host.SetComposeStatus(threadError, composeInjection);
                        host.ShowSendError(
                            PlaySendTrace.FormatRunContextForError(threadError),
                            isWarning: true);
                        await host.RestoreComposeInputAsync(playerLine, composeInjection);
                        traceScope.Complete("failed", "play_thread_not_ready");
                        return new PlaySendResult(PlaySendOutcome.Failed, "play_thread_not_ready");
                    }
                }

                var priorThreadUserMessageCount = await turnService.GetUserTurnCountAsync(core, cancellationToken);
                host.ArtifactStore.Bind(bundle);

                PreparedSendArtifact? artifact;
                PromptInjectionPrepareResult prepared;
                var usePrebuiltPacket = false;
                var cachedArtifact = host.ArtifactStore.RequireForSend();
                if (cachedArtifact is not null
                    && string.Equals(cachedArtifact.PlayerLine, playerLine, StringComparison.Ordinal)
                    && cachedArtifact.PriorThreadUserMessageCount == priorThreadUserMessageCount)
                {
                    artifact = cachedArtifact;
                    prepared = PreparedSendArtifactMapper.ToPrepareResult(artifact);
                    usePrebuiltPacket = ConversationStreamParser.IsInjectedContextUserMessage(playerLine);
                    PlaySendTraceMapper.LogArtifactLoaded(artifact, fromCache: true);
                }
                else
                {
                    var prepareSession = PlayPacketPrepareSession.Prepare(
                        new PlayPacketPrepareRequest
                        {
                            Bundle = bundle,
                            ComposeText = playerLine,
                            AttachmentContext = attachmentContext,
                            ConsumeContinuationQueue = false,
                            ApplySurfaceActions = false,
                            PriorThreadUserMessageCount = priorThreadUserMessageCount,
                            UserChoseInlineFallback = userChoseInlineFallback,
                        },
                        (_, _, _) => playerLine);

                    playerLine = prepareSession.PlayerLine;
                    prepared = prepareSession.Prepared;
                    usePrebuiltPacket = prepareSession.UsePrebuiltPacket;
                    artifact = PreparedSendArtifactBuilder.FromPrepareResult(
                        playerLine,
                        prepared,
                        priorThreadUserMessageCount,
                        bundle);
                    PlaySendTraceMapper.LogArtifactLoaded(artifact, fromCache: false);
                }

                PlaySendTrace.Event(
                    PlaySendTraceEvents.PacketPrepared,
                    PlaySendCategory.Host,
                    PlaySendLevel.Info,
                    "Merged packet prepared",
                    data: new
                    {
                        wasTrimmed = prepared.WasTrimmed,
                        playerLineLength = playerLine.Length,
                        mergedLength = prepared.MergedText.Length,
                        hash = prepared.Hash,
                        priorThreadUserMessageCount,
                        attachmentKinds = AttachmentSendPolicy.AttachmentKinds(attachmentContext),
                    });

                host.SetMergedPreview(bundle, prepared.MergedText);
                if (prepared.WasTrimmed)
                    host.SetComposeStatus("Packet was trimmed to fit size limits.", composeInjection);

                var injectionGuard = PlayInjectionSendGuard.Validate(bundle, prepared, usePrebuiltPacket);
                if (!injectionGuard.Ok)
                {
                    host.SetComposeStatus(injectionGuard.UserMessage ?? "Packet validation failed.", composeInjection);
                    host.ShowSendError(
                        PlaySendTrace.FormatRunContextForError(
                            injectionGuard.UserMessage ?? "Packet validation failed."),
                        isWarning: true);
                    await host.RestoreComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", injectionGuard.DiagnosticCode);
                    return new PlaySendResult(PlaySendOutcome.Failed, injectionGuard.DiagnosticCode);
                }

                await host.PrefetchSendWarmupAsync(core, bundle);
                var assistantBaseline = await turnService.GetAssistantTurnCountAsync(core, cancellationToken);

                var displayPlayerLine = usePrebuiltPacket
                    ? ConversationStreamParser.ExtractTranscriptPlayerText(playerLine)
                      ?? AdventureBootstrapService.GetOpeningPlayerLine(bundle.Scenario)
                    : AttachmentSendPolicy.ResolveDisplayPlayerLine(
                        bundle,
                        playerLine,
                        attachmentContext);

                if (pendingAttachments.Count > 0 && !attachmentsPreStaged)
                    host.SetComposeStatus("Staging attachments…", composeInjection);

                IReadOnlyList<DomAttachmentPayload>? domAttachments = null;
                if (pendingAttachments.Count > 0)
                {
                    domAttachments = pendingAttachments
                        .Select(a => new DomAttachmentPayload
                        {
                            Name = a.Name,
                            MimeType = a.MimeType,
                            Content = a.Content,
                        })
                        .ToList();
                }

                var deliveryResult = await host.DeliverPacketAsync(new PlaySendDeliveryRequest(
                    core,
                    bundle,
                    capabilities,
                    turnService,
                    prepared.MergedText,
                    displayPlayerLine,
                    prepared.Hash,
                    domAttachments,
                    attachmentsPreStaged));

                var verification = await DeliveryVerifier.VerifyAsync(
                    turnService,
                    core,
                    artifact,
                    deliveryResult,
                    priorThreadUserMessageCount,
                    capabilities.DeliveryChannel,
                    cancellationToken);

                if (!verification.Verified)
                {
                    host.InvalidatePlayContext(adventureId);
                    host.CopyToClipboard(prepared.MergedText);
                    var failureDetail = verification.FailureCode == "delivery_failed"
                                        && !string.IsNullOrWhiteSpace(deliveryResult.Error)
                        ? $"{verification.FailureCode} ({deliveryResult.Error})"
                        : verification.FailureCode;
                    var error = $"Delivery could not be verified ({failureDetail}).";
                    host.SetComposeStatus($"Send failed: {error}", composeInjection);
                    host.ShowSendError(
                        PlaySendTrace.FormatRunContextForError(
                            error + "\n\nThe merged packet was copied to your clipboard."),
                        isWarning: true);
                    await host.RestoreComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", verification.FailureCode);
                    return new PlaySendResult(PlaySendOutcome.Failed, verification.FailureCode);
                }

                if (!deliveryResult.Success)
                {
                    host.InvalidatePlayContext(adventureId);
                    host.CopyToClipboard(prepared.MergedText);
                    var error = deliveryResult.Error ?? "Could not send the prompt to ChatGPT.";
                    host.SetComposeStatus($"Send failed: {error}", composeInjection);
                    host.ShowSendError(
                        PlaySendTrace.FormatRunContextForError(
                            error + "\n\nThe merged packet was copied to your clipboard."),
                        isWarning: true);
                    await host.RestoreComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", error);
                    return new PlaySendResult(PlaySendOutcome.Failed, error);
                }

                turn = TurnTimelineService.CreateTurn(bundle, playerLine);
                turnService.RecordPrompt(
                    bundle,
                    turn,
                    artifact,
                    new FlightDeliverySnapshot
                    {
                        Channel = capabilities.DeliveryChannel.ToString(),
                        Outcome = "ok",
                        Verified = true,
                        ConversationId = deliveryResult.ConversationId,
                    },
                    PlaySendTrace.ActiveRunId,
                    bundle.Metadata.LastDispatchedUtilityJobs);

                if (!string.IsNullOrWhiteSpace(deliveryResult.ConversationId))
                {
                    AdventureSessionService.AttachTurnToSession(bundle, turn);
                    PlayTurnScopeService.AssignConversation(turn, deliveryResult.ConversationId);
                    PlayThreadBindingService.MarkVerified(bundle, deliveryResult.ConversationId);
                    if (string.IsNullOrWhiteSpace(
                            AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.PinnedTabKey))
                    {
                        // Pin is applied by host success handler when WebView is available.
                    }

                    AdventureStore.Save(bundle);
                }

                host.ClearComposePrompt(composeInjection);

                var logStatus = await host.CompleteTurnAfterSendAsync(new PlaySendTurnCompletionRequest(
                    bundle,
                    turn,
                    deliveryResult,
                    core,
                    turnService,
                    composeInjection,
                    assistantBaseline));

                var successStatus = string.IsNullOrWhiteSpace(logStatus)
                    ? $"Sent injected packet ({prepared.MergedText.Length:N0} chars)."
                    : $"{logStatus} ({prepared.MergedText.Length:N0} chars sent)";

                host.OnSendSucceeded(new PlaySendSuccessRequest(
                    adventureId,
                    bundle,
                    playTabHost!,
                    composeInjection,
                    successStatus,
                    prepared.MergedText.Length));

                traceScope.Complete("ok", data: new { deliveryResult.ConversationId, verification.Channel });
                return new PlaySendResult(
                    PlaySendOutcome.Ok,
                    ConversationId: deliveryResult.ConversationId);
            }
            catch (Exception ex)
            {
                host.InvalidatePlayContext(adventureId);
                host.SetComposeStatus($"Send failed: {ex.Message}", composeInjection);
                host.ShowSendError(PlaySendTrace.FormatRunContextForError(ex.Message));
                if (!string.IsNullOrWhiteSpace(playerLine))
                    await host.RestoreComposeInputAsync(playerLine, composeInjection);

                traceScope.Complete("failed", ex.Message, new { exception = ex.GetType().Name });
                return new PlaySendResult(PlaySendOutcome.Failed, ex.Message);
            }
        }
        finally
        {
            host.DecrementActiveSendCount();
            host.ReleaseSendGate();
            host.OnSendFinally(composeInjection);
        }
    }

    private static string? TruncateForLog(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[..maxChars] + "…";
    }
}
