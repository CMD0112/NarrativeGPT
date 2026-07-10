using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>WinUI implementation of <see cref="IPlaySendHost"/>.</summary>
internal sealed class WinUiPlaySendHost : IPlaySendHost
{
    private readonly WinUiPlaySessionService _session;
    private readonly PlaySendOrchestrator _orchestrator = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly PreparedSendArtifactStore _artifactStore = new();
    private readonly Dictionary<object, AdventureTurnService> _turnServices = new();
    private readonly Dictionary<object, ChatGptAdventureBridgeInjection> _bridges = new();

    private object? _activePlayTabHost;
    private int _activeSendCount;
    private ChatGptConversationSendService? _conversationSend;

    public WinUiPlaySendHost(WinUiPlaySessionService session) => _session = session;

    public Guid? ActiveAdventureId => _session.CurrentBundle?.Metadata.Id;

    public IPlayTabRegistry TabRegistry => _session.TabRegistry;

    public PreparedSendArtifactStore ArtifactStore => _artifactStore;

    public object? ActivePlayTabHost
    {
        get => _activePlayTabHost;
        set => _activePlayTabHost = value;
    }

    public Task<bool> TryAcquireSendGateAsync() => _sendGate.WaitAsync(0);

    public void ReleaseSendGate() => _sendGate.Release();

    public int IncrementActiveSendCount() => Interlocked.Increment(ref _activeSendCount);

    public void DecrementActiveSendCount() => Interlocked.Decrement(ref _activeSendCount);

    public void OnSendFinally(ChatGptPlayComposeInjection? composeInjection) =>
        RefreshArmState(composeInjection);

    public ChatGptPlayComposeInjection? GetActiveComposeInjection() =>
        _session.GetActiveComposeInjection();

    public PlayTabCapabilities ResolveCapabilities(AdventureBundle bundle, object tabHost)
    {
        var source = PlayWebViewCoreBridge.GetSource(TabRegistry.GetCoreWebView(tabHost));
        var ctx = PlayTabCapabilityContext.FromRegistry(bundle, tabHost, TabRegistry, source);
        return PlayTabCapabilityResolver.Resolve(ctx, PlayTabSessionFactory.FromBundle(bundle));
    }

    public string ResolvePlayerInput(AdventureBundle bundle, bool consumeQueue, string? composeText) =>
        PlaySendHostRuntime.ResolvePlayerInput(
            bundle,
            consumeQueue,
            composeText,
            () => GetActiveComposeInjection()?.GetText() ?? "",
            onQueueConsumed: b => _session.ReloadBundle(b.Metadata.Id));

    public AttachmentContext? BuildAttachmentContext(
        PlayComposeSendEventArgs? sendRequest,
        IReadOnlyList<PlayComposePendingAttachment> pendingAttachments) =>
        PlaySendHostRuntime.BuildAttachmentContext(sendRequest, pendingAttachments);

    public void SyncPlayThreadScopeForPacket(AdventureBundle bundle)
    {
        var source = PlayWebViewCoreBridge.GetSource(
            GetActiveComposeInjection()?.CoreWebView
            ?? (_activePlayTabHost is not null
                ? TabRegistry.GetCoreWebView(_activePlayTabHost)
                : null));
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (PlayConversationPageService.TryAdoptBrowserConversation(bundle, source)
            || PlayContextSessionCache.TrySyncPlayThreadFromSource(bundle, source))
        {
            AdventureStore.Save(bundle);
            _session.ReloadBundle(bundle.Metadata.Id);
        }
    }

    public async Task SyncComposeUiAsync(PlayComposeUiState state, ChatGptPlayComposeInjection? injection)
    {
        injection ??= GetActiveComposeInjection();
        if (injection?.CoreWebView is not { } coreObj)
            return;

        await injection.ApplyComposeStateFromHost(coreObj, state);
        _session.NotifyStatusChanged();
    }

