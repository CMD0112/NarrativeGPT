using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow : IPlaySendHost
{
    private readonly PlaySendOrchestrator _playSendOrchestrator = new();

    Guid? IPlaySendHost.ActiveAdventureId => _activeAdventureId;

    PreparedSendArtifactStore IPlaySendHost.ArtifactStore => _preparedSendArtifactStore;

    Task<bool> IPlaySendHost.TryAcquireSendGateAsync() => _playSendGate.WaitAsync(0);

    void IPlaySendHost.ReleaseSendGate() => _playSendGate.Release();

    int IPlaySendHost.IncrementActiveSendCount() =>
        Interlocked.Increment(ref _activePlaySendCount);

    void IPlaySendHost.DecrementActiveSendCount() =>
        Interlocked.Decrement(ref _activePlaySendCount);

    void IPlaySendHost.OnSendFinally(ChatGptPlayComposeInjection? composeInjection)
    {
        PlayPromptComposer?.SetBusy(false);
        RefreshPlaySendArmState(composeInjection);
    }

    IPlayTabRegistry IPlaySendHost.TabRegistry => new WpfPlayTabRegistry(ChatTabs);

    ChatGptPlayComposeInjection? IPlaySendHost.GetActiveComposeInjection() =>
        GetActivePlayComposeInjection();

    PlayTabCapabilities IPlaySendHost.ResolveCapabilities(AdventureBundle bundle, object tabHost) =>
        PlayTabSessionResolver.ResolveCapabilities(bundle, tabHost, new WpfPlayTabRegistry(ChatTabs));

    string IPlaySendHost.ResolvePlayerInput(AdventureBundle bundle, bool consumeQueue, string? composeText) =>
        ResolvePlayPlayerInput(bundle, consumeQueue, composeText);

    AttachmentContext? IPlaySendHost.BuildAttachmentContext(
        PlayComposeSendEventArgs? sendRequest,
        IReadOnlyList<PlayComposePendingAttachment> pendingAttachments) =>
        BuildAttachmentContext(sendRequest, pendingAttachments);

    void IPlaySendHost.SyncPlayThreadScopeForPacket(AdventureBundle bundle) =>
        SyncPlayThreadScopeForPacket(bundle);

    Task IPlaySendHost.SyncComposeUiAsync(PlayComposeUiState state, ChatGptPlayComposeInjection? injection) =>
        SyncPlayComposeUiAsync(state, injection);

    Task IPlaySendHost.SetComposeBusyAsync(bool busy, string? message, ChatGptPlayComposeInjection? injection) =>
        SetPlayComposeBusyAsync(busy, message, injection);

    void IPlaySendHost.SetComposeStatus(string? text, ChatGptPlayComposeInjection? injection) =>
        SetPlayComposeStatus(text, injection);

    Task IPlaySendHost.RestoreComposeInputAsync(string text, ChatGptPlayComposeInjection? injection) =>
        RestorePlayComposeInputAsync(text, injection);

    void IPlaySendHost.SetMergedPreview(AdventureBundle bundle, string? mergedText) =>
        PlayPromptComposer?.SetMergedPreview(
            mergedText is null ? null : FormatMergedPreviewForUi(bundle, mergedText));

    void IPlaySendHost.ClearComposePrompt(ChatGptPlayComposeInjection? injection)
    {
        injection?.ClearCachedText();
        PlayPromptComposer?.ClearPrompt();
        SyncPlayComposeUi(new PlayComposeUiState { Clear = true }, injection);
    }

    Task IPlaySendHost.EnsurePlayWebViewReadyAsync(
        Guid adventureId,
        bool selectTab,
        bool prepareContext,
        bool navigateToBrowseTarget) =>
        EnsurePlayWebViewReadyAsync(adventureId, selectTab, prepareContext, navigateToBrowseTarget);

    object? IPlaySendHost.ActivePlayTabHost
    {
        get => _playWebView;
        set => _playWebView = value as WebView2;
    }

    AdventureTurnService? IPlaySendHost.GetOrCreateTurnService(object tabHost) =>
        tabHost is WebView2 wv ? GetOrCreateTurnService(wv) : null;

    void IPlaySendHost.FocusPlayTab(object tabHost)
    {
        if (tabHost is WebView2 wv)
            wv.Focus();
    }

    Task<PlayContextResult?> IPlaySendHost.RequireLinkedPlayThreadForSendAsync(
        AdventureBundle bundle,
        object core) =>
        RequireLinkedPlayThreadForSendAsync(bundle, (CoreWebView2)core);

    async Task IPlaySendHost.PrefetchSendWarmupAsync(object core, AdventureBundle bundle)
    {
        if (_playSendWarmupService is not null)
            await _playSendWarmupService.PrefetchAsync((CoreWebView2)core, bundle);
    }

    PlaySendSourcesPromptResult IPlaySendHost.PromptSourcesInlineFallback(string warnMessage)
    {
        var result = MessageBox.Show(
            this,
            warnMessage,
            "Project sources",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _ = OpenSourceManagerDialogAsync(_activeAdventureId!.Value);
            return PlaySendSourcesPromptResult.CancelSend;
        }

        return PlaySendSourcesPromptResult.SendWithInlineFallback;
    }

    Task<AdventureTurnResult> IPlaySendHost.DeliverPacketAsync(PlaySendDeliveryRequest request) =>
        PlaySendDeliveryService.DeliverAsync(
            request.TurnService,
            request,
            bundle => EnsureLinkedPlayContextForBundleAsync(bundle));

    Task<string> IPlaySendHost.CompleteTurnAfterSendAsync(PlaySendTurnCompletionRequest request) =>
        CompletePlayTurnAfterSendAsync(
            request.Bundle,
            request.Turn,
            request.SendResult,
            (CoreWebView2)request.Core,
            request.TurnService,
            request.ComposeInjection,
            request.AssistantBaselineCount);

    void IPlaySendHost.OnSendSucceeded(PlaySendSuccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(
                AdventureThreadRegistryService.GetActiveEntry(request.Bundle, AdventureThreadKind.Play)?.PinnedTabKey))
        {
            if (request.PlayTabHost is WebView2 wv)
                PlayTabPinService.PinTab(request.Bundle, wv, ChatTabs);
        }

        PlayHandoffService.TryReconcileAfterFirstSend(request.Bundle);
        if (request.PlayTabHost is WebView2 playWv)
            playWv.Focus();
        ReloadPlayAdventure(request.AdventureId);
        UpdatePlayLinkStatus();
        PlayPromptComposer?.SetMergedPreview(null);

        _ = SyncPlayComposeUiAsync(new PlayComposeUiState
        {
            Busy = false,
            Focus = true,
            Status = request.SuccessStatus,
        }, request.ComposeInjection);

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitResult,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Play send orchestrator completed",
            outcome: "ok",
            data: new { mergedLength = request.MergedLength });

        RefreshPlaySendArmState(request.ComposeInjection);
    }

    void IPlaySendHost.SchedulePostTurnJobs(AdventureBundle bundle, TurnRecord turn) =>
        _ = RunScheduledJobsAfterTurnAsync(bundle, turn);

    void IPlaySendHost.ShowSendError(string message, bool isWarning)
    {
        MessageBox.Show(
            this,
            message,
            "Send",
            MessageBoxButton.OK,
            isWarning ? MessageBoxImage.Warning : MessageBoxImage.Error);
    }

    void IPlaySendHost.CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { /* ignore */ }
    }

    void IPlaySendHost.InvalidatePlayContext(Guid adventureId) =>
        PlayContextSessionCache.Invalidate(adventureId);

    string IPlaySendHost.FormatMergedPreview(AdventureBundle bundle, string mergedText) =>
        FormatMergedPreviewForUi(bundle, mergedText);

    internal void RefreshPlaySendArmState(ChatGptPlayComposeInjection? injection = null)
    {
        injection ??= GetActivePlayComposeInjection();
        if (_activeAdventureId is not { } id || injection?.TabHost is not { } tabHost)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var registry = new WpfPlayTabRegistry(ChatTabs);
        var caps = PlayTabSessionResolver.ResolveCapabilities(bundle, tabHost, registry);
        var arm = PlaySendArmService.Evaluate(caps, _preparedSendArtifactStore);
        PlaySendTraceMapper.LogArmState(arm);

        SyncPlayComposeUi(new PlayComposeUiState
        {
            SendEnabled = arm.IsArmed,
            InjectionArmed = arm.IsArmed,
            InjectionArmReason = arm.ReasonCode,
            Status = arm.IsArmed ? null : arm.UserGuidance,
        }, injection);

        UiEventLogger.Debug(
            "play_send_arm_state",
            arm.Label,
            new
            {
                armed = arm.IsArmed,
                reason = arm.ReasonCode,
                guidance = arm.UserGuidance,
            });
    }
}
