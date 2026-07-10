using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private WebView2? _playWebView;
    private readonly Dictionary<WebView2, ChatGptAdventureBridgeInjection> _adventureBridges = new();
    private readonly Dictionary<WebView2, ChatGptContextTagsInjection> _contextTagInjections = new();
    private readonly Dictionary<WebView2, ChatGptPlayComposeInjection> _playComposeInjections = new();

    public WebView2? GetPlayWebView() => _playWebView;

    public bool PinActiveTabForPlay(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || GetActiveWebView() is not { } active)
            return false;

        PlayTabPinService.PinTab(bundle, active, ChatTabs);
        _playWebView = active;
        GetOrRegisterAdventureBridge(active);
        SelectTabForWebView(active);
        UpdatePlayLinkStatus();
        ApplyWrapperComposerToPlayTab(_appMode == AppMode.Play);
        return true;
    }

    public void ClearPlayTabPin(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        PlayTabPinService.ClearPin(bundle);
        if (_activeAdventureId == adventureId)
            _playWebView = null;

        UpdatePlayLinkStatus();
    }

    public async Task StartNarrativeFromSourcesAsync(Guid adventureId) =>
        await RotatePlayThreadAsync(
            adventureId,
            new PlayThreadStartRequest { Kind = PlayThreadStartKind.FreshStart });

    public async Task HandoffPlayThreadAsync(Guid adventureId, PlayThreadStartRequest request) =>
        await RotatePlayThreadAsync(adventureId, request);

    public Task StartNewPlayThreadAsync(Guid adventureId, PlayThreadStartRequest? request = null) =>
        RotatePlayThreadAsync(adventureId, request ?? new PlayThreadStartRequest());

    private async Task RotatePlayThreadAsync(Guid adventureId, PlayThreadStartRequest request)
    {
        var bundle = PlayThreadPacketService.ReloadFresh(adventureId);
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            MessageBox.Show(
                this,
                PlayThreadRotationCopy.LinkProjectFirstMessage,
                PlayThreadRotationCopy.NarrativeFromSourcesTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        var isHandoff = request.Kind == PlayThreadStartKind.Handoff;
        var hasPlayHistory = PlayTurnScopeService.GetPacketAcceptedTurns(bundle).Count > 0;

        var confirmBody = isHandoff
            ? PlayThreadRotationCopy.HandoffConfirmBody
            : PlayThreadRotationCopy.NarrativeFromSourcesConfirmBody(hasPlayHistory);

        var dialogTitle = isHandoff
            ? PlayThreadRotationCopy.HandoffToNewChatTitle
            : PlayThreadRotationCopy.NarrativeFromSourcesTitle;

        if (!request.SkipConfirmation
            && MessageBox.Show(
                this,
                confirmBody,
                dialogTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await EnsureChatWebViewEnvironmentReadyAsync();

        var playTabForReuse = ResolvePlayWebView(bundle) ?? _playWebView;

        var clipboardPacket = PlayHandoffService.PrepareClipboardPacket(bundle, request, request.Kind);

        ProjectChatDraftService.BeginPlayDraft(bundle);
        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);
        PlayContextSessionCache.Invalidate(adventureId);

        var clipboardLabel = isHandoff ? "PlayHandoff" : "StartNarrativeFromSources";
        if (!ClipboardCopy.TrySetText(clipboardPacket, clipboardLabel))
        {
            MessageBox.Show(
                this,
                isHandoff
                    ? "Could not copy the handoff packet to the clipboard.\n\n"
                      + $"Use Play settings → {PlayThreadRotationCopy.PreviewHandoffPacketButton} to copy it manually."
                    : "Could not copy the narrative start packet to the clipboard.\n\n"
                      + $"Use Play settings → Play packet → {PlayThreadRotationCopy.PreviewNarrativePacketButton} to copy it manually, then link a Play tab.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var wv = ResolveExistingChatWebView(playTabForReuse);
        if (wv is null)
        {
            MessageBox.Show(
                this,
                "No browser tab is available.\n\n"
                + "The packet is still on your clipboard.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        if (wv.CoreWebView2 is not { } core)
        {
            MessageBox.Show(
                this,
                "Browser tab is still initializing — try again shortly.\n\n"
                + "The packet is still on your clipboard.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);

        SelectTabForWebView(wv);
        _playWebView = wv;
        ProjectChatDraftService.NoteDraftTab(bundle, wv, ChatTabs);

        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: projectUrl);
        }

        var reloaded = AdventureStore.Load(adventureId);
        if (reloaded is null)
            return;

        PlayTabPinService.PinTab(reloaded, wv, ChatTabs);
        PlayTabPinService.TryBindProjectSessionFromWebView(reloaded, wv);

        ReloadPlayAdventure(adventureId);
        UpdatePlayLinkStatus();

        MessageBox.Show(
            this,
            isHandoff
                ? PlayThreadRotationService.FormatHandoffThreadReadyMessage(core.Source, reloaded)
                : PlayThreadRotationService.FormatNarrativeFromSourcesReadyMessage(core.Source, reloaded),
            dialogTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public async Task DraftNewProjectChatAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            MessageBox.Show(
                this,
                "Link a ChatGPT Project first.",
                "Draft new project chat",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        await EnsureChatWebViewEnvironmentReadyAsync();

        ProjectChatDraftService.BeginDraftOnProjectPage(bundle);

        var wv = GetActiveWebView();
        if (wv is null)
        {
            foreach (var item in ChatTabs.Items)
            {
                if (item is TabItem { Content: WebView2 existing })
                {
                    wv = existing;
                    break;
                }
            }
        }

        if (wv is null && _chatWebViewEnvironment is not null)
            wv = await AddChatTabAsync("ChatGPT");

        if (wv is null)
        {
            MessageBox.Show(
                this,
                "No browser tab is available. Open a ChatGPT tab first.",
                "Draft new project chat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        if (wv.CoreWebView2 is not { } core)
            return;

        SelectTabForWebView(wv);
        ProjectChatDraftService.NoteDraftTab(bundle, wv, ChatTabs);
        await SuspendPlayAutomationForDraftTabAsync(wv);

        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: projectUrl);
        }

        MessageBox.Show(
            this,
            "Drafting mode is on — the wrapper will not redirect this tab to your pinned play thread "
            + "while you stay on the Project page.\n\n"
            + "Click New chat in ChatGPT, then pin the tab as your play thread when ready.\n\n"
            + "Use Cancel drafting in Play settings → Session to restore normal navigation.",
            "Draft new project chat",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public void CancelProjectChatDraft(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || !ProjectChatDraftService.IsActive(bundle))
            return;

        WebView2? draftTab = null;
        foreach (var item in ChatTabs.Items)
        {
            if (item is TabItem { Content: WebView2 wv }
                && ProjectChatDraftService.IsDraftTab(bundle, wv, ChatTabs))
            {
                draftTab = wv;
                break;
            }
        }

        ProjectChatDraftService.Cancel(bundle);
        UpdatePlayLinkStatus();

        if (draftTab is not null && !IsPinnedPlayWebView(draftTab))
            _ = RestorePlayAutomationForTabAsync(draftTab);
    }

    private async Task SuspendPlayAutomationForDraftTabAsync(WebView2 wv)
    {
        if (wv.CoreWebView2 is not { } core)
            return;

        if (_playComposeInjections.TryGetValue(wv, out var injection))
            await injection.SetNativePassthroughAsync(true);
        else
            await ChatGptPlayComposeInjection.ApplyNativePassthroughAsync(core, true);
    }

    private async Task RestorePlayAutomationForTabAsync(WebView2 wv)
    {
        if (wv.CoreWebView2 is not { } core)
            return;

        if (_playComposeInjections.TryGetValue(wv, out var injection))
            await injection.SetNativePassthroughAsync(false);
        else
            await ChatGptPlayComposeInjection.ApplyNativePassthroughAsync(core, false);
    }

    private bool ShouldUseLegacyNativePassthrough(AdventureBundle bundle, WebView2 wv, string? source = null) =>
        PlayTabSessionResolver.ResolveCapabilities(bundle, wv, ChatTabs, source)
            .LegacySuppressPlayAutomation;

    internal void RefreshPlayComposeNavigationState(WebView2 wv, AdventureBundle bundle)
    {
        if (wv.CoreWebView2 is not { } core)
            return;

        if (PlayTabPinService.TryReconcileStalePlayPin(bundle, wv, ChatTabs))
        {
            bundle = AdventureStore.Load(bundle.Metadata.Id) ?? bundle;
            UiEventLogger.Info(
                "play_pin_reconciled",
                "Re-pinned play tab after URL/tab-key drift",
                new { source = core.Source });
        }

        var suppress = ShouldUseLegacyNativePassthrough(bundle, wv, core.Source);

        if (_playComposeInjections.TryGetValue(wv, out var injection))
            _ = injection.SetNativePassthroughAsync(suppress);
        else if (suppress)
            _ = ChatGptPlayComposeInjection.ApplyNativePassthroughAsync(core, true);
        else if (_appMode == AppMode.Play)
            RegisterPlayComposeInjection(wv);

        if (_appMode == AppMode.Play)
            ApplyWrapperComposerForWebView(wv);

        RefreshPlaySendArmState(_playComposeInjections.GetValueOrDefault(wv));
    }

    private WebView2? ResolveExistingChatWebView(WebView2? preferred)
    {
        if (preferred is not null && PlayTabPinService.GetTabKey(preferred, ChatTabs) is not null)
            return preferred;

        if (GetActiveWebView() is { } active)
            return active;

        foreach (var item in ChatTabs.Items)
        {
            if (item is TabItem { Content: WebView2 wv })
                return wv;
        }

        return null;
    }

    public bool SelectPinnedPlayTab(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        var pinKey = bundle is null ? null : PlayTabPinService.GetPlayPinKey(bundle);
        if (string.IsNullOrWhiteSpace(pinKey))
            return false;

        if (PlayTabPinService.FindWebViewByPinKey(ChatTabs, pinKey) is not { } pinned)
            return false;

        SelectTabForWebView(pinned);
        if (_activeAdventureId == adventureId)
            _playWebView = pinned;

        return true;
    }

    private WebView2? ResolvePlayWebView(AdventureBundle? bundle)
    {
        if (bundle is null)
            return null;

        if (ProjectChatDraftService.ShouldSuppressPlayTabSelection(bundle)
            && _appMode == AppMode.Play
            && GetActiveWebView() is { } draftActive)
        {
            var draftSource = draftActive.CoreWebView2?.Source;
            if (AdventureNavigationService.IsOnLinkedProjectPage(draftSource, bundle))
                return draftActive;
        }

        if (PlayTabPinService.TryFindWebViewForPlaySession(ChatTabs, bundle) is { } sessionTab)
            return sessionTab;

        if (_appMode == AppMode.Play && GetActiveWebView() is { } active)
        {
            var source = active.CoreWebView2?.Source;
            if (PlayTabPinService.IsOnPlayTarget(source, bundle))
                return active;

            if ((string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle))
                 || ProjectChatDraftService.ShouldStayOnProjectPage(bundle, source))
                && AdventureNavigationService.IsOnLinkedProjectPage(source, bundle))
            {
                return active;
            }
        }

        return null;
    }

    private async Task<WebView2?> ResolvePlayWebViewAsync(
        Guid adventureId,
        bool promptToPinIfMissing = false,
        bool selectTab = true,
        bool navigateToBrowseTarget = true)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        AdventureNavigationService.SyncLinkedFields(bundle);
        PersistPromotedLinkMetadataIfNeeded(bundle);

        var wv = ResolvePlayWebView(bundle) ?? ThreadWebViewResolver.TryFindExisting(ChatTabs, bundle, AdventureThreadKind.Play);
        if (wv is not null)
        {
            _playWebView = wv;
            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            if (navigateToBrowseTarget && wv.CoreWebView2 is { } coreBeforeNav)
            {
                var browseUrl = AdventureNavigationService.ResolvePlayBrowseUrl(bundle);
                if (browseUrl is not null
                    && AdventureNavigationService.ShouldNavigateToPlayTarget(coreBeforeNav.Source, bundle, browseUrl))
                {
                    coreBeforeNav.Navigate(browseUrl);
                    await WaitForChatGptNavigationAsync(coreBeforeNav, expectedDestination: browseUrl);
                }
            }

            if (selectTab)
                SelectTabForWebView(wv);
            return wv;
        }

        if (ThreadWebViewResolver.HasPersistedSession(bundle, AdventureThreadKind.Play))
        {
            wv = await RestorePlayWebViewAsync(bundle, selectTab);
            if (wv is not null)
            {
                _playWebView = wv;
                return wv;
            }
        }

        if (!promptToPinIfMissing || !PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle))
            return null;

        if (_activeAdventureId == adventureId)
            OpenThreadManagerDialog(adventureId, AdventureThreadKind.Play);

        return null;
    }

    private static void PersistPromotedLinkMetadataIfNeeded(AdventureBundle bundle)
    {
        var onDisk = AdventureStore.Load(bundle.Metadata.Id);
        if (onDisk is null)
            return;

        var diskProject = AdventureProjectBindingService.GetLinkedProjectId(onDisk.Metadata);
        var memProject = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (!string.IsNullOrWhiteSpace(diskProject) || string.IsNullOrWhiteSpace(memProject))
            return;

        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
    }

    private async Task<WebView2?> RestorePlayWebViewAsync(AdventureBundle bundle, bool selectTab)
    {
        await EnsureChatWebViewEnvironmentReadyAsync();

        var targetUrl = AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle)
                      ?? ThreadWebViewResolver.ResolveTargetUrl(bundle, AdventureThreadKind.Play);
        if (targetUrl is null)
            return null;

        WebView2? wv = ThreadWebViewResolver.SelectForRestore(ChatTabs, bundle, AdventureThreadKind.Play);

        if (wv is null)
        {
            if (_chatWebViewEnvironment is null)
                return null;

            wv = await AddChatTabAsync("ChatGPT");
        }

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        if (wv.CoreWebView2 is { } core
            && ThreadWebViewResolver.ShouldNavigateToTarget(bundle, AdventureThreadKind.Play, core.Source, targetUrl))
        {
            core.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: targetUrl);
        }

        PlayTabPinService.PinTab(bundle, wv, ChatTabs);
        if (selectTab)
            SelectTabForWebView(wv);

        return wv;
    }

    private CancellationTokenSource? _playWarmupDebounceCts;

    private void DebouncedPlaySendWarmup(AdventureBundle bundle, CoreWebView2 core)
    {
        _playWarmupDebounceCts?.Cancel();
        _playWarmupDebounceCts = new CancellationTokenSource();
        var token = _playWarmupDebounceCts.Token;
        _ = DebouncedPlaySendWarmupCoreAsync(bundle, core, token);
    }

    private async Task DebouncedPlaySendWarmupCoreAsync(
        AdventureBundle bundle,
        CoreWebView2 core,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
            if (_playSendWarmupService is null)
                return;

            await _playSendWarmupService.PrefetchAsync(core, bundle, token);
        }
        catch (OperationCanceledException)
        {
            /* superseded */
        }
    }

    internal async Task EnsurePlayWebViewReadyAsync(
        Guid adventureId,
        bool selectTab = true,
        bool prepareContext = false,
        bool navigateToBrowseTarget = true)
    {
        await EnsurePlaySessionAsync(adventureId, selectTab, prepareContext, navigateToBrowseTarget);
    }

    private async Task EnsurePlaySessionAsync(
        Guid adventureId,
        bool selectTab = true,
        bool prepareContext = true,
        bool navigateToBrowseTarget = true)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        AdventureNavigationService.SyncLinkedFields(bundle);

        if (PlayThreadBindingService.SanitizeOnPlayOpen(bundle))
            bundle = AdventureStore.Load(adventureId) ?? bundle;

        var linkedProject = AdventureNavigationService.HasLinkedProject(bundle);
        var drafting = ProjectChatDraftService.IsActive(bundle);
        if (drafting)
            selectTab = false;

        var wv = await ResolvePlayWebViewAsync(
            adventureId,
            promptToPinIfMissing: false,
            selectTab,
            navigateToBrowseTarget);
        if (wv is null)
        {
            var promptPin = prepareContext
                && AdventureNavigationService.HasLinkedProject(bundle)
                && PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle);
            wv = await ResolvePlayWebViewAsync(adventureId, promptToPinIfMissing: promptPin, selectTab, navigateToBrowseTarget);
            if (wv is null)
                return;
        }

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        GetOrRegisterAdventureBridge(wv);
        _playWebView = wv;
        WireProjectServices(wv);
        GetOrCreateTurnService(wv);

        if (!prepareContext || wv.CoreWebView2 is not { } core)
            return;

        if (linkedProject)
        {
            if (PlayContextSessionCache.TrySyncConversationFromUrl(bundle, core.Source))
                AdventureStore.Save(bundle);

            var deferPlayContext =
                AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle);
            if (deferPlayContext
                && !string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle))
                && !PlayThreadBindingService.IsVerified(bundle))
            {
                PlayThreadRotationService.ReleasePlayThread(bundle);
                bundle.Metadata.PinnedPlayTabUrl =
                    AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle);
                AdventureStore.Save(bundle);
            }

            var browseUrl = deferPlayContext || drafting
                ? AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle)
                : AdventureNavigationService.ResolvePlayBrowseUrl(bundle);
            if (navigateToBrowseTarget
                && browseUrl is not null
                && AdventureNavigationService.ShouldNavigateToPlayTarget(core.Source, bundle, browseUrl))
            {
                core.Navigate(browseUrl);
                await WaitForChatGptNavigationAsync(core, expectedDestination: browseUrl);
            }

            if (deferPlayContext || drafting || !prepareContext)
                return;

            if (await PlayContextSessionCache.ShouldSkipReensureAsync(bundle, core, GetOrCreateTurnService(wv)))
            {
                _playSendWarmupService?.PrefetchFireAndForget(core, bundle);
                return;
            }

            var ctx = await EnsureLinkedPlayContextForBundleAsync(bundle);
            if (_playView is not null && _activeAdventureId == adventureId && ctx is not null && !ctx.IsReady)
                _playView.SetSessionError(AdventureNavigationService.FormatPlaySessionError(ctx));

            return;
        }

        var activeConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        if (!string.IsNullOrWhiteSpace(activeConversationId))
        {
            var conversationUrl = ChatGptUrls.BuildConversationUrl(activeConversationId);
            if (!AdventurePlayContextService.IsOnConversationPage(core.Source, activeConversationId))
            {
                core.Navigate(conversationUrl);
                await WaitForChatGptNavigationAsync(core, expectedDestination: conversationUrl);
            }

            return;
        }

        var fallbackUrl = AdventureNavigationService.ResolveTrustedFallbackUrl(bundle);
        if (AdventureNavigationService.IsGenericHomepage(core.Source)
            || !Uri.TryCreate(core.Source, UriKind.Absolute, out var currentUri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(currentUri))
        {
            core.Navigate(fallbackUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: fallbackUrl);
        }
    }

    private ChatGptAdventureBridgeInjection GetOrRegisterAdventureBridge(WebView2 wv)
    {
        if (!_adventureBridges.TryGetValue(wv, out var bridge))
        {
            bridge = new ChatGptAdventureBridgeInjection(wv);
            _adventureBridges[wv] = bridge;
            WireTurnInvalidationBridge(bridge);
            if (wv.CoreWebView2 is not null)
                bridge.Register(GetOrCreatePageHost(wv));
        }
        else if (wv.CoreWebView2 is not null && !bridge.IsRegistered)
        {
            bridge.Register(GetOrCreatePageHost(wv));
        }

        RegisterContextTagsInjection(wv);
        RegisterPlayComposeInjection(wv);
        return bridge;
    }

    private AdventureTurnService GetOrCreateTurnService(WebView2 wv)
    {
        var service = SessionHost.GetTurnService(wv);
        if (ReferenceEquals(wv, _playWebView) || IsPinnedPlayWebView(wv))
            _turnService = service;
        return service;
    }

    internal async Task<int> GetPlayThreadUserMessageCountAsync()
    {
        if (_activeAdventureId is not { } adventureId)
            return 0;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return 0;

        var wv = ResolvePlayWebView(bundle) ?? _playWebView;
        if (wv?.CoreWebView2 is not { } core)
            return 0;

        var turnService = GetOrCreateTurnService(wv);
        return await turnService.GetUserTurnCountAsync(core);
    }

    private void RegisterContextTagsInjection(WebView2 wv)
    {
        if (_activeAdventureId is { } adventureId)
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is not null
                && ShouldUseLegacyNativePassthrough(bundle, wv, wv.CoreWebView2?.Source))
            {
                return;
            }
        }

        if (!ReferenceEquals(wv, _playWebView) && !IsPinnedPlayWebView(wv))
            return;

        if (_contextTagInjections.ContainsKey(wv))
            return;

        var injection = new ChatGptContextTagsInjection(
            wv,
            () => _chrome.HideContextTagsInThread,
            () => _chrome.ExpandHiddenContextInThread);
        _contextTagInjections[wv] = injection;
        if (wv.CoreWebView2 is not null)
            injection.Register(GetOrCreatePageHost(wv));
    }

    private readonly HashSet<ChatGptPlayComposeInjection> _wiredPlayComposeInjections = new();

    private void WirePlayComposeInjection(ChatGptPlayComposeInjection injection)
    {
        if (!_wiredPlayComposeInjections.Add(injection))
            return;

        injection.SendRequested += (sender, args) =>
        {
            var composeInjection = sender as ChatGptPlayComposeInjection;
            _ = SendPlayPromptAsync(args, composeInjection);
        };
        injection.UploadRequested += (sender, args) =>
        {
            var composeInjection = sender as ChatGptPlayComposeInjection;
            _ = HandleComposeUploadRequestAsync(args, composeInjection);
        };
        injection.TextChanged += (_, _) =>
        {
            DebouncedUpdatePlayMergedPreview();
            if (_activeAdventureId is not { } adventureId)
                return;

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || injection.CoreWebView2 is not { } core)
                return;

            if (GetPlayComposeWebView(injection) is not { } composeWebView)
                return;

            if (ShouldUseLegacyNativePassthrough(bundle, composeWebView, core.Source))
                return;

            DebouncedPlaySendWarmup(bundle, core);
        };
    }

    private void RegisterPlayComposeInjection(WebView2 wv)
    {
        if (_appMode != AppMode.Play)
            return;

        if (_activeAdventureId is { } adventureId)
        {
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is not null)
            {
                if (ShouldUseLegacyNativePassthrough(bundle, wv, wv.CoreWebView2?.Source))
                {
                    if (_playComposeInjections.TryGetValue(wv, out var existing))
                        _ = existing.SetNativePassthroughAsync(true);
                    if (_appMode == AppMode.Play)
                        ApplyWrapperComposerForWebView(wv);
                    return;
                }
            }
        }

        var candidateTabKey = PlayTabPinService.GetTabKey(wv, ChatTabs);
        var playWebViewTabKey = _playWebView is not null
            ? PlayTabPinService.GetTabKey(_playWebView, ChatTabs)
            : null;
        var activeTabKey = GetActiveWebView() is { } active
            ? PlayTabPinService.GetTabKey(active, ChatTabs)
            : null;
        AdventureBundle? registrationBundle = _activeAdventureId is { } regId
            ? AdventureStore.Load(regId)
            : null;

        var suppressOnActiveOnly = false;
        if (registrationBundle is not null
            && !ReferenceEquals(wv, _playWebView)
            && !PlayTabPinService.IsSameTabAsPlayPin(registrationBundle, wv, ChatTabs)
            && ReferenceEquals(wv, GetActiveWebView()))
        {
            suppressOnActiveOnly = ShouldUseLegacyNativePassthrough(
                registrationBundle,
                wv,
                wv.CoreWebView2?.Source);
        }

        if (!PlayComposeInjectionPolicy.ShouldRegisterIntercept(
                new PlayComposeRegistrationContext(
                    IsPlayMode: true,
                    Bundle: registrationBundle,
                    CandidateTabKey: candidateTabKey,
                    PlayWebViewTabKey: playWebViewTabKey,
                    ActiveWebViewTabKey: activeTabKey,
                    SuppressPlayAutomation: false,
                    SuppressPlayAutomationOnActiveOnly: suppressOnActiveOnly)))
        {
            return;
        }

        if (suppressOnActiveOnly)
        {
            if (_playComposeInjections.TryGetValue(wv, out var existing))
                _ = existing.SetNativePassthroughAsync(true);
            if (_appMode == AppMode.Play)
                ApplyWrapperComposerForWebView(wv);
            return;
        }

        if (!_playComposeInjections.TryGetValue(wv, out var injection))
        {
            injection = new ChatGptPlayComposeInjection(wv, () => ShouldUseWrapperComposer(wv));
            WirePlayComposeInjection(injection);
            _playComposeInjections[wv] = injection;
        }

        if (wv.CoreWebView2 is not null)
        {
            injection.Register(GetOrCreatePageHost(wv));
            _ = injection.SetNativePassthroughAsync(false);
            ApplyWrapperComposerForWebView(wv);
        }
    }

    internal ChatGptPlayComposeInjection? GetActivePlayComposeInjection()
    {
        AdventureBundle? bundle = _activeAdventureId is { } id ? AdventureStore.Load(id) : null;
        WebView2? wv = null;
        if (bundle is not null)
        {
            var session = PlayTabSessionFactory.FromBundle(bundle);
            wv = PlayTabSessionResolver.ResolvePinnedWebView(ChatTabs, session)
                 ?? PlayTabSessionResolver.ResolvePlayWebView(ChatTabs, bundle, _playWebView);
        }

        wv ??= _playWebView;
        if (wv is null)
            return null;

        return _playComposeInjections.TryGetValue(wv, out var injection) ? injection : null;
    }

    internal void ApplyWrapperComposerToPlayTab(bool enabled)
    {
        foreach (var wv in _playComposeInjections.Keys.ToArray())
        {
            if (!_playComposeInjections.ContainsKey(wv))
                continue;
            if (wv.CoreWebView2 is not { } core)
                continue;

            var tabEnabled = enabled && ShouldUseWrapperComposer(wv);
            _ = ChatGptPlayComposeInjection.ReapplyAsync(core, tabEnabled);
        }
    }

    private bool ShouldUseWrapperComposer(WebView2 wv)
    {
        if (_appMode != AppMode.Play || _activeAdventureId is not { } id)
            return false;

        var bundle = AdventureStore.Load(id);
        if (bundle is null || wv.CoreWebView2 is not { } core)
            return false;

        var caps = PlayTabSessionResolver.ResolveCapabilities(bundle, wv, ChatTabs, core.Source);
        return PlayWrapperComposerPolicy.ShouldUseWrapperComposer(caps);
    }

    internal void ApplyWrapperComposerForWebView(WebView2 wv)
    {
        if (wv.CoreWebView2 is not { } core)
            return;

        var enabled = _appMode == AppMode.Play && ShouldUseWrapperComposer(wv);
        _ = ChatGptPlayComposeInjection.ReapplyAsync(core, enabled);
    }

    internal void ApplyContextTagsToPlayTab()
    {
        if (_playWebView?.CoreWebView2 is not { } core)
            return;

        _ = ChatGptContextTagsInjection.ReapplyAsync(
            core,
            _chrome.HideContextTagsInThread,
            _chrome.ExpandHiddenContextInThread);

        ApplyInlineUtilityToPlayTab();
    }

    internal void ApplyInlineUtilityToPlayTab()
    {
        if (_playWebView?.CoreWebView2 is not { } core || _activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        _ = ChatGptAdventureBridgeInjection.ApplyInlineUtilityPreferencesAsync(
            core,
            UtilityDeliveryModeService.ShouldHideInlineUtility(bundle),
            UtilityDeliveryModeService.ShouldShowInlineUtilityTraffic(bundle));
    }

    internal void ApplyPlaySurfaceActionsToPlayTab()
    {
        if (_playWebView?.CoreWebView2 is not { } core || _activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        _ = ChatGptAdventureBridgeInjection.ApplyPlaySurfaceActionsAsync(
            core,
            bundle.Metadata.Settings.PlaySurfaceActions);
    }

    private bool IsPinnedPlayWebView(WebView2 wv)
    {
        if (_activeAdventureId is not { } id)
            return false;

        var bundle = AdventureStore.Load(id);
        return bundle is not null && PlayTabPinService.IsSameTabAsPlayPin(bundle, wv, ChatTabs);
    }
}