    public Task SetComposeBusyAsync(bool busy, string? message, ChatGptPlayComposeInjection? injection) =>
        SyncComposeUiAsync(new PlayComposeUiState { Busy = busy, Status = message }, injection);

    public void SetComposeStatus(string? text, ChatGptPlayComposeInjection? injection) =>
        _ = SetComposeBusyAsync(false, text, injection);

    public async Task RestoreComposeInputAsync(string text, ChatGptPlayComposeInjection? injection)
    {
        injection ??= GetActiveComposeInjection();
        if (injection?.CoreWebView is not { } coreObj)
            return;

        await injection.ApplyComposeStateFromHost(coreObj, new PlayComposeUiState { Text = text, Focus = true });
    }

    public void SetMergedPreview(AdventureBundle bundle, string? mergedText) =>
        _session.SetMergedPreview(mergedText is null ? null : FormatMergedPreviewText(bundle, mergedText));

    public void ClearComposePrompt(ChatGptPlayComposeInjection? injection)
    {
        injection?.ClearCachedText();
        _session.ClearMergedPreview();
        _ = SyncComposeUiAsync(new PlayComposeUiState { Clear = true }, injection);
    }

    public async Task EnsurePlayWebViewReadyAsync(
        Guid adventureId,
        bool selectTab,
        bool prepareContext,
        bool navigateToBrowseTarget) =>
        await _session.EnsurePlayTabReadyAsync(adventureId, selectTab, navigateToBrowseTarget);

    public AdventureTurnService? GetOrCreateTurnService(object tabHost)
    {
        if (!_turnServices.TryGetValue(tabHost, out var service))
        {
            var bridge = GetOrRegisterBridge(tabHost);
            service = new AdventureTurnService(bridge);
            if (_conversationSend is not null)
                service.SetConversationSendService(_conversationSend);
            _turnServices[tabHost] = service;
        }
        else if (_conversationSend is not null)
        {
            service.SetConversationSendService(_conversationSend);
        }

        return service;
    }

    public async Task<PlayContextResult?> RequireLinkedPlayThreadForSendAsync(
        AdventureBundle bundle,
        object coreObj)
    {
        var result = await PlaySendHostRuntime.RequireLinkedPlayThreadForSendAsync(coreObj, bundle);
        if (result?.Status == PlayContextStatus.Ready)
        {
            AdventureStore.Save(bundle);
            _session.ReloadBundle(bundle.Metadata.Id);
        }

        return result;
    }

    public Task PrefetchSendWarmupAsync(object coreObj, AdventureBundle bundle) =>
        Task.CompletedTask;

    public PlaySendSourcesPromptResult PromptSourcesInlineFallback(string warnMessage)
    {
        WinUiEventLogger.Debug("play_send_sources_prompt", warnMessage);
        return PlaySendSourcesPromptResult.SendWithInlineFallback;
    }

    public Task<AdventureTurnResult> DeliverPacketAsync(PlaySendDeliveryRequest request) =>
        PlaySendDeliveryService.DeliverAsync(
            request.TurnService,
            request,
            EnsureLinkedPlayContextForBundleAsync);

    public async Task<string> CompleteTurnAfterSendAsync(PlaySendTurnCompletionRequest request) =>
        await _session.CompleteTurnAfterSendAsync(request, this);

    public void OnSendSucceeded(PlaySendSuccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(
                AdventureThreadRegistryService.GetActiveEntry(request.Bundle, AdventureThreadKind.Play)?.PinnedTabKey))
        {
            if (request.PlayTabHost is Microsoft.UI.Xaml.Controls.WebView2 wv)
                _session.PinActiveTab(wv);
        }

        PlayHandoffService.TryReconcileAfterFirstSend(request.Bundle);
        FocusPlayTab(request.PlayTabHost);
        _session.ReloadBundle(request.AdventureId);
        _session.ClearMergedPreview();

