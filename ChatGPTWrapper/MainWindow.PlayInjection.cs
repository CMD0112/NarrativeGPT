using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private static WebView2? GetPlayComposeWebView(ChatGptPlayComposeInjection? injection) =>
        injection?.TabHost as WebView2;

    private CancellationTokenSource? _mergedPreviewDebounceCts;
    private readonly SemaphoreSlim _playSendGate = new(1, 1);
    private int _activePlaySendCount;
    private readonly PreparedSendArtifactStore _preparedSendArtifactStore = new();

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
            await Dispatcher.InvokeAsync(async () => await UpdatePlayMergedPreviewAsync());
        }
        catch (OperationCanceledException)
        {
            /* superseded by newer input */
        }
    }

    internal string GetPlayPlayerLineText()
    {
        if (_appMode == AppMode.Play
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
        if (injection?.CoreWebView2 is not { } core)
            return;

        await injection.ApplyStateAsync(core, state);
    }

    private async Task RestorePlayComposeInputAsync(
        string text,
        ChatGptPlayComposeInjection? injection = null)
    {
        PlayPromptComposer?.SetPromptText(text);
        injection ??= GetActivePlayComposeInjection();
        if (injection?.CoreWebView2 is { } core
            && GetPlayComposeWebView(injection) is { } composeWebView
            && !ShouldUseWrapperComposer(composeWebView))
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

            GetOrRegisterAdventureBridge(composeWebView)
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
        if (injection?.CoreWebView2 is not { } core
            || GetPlayComposeWebView(injection) is not { } composeWebView)
            return;

        await SetPlayComposeBusyAsync(false, null, injection);

        var useWrapper = ShouldUseWrapperComposer(composeWebView);
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

    internal async Task UpdatePlayMergedPreviewAsync()
    {
        if (_activeAdventureId is not { } id || PlayPromptComposer is null)
            return;

        _playView?.SaveConfiguration();
        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var priorThreadUserMessageCount = await GetPlayThreadUserMessageCountAsync();
        var attachmentContext = GetActivePlayComposeInjection()?.GetLastAttachmentContext();
        _preparedSendArtifactStore.Bind(bundle);
        var artifact = PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = GetPlayPlayerLineText(),
            AttachmentContext = attachmentContext,
            ConsumeContinuationQueue = false,
            ApplySurfaceActions = true,
            PriorThreadUserMessageCount = priorThreadUserMessageCount,
            ResolvePlayerLine = ResolvePlayPlayerInput,
            SyncThreadScope = SyncPlayThreadScopeForPacket,
        });
        _preparedSendArtifactStore.Set(artifact);

        if (artifact is null)
        {
            PlayPromptComposer.SetMergedPreview(null);
            return;
        }

        PlayPromptComposer.SetMergedPreview(FormatMergedPreviewForUi(bundle, artifact.MergedText));
        RefreshPlaySendArmState();
    }

    internal void UpdatePlayMergedPreview() =>
        _ = UpdatePlayMergedPreviewAsync();

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

    private void CopyPlayPacket() => _ = CopyPlayPacketAsync();

    private async Task CopyPlayPacketAsync()
    {
        if (_activeAdventureId is not { } id)
            return;

        _playView?.SaveConfiguration();
        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        SyncPlayThreadScopeForPacket(bundle);

        var priorThreadUserMessageCount = await GetPlayThreadUserMessageCountAsync();

        var attachmentContext = GetActivePlayComposeInjection()?.GetLastAttachmentContext();
        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = GetPlayPlayerLineText(),
                AttachmentContext = attachmentContext,
                ConsumeContinuationQueue = false,
                ApplySurfaceActions = true,
                PriorThreadUserMessageCount = priorThreadUserMessageCount,
            },
            ResolvePlayPlayerInput,
            syncThreadScope: null);

        if (string.IsNullOrWhiteSpace(session.PlayerLine))
        {
            SetPlayComposeStatus("Enter a prompt or add lines to the continuation queue.");
            return;
        }

        PlayPromptComposer?.SetMergedPreview(FormatMergedPreviewForUi(bundle, session.Prepared.MergedText));
        try
        {
            Clipboard.SetText(session.Prepared.MergedText);
            SetPlayComposeStatus("Merged packet copied to clipboard.");
        }
        catch
        {
            SetPlayComposeStatus("Could not copy to clipboard.");
        }
    }

    private Task SendPlayPromptAsync(
        PlayComposeSendEventArgs? sendRequest = null,
        ChatGptPlayComposeInjection? composeInjection = null) =>
        _playSendOrchestrator.RequestSendAsync(sendRequest, composeInjection, this);

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
        if (composeInjection?.CoreWebView2 is not { } core
            || GetPlayComposeWebView(composeInjection) is not { } composeWebView)
            return;

        if (!ShouldUseWrapperComposer(composeWebView))
            return;

        var bridge = GetOrRegisterAdventureBridge(composeWebView);
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
        var activePlayConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        if (!page.Success)
        {
            return new PlayContextResult
            {
                Status = string.IsNullOrWhiteSpace(activePlayConversationId)
                    ? PlayContextStatus.NoConversation
                    : PlayContextStatus.NavigationFailed,
                ConversationId = page.ConversationId ?? activePlayConversationId,
                Error = page.Error,
            };
        }

        AdventureStore.Save(bundle);

        return new PlayContextResult
        {
            Status = PlayContextStatus.Ready,
            ConversationId = page.ConversationId ?? activePlayConversationId,
        };
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
        var conversationId = sendResult.ConversationId ?? PlayThreadBindingService.GetActiveConversationId(bundle);

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
            AdventureStore.Save(bundle);
            _ = SyncActiveThreadLogAsync(
                bundle.Metadata.Id,
                AdventureThreadKind.Play,
                ThreadConversationLogCaptureSource.Send,
                snapshotTrigger: ThreadConversationLogSnapshotTrigger.Send,
                snapshotCorrelation: BuildSendSnapshotCorrelation(bundle, turn));
            return string.IsNullOrWhiteSpace(narratorText)
                ? "Sent — narrator response not captured yet. Send again or use context menu Edit response when ready."
                : "Sent — narrator still generating (placeholder captured). Use context menu Edit response or retry when ready.";
        }

        var fullAssistant = sendResult.NarratorText ?? narratorText;

        if (PlayUtilityRetrievalService.ProcessAssistantResponse(bundle, fullAssistant, conversationId).AnyProcessed)
            AdventureStore.Save(bundle);

        var narratorForTurn = PlayUtilityRetrievalService.StripUtilityResponsesForNarrator(fullAssistant);
        if (string.IsNullOrWhiteSpace(narratorForTurn))
            narratorForTurn = narratorText ?? "";

        NarratorOverrideResolver.ClearTurnOverrides(bundle.Metadata.Settings);
        CanonReconciliationService.ClearNotify(bundle);
        AdventureStore.Save(bundle);
        await SyncActiveThreadLogAsync(
            bundle.Metadata.Id,
            AdventureThreadKind.Play,
            ThreadConversationLogCaptureSource.Send,
            snapshotTrigger: ThreadConversationLogSnapshotTrigger.Send,
            snapshotCorrelation: BuildSendSnapshotCorrelation(bundle, turn));

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            _ = RunScheduledJobsAfterTurnAsync(bundle, turn);

        return string.IsNullOrWhiteSpace(narratorForTurn)
            ? "Sent — turn logged without narrator text. Use context menu Edit response if needed."
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
        var core = GetActivePlayComposeInjection()?.CoreWebView2
                   ?? _playWebView?.CoreWebView2;
        if (core is null)
            return;

        if (PlayConversationPageService.TryAdoptBrowserConversation(bundle, core.Source)
            || PlayContextSessionCache.TrySyncPlayThreadFromSource(bundle, core.Source))
            AdventureStore.Save(bundle);
    }
}
