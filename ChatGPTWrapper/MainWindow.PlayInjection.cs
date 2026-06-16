using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private CancellationTokenSource? _mergedPreviewDebounceCts;
    private readonly SemaphoreSlim _playSendGate = new(1, 1);
    private int _activePlaySendCount;

    private void DebouncedUpdatePlayMergedPreview()
    {
        _mergedPreviewDebounceCts?.Cancel();
        _mergedPreviewDebounceCts = new CancellationTokenSource();
        var token = _mergedPreviewDebounceCts.Token;
        _ = DebouncedUpdatePlayMergedPreviewCoreAsync(token);
    }

    private async Task DebouncedUpdatePlayMergedPreviewCoreAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            await Dispatcher.InvokeAsync(UpdatePlayMergedPreview);
        }
        catch (OperationCanceledException)
        {
            /* superseded by newer input */
        }
    }

    internal string GetPlayPlayerLineText()
    {
        if (_appMode == AppMode.Play
            && _playWebView is not null
            && GetActivePlayComposeInjection() is { } injection)
        {
            return injection.GetText();
        }

        return PlayPromptComposer?.GetPromptText() ?? "";
    }

    internal void SetPlayPlayerLineText(string text)
    {
        PlayPromptComposer?.SetPromptText(text);
        SyncPlayComposeUi(new PlayComposeUiState { Text = text });
    }

    internal void AppendPlayPlayerLineText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var existing = GetPlayPlayerLineText();
        var merged = string.IsNullOrWhiteSpace(existing) ? text : existing + " " + text;
        SetPlayPlayerLineText(merged);
    }

    private void SyncPlayComposeUi(PlayComposeUiState state, ChatGptPlayComposeInjection? injection = null) =>
        _ = SyncPlayComposeUiAsync(state, injection);

    private async Task SyncPlayComposeUiAsync(
        PlayComposeUiState state,
        ChatGptPlayComposeInjection? injection = null)
    {
        injection ??= GetActivePlayComposeInjection();
        if (injection?.WebView.CoreWebView2 is not { } core)
            return;

        await injection.ApplyStateAsync(core, state);
    }

    private async Task RestorePlayComposeInputAsync(
        string text,
        ChatGptPlayComposeInjection? injection = null)
    {
        PlayPromptComposer?.SetPromptText(text);
        injection ??= GetActivePlayComposeInjection();
        if (injection?.WebView.CoreWebView2 is { } core
            && !ShouldUseWrapperComposer(injection.WebView))
        {
            if (ConversationStreamParser.IsInjectedContextUserMessage(text))
            {
                try { Clipboard.SetText(text); } catch { /* ignore */ }
                await SyncPlayComposeUiAsync(new PlayComposeUiState
                {
                    Busy = false,
                    Status = "Start packet copied to clipboard — paste again (Ctrl+V).",
                }, injection);
                return;
            }

            GetOrRegisterAdventureBridge(injection.WebView)
                .SendFillComposerCommand(core, text);
            await SyncPlayComposeUiAsync(new PlayComposeUiState { Busy = false }, injection);
            return;
        }

        await SyncPlayComposeUiAsync(new PlayComposeUiState
        {
            Text = text,
            Busy = false,
            Focus = true,
        }, injection);
    }

    private void SetPlayComposeStatus(string? text, ChatGptPlayComposeInjection? injection = null)
    {
        PlayPromptComposer?.SetStatus(text);
        SyncPlayComposeUi(new PlayComposeUiState { Status = text }, injection);
    }

    private async Task SetPlayComposeBusyAsync(
        bool busy,
        string? busyMessage = null,
        ChatGptPlayComposeInjection? injection = null)
    {
        PlayPromptComposer?.SetBusy(busy, busyMessage);
        await SyncPlayComposeUiAsync(new PlayComposeUiState
        {
            Busy = busy,
            Status = busyMessage,
        }, injection);
    }

    internal async Task RestorePlayComposerAsync(ChatGptPlayComposeInjection? injection = null)
    {
        injection ??= GetActivePlayComposeInjection();
        if (injection?.WebView.CoreWebView2 is not { } core)
            return;

        await SetPlayComposeBusyAsync(false, null, injection);

        var useWrapper = ShouldUseWrapperComposer(injection.WebView);
        await ChatGptPlayComposeInjection.ReapplyAsync(core, useWrapper);

        await core.ExecuteScriptAsync(
            """
            (function () {
              if (typeof globalThis.__cgwApplyPlaySurfaceActions === "function") {
                globalThis.__cgwApplyPlaySurfaceActions();
              }
              if (typeof globalThis.__cgwPlayComposeScheduleMount === "function") {
                globalThis.__cgwPlayComposeScheduleMount();
              }
            })();
            """);
    }

    private void ClearPlayComposePrompt()
    {
        if (GetActivePlayComposeInjection() is { } injection)
            injection.ClearCachedText();

        PlayPromptComposer?.ClearPrompt();
        SyncPlayComposeUi(new PlayComposeUiState { Clear = true });
    }

    private void InitializePlayPromptComposer()
    {
        UpdatePlayPromptComposerVisibility();
    }

    private static string FormatMergedPreviewForUi(AdventureBundle bundle, string mergedText) =>
        bundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(mergedText)
            : mergedText;

    internal void UpdatePlayMergedPreview()
    {
        if (_activeAdventureId is not { } id || PlayPromptComposer is null)
            return;

        _playView?.SaveConfiguration();
        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        SyncPlayThreadScopeForPacket(bundle);

        var playerLine = ResolvePlayPlayerInput(bundle, consumeQueue: false);
        if (string.IsNullOrWhiteSpace(playerLine))
        {
            PlayPromptComposer.SetMergedPreview(null);
            return;
        }

        var attachmentContext = GetActivePlayComposeInjection()?.GetLastAttachmentContext();
        var prepared = PromptInjectionService.PrepareSend(bundle, playerLine, attachmentContext);
        PlayPromptComposer.SetMergedPreview(FormatMergedPreviewForUi(bundle, prepared.MergedText));
    }

    private void UpdatePlayPromptComposerVisibility()
    {
        if (PlayPromptComposer is null)
            return;

        var inPageComposer = _appMode == AppMode.Play;
        PlayPromptComposer.Visibility = Visibility.Collapsed;
        PlayPromptComposer.IsEnabled = !inPageComposer;

        if (inPageComposer && _playWebView is not null)
            GetOrRegisterAdventureBridge(_playWebView);

        ApplyWrapperComposerToPlayTab(inPageComposer);
        if (inPageComposer)
        {
            SyncPlayComposeUi(new PlayComposeUiState
            {
                Placeholder = "Message ChatGPT",
            });
        }
    }

    private void CopyPlayPacket()
    {
        if (_activeAdventureId is not { } id)
            return;

        _playView?.SaveConfiguration();
        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        SyncPlayThreadScopeForPacket(bundle);

        var playerLine = ResolvePlayPlayerInput(bundle, consumeQueue: false);
        if (string.IsNullOrWhiteSpace(playerLine))
        {
            SetPlayComposeStatus("Enter a prompt or add lines to the continuation queue.");
            return;
        }

        var attachmentContext = GetActivePlayComposeInjection()?.GetLastAttachmentContext();
        var prepared = PromptInjectionService.PrepareSend(bundle, playerLine, attachmentContext);
        PlayPromptComposer?.SetMergedPreview(FormatMergedPreviewForUi(bundle, prepared.MergedText));
        try
        {
            Clipboard.SetText(prepared.MergedText);
            SetPlayComposeStatus("Merged packet copied to clipboard.");
        }
        catch
        {
            SetPlayComposeStatus("Could not copy to clipboard.");
        }
    }

    private async Task SendPlayPromptAsync(
        PlayComposeSendEventArgs? sendRequest = null,
        ChatGptPlayComposeInjection? composeInjection = null)
    {
        var composeText = sendRequest?.Text;
        var pendingAttachments = sendRequest?.Attachments ?? [];
        var attachmentsPreStaged = sendRequest?.AttachmentsPreStaged == true;
        composeInjection ??= GetActivePlayComposeInjection();
        PlaySendScope? traceScope = null;

        async Task ReleaseComposeSendLockAsync(string? status = null)
        {
            var state = new PlayComposeUiState { Busy = false, Focus = true };
            if (status is not null)
                state = new PlayComposeUiState { Busy = false, Focus = true, Status = status };

            await SyncPlayComposeUiAsync(state, composeInjection);
        }

        if (_activeAdventureId is not { } adventureId)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.SendGate,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                "Send aborted: no active adventure",
                outcome: "no_adventure");
            SetPlayComposeStatus("No active adventure.", composeInjection);
            await ReleaseComposeSendLockAsync();
            return;
        }

        traceScope = PlaySendTrace.BeginSend(
            adventureId,
            composeText,
            composeInjection?.WebView.CoreWebView2?.Source);

        if (!await _playSendGate.WaitAsync(0))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.SendGate,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                "Send aborted: send gate already held",
                outcome: "already_sending");
            SetPlayComposeStatus("Already sending…", composeInjection);
            await ReleaseComposeSendLockAsync();
            traceScope.Complete("blocked", "already_sending");
            traceScope = null;
            return;
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.SendGate,
            PlaySendCategory.Host,
            PlaySendLevel.Debug,
            "Send gate acquired");

        Interlocked.Increment(ref _activePlaySendCount);

        string? playerLine = null;
        try
        {
            _playView?.SaveConfiguration();
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
            {
                PlaySendTrace.Event(
                    PlaySendTraceEvents.SendRunEnd,
                    PlaySendCategory.Host,
                    PlaySendLevel.Error,
                    "Send aborted: adventure bundle missing",
                    outcome: "bundle_missing");
                SetPlayComposeStatus("Could not load adventure.", composeInjection);
                await ReleaseComposeSendLockAsync();
                traceScope.Complete("failed", "bundle_missing");
                traceScope = null;
                return;
            }

            playerLine = ResolvePlayPlayerInput(bundle, consumeQueue: true, composeText);
            var attachmentContext = BuildAttachmentContext(sendRequest, pendingAttachments);
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
                    preview = TruncateForSendLog(playerLine, 120),
                    queueCount = bundle.ContinuationQueue.Count,
                });

            if (string.IsNullOrWhiteSpace(playerLine)
                && attachmentContext is not { HasAttachments: true }
                && pendingAttachments.Count == 0
                && !attachmentsPreStaged)
            {
                SetPlayComposeStatus(
                    "Enter a prompt in the composer, set a fallback line in Play settings, add lines to the continuation queue, or attach a file.",
                    composeInjection);
                await ReleaseComposeSendLockAsync();
                traceScope.Complete("failed", "empty_player_line");
                traceScope = null;
                return;
            }

            var sourceReadiness = ProjectSourceInjectionService.Evaluate(bundle);
            if (sourceReadiness.HasLinkedProject && !sourceReadiness.CanDelegateStaticContent)
            {
                var warnMessage =
                    "Project sources are not fully published — this send will use fat packets with inline lore. "
                    + "Publish files in Play settings → Sources or Source Manager (drag to ChatGPT Project, check Published)."
                    + (string.IsNullOrWhiteSpace(sourceReadiness.BlockingReason)
                        ? ""
                        : $"\n\nReason: {sourceReadiness.BlockingReason}")
                    + "\n\nClick Yes to open Source Manager and cancel this send, or No to send anyway with fat packets.";

                if (MessageBox.Show(
                        this,
                        warnMessage,
                        "Project sources",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    await OpenSourceManagerDialogAsync(adventureId);
                    SetPlayComposeStatus(
                        "Publish sources in Source Manager (or Play settings → Sources), then send again.",
                        composeInjection);
                    await ReleaseComposeSendLockAsync();
                    traceScope.Complete("blocked", "sources_not_published");
                    traceScope = null;
                    return;
                }
            }

            SyncPlayThreadScopeForPacket(bundle);

            await SetPlayComposeBusyAsync(true, "Preparing…", composeInjection);

            TurnRecord? turn = null;
            PromptInjectionPrepareResult prepared;
            try
            {
                if (composeInjection?.WebView is { } composeTab)
                    _playWebView = composeTab;

                await EnsurePlayWebViewReadyAsync(
                    adventureId,
                    selectTab: false,
                    prepareContext: false,
                    navigateToBrowseTarget: false);

                var playWebView = composeInjection?.WebView ?? _playWebView;
                if (playWebView?.CoreWebView2 is not { } core)
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.WebViewReady,
                        PlaySendCategory.Host,
                        PlaySendLevel.Error,
                        "Send aborted: no play WebView/core",
                        outcome: "no_webview");
                    SetPlayComposeStatus("Pin a ChatGPT tab for this adventure first.", composeInjection);
                    MessageBox.Show(
                        this,
                        PlaySendTrace.FormatRunContextForError("Pin a ChatGPT tab for this adventure first."),
                        "Send");
                    await RestorePlayComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", "no_webview");
                    traceScope = null;
                    return;
                }

                PlaySendTrace.Event(
                    PlaySendTraceEvents.WebViewReady,
                    PlaySendCategory.Host,
                    PlaySendLevel.Info,
                    "Play WebView ready for bridge submit",
                    data: new
                    {
                        source = core.Source,
                        composeInjectionPresent = composeInjection is not null,
                        activeInjectionPresent = GetActivePlayComposeInjection() is not null,
                    });

                _playWebView = playWebView;
                GetOrCreateTurnService(playWebView);
                if (_turnService is null)
                {
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.BridgeSubmitStart,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Error,
                        "Send aborted: turn service missing",
                        outcome: "turn_service_missing");
                    SetPlayComposeStatus("Adventure bridge is not ready.", composeInjection);
                    MessageBox.Show(
                        this,
                        PlaySendTrace.FormatRunContextForError("Adventure bridge is not ready."),
                        "Send");
                    await RestorePlayComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", "turn_service_missing");
                    traceScope = null;
                    return;
                }

                SetPlayComposeStatus("Sending to ChatGPT…", composeInjection);

                bundle = AdventureStore.Load(adventureId) ?? bundle;
                var linkedProject = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);
                if (linkedProject)
                {
                    var threadReady = await RequireLinkedPlayThreadForSendAsync(bundle, core);
                    if (threadReady is not null && !threadReady.IsReady)
                    {
                        var threadError = AdventureNavigationService.FormatPlaySessionError(threadReady);
                        PlaySendTrace.Event(
                            PlaySendTraceEvents.WebViewReady,
                            PlaySendCategory.Host,
                            PlaySendLevel.Error,
                            $"Send aborted: play thread not ready ({threadError})",
                            outcome: "play_thread_not_ready");
                        SetPlayComposeStatus(threadError, composeInjection);
                        MessageBox.Show(
                            this,
                            PlaySendTrace.FormatRunContextForError(threadError),
                            "Send",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        await RestorePlayComposeInputAsync(playerLine, composeInjection);
                        traceScope.Complete("failed", "play_thread_not_ready");
                        traceScope = null;
                        return;
                    }
                }

                var priorThreadUserMessageCount = await _turnService.GetUserTurnCountAsync(core);
                var usePrebuiltPacket = string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
                    && ConversationStreamParser.IsInjectedContextUserMessage(playerLine);
                prepared = usePrebuiltPacket
                    ? PromptInjectionService.PreparePrebuiltPacket(playerLine)
                    : PromptInjectionService.PrepareSend(
                        bundle,
                        playerLine,
                        attachmentContext,
                        priorThreadUserMessageCount);
                var scopedTurnCount = PlayTurnScopeService.GetPacketContextTurns(bundle).Count;
                var nextPacketTurnIndex = PlayTurnScopeService.ResolveNextPacketTurnIndex(
                    bundle,
                    priorThreadUserMessageCount);
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
                        scopedTurnCount,
                        priorThreadUserMessageCount,
                        nextPacketTurnIndex,
                        attachmentKinds = AttachmentSendPolicy.AttachmentKinds(attachmentContext),
                        attachmentOnly = attachmentContext?.IsAttachmentOnly(playerLine) == true,
                        attachmentContextMode = bundle.Metadata.Settings.AttachmentContextMode.ToString(),
                    });

                PlayPromptComposer?.SetMergedPreview(FormatMergedPreviewForUi(bundle, prepared.MergedText));
                if (prepared.WasTrimmed)
                    SetPlayComposeStatus("Packet was trimmed to fit size limits.", composeInjection);

                if (_playSendWarmupService is not null)
                    await _playSendWarmupService.PrefetchAsync(core, bundle);

                var assistantBaseline = await _turnService!.GetAssistantTurnCountAsync(core);

                var displayPlayerLine = usePrebuiltPacket
                    ? ConversationStreamParser.ExtractTranscriptPlayerText(playerLine)
                      ?? AdventureBootstrapService.GetOpeningPlayerLine(bundle.Scenario)
                    : AttachmentSendPolicy.ResolveDisplayPlayerLine(
                        bundle,
                        playerLine,
                        attachmentContext);

                if (pendingAttachments.Count > 0 && !attachmentsPreStaged)
                    SetPlayComposeStatus("Staging attachments…", composeInjection);

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

                var result = await SendPlayPromptWithContextAsync(
                    core,
                    bundle,
                    prepared.MergedText,
                    displayPlayerLine,
                    prepared.Hash,
                    attachments: null,
                    domAttachments,
                    attachmentsPreStaged);

                if (!result.Success)
                {
                    PlayContextSessionCache.Invalidate(adventureId);
                    try { Clipboard.SetText(prepared.MergedText); } catch { /* ignore */ }

                    var error = result.Error ?? "Could not send the prompt to ChatGPT.";
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.BridgeSubmitResult,
                        PlaySendCategory.Bridge,
                        PlaySendLevel.Error,
                        $"Bridge submit failed: {error}",
                        outcome: "failed",
                        data: new
                        {
                            error,
                            requiresManualFallback = result.RequiresManualFallback,
                        });

                    SetPlayComposeStatus($"Send failed: {error}", composeInjection);
                    MessageBox.Show(
                        this,
                        PlaySendTrace.FormatRunContextForError(
                            error + "\n\nThe merged packet was copied to your clipboard."),
                        "Send",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    await RestorePlayComposeInputAsync(playerLine, composeInjection);
                    traceScope.Complete("failed", error, new { result.Error, result.RequiresManualFallback });
                    traceScope = null;
                    return;
                }

                turn = TurnTimelineService.CreateTurn(bundle, playerLine);
                _turnService!.RecordPrompt(bundle, turn, prepared.MergedText, prepared.Hash);

                if (!string.IsNullOrWhiteSpace(result.ConversationId)
                    && AdventurePlayContextService.ShouldAcceptLinkedConversationId(bundle, result.ConversationId))
                {
                    var previousConversationId = bundle.Metadata.LinkedConversationId;
                    PlayTurnScopeService.OnPlayThreadChanged(
                        bundle,
                        previousConversationId,
                        result.ConversationId);
                    AdventureSessionService.AttachTurnToSession(bundle, turn);
                    PlayTurnScopeService.AssignConversation(turn, result.ConversationId);
                    bundle.Metadata.LinkedConversationId = result.ConversationId;
                    if (string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey))
                        PlayTabPinService.PinTab(bundle, playWebView, ChatTabs);
                }

                composeInjection?.ClearCachedText();
                PlayPromptComposer?.ClearPrompt();

                var logStatus = await CompletePlayTurnAfterSendAsync(
                    bundle,
                    turn,
                    result,
                    core,
                    _turnService,
                    composeInjection,
                    assistantBaseline);

                playWebView.Focus();

                ReloadPlayAdventure(adventureId);
                UpdatePlayLinkStatus();
                PlayPromptComposer?.SetMergedPreview(null);

                await SyncPlayComposeUiAsync(new PlayComposeUiState
                {
                    Busy = false,
                    Focus = true,
                    Status = logStatus,
                }, composeInjection);

                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitResult,
                    PlaySendCategory.Bridge,
                    PlaySendLevel.Info,
                    "Bridge submit succeeded",
                    outcome: "ok",
                    data: new { conversationId = result.ConversationId });

                traceScope.Complete("ok", data: new { result.ConversationId });
                traceScope = null;
            }
            catch (Exception ex)
            {
                PlayContextSessionCache.Invalidate(adventureId);
                PlaySendTrace.Event(
                    PlaySendTraceEvents.BridgeSubmitResult,
                    PlaySendCategory.Host,
                    PlaySendLevel.Error,
                    $"Send exception: {ex.Message}",
                    outcome: "exception",
                    data: new { exception = ex.GetType().Name });

                SetPlayComposeStatus($"Send failed: {ex.Message}", composeInjection);
                MessageBox.Show(
                    this,
                    PlaySendTrace.FormatRunContextForError(ex.Message),
                    "Send",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                if (!string.IsNullOrWhiteSpace(playerLine))
                    await RestorePlayComposeInputAsync(playerLine, composeInjection);

                traceScope?.Complete("failed", ex.Message, new { exception = ex.GetType().Name });
                traceScope = null;
            }
            finally
            {
                PlayPromptComposer?.SetBusy(false);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activePlaySendCount);
            _playSendGate.Release();
            traceScope?.Complete("aborted", "unhandled_exit");
        }
    }

    private static string? TruncateForSendLog(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[..maxChars] + "…";
    }

    private string ResolvePlayPlayerInput(
        AdventureBundle bundle,
        bool consumeQueue,
        string? composeText = null)
    {
        var input = !string.IsNullOrWhiteSpace(composeText)
            ? composeText.Trim()
            : GetPlayPlayerLineText();
        if (string.IsNullOrWhiteSpace(input))
            input = _playView?.GetPreviewPlayerLineText() ?? "";

        if (!string.IsNullOrWhiteSpace(input))
            return input;

        if (bundle.ContinuationQueue.Count == 0)
            return "";

        var line = bundle.ContinuationQueue[0];
        if (consumeQueue)
        {
            bundle.ContinuationQueue.RemoveAt(0);
            AdventureStore.Save(bundle);
            ReloadPlayAdventure(bundle.Metadata.Id);
        }

        return line;
    }

    private async Task HandleComposeUploadRequestAsync(
        PlayComposeUploadEventArgs args,
        ChatGptPlayComposeInjection? composeInjection = null)
    {
        composeInjection ??= GetActivePlayComposeInjection();
        if (composeInjection?.WebView.CoreWebView2 is not { } core)
            return;

        if (!ShouldUseWrapperComposer(composeInjection.WebView))
            return;

        var bridge = GetOrRegisterAdventureBridge(composeInjection.WebView);
        var uploadService = new PlayComposeNativeUploadService(bridge);
        var payloads = args.Attachments
            .Select(a => new DomAttachmentPayload
            {
                Name = a.Name,
                MimeType = a.MimeType,
                Content = a.Content,
            })
            .ToList();

        try
        {
            await composeInjection.ApplyUploadStatusAsync(
                core,
                args.JobId,
                args.AttachmentIds,
                "uploading");

            var ok = await uploadService.UploadBatchAsync(core, payloads);
            await composeInjection.ApplyUploadStatusAsync(
                core,
                args.JobId,
                args.AttachmentIds,
                ok ? "ready" : "error",
                ok ? null : "upload_failed");
        }
        catch (Exception ex)
        {
            await composeInjection.ApplyUploadStatusAsync(
                core,
                args.JobId,
                args.AttachmentIds,
                "error",
                ex.Message);
        }
    }

    private async Task<PlayContextResult?> RequireLinkedPlayThreadForSendAsync(
        AdventureBundle bundle,
        CoreWebView2 core)
    {
        if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
            return null;

        var page = await PlayConversationPageService.EnsureReadyForPlaySendAsync(core, bundle);
        if (!page.Success)
        {
            return new PlayContextResult
            {
                Status = string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
                    ? PlayContextStatus.NoConversation
                    : PlayContextStatus.NavigationFailed,
                ConversationId = page.ConversationId ?? bundle.Metadata.LinkedConversationId,
                Error = page.Error,
            };
        }

        AdventureStore.Save(bundle);

        return new PlayContextResult
        {
            Status = PlayContextStatus.Ready,
            ConversationId = page.ConversationId ?? bundle.Metadata.LinkedConversationId,
        };
    }

    private async Task<AdventureTurnResult> SendPlayPromptWithContextAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string packetText,
        string? displayPlayerLine = null,
        string? packetHash = null,
        IReadOnlyList<ChatAttachmentRef>? attachments = null,
        IReadOnlyList<DomAttachmentPayload>? domAttachments = null,
        bool attachmentsPreStaged = false)
    {
        var linkedProject = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);
        if (PlayConversationPageService.TryAdoptBrowserConversation(bundle, core.Source)
            || PlayContextSessionCache.TrySyncPlayThreadFromSource(bundle, core.Source))
        {
            AdventureStore.Save(bundle);
        }

        if (linkedProject
            && !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
            && Uri.TryCreate(core.Source, UriKind.Absolute, out var currentUri)
            && ChatGptUrls.TryParseConversationId(currentUri, out var urlConversationId)
            && !string.Equals(urlConversationId, bundle.Metadata.LinkedConversationId, StringComparison.OrdinalIgnoreCase))
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ContextMismatch,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                "Pinned tab conversation differs from linked play thread",
                data: new
                {
                    linkedConversationId = bundle.Metadata.LinkedConversationId,
                    urlConversationId,
                    source = core.Source,
                });
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitStart,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Submitting prompt through adventure bridge",
            data: new
            {
                packetLength = packetText.Length,
                attachmentCount = attachments?.Count ?? 0,
                linkedProject,
                source = core.Source,
            });

        var result = await _turnService!.SubmitPromptAsync(
            core,
            bundle,
            packetText,
            displayPlayerLine,
            packetHash,
            attachments,
            domAttachments,
            attachmentsPreStaged);

        if (!result.Success
            && linkedProject
            && result.Error is "project_context_required"
                or "bridge_not_ready" or "unknown_action")
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ContextRetry,
                PlaySendCategory.Host,
                PlaySendLevel.Warn,
                $"Retrying after bridge error {result.Error}",
                data: new { result.Error });

            PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
            var ctx = await EnsureLinkedPlayContextForBundleAsync(bundle);
            if (ctx is not null && !ctx.IsReady)
                return PlayContextFailureResult(ctx, packetText);

            result = await _turnService.SubmitPromptAsync(
                core,
                bundle,
                packetText,
                displayPlayerLine,
                packetHash,
                attachments,
                domAttachments,
                attachmentsPreStaged);
        }

        if (result.Success)
        {
            PlayContextSessionCache.Record(
                bundle.Metadata.Id,
                core.Source,
                result.ConversationId ?? bundle.Metadata.LinkedConversationId,
                composerFound: true);
        }

        return result;
    }

    private async Task<string> CompletePlayTurnAfterSendAsync(
        AdventureBundle bundle,
        TurnRecord turn,
        AdventureTurnResult sendResult,
        CoreWebView2 core,
        AdventureTurnService turnService,
        ChatGptPlayComposeInjection? composeInjection,
        int assistantBaselineCount)
    {
        var narratorText = string.IsNullOrWhiteSpace(sendResult.NarratorText)
            ? null
            : sendResult.NarratorText.Trim();
        var conversationId = sendResult.ConversationId ?? bundle.Metadata.LinkedConversationId;

        if (PlayTurnScopeService.NeedsNarratorCapture(narratorText))
        {
            await SetPlayComposeBusyAsync(true, "Logging response…", composeInjection);

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

            await SetPlayComposeBusyAsync(false, null, composeInjection);
        }

        PlayTurnScopeService.AssignConversation(turn, conversationId);

        if (PlayTurnScopeService.IsIncompleteNarratorCapture(narratorText))
        {
            TurnTimelineService.LeavePendingIncompleteCapture(turn, narratorText);
            ThreadMetadataService.RecordPlayTurnExchange(
                bundle,
                turn,
                turn.PlayerText,
                null,
                turn.PromptPacketHash,
                conversationId);
            AdventureStore.Save(bundle);
            return string.IsNullOrWhiteSpace(narratorText)
                ? "Sent — narrator response not captured yet. Send again or use Edit turn when ready."
                : "Sent — narrator still generating (placeholder captured). Edit turn or retry when ready.";
        }

        TurnTimelineService.AcceptTurn(turn, narratorText!);
        ThreadMetadataService.RecordPlayTurnExchange(
            bundle,
            turn,
            turn.PlayerText,
            narratorText,
            turn.PromptPacketHash,
            sendResult.ConversationId ?? bundle.Metadata.LinkedConversationId);
        AdventureStore.Save(bundle);
        _ = ApplyThreadOrdinalMapToPlayTabAsync();

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            _ = RunScheduledJobsAfterTurnAsync(bundle, turn);

        return string.IsNullOrWhiteSpace(narratorText)
            ? "Sent — turn logged without narrator text. Use Edit turn if needed."
            : "Sent — turn logged.";
    }

    private static AttachmentContext? BuildAttachmentContext(
        PlayComposeSendEventArgs? sendRequest,
        IReadOnlyList<PlayComposePendingAttachment> pendingAttachments)
    {
        if (sendRequest?.AttachmentMeta is { Count: > 0 } meta)
        {
            return AttachmentContext.FromMeta(meta.Select(m => new ComposerAttachmentMeta
            {
                Name = m.Name,
                MimeType = m.MimeType,
                SizeBytes = m.SizeBytes,
            }));
        }

        if (pendingAttachments.Count > 0)
        {
            return AttachmentContext.FromPending(pendingAttachments.Select(a => new PlayComposePendingAttachmentRef
            {
                Name = a.Name,
                MimeType = a.MimeType,
                SizeBytes = a.Content?.LongLength,
            }));
        }

        if (sendRequest?.AttachmentsPreStaged == true)
        {
            return AttachmentContext.FromMeta(
            [
                new ComposerAttachmentMeta { Name = "attachment", MimeType = null },
            ]);
        }

        return null;
    }

    private void SyncPlayThreadScopeForPacket(AdventureBundle bundle)
    {
        var core = GetActivePlayComposeInjection()?.WebView.CoreWebView2
                   ?? _playWebView?.CoreWebView2;
        if (core is null)
            return;

        if (PlayConversationPageService.TryAdoptBrowserConversation(bundle, core.Source)
            || PlayContextSessionCache.TrySyncPlayThreadFromSource(bundle, core.Source))
            AdventureStore.Save(bundle);
    }
}