        _ = SyncComposeUiAsync(new PlayComposeUiState
        {
            Busy = false,
            Focus = true,
            Status = request.SuccessStatus,
        }, request.ComposeInjection);

        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitResult,
            PlaySendCategory.Bridge,
            PlaySendLevel.Info,
            "Play send orchestrator completed on WinUI host",
            outcome: "ok",
            data: new { mergedLength = request.MergedLength });

        RefreshArmState(request.ComposeInjection);
        _session.NotifyStatusChanged();
    }

    public void SchedulePostTurnJobs(AdventureBundle bundle, TurnRecord turn)
    {
        var jobs = GenerationJobScheduler.GetJobsAfterTurn(bundle, turn);
        if (jobs.Count == 0)
            return;

        if (!PlayUtilityInjectionService.UsesInjectionFirst(bundle))
            return;

        PlayUtilityInjectionService.EnqueueAfterTurn(bundle, turn, jobs);
        AdventureStore.Save(bundle);
        if (UtilityOutboxService.PendingCount(bundle.Metadata.Id) > 0)
            _session.UtilityWorker.RequestOutboxPump(bundle);
        _session.UtilityWorker.UpdateActiveJobCount(bundle.Metadata.Id);
    }

    public async void ShowSendError(string message, bool isWarning)
    {
        var dialog = new ContentDialog
        {
            Title = "Send",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = WinUiShellHost.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
        }
        catch
        {
            /* ignore */
        }
    }

    public void InvalidatePlayContext(Guid adventureId) =>
        PlayContextSessionCache.Invalidate(adventureId);

    public string FormatMergedPreview(AdventureBundle bundle, string mergedText) =>
        FormatMergedPreviewText(bundle, mergedText);

    public void FocusPlayTab(object tabHost) => TabRegistry.FocusTabHost(tabHost);

    public Task RequestSendAsync(
        PlayComposeSendEventArgs? sendRequest,
        ChatGptPlayComposeInjection? composeInjection = null) =>
        _orchestrator.RequestSendAsync(sendRequest, composeInjection, this);

    internal void WireConversationSend(ChatGptConversationSendService conversationSend)
    {
        _conversationSend = conversationSend;
        foreach (var turnService in _turnServices.Values)
            turnService.SetConversationSendService(conversationSend);
    }

    internal void RefreshArmState(ChatGptPlayComposeInjection? injection = null)
    {
        injection ??= GetActiveComposeInjection();
        if (ActiveAdventureId is not { } id || injection?.TabHost is not { } tabHost)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var caps = ResolveCapabilities(bundle, tabHost);
        var arm = PlaySendArmService.Evaluate(caps, _artifactStore);
        PlaySendTraceMapper.LogArmState(arm);

        _ = SyncComposeUiAsync(new PlayComposeUiState
        {
            SendEnabled = arm.IsArmed,
            InjectionArmed = arm.IsArmed,
            InjectionArmReason = arm.ReasonCode,
            Status = arm.IsArmed ? null : arm.UserGuidance,
        }, injection);
    }

    private ChatGptAdventureBridgeInjection GetOrRegisterBridge(object tabHost)
    {
        if (_bridges.TryGetValue(tabHost, out var existing))
            return existing;

        var coreObj = TabRegistry.GetCoreWebView(tabHost)
                      ?? throw new InvalidOperationException("WebView core is not ready.");

        var bridge = ChatGptAdventureBridgeInjection.CreateForCore(coreObj);
        bridge.Register();
        WinUiTurnInvalidationBridge.Wire(bridge, _session);
        _bridges[tabHost] = bridge;
        return bridge;
    }

    private Task<PlayContextResult?> EnsureLinkedPlayContextForBundleAsync(AdventureBundle bundle) =>
        Task.FromResult<PlayContextResult?>(null);

    private static string FormatMergedPreviewText(AdventureBundle bundle, string mergedText)
    {
        var preview = bundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(mergedText)
            : mergedText;
        return preview.Length > 4000 ? preview[..4000] + "…" : preview;
    }
}
