using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class AdventureTurnService
{
    private readonly ChatGptAdventureBridgeInjection _bridge;
    private readonly SemaphoreSlim _bridgeGate = new(1, 1);
    private TaskCompletionSource<AdventureBridgeMessage>? _pendingTurn;
    private string[]? _pendingTurnAcceptedTypes;
    private ChatGptConversationSendService? _conversationSend;
    private string? _lastApiUserMessageId;

    public AdventureTurnService(ChatGptAdventureBridgeInjection bridge)
    {
        _bridge = bridge;
        _bridge.MessageReceived += OnBridgeMessage;
    }

    public void SetConversationSendService(ChatGptConversationSendService? service) =>
        _conversationSend = service;

    public Task<bool> EnsureUtilityBridgeReadyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default) =>
        _bridge.EnsureBridgeReadyAsync(core, cancellationToken);

    /// <summary>
    /// Probes adventure-bridge composer/submit via ping. Does not use the API-bridge ping shortcut.
    /// </summary>
    public async Task<BridgeHealthStatus> GetAdventureComposerHealthAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("pong", "probeResult");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new { action = "ping" });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                return new BridgeHealthStatus
                {
                    BridgeReachable = msg.Type is "pong" or "probeResult",
                    ComposerFound = msg.ComposerFound,
                    SubmitFound = msg.SubmitFound,
                    ConversationId = msg.ConversationId,
                };
            }
            catch (TimeoutException)
            {
                return new BridgeHealthStatus
                {
                    BridgeReachable = false,
                    Error = "ping_timeout",
                };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    public async Task EnsureUtilityComposerReadyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default,
        int maxWaitSeconds = 45,
        string? conversationId = null,
        string? gizmoId = null) =>
        await WaitForUtilityComposerSubmitReadyAsync(
            core,
            cancellationToken,
            maxWaitSeconds: maxWaitSeconds,
            conversationId: conversationId,
            gizmoId: gizmoId);

    internal void RecordPrompt(
        AdventureBundle bundle,
        TurnRecord turn,
        PreparedSendArtifact artifact,
        FlightDeliverySnapshot delivery,
        Guid? playSendTraceRunId = null,
        IReadOnlyList<PendingUtilityInjection>? utilityDispatches = null)
    {
        FlightRecordCaptureService.CapturePlaySend(
            bundle,
            turn,
            artifact,
            delivery,
            playSendTraceRunId,
            utilityDispatches);
    }

    public async Task<ComposerFillResult> FillComposerAsync(
        CoreWebView2 core,
        string text,
        CancellationToken cancellationToken = default)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("composerFilled");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new { action = "fillComposer", text });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                return new ComposerFillResult
                {
                    Success = msg.Type == "composerFilled" && msg.Ok,
                    Error = msg.Error,
                    ConversationId = msg.ConversationId,
                };
            }
            catch (TimeoutException)
            {
                return new ComposerFillResult { Success = false, Error = "fill_timeout" };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    public async Task<CaptureAssistantResult> CaptureAssistantAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        var conversationId = ResolveConversationIdForSend(bundle, core);
        if (PlaySendDeliveryPolicy.ShouldUseApiCapture(bundle)
            && _conversationSend is not null
            && !string.IsNullOrWhiteSpace(conversationId))
        {
            var apiResult = await _conversationSend.CaptureAssistantViaApiAsync(
                core,
                conversationId,
                _lastApiUserMessageId,
                cancellationToken);

            if (apiResult.Success && !string.IsNullOrWhiteSpace(apiResult.Text))
            {
                var apiText = ContextTagFormat.StripTaggedBlocks(apiResult.Text);
                if (!PlayTurnScopeService.IsIncompleteNarratorCapture(apiText))
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.ApiCaptureUsed,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Info,
                        "Captured assistant text via API cache/fetch",
                        data: new { conversationId, textLength = apiText.Length });

                    return new CaptureAssistantResult
                    {
                        Success = true,
                        Text = apiText,
                        ConversationId = apiResult.ConversationId ?? conversationId,
                    };
                }

                PlaySendTrace.Event(
                    PlaySendTraceEvents.ApiCaptureFetch,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Debug,
                    "API capture returned placeholder; falling back to DOM",
                    data: new { conversationId, error = apiResult.Error });
            }
            else
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.ApiCaptureFetch,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Debug,
                    "API capture unavailable; falling back to DOM",
                    data: new { conversationId, error = apiResult.Error });
            }
        }

        var domResult = await CaptureLastAssistantAsync(
            core,
            expectedConversationId: conversationId,
            cancellationToken: cancellationToken);
        if (domResult.Success
            && PlayTurnScopeService.IsIncompleteNarratorCapture(domResult.Text))
        {
            domResult = new CaptureAssistantResult
            {
                Success = false,
                Text = domResult.Text,
                Error = "capture_premature",
                ConversationId = domResult.ConversationId,
            };
        }

        if (domResult.Success)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.DomCaptureFallback,
                PlaySendCategory.Bridge,
                PlaySendLevel.Warn,
                "Captured assistant text via DOM fallback",
                data: new { conversationId });
        }

        return domResult;
    }

    public async Task<ThreadTranscriptCaptureResult> CaptureThreadTranscriptAsync(
        CoreWebView2 core,
        int maxPairs,
        CancellationToken cancellationToken = default)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("transcriptResult");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new { action = "captureThreadTranscript", maxPairs });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                return ParseThreadTranscriptResult(msg);
            }
            catch (TimeoutException)
            {
                return new ThreadTranscriptCaptureResult { Success = false, Error = "capture_timeout" };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    private static ThreadTranscriptCaptureResult ParseThreadTranscriptResult(AdventureBridgeMessage msg)
    {
        if (msg.Type != "transcriptResult" || !msg.Ok)
        {
            return new ThreadTranscriptCaptureResult
            {
                Success = false,
                Error = msg.Error ?? "transcript_capture_failed",
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(msg.RawJson);
            var pairs = new List<TranscriptTurnPair>();
            if (doc.RootElement.TryGetProperty("pairs", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var player = item.TryGetProperty("player", out var p) && p.ValueKind == JsonValueKind.String
                        ? p.GetString() ?? ""
                        : "";
                    var narrator = item.TryGetProperty("narrator", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? ""
                        : "";
                    if (string.IsNullOrWhiteSpace(player) && string.IsNullOrWhiteSpace(narrator))
                        continue;

                    pairs.Add(new TranscriptTurnPair
                    {
                        PlayerText = TranscriptTextSanitizer.Sanitize(player),
                        NarratorText = TranscriptTextSanitizer.Sanitize(narrator),
                    });
                }
            }

            return new ThreadTranscriptCaptureResult
            {
                Success = pairs.Count > 0,
                TurnPairs = pairs,
                ConversationId = msg.ConversationId,
                Error = pairs.Count == 0 ? "no_transcript_pairs" : null,
            };
        }
        catch (JsonException ex)
        {
            return new ThreadTranscriptCaptureResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<CaptureAssistantResult> CaptureLastAssistantAsync(
        CoreWebView2 core,
        string? expectedConversationId = null,
        string? expectedGizmoId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(expectedConversationId)
            && !string.IsNullOrWhiteSpace(expectedGizmoId)
            && !AdventurePlayContextService.IsOnConversationPage(core.Source, expectedConversationId))
        {
            await UtilityConversationPageService.EnsureOnProjectConversationAsync(
                core,
                expectedConversationId,
                expectedGizmoId,
                cancellationToken);
        }

        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("captureResult");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new { action = "captureLastAssistant" });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                var text = msg.Text;
                if (msg.Ok && !string.IsNullOrWhiteSpace(text))
                    text = ContextTagFormat.StripTaggedBlocks(text);

                if (msg.Type == "captureResult"
                    && msg.Ok
                    && !string.IsNullOrWhiteSpace(expectedConversationId)
                    && !string.IsNullOrWhiteSpace(msg.ConversationId)
                    && !string.Equals(msg.ConversationId, expectedConversationId, StringComparison.OrdinalIgnoreCase))
                {
                    return new CaptureAssistantResult
                    {
                        Success = false,
                        Error = "conversation_mismatch",
                        ConversationId = msg.ConversationId,
                    };
                }

                return new CaptureAssistantResult
                {
                    Success = msg.Type == "captureResult" && msg.Ok && !string.IsNullOrWhiteSpace(text),
                    Text = text,
                    Error = msg.Error,
                    ConversationId = msg.ConversationId,
                };
            }
            catch (TimeoutException)
            {
                return new CaptureAssistantResult { Success = false, Error = "capture_timeout" };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    public async Task<AdventureTurnResult> SendPromptAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        int timeoutMs = 120000,
        CancellationToken cancellationToken = default) =>
        await ExecuteSendPromptAsync(core, bundle, packetText, regenerate: false, timeoutMs, cancellationToken);

    public async Task<AdventureTurnResult> SubmitPromptAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        string? displayPlayerLine = null,
        string? packetHash = null,
        IReadOnlyList<ChatAttachmentRef>? attachments = null,
        IReadOnlyList<DomAttachmentPayload>? domAttachments = null,
        bool attachmentsPreStaged = false,
        PlayDeliveryChannel deliveryChannel = PlayDeliveryChannel.None,
        CancellationToken cancellationToken = default)
    {
        var timeoutMs = attachments is { Count: > 0 }
                        || domAttachments is { Count: > 0 }
                        || attachmentsPreStaged
            ? 120_000
            : 60_000;
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            // Composer attachments use native staging only. API upload before DOM fallback
            // causes ChatGPT to reject the same file as a duplicate.
            if (attachmentsPreStaged || domAttachments is { Count: > 0 })
                return await SubmitDomAttachmentPromptAsync(
                    core,
                    bundle,
                    packetText,
                    displayPlayerLine,
                    packetHash,
                    timeoutMs,
                    domAttachments ?? [],
                    attachmentsPreStaged,
                    cancellationToken);

            if (attachments is { Count: > 0 })
            {
                return new AdventureTurnResult
                {
                    Success = false,
                    Error = "play_attach_requires_dom_staging",
                    RequiresManualFallback = true,
                    PacketText = packetText,
                };
            }

            var conversationId = ResolveConversationIdForSend(bundle, core);
            var useApiText = PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle, deliveryChannel);

            if (_conversationSend is not null
                && !string.IsNullOrWhiteSpace(conversationId)
                && useApiText)
            {
                var apiResult = await _conversationSend.SendUserMessageAsync(
                    core,
                    conversationId,
                    bundle.Metadata.LinkedProjectId,
                    packetText,
                    cancellationToken);

                if (!apiResult.Success)
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.ApiSendFallbackDom,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Warn,
                        $"API send failed ({apiResult.Error}); falling back to DOM submit",
                        data: new { error = apiResult.Error, conversationId });
                }
                else
                {
                    return await BuildApiSubmitSuccessResultAsync(
                        core,
                        bundle,
                        packetText,
                        displayPlayerLine,
                        packetHash,
                        conversationId,
                        apiResult);
                }

                if (deliveryChannel == PlayDeliveryChannel.Api
                    && !ShouldDomFallbackAfterApiTextFailure(apiResult.Error))
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.BridgeSubmitResult,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Error,
                        $"API send failed ({apiResult.Error}); DOM fallback disabled for API channel",
                        outcome: "api_send_failed",
                        data: new { error = apiResult.Error, conversationId });

                    return new AdventureTurnResult
                    {
                        Success = false,
                        Error = apiResult.Error ?? "api_send_failed",
                        RequiresManualFallback = true,
                        PacketText = packetText,
                    };
                }
            }
            else if (!PlaySendDeliveryPolicy.ShouldUseApiTextPlaySend(bundle, deliveryChannel)
                     && !string.IsNullOrWhiteSpace(conversationId))
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.DomSendPreferred,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Info,
                    "Skipping conversation API; using DOM composer submit",
                    data: new { conversationId, packetLength = packetText.Length });
            }

            return await SubmitPromptViaDomAsync(
                core,
                bundle,
                packetText,
                displayPlayerLine,
                packetHash,
                timeoutMs,
                domAttachments: null,
                hostCdpStaged: false,
                attachmentsPreStaged: false,
                cancellationToken);
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    private async Task<AdventureTurnResult> SubmitDomAttachmentPromptAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        string? displayPlayerLine,
        string? packetHash,
        int timeoutMs,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        bool attachmentsPreStaged,
        CancellationToken cancellationToken)
    {
        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitStart,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            attachmentsPreStaged
                ? "Using pre-uploaded native composer attachments (skipping API upload/send)"
                : "Using native composer attachment path (skipping API upload/send)",
            data: new
            {
                attachmentCount = domAttachments.Count,
                packetLength = packetText.Length,
                attachmentsPreStaged,
                source = core.Source,
            });

        try
        {
            await _bridge.InjectAsync(core);
            var hostCdpStaged = attachmentsPreStaged;
            if (!attachmentsPreStaged)
            {
                var stage = await NativeComposerDomStaging.StageAttachmentsAsync(
                    _bridge,
                    core,
                    domAttachments,
                    attachmentsPreStaged: false,
                    cancellationToken);
                hostCdpStaged = stage.HostCdpStaged;

                if (!hostCdpStaged)
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.BridgeSubmitInvoke,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Warn,
                        $"CDP attachment staging failed ({stage.CdpError}); falling back to in-page staging",
                        outcome: "cdp_stage_failed",
                        data: new { error = stage.CdpError });
                }
            }

            return await SubmitPromptViaDomAsync(
                core,
                bundle,
                packetText,
                displayPlayerLine,
                packetHash,
                timeoutMs,
                domAttachments,
                hostCdpStaged,
                attachmentsPreStaged,
                cancellationToken);
        }
        finally
        {
            NativeComposerFileStaging.CleanupStagedFiles();
        }
    }

    private async Task<AdventureTurnResult> BuildApiSubmitSuccessResultAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        string? displayPlayerLine,
        string? packetHash,
        string conversationId,
        ConversationSendResult apiResult)
    {
        if (AdventurePlayContextService.ShouldAcceptLinkedConversationId(
                bundle,
                apiResult.ConversationId ?? conversationId))
        {
            bundle.Metadata.LinkedConversationId = apiResult.ConversationId ?? conversationId;
        }

        _lastApiUserMessageId = apiResult.ParentMessageId;

        await ChatGptAdventureBridgeInjection.StampUserDisplayAsync(
            core,
            displayPlayerLine,
            packetHash);

        string? narratorText = null;
        if (!string.IsNullOrWhiteSpace(apiResult.AssistantText))
            narratorText = ContextTagFormat.StripTaggedBlocks(apiResult.AssistantText);

        return new AdventureTurnResult
        {
            Success = true,
            ConversationId = apiResult.ConversationId ?? conversationId,
            PacketText = packetText,
            NarratorText = narratorText,
        };
    }

    private static string? ResolveConversationIdForSend(AdventureBundle bundle, CoreWebView2 core) =>
        PlayConversationIdResolver.Resolve(bundle, core);

    /// <summary>
    /// Atomic utility turn: strict nav, bridge gate, sendPrompt, turnComplete with assistant text.
    /// </summary>
    internal const int UtilityMinCapturedTextLength = 16;

    internal static bool IsUtilityCapturePremature(string? jobId, string assistantText) =>
        !string.IsNullOrWhiteSpace(jobId)
            ? !GenerationJobHandlers.IsSettledJobResponse(jobId, assistantText, streamComplete: true)
            : assistantText.Length < UtilityMinCapturedTextLength;

    /// <summary>
    /// Sends a utility job on the play thread wrapped in [[cgw:utility]] tags.
    /// Does not create story turns.
    /// </summary>
    public async Task<ConversationSendResult> SubmitInlineUtilityJobAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string jobId,
        string jobBody,
        CancellationToken cancellationToken = default)
    {
        var conversationId = ResolveConversationIdForSend(bundle, core);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "play_thread_unlinked",
            };
        }

        var wrapped = ContextTagFormat.WrapUtilityJob(jobId, jobBody);
        return await SubmitUtilityJobAsync(
            core,
            conversationId,
            bundle.Metadata.LinkedProjectId,
            wrapped,
            jobId: jobId,
            cancellationToken: cancellationToken);
    }

    public async Task<ConversationSendResult> SubmitUtilityJobAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        int? timeoutMs = null,
        string? jobId = null,
        bool skipPageEnsure = false,
        int? maxComposerWaitSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeoutMs ?? ComputeUtilityJobTimeoutMs(messageText.Length);

        if (!skipPageEnsure && !string.IsNullOrWhiteSpace(gizmoId))
        {
            var page = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                core,
                conversationId,
                gizmoId,
                cancellationToken);
            if (!page.Success)
            {
                return new ConversationSendResult
                {
                    Success = false,
                    Error = page.Error ?? "utility_page_not_ready",
                    ConversationId = conversationId,
                };
            }
        }

        if (!await _bridge.EnsureBridgeReadyAsync(core, cancellationToken))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "bridge_not_ready",
                ConversationId = conversationId,
            };
        }

        await WaitForUtilityComposerSubmitReadyAsync(
            core,
            cancellationToken,
            skipNavigation: skipPageEnsure,
            maxWaitSeconds: maxComposerWaitSeconds ?? 45,
            conversationId: conversationId,
            gizmoId: gizmoId);

        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            return await SubmitUtilityJobViaDomAsync(
                core,
                conversationId,
                gizmoId,
                messageText,
                effectiveTimeout,
                jobId,
                skipPageVerify: skipPageEnsure,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    /// <summary>
    /// Sends a utility/job packet via atomic DOM turn (sendPrompt → turnComplete).
    /// </summary>
    public Task<ConversationSendResult> SubmitUtilityMessageAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        CancellationToken cancellationToken = default) =>
        SubmitUtilityJobAsync(core, conversationId, gizmoId, messageText, cancellationToken: cancellationToken);

    /// <summary>
    /// Utility/job packet with staged file attachments via DOM composer (worker lane).
    /// </summary>
    public async Task<ConversationSendResult> SubmitUtilityJobWithAttachmentsAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        string? jobId = null,
        bool skipPageEnsure = false,
        bool allowKeyboardSubmitOnProjectHome = false,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = ComputeUtilityJobTimeoutMs(messageText.Length);

        var pageEnsured = false;
        if (!skipPageEnsure && !string.IsNullOrWhiteSpace(gizmoId))
        {
            var page = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                core,
                conversationId,
                gizmoId,
                cancellationToken);
            if (!page.Success)
            {
                return new ConversationSendResult
                {
                    Success = false,
                    Error = page.Error ?? "utility_page_not_ready",
                    ConversationId = conversationId,
                };
            }

            pageEnsured = true;
        }

        if (!await _bridge.EnsureBridgeReadyAsync(core, cancellationToken))
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "bridge_not_ready",
                ConversationId = conversationId,
            };
        }

        await WaitForUtilityComposerSubmitReadyAsync(
            core,
            cancellationToken,
            skipNavigation: skipPageEnsure || pageEnsured,
            conversationId: conversationId,
            gizmoId: gizmoId);

        var pageHref = await UtilityConversationPageService.GetPageHrefAsync(core);
        var health = await GetAdventureComposerHealthAsync(core, cancellationToken);
        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitInvoke,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Utility DOM attach composer ready",
            outcome: "attach_probe",
            data: new
            {
                pageHref,
                composerFound = health.ComposerFound,
                submitFound = health.SubmitFound,
                conversationId = health.ConversationId ?? conversationId,
                attachmentCount = domAttachments.Count,
                skipPageEnsure,
                pageEnsured,
                allowKeyboardSubmitOnProjectHome,
            });

        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            await NativeComposerFileStaging.ExposeComposerForUploadAsync(core, cancellationToken);
            var stage = await NativeComposerDomStaging.StageAttachmentsAsync(
                _bridge,
                core,
                domAttachments,
                attachmentsPreStaged: false,
                cancellationToken);
            var hostCdpStaged = stage.HostCdpStaged;
            if (!hostCdpStaged)
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitInvoke,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Warn,
                    $"Utility CDP attachment staging failed ({stage.CdpError})",
                    outcome: "cdp_stage_failed",
                    data: new { error = stage.CdpError, attachmentCount = domAttachments.Count });
            }
            else
            {
                await NativeComposerFileStaging.PrepareNativeComposerAsync(core, cancellationToken);
                var totalBytes = domAttachments.Sum(a => a.Content.Length);
                var uploadReady = await NativeComposerFileStaging.WaitForUploadReadyAsync(
                    core,
                    totalBytes,
                    maxWait: TimeSpan.FromMinutes(2),
                    cancellationToken);
                if (!uploadReady.Success)
                {
                    return new ConversationSendResult
                    {
                        Success = false,
                        Error = uploadReady.Error ?? "attachment_not_ready",
                        ConversationId = conversationId,
                    };
                }
            }

            return await SubmitUtilityJobViaDomAsync(
                core,
                conversationId,
                gizmoId,
                messageText,
                effectiveTimeout,
                jobId,
                skipPageVerify: true,
                hostCdpStaged: hostCdpStaged,
                useWrapperAttachmentStash: !hostCdpStaged,
                allowKeyboardSubmitOnProjectHome: allowKeyboardSubmitOnProjectHome,
                cancellationToken);
        }
        finally
        {
            await NativeComposerFileStaging.RestoreComposerExposeAsync(core);
            NativeComposerFileStaging.CleanupStagedFiles();
            _bridgeGate.Release();
        }
    }

    internal static int ComputeUtilityJobTimeoutMs(int messageLength) =>
        messageLength > 4000
            ? Math.Min(180_000, 90_000 + messageLength * 8)
            : 120_000;

    internal static int ComputeComposerStableWaitMs(int messageLength) =>
        messageLength <= 200
            ? 1400
            : Math.Min(15_000, Math.Max(3500, 600 + (int)(messageLength * 1.2)));

    internal static string MapUtilityBridgeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "capture_no_assistant";

        if (string.Equals(error, "submit_not_verified", StringComparison.OrdinalIgnoreCase))
            return "submit_not_observed";

        if (string.Equals(error, "timeout", StringComparison.OrdinalIgnoreCase))
            return "capture_timeout";

        if (string.Equals(error, "composer_not_found", StringComparison.OrdinalIgnoreCase))
            return "utility_page_not_ready";

        return error;
    }

    internal static string NormalizeUtilityCapturedAssistantText(string text)
    {
        if (ContextTagFormat.IsUtilityResponseTagged(text))
            return ContextTagFormat.UnwrapUtilityJobResponse(text);

        return ContextTagFormat.StripTaggedBlocks(text);
    }

    public async Task<int> GetAssistantTurnCountAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("assistantTurnCount");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new { action = "getAssistantTurnCount" });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                if (msg.Type == "assistantTurnCount" && msg.AssistantTurnCount is >= 0)
                    return msg.AssistantTurnCount.Value;
            }
            catch (TimeoutException)
            {
                /* fall through */
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }

        return 0;
    }

    public async Task<int> GetUserTurnCountAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            return await GetUserTurnCountCoreAsync(core, cancellationToken);
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    /// <summary>
    /// Caller must hold <see cref="_bridgeGate"/> when invoked from an already-gated send path.
    /// </summary>
    private async Task<int> GetUserTurnCountCoreAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await _bridge.InjectAsync(core);
        var pending = BeginPendingTurn("userTurnCount");

        await using var reg = cancellationToken.Register(() =>
            pending.TrySetCanceled(cancellationToken));

        _bridge.SendCommand(core, new { action = "getUserTurnCount" });

        try
        {
            var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            if (msg.Type == "userTurnCount" && msg.AssistantTurnCount is >= 0)
                return msg.AssistantTurnCount.Value;
        }
        catch (TimeoutException)
        {
            /* fall through */
        }
        finally
        {
            EndPendingTurn();
        }

        return 0;
    }

    public async Task<CaptureAssistantResult> CaptureStableAssistantAsync(
        CoreWebView2 core,
        int baselineCount,
        int timeoutMs,
        string expectedConversationId,
        string? expectedGizmoId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(expectedGizmoId))
        {
            var page = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                core,
                expectedConversationId,
                expectedGizmoId,
                cancellationToken);
            if (!page.Success)
            {
                return new CaptureAssistantResult
                {
                    Success = false,
                    Error = page.Error ?? "utility_page_not_ready",
                    ConversationId = expectedConversationId,
                };
            }
        }

        await _bridge.EnsureBridgeReadyAsync(core, cancellationToken);

        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("captureResult");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new
            {
                action = "captureStableAssistant",
                baselineCount,
                timeoutMs,
            });

            try
            {
                var msg = await pending.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(timeoutMs + 5000),
                    cancellationToken);
                var text = msg.Text;
                if (msg.Ok && !string.IsNullOrWhiteSpace(text))
                    text = NormalizeUtilityCapturedAssistantText(text);

                if (msg.Type == "captureResult"
                    && msg.Ok
                    && !string.IsNullOrWhiteSpace(expectedConversationId)
                    && !string.IsNullOrWhiteSpace(msg.ConversationId)
                    && !string.Equals(msg.ConversationId, expectedConversationId, StringComparison.OrdinalIgnoreCase))
                {
                    return new CaptureAssistantResult
                    {
                        Success = false,
                        Error = "conversation_mismatch",
                        ConversationId = msg.ConversationId,
                    };
                }

                if (msg.Type == "captureResult" && msg.Ok && !string.IsNullOrWhiteSpace(text))
                {
                    return new CaptureAssistantResult
                    {
                        Success = true,
                        Text = text,
                        ConversationId = msg.ConversationId ?? expectedConversationId,
                    };
                }

                return new CaptureAssistantResult
                {
                    Success = false,
                    Error = string.Equals(msg.Error, "timeout", StringComparison.OrdinalIgnoreCase)
                        ? "capture_timeout"
                        : msg.Error ?? "capture_no_assistant",
                    ConversationId = msg.ConversationId ?? expectedConversationId,
                };
            }
            catch (TimeoutException)
            {
                return new CaptureAssistantResult
                {
                    Success = false,
                    Error = "capture_timeout",
                    ConversationId = expectedConversationId,
                };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    /// <summary>
    /// Utility attachment send — mirrors play <c>submitPrompt</c> (not <c>sendPrompt</c>) so submit dispatches reliably.
    /// Assistant capture is handled by the worker pull lane after push correlation.
    /// </summary>
    private async Task<ConversationSendResult> SubmitUtilityAttachmentViaDomAsync(
        CoreWebView2 core,
        string conversationId,
        string messageText,
        int timeoutMs,
        string? jobId,
        bool requireProjectContext,
        bool hostCdpStaged,
        bool useWrapperAttachmentStash,
        string? pageHref,
        UtilityPageVerifyResult? verify,
        bool allowKeyboardSubmitOnProjectHome,
        CancellationToken cancellationToken)
    {
        var priorUserCount = await GetUserTurnCountCoreAsync(core, cancellationToken);
        var pending = BeginPendingTurn("turnComplete", "promptSubmitted");

        await using var reg = cancellationToken.Register(() =>
            pending.TrySetCanceled(cancellationToken));

        // Host already CDP-staged and polled upload — tell bridge files are on the composer.
        var attachmentsPreStaged = hostCdpStaged && !useWrapperAttachmentStash;
        var invoked = await _bridge.InvokeSubmitPromptAsync(
            core,
            messageText,
            requireProjectContext,
            attachmentsPreStaged: attachmentsPreStaged,
            hostCdpStaged: false,
            useWrapperAttachmentStash: useWrapperAttachmentStash && !hostCdpStaged,
            allowKeyboardSubmitOnProjectHome: allowKeyboardSubmitOnProjectHome);

        if (!invoked)
        {
            _bridge.SendSubmitPromptCommand(
                core,
                messageText,
                requireProjectContext,
                displayPlayerLine: null,
                packetHash: null,
                useWrapperAttachmentStash: useWrapperAttachmentStash && !hostCdpStaged,
                hostCdpStaged: false,
                attachmentsPreStaged: attachmentsPreStaged,
                allowKeyboardSubmitOnProjectHome: allowKeyboardSubmitOnProjectHome);
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitInvoke,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            attachmentsPreStaged
                ? "Utility submitPrompt with pre-staged attachments"
                : "Utility submitPrompt with attachments posted via PostWebMessage",
            outcome: invoked ? "invoke_ok" : "post_message",
            data: new
            {
                requireProjectContext,
                packetLength = messageText.Length,
                pageHref,
                coreSource = verify?.CoreSource ?? core.Source,
                hrefMatchesTarget = verify?.Matches ?? true,
                channel = "utility",
                timeoutMs,
                attachmentsPreStaged,
                useWrapperAttachmentStash,
            });

        // Bridge attachment path: up to ~90s submit-ready wait + verify retries (12s × 20).
        var hasAttachmentDomPath = hostCdpStaged || useWrapperAttachmentStash;
        var submitTimeoutCapMs = hasAttachmentDomPath ? 240_000 : 120_000;
        var submitTimeoutMs = hasAttachmentDomPath
            ? Math.Max(timeoutMs, submitTimeoutCapMs)
            : Math.Min(timeoutMs, submitTimeoutCapMs);
        AdventureBridgeMessage msg;
        try
        {
            msg = await pending.Task.WaitAsync(
                TimeSpan.FromMilliseconds(submitTimeoutMs),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            var afterCount = await GetUserTurnCountCoreAsync(core, cancellationToken);
            if (afterCount > priorUserCount)
            {
                return await BuildUtilityAttachmentPushSuccessAsync(
                    core,
                    conversationId,
                    cancellationToken);
            }

            return new ConversationSendResult
            {
                Success = false,
                Error = "submit_timeout",
                ConversationId = conversationId,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = ex.Message,
                ConversationId = conversationId,
            };
        }
        finally
        {
            EndPendingTurn();
        }

        if (msg.Type == "promptSubmitted" && msg.Ok)
        {
            return await BuildUtilityAttachmentPushSuccessAsync(
                core,
                string.IsNullOrWhiteSpace(msg.ConversationId) ? conversationId : msg.ConversationId!,
                cancellationToken);
        }

        if (msg.Type == "turnComplete" && msg.Ok && !string.IsNullOrWhiteSpace(msg.Text))
        {
            var convId = string.IsNullOrWhiteSpace(msg.ConversationId) ? conversationId : msg.ConversationId;
            var assistantText = NormalizeUtilityCapturedAssistantText(msg.Text!);
            return new ConversationSendResult
            {
                Success = true,
                ConversationId = convId,
                ParentMessageId = await ResolveDomSubmitParentMessageIdAsync(core, convId!, cancellationToken),
                AssistantText = assistantText,
                StreamComplete = !IsUtilityCapturePremature(jobId, assistantText),
            };
        }

        var recoveredCount = await GetUserTurnCountCoreAsync(core, cancellationToken);
        if (recoveredCount > priorUserCount)
        {
            return await BuildUtilityAttachmentPushSuccessAsync(
                core,
                conversationId,
                cancellationToken);
        }

        return new ConversationSendResult
        {
            Success = false,
            Error = MapUtilityBridgeError(msg.Error) ?? "submit_not_verified",
            ConversationId = conversationId,
        };
    }

    private async Task<ConversationSendResult> BuildUtilityAttachmentPushSuccessAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken)
    {
        ConversationParentCache.Invalidate(conversationId);
        ConversationConduitCache.Invalidate(conversationId);

        return new ConversationSendResult
        {
            Success = true,
            ConversationId = conversationId,
            ParentMessageId = await ResolveDomSubmitParentMessageIdAsync(core, conversationId, cancellationToken),
            StreamComplete = false,
        };
    }

    private async Task<ConversationSendResult> SubmitUtilityJobViaDomAsync(
        CoreWebView2 core,
        string conversationId,
        string? gizmoId,
        string messageText,
        int timeoutMs,
        string? jobId,
        bool skipPageVerify = false,
        bool hostCdpStaged = false,
        bool useWrapperAttachmentStash = false,
        bool allowKeyboardSubmitOnProjectHome = false,
        CancellationToken cancellationToken = default)
    {
        var requireProjectContext = !string.IsNullOrWhiteSpace(gizmoId);

        UtilityPageVerifyResult? verify = null;
        if (!skipPageVerify && !string.IsNullOrWhiteSpace(gizmoId))
        {
            verify = await UtilityConversationPageService.VerifyOnTargetPageAsync(
                core,
                conversationId,
                gizmoId);
            if (!verify.Matches)
            {
                return new ConversationSendResult
                {
                    Success = false,
                    Error = "utility_page_not_ready",
                    ConversationId = conversationId,
                };
            }
        }

        var pageHref = verify?.PageHref ?? await UtilityConversationPageService.GetPageHrefAsync(core);
        var composerStableWaitMs = ComputeComposerStableWaitMs(messageText.Length);
        var hasAttachments = hostCdpStaged || useWrapperAttachmentStash;
        if (hasAttachments)
        {
            return await SubmitUtilityAttachmentViaDomAsync(
                core,
                conversationId,
                messageText,
                timeoutMs,
                jobId,
                requireProjectContext,
                hostCdpStaged,
                useWrapperAttachmentStash,
                pageHref,
                verify,
                allowKeyboardSubmitOnProjectHome,
                cancellationToken);
        }

        var pending = BeginPendingTurn("turnComplete");

        await using var reg = cancellationToken.Register(() =>
            pending.TrySetCanceled(cancellationToken));

        var invoked = await _bridge.InvokeSendPromptAsync(
            core,
            messageText,
            timeoutMs,
            requireProjectContext,
            composerStableWaitMs);
        if (!invoked)
        {
            _bridge.SendCommand(core, new
            {
                action = "sendPrompt",
                text = messageText,
                timeoutMs,
                requireProjectContext,
                composerStableWaitMs,
            });
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitInvoke,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            invoked
                ? "Utility sendPrompt invoked via ExecuteScript"
                : "Utility sendPrompt dispatched via PostWebMessage fallback",
            outcome: invoked ? "invoke_ok" : "send_prompt",
            data: new
            {
                requireProjectContext,
                packetLength = messageText.Length,
                pageHref,
                coreSource = verify?.CoreSource ?? core.Source,
                hrefMatchesTarget = verify?.Matches ?? true,
                channel = "utility",
                timeoutMs,
            });

        AdventureBridgeMessage msg;
        try
        {
            msg = await pending.Task.WaitAsync(
                TimeSpan.FromMilliseconds(timeoutMs + 5000),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "capture_timeout",
                ConversationId = conversationId,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = ex.Message,
                ConversationId = conversationId,
            };
        }
        finally
        {
            EndPendingTurn();
        }

        if (msg.Type == "turnComplete" && msg.Ok && !string.IsNullOrWhiteSpace(msg.Text))
        {
            var convId = string.IsNullOrWhiteSpace(msg.ConversationId) ? conversationId : msg.ConversationId;
            if (!string.IsNullOrWhiteSpace(convId))
            {
                ConversationParentCache.Invalidate(convId);
                ConversationConduitCache.Invalidate(convId);
            }

            var assistantText = NormalizeUtilityCapturedAssistantText(msg.Text!);
            var isSettled = !IsUtilityCapturePremature(jobId, assistantText);
            var conversationDrifted = !string.IsNullOrWhiteSpace(msg.ConversationId)
                && !string.Equals(msg.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase);
            var provisionedConversation = string.IsNullOrWhiteSpace(conversationId)
                && !string.IsNullOrWhiteSpace(msg.ConversationId);

            if (conversationDrifted && !isSettled && !provisionedConversation)
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitResult,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Warn,
                    "Utility DOM turn completed on unexpected conversation",
                    outcome: "conversation_mismatch",
                    data: new
                    {
                        expectedConversationId = conversationId,
                        conversationId = convId,
                        channel = "utility",
                        textLength = assistantText.Length,
                        textPreview = TruncateForTrace(assistantText, 120),
                        jobId,
                    });

                return new ConversationSendResult
                {
                    Success = false,
                    Error = "conversation_mismatch",
                    ConversationId = convId,
                    AssistantText = assistantText,
                    StreamComplete = true,
                };
            }

            if (!isSettled)
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitResult,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Warn,
                    "Utility DOM turn completed with premature capture",
                    outcome: "capture_premature",
                    data: new
                    {
                        conversationId = convId,
                        channel = "utility",
                        textLength = assistantText.Length,
                        textPreview = TruncateForTrace(assistantText, 120),
                        jobId,
                    });

                return new ConversationSendResult
                {
                    Success = false,
                    Error = "capture_premature",
                    ConversationId = convId,
                    AssistantText = assistantText,
                    StreamComplete = true,
                    ParentMessageId = await ResolveDomSubmitParentMessageIdAsync(core, convId, cancellationToken),
                };
            }

            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeSubmitResult,
                PlaySendCategory.Bridge,
                PlaySendLevel.Info,
                "Utility DOM turn completed",
                outcome: "ok",
                data: new
                {
                    conversationId = convId,
                    expectedConversationId = conversationId,
                    conversationDrifted,
                    channel = "utility",
                    textLength = assistantText.Length,
                });

            return new ConversationSendResult
            {
                Success = true,
                ConversationId = convId,
                ParentMessageId = await ResolveDomSubmitParentMessageIdAsync(core, convId, cancellationToken),
                AssistantText = assistantText,
                StreamComplete = true,
            };
        }

        return new ConversationSendResult
        {
            Success = false,
            Error = MapUtilityBridgeError(msg.Error),
            ConversationId = conversationId,
        };
    }

    private static string TruncateForTrace(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    private async Task WaitForUtilityComposerSubmitReadyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken,
        bool skipNavigation = false,
        int maxWaitSeconds = 45,
        string? conversationId = null,
        string? gizmoId = null)
    {
        await _bridge.InjectAsync(core);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(maxWaitSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!skipNavigation
                && !string.IsNullOrWhiteSpace(conversationId)
                && !string.IsNullOrWhiteSpace(gizmoId))
            {
                await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                    core,
                    conversationId,
                    gizmoId,
                    cancellationToken);
            }

            var health = await GetAdventureComposerHealthAsync(core, cancellationToken);
            if (health.BridgeReachable && health.ComposerFound)
                return;

            await Task.Delay(500, cancellationToken);
        }
    }

    private async Task<string?> ResolveDomSubmitParentMessageIdAsync(
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken)
    {
        if (_conversationSend is null || string.IsNullOrWhiteSpace(conversationId))
            return null;

        ConversationParentCache.Invalidate(conversationId);
        return await _conversationSend.PrefetchParentAsync(core, conversationId, cancellationToken);
    }

    private async Task<AdventureTurnResult> SubmitPromptViaDomAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        string? displayPlayerLine,
        string? packetHash,
        int timeoutMs,
        IReadOnlyList<DomAttachmentPayload>? domAttachments,
        bool hostCdpStaged,
        bool attachmentsPreStaged,
        CancellationToken cancellationToken)
    {
        var conversationId = PlayConversationIdResolver.Resolve(bundle, core);
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (!string.IsNullOrWhiteSpace(gizmoId))
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
                {
                    return new AdventureTurnResult
                    {
                        Success = false,
                        Error = "play_thread_unlinked",
                        RequiresManualFallback = true,
                        PacketText = packetText,
                    };
                }
            }
            else
            {
                if (PlayConversationPageService.TryAdoptBrowserConversation(bundle, core.Source))
                    conversationId = PlayThreadBindingService.GetActiveConversationId(bundle) ?? conversationId;

                if (!AdventurePlayContextService.IsOnPlayConversationPage(core.Source, conversationId, gizmoId))
                {
                    var page = await PlayConversationPageService.EnsureReadyForPlaySendAsync(
                        core,
                        bundle,
                        cancellationToken);
                    if (!page.Success)
                    {
                        PlaySendTrace.Event(
                            PlaySendTraceEvents.BridgeSubmitResult,
                            PlaySendCategory.Bridge,
                            PlaySendLevel.Error,
                            $"Play DOM submit blocked: not on linked play thread ({page.Error})",
                            outcome: "play_page_not_ready",
                            data: new
                            {
                                error = page.Error,
                                conversationId,
                                source = core.Source,
                            });

                        return new AdventureTurnResult
                        {
                            Success = false,
                            Error = page.Error ?? "play_page_not_ready",
                            RequiresManualFallback = true,
                            PacketText = packetText,
                        };
                    }

                    conversationId = page.ConversationId ?? conversationId;
                }
                else if (await AdventureNavigationRecoveryProbe.ShowsAccessDeniedAsync(core))
                {
                    return new AdventureTurnResult
                    {
                        Success = false,
                        Error = "play_thread_access_denied",
                        RequiresManualFallback = true,
                        PacketText = packetText,
                    };
                }
            }
        }

        var priorUserCount = await GetUserTurnCountCoreAsync(core, cancellationToken);
        var pending = BeginPendingTurn("turnComplete", "promptSubmitted");

        await using var reg = cancellationToken.Register(() =>
            pending.TrySetCanceled(cancellationToken));

        var requireProjectContext = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);
        var hasDomAttachments = domAttachments is { Count: > 0 };
        var needsAttachmentFlags = attachmentsPreStaged || hasDomAttachments;
        var invoked = false;
        if (!needsAttachmentFlags)
        {
            invoked = await _bridge.InvokeSubmitPromptAsync(
                core,
                packetText,
                requireProjectContext,
                displayPlayerLine,
                packetHash);
        }
        else if (!hasDomAttachments)
        {
            invoked = await _bridge.InvokeSubmitPromptAsync(
                core,
                packetText,
                requireProjectContext,
                displayPlayerLine,
                packetHash,
                attachmentsPreStaged: attachmentsPreStaged,
                hostCdpStaged: hostCdpStaged);
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitInvoke,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            hasDomAttachments
                ? "submitPrompt with attachments posted via PostWebMessage"
                : invoked
                    ? "submitPrompt invoked via ExecuteScript"
                    : "submitPrompt bridge function missing; falling back to PostWebMessage",
            outcome: hasDomAttachments ? "post_message" : invoked ? "invoke_ok" : "invoke_missing",
            data: new
            {
                requireProjectContext,
                packetLength = packetText.Length,
                packetHash,
                displayPlayerLineLength = displayPlayerLine?.Length ?? 0,
                attachmentCount = domAttachments?.Count ?? 0,
                source = core.Source,
            });

        if (!invoked)
        {
            _bridge.SendSubmitPromptCommand(
                core,
                packetText,
                requireProjectContext,
                displayPlayerLine,
                packetHash,
                useWrapperAttachmentStash: hasDomAttachments && !attachmentsPreStaged,
                hostCdpStaged: hostCdpStaged,
                attachmentsPreStaged: attachmentsPreStaged);
        }

        AdventureBridgeMessage msg;
        try
        {
            msg = await pending.Task.WaitAsync(
                TimeSpan.FromMilliseconds(timeoutMs),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            var afterCount = await GetUserTurnCountCoreAsync(core, cancellationToken);
            if (afterCount > priorUserCount)
            {
                var recoveredConvId = ResolveConversationIdForSend(bundle, core);
                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitResult,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Warn,
                    "Bridge submit ack timed out but user turn count increased",
                    outcome: "recovered",
                    data: new
                    {
                        timeoutMs,
                        priorUserCount,
                        afterCount,
                        conversationId = recoveredConvId,
                    });

                return new AdventureTurnResult
                {
                    Success = true,
                    ConversationId = recoveredConvId,
                    PacketText = packetText,
                };
            }

            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeSubmitResult,
                PlaySendCategory.Bridge,
                PlaySendLevel.Error,
                "Bridge submit timed out waiting for promptSubmitted",
                outcome: "timeout",
                data: new { timeoutMs, priorUserCount, afterCount });
            return new AdventureTurnResult
            {
                Success = false,
                Error = "timeout",
                RequiresManualFallback = true,
                PacketText = packetText,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeSubmitResult,
                PlaySendCategory.Bridge,
                PlaySendLevel.Error,
                $"Bridge submit failed while waiting: {ex.Message}",
                outcome: "exception",
                data: new { exception = ex.GetType().Name });
            return new AdventureTurnResult
            {
                Success = false,
                Error = ex.Message,
                RequiresManualFallback = true,
                PacketText = packetText,
            };
        }
        finally
        {
            EndPendingTurn();
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeMessage,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            $"Bridge response type={msg.Type ?? "(null)"} ok={msg.Ok}",
            outcome: msg.Ok ? "ok" : "failed",
            data: new
            {
                type = msg.Type,
                ok = msg.Ok,
                error = msg.Error,
                conversationId = msg.ConversationId,
                composerFound = msg.ComposerFound,
                submitFound = msg.SubmitFound,
            });

        if (msg.Type == "promptSubmitted" && msg.Ok)
        {
            var convId = msg.ConversationId;
            if (!string.IsNullOrWhiteSpace(convId)
                && AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, convId))
            {
                bundle.Metadata.LinkedConversationId = convId;
            }

            return new AdventureTurnResult
            {
                Success = true,
                ConversationId = convId,
                PacketText = packetText,
            };
        }

        return new AdventureTurnResult
        {
            Success = false,
            Error = msg.Error ?? (invoked ? "submit_failed" : "bridge_not_ready"),
            RequiresManualFallback = true,
            PacketText = packetText,
        };
    }

    public async Task<AdventureTurnResult> SendTurnAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        TurnRecord turn,
        string packetText,
        bool regenerate = false,
        int timeoutMs = 120000,
        CancellationToken cancellationToken = default)
    {
        if (!bundle.Metadata.Settings.AdventureAutomationEnabled)
        {
            return new AdventureTurnResult
            {
                Success = false,
                Error = "automation_disabled",
                RequiresManualFallback = true,
                PacketText = packetText,
            };
        }

        return await ExecuteSendPromptAsync(
            core,
            bundle,
            packetText,
            regenerate,
            timeoutMs,
            cancellationToken);
    }

    private async Task<AdventureTurnResult> ExecuteSendPromptAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        bool regenerate,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (regenerate)
        {
            var conversationId = ResolveConversationIdForSend(bundle, core);
            if (PlaySendDeliveryPolicy.ShouldUseApiRegenerate(bundle)
                && _conversationSend is not null
                && !string.IsNullOrWhiteSpace(conversationId))
            {
                var apiRegen = await _conversationSend.RegenerateLastAssistantAsync(
                    core,
                    conversationId,
                    bundle.Metadata.LinkedProjectId,
                    cancellationToken);

                if (apiRegen.Success && !string.IsNullOrWhiteSpace(apiRegen.AssistantText))
                {
                    _lastApiUserMessageId = apiRegen.ParentMessageId;
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.ApiRegenerateUsed,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Info,
                        "Regenerated assistant via API",
                        data: new { conversationId, textLength = apiRegen.AssistantText.Length });

                    return new AdventureTurnResult
                    {
                        Success = true,
                        NarratorText = ContextTagFormat.StripTaggedBlocks(apiRegen.AssistantText!),
                        FromRegenerate = true,
                        ConversationId = conversationId,
                        PacketText = packetText,
                    };
                }

                PlaySendTrace.Event(
                    PlaySendTraceEvents.DomRegenerateFallback,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Warn,
                    "API regenerate failed; falling back to DOM",
                    data: new { conversationId, error = apiRegen.Error });
            }
        }

        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);

            var pending = BeginPendingTurn("turnComplete", "promptSubmitted");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            if (regenerate)
                _bridge.SendCommand(core, new { action = "regenerateLast", timeoutMs });
            else
            {
                var requireProjectContext = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);
                _bridge.SendCommand(core, new
                {
                    action = "sendPrompt",
                    text = packetText,
                    timeoutMs,
                    requireProjectContext,
                });
            }

            AdventureBridgeMessage msg;
            try
            {
                msg = await pending.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(timeoutMs + 5000),
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                return new AdventureTurnResult
                {
                    Success = false,
                    Error = "timeout",
                    RequiresManualFallback = true,
                    PacketText = packetText,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new AdventureTurnResult
                {
                    Success = false,
                    Error = ex.Message,
                    RequiresManualFallback = true,
                    PacketText = packetText,
                };
            }
            finally
            {
                EndPendingTurn();
            }

            if (msg.Type == "turnComplete" && msg.Ok && !string.IsNullOrWhiteSpace(msg.Text))
            {
                var convId = msg.ConversationId;
                if (string.IsNullOrWhiteSpace(convId))
                    convId = await TryFetchConversationIdAsync(core);

                if (!string.IsNullOrWhiteSpace(convId)
                    && AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, convId))
                {
                    bundle.Metadata.LinkedConversationId = convId;
                }

                return new AdventureTurnResult
                {
                    Success = true,
                    NarratorText = ContextTagFormat.StripTaggedBlocks(msg.Text!),
                    FromRegenerate = msg.FromRegenerate,
                    ConversationId = convId,
                };
            }

            return new AdventureTurnResult
            {
                Success = false,
                Error = msg.Error ?? "automation_failed",
                RequiresManualFallback = true,
                PacketText = packetText,
            };
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    public async Task<string?> GetConversationIdAsync(CoreWebView2 core)
    {
        await _bridgeGate.WaitAsync();
        try
        {
            return await GetConversationIdCoreAsync(core);
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    public async Task<ProjectChatStartResult> StartProjectChatAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("projectChatReady");

            await using var reg = cancellationToken.Register(() =>
                pending.TrySetCanceled(cancellationToken));

            _bridge.SendCommand(core, new { action = "startProjectChat" });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
                return new ProjectChatStartResult
                {
                    Success = msg.Ok || msg.ComposerFound,
                    ConversationId = msg.ConversationId,
                    ComposerReady = msg.ComposerFound,
                    Error = msg.Error,
                };
            }
            catch (TimeoutException)
            {
                return new ProjectChatStartResult { Success = false, Error = "project_chat_timeout" };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ProjectChatStartResult { Success = false, Error = ex.Message };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    public async Task<BridgeHealthStatus> GetHealthAsync(CoreWebView2 core)
    {
        if (_conversationSend is not null && await _conversationSend.PingAsync(core))
        {
            return new BridgeHealthStatus
            {
                BridgeReachable = true,
            };
        }

        await _bridgeGate.WaitAsync();
        try
        {
            await _bridge.InjectAsync(core);
            var pending = BeginPendingTurn("pong", "probeResult");

            _bridge.SendCommand(core, new { action = "ping" });

            try
            {
                var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(3));
                return new BridgeHealthStatus
                {
                    BridgeReachable = msg.Type is "pong" or "probeResult",
                    ComposerFound = msg.ComposerFound,
                    SubmitFound = msg.SubmitFound,
                    ConversationId = msg.ConversationId,
                };
            }
            catch
            {
                return new BridgeHealthStatus
                {
                    BridgeReachable = false,
                    Error = "ping_timeout",
                };
            }
            finally
            {
                EndPendingTurn();
            }
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    private async Task<string?> GetConversationIdCoreAsync(CoreWebView2 core)
    {
        await _bridge.InjectAsync(core);
        var pending = BeginPendingTurn("conversationId");

        _bridge.SendCommand(core, new { action = "getConversationId" });

        try
        {
            var msg = await pending.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return msg.ConversationId;
        }
        catch
        {
            return null;
        }
        finally
        {
            EndPendingTurn();
        }
    }

    private Task<string?> TryFetchConversationIdAsync(CoreWebView2 core) =>
        GetConversationIdCoreAsync(core);

    private void OnBridgeMessage(object? sender, AdventureBridgeMessage e)
    {
        if (_pendingTurn is not null)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.BridgeMessage,
                PlaySendCategory.Bridge,
                PlaySendLevel.Debug,
                $"Bridge message while submit pending type={e.Type ?? "(null)"} ok={e.Ok}",
                data: new
                {
                    type = e.Type,
                    ok = e.Ok,
                    error = e.Error,
                    conversationId = e.ConversationId,
                });

            if (AcceptsPendingTurnMessage(e))
                _pendingTurn.TrySetResult(e);
        }
    }

    private TaskCompletionSource<AdventureBridgeMessage> BeginPendingTurn(params string[] acceptedTypes)
    {
        _pendingTurnAcceptedTypes = acceptedTypes;
        _pendingTurn = new TaskCompletionSource<AdventureBridgeMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _pendingTurn;
    }

    private void EndPendingTurn()
    {
        _pendingTurn = null;
        _pendingTurnAcceptedTypes = null;
    }

    private bool AcceptsPendingTurnMessage(AdventureBridgeMessage e)
    {
        if (_pendingTurnAcceptedTypes is not { Length: > 0 })
            return true;

        var type = e.Type ?? "";
        if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
            return true;

        return _pendingTurnAcceptedTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ADR tier-2 DOM fallback: linked play threads often return http_403 on API POST
    /// even when prepare/parent resolution succeeds.
    /// </summary>
    private static bool ShouldDomFallbackAfterApiTextFailure(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (UtilityConversationReadinessService.IsUnregisteredFetchError(error)
            || UtilityConversationReadinessService.IsRateLimitFetchError(error)
            || ChatGptConversationSendService.IsRetryableSendError(error));
}

public sealed class ComposerFillResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }
}

public sealed class CaptureAssistantResult
{
    public bool Success { get; init; }

    public string? Text { get; init; }

    public string? Error { get; init; }

    public string? ConversationId { get; init; }
}

public sealed class ThreadTranscriptCaptureResult
{
    public bool Success { get; init; }

    public IReadOnlyList<TranscriptTurnPair> TurnPairs { get; init; } = [];

    public string? Error { get; init; }

    public string? ConversationId { get; init; }
}

public sealed class ProjectChatStartResult
{
    public bool Success { get; init; }

    public bool ComposerReady { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }
}

public sealed class AdventureTurnResult
{
    public bool Success { get; init; }

    public string? NarratorText { get; init; }

    public string? Error { get; init; }

    public bool RequiresManualFallback { get; init; }

    public string? PacketText { get; init; }

    public bool FromRegenerate { get; init; }

    public string? ConversationId { get; init; }
}
