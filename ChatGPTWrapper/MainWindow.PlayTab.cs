using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
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

    public async Task StartNewPlayThreadAsync(Guid adventureId)
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
                "Link a ChatGPT Project before starting a new play thread.",
                "Start new play thread",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (MessageBox.Show(
                this,
                "This will release the current play thread binding (conversation id and pinned tab) "
                + "while keeping your linked Project.\n\n"
                + "The play start packet will be copied to your clipboard and your Play tab "
                + "will navigate to your Project.\n\n"
                + "Click New chat in ChatGPT, paste (Ctrl+V), and press Send. Your adventure log is kept.",
                "Start new play thread",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await EnsureChatWebViewEnvironmentReadyAsync();

        var playTabForReuse = ResolvePlayWebView(bundle) ?? _playWebView;

        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);
        PlayContextSessionCache.Invalidate(adventureId);

        var startPacket = AdventureBootstrapService.BuildStartPacket(bundle);
        if (!ClipboardCopy.TrySetText(startPacket, "StartNewPlayThread"))
        {
            MessageBox.Show(
                this,
                "Could not copy the start packet to the clipboard.\n\n"
                + "Use Play settings → Preview start packet to copy it manually, then link a Play tab.",
                "Start new play thread",
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
                + "The start packet is still on your clipboard.",
                "Start new play thread",
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
                + "The start packet is still on your clipboard.",
                "Start new play thread",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);

        SelectTabForWebView(wv);
        _playWebView = wv;

        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core);
        }

        var reloaded = AdventureStore.Load(adventureId);
        if (reloaded is null)
            return;

        if (PlayTabPinService.HasUtilityPin(reloaded)
            && string.Equals(
                PlayTabPinService.GetTabKey(wv, ChatTabs),
                reloaded.Metadata.PinnedUtilityTabKey,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "This tab is pinned for utility jobs. Select a different browser tab for play.\n\n"
                + "The start packet is still on your clipboard.",
                "Start new play thread",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        PlayTabPinService.PinTab(reloaded, wv, ChatTabs);
        PlayTabPinService.TryBindProjectSessionFromWebView(reloaded, wv);

        ReloadPlayAdventure(adventureId);
        UpdatePlayLinkStatus();

        MessageBox.Show(
            this,
            PlayThreadRotationService.FormatStartThreadReadyMessage(core.Source, reloaded),
            "Start new play thread",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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

    public bool PinActiveTabForUtility(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || GetActiveWebView() is not { } active)
            return false;

        if (active.CoreWebView2 is not { } core)
            return false;

        if (PlayTabPinService.IsSameTabAsPlayPin(bundle, active, ChatTabs))
        {
            MessageBox.Show(
                this,
                "The play tab and utility tab must be different browser tabs.\n\n"
                + "1. Click + to open a new ChatGPT tab.\n"
                + "2. In that tab, open your Project and click New chat.\n"
                + "3. Select the new tab and pin it here as the utility tab.",
                "Pin utility tab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!PlayTabPinService.TryResolveUtilityConversationId(bundle, core, out _, out var error))
        {
            var message = error switch
            {
                "utility_same_as_play_thread" =>
                    "This conversation is the play thread. In a separate browser tab, create a new Project chat and pin that tab.",
                "utility_tab_not_on_conversation" =>
                    "Open a Project conversation (/c/…) in this tab, then pin it as the utility tab.",
                _ => "Could not pin this tab for utility jobs. Open a Project conversation page first.",
            };
            MessageBox.Show(this, message, "Pin utility tab", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        PlayTabPinService.PinUtilityTab(bundle, active, ChatTabs);
        GetOrRegisterAdventureBridge(active);
        SelectTabForWebView(active);
        UpdatePlayLinkStatus();
        return true;
    }

    public void ClearUtilityTabPin(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        PlayTabPinService.ClearUtilityPin(bundle);
        UpdatePlayLinkStatus();
    }

    public bool SelectPinnedPlayTab(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey))
            return false;

        if (PlayTabPinService.FindWebViewByPinKey(ChatTabs, bundle.Metadata.PinnedPlayTabKey) is not { } pinned)
            return false;

        SelectTabForWebView(pinned);
        if (_activeAdventureId == adventureId)
            _playWebView = pinned;

        return true;
    }

    public bool SelectPinnedUtilityTab(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.PinnedUtilityTabKey))
            return false;

        if (PlayTabPinService.FindWebViewByUtilityPinKey(ChatTabs, bundle.Metadata.PinnedUtilityTabKey) is not { } pinned)
            return false;

        SelectTabForWebView(pinned);
        GetOrRegisterAdventureBridge(pinned);
        return true;
    }

    private WebView2? ResolvePlayWebView(AdventureBundle? bundle)
    {
        if (bundle is null)
            return null;

        if (PlayTabPinService.TryFindWebViewForPlaySession(ChatTabs, bundle) is { } sessionTab)
            return sessionTab;

        if (_appMode == AppMode.Play && GetActiveWebView() is { } active)
        {
            var source = active.CoreWebView2?.Source;
            if (PlayTabPinService.IsOnPlayTarget(source, bundle))
                return active;

            if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
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

        var wv = ResolvePlayWebView(bundle);
        if (wv is not null)
        {
            _playWebView = wv;
            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            if (navigateToBrowseTarget)
            {
                var browseUrl = AdventureNavigationService.ResolvePlayBrowseUrl(bundle);
                if (browseUrl is not null
                    && wv.CoreWebView2 is { } core
                    && AdventureNavigationService.ShouldNavigateToPlayTarget(core.Source, bundle, browseUrl))
                {
                    core.Navigate(browseUrl);
                    await WaitForChatGptNavigationAsync(core);
                }
            }

            if (selectTab)
                SelectTabForWebView(wv);
            return wv;
        }

        if (PlayTabPinService.HasPersistedPlaySession(bundle)
            || AdventureNavigationService.HasLinkedProject(bundle))
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

        await EnsureChatWebViewEnvironmentReadyAsync();

        WebView2? active = GetActiveWebView();
        if (active is null)
        {
            if (ChatTabs.Items.Count == 0 && _chatWebViewEnvironment is not null)
                active = await AddChatTabAsync("ChatGPT");

            if (active is null)
                return null;
        }

        var answer = MessageBox.Show(
            this,
            "Pin the current ChatGPT tab for this adventure? Send automation will use that tab.",
            "Pin play tab",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return null;

        PlayTabPinService.PinTab(bundle, active, ChatTabs);
        _playWebView = active;
        if (selectTab)
            SelectTabForWebView(active);

        UpdatePlayLinkStatus();
        return active;
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
                      ?? AdventureNavigationService.ResolvePlayBrowseUrl(bundle);
        if (targetUrl is null)
            return null;

        WebView2? wv = GetActiveWebView();
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

        if (wv is null)
        {
            if (_chatWebViewEnvironment is null)
                return null;

            wv = await AddChatTabAsync("ChatGPT");
        }

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        if (wv.CoreWebView2 is { } core
            && AdventureNavigationService.ShouldNavigateToPlayTarget(core.Source, bundle, targetUrl))
        {
            core.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(core);
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

        var linkedProject = AdventureNavigationService.HasLinkedProject(bundle);
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
            if (deferPlayContext && !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            {
                bundle.Metadata.LinkedConversationId = null;
                if (bundle.Metadata.ProjectLink is not null)
                    bundle.Metadata.ProjectLink.PlayConversationId = null;
                bundle.Metadata.PinnedPlayTabUrl =
                    AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle);
                AdventureStore.Save(bundle);
            }

            var browseUrl = deferPlayContext
                ? AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle)
                : AdventureNavigationService.ResolvePlayBrowseUrl(bundle);
            if (navigateToBrowseTarget
                && browseUrl is not null
                && AdventureNavigationService.ShouldNavigateToPlayTarget(core.Source, bundle, browseUrl))
            {
                core.Navigate(browseUrl);
                await WaitForChatGptNavigationAsync(core);
            }

            if (deferPlayContext || !prepareContext)
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

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
        {
            var conversationUrl = ChatGptUrls.BuildConversationUrl(bundle.Metadata.LinkedConversationId);
            if (!AdventurePlayContextService.IsOnConversationPage(core.Source, bundle.Metadata.LinkedConversationId))
            {
                core.Navigate(conversationUrl);
                await WaitForChatGptNavigationAsync(core);
            }

            return;
        }

        var fallbackUrl = AdventureNavigationService.ResolveTrustedFallbackUrl(bundle);
        if (AdventureNavigationService.IsGenericHomepage(core.Source)
            || !Uri.TryCreate(core.Source, UriKind.Absolute, out var currentUri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(currentUri))
        {
            core.Navigate(fallbackUrl);
            await WaitForChatGptNavigationAsync(core);
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
        _turnService = service;
        return service;
    }

    private void RegisterContextTagsInjection(WebView2 wv)
    {
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
            if (bundle is null || injection.WebView.CoreWebView2 is not { } core)
                return;

            DebouncedPlaySendWarmup(bundle, core);
        };
    }

    private void RegisterPlayComposeInjection(WebView2 wv)
    {
        if (_appMode != AppMode.Play)
            return;

        if (!ReferenceEquals(wv, _playWebView)
            && !IsPinnedPlayWebView(wv)
            && !ReferenceEquals(wv, GetActiveWebView()))
        {
            return;
        }

        if (!_playComposeInjections.TryGetValue(wv, out var injection))
        {
            injection = new ChatGptPlayComposeInjection(wv, () => ShouldUseWrapperComposer(wv));
            WirePlayComposeInjection(injection);
            _playComposeInjections[wv] = injection;
        }

        if (wv.CoreWebView2 is not null)
            injection.Register(GetOrCreatePageHost(wv));
    }

    internal ChatGptPlayComposeInjection? GetActivePlayComposeInjection()
    {
        if (_playWebView is null)
            return null;

        return _playComposeInjections.TryGetValue(_playWebView, out var injection) ? injection : null;
    }

    internal void ApplyWrapperComposerToPlayTab(bool enabled)
    {
        foreach (var (wv, _) in _playComposeInjections)
        {
            if (wv.CoreWebView2 is not { } core)
                continue;

            var tabEnabled = enabled && ShouldUseWrapperComposer(wv);
            _ = ChatGptPlayComposeInjection.ReapplyAsync(core, tabEnabled);
        }
    }

    private bool ShouldUseWrapperComposer(WebView2 wv)
    {
        if (_appMode != AppMode.Play)
            return false;

        if (!ReferenceEquals(wv, _playWebView) && !IsPinnedPlayWebView(wv))
            return false;

        if (_activeAdventureId is not { } id)
            return false;

        var bundle = AdventureStore.Load(id);
        return bundle?.Metadata.Settings.UseWrapperComposer == true;
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
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey))
            return false;

        return string.Equals(
            PlayTabPinService.GetTabKey(wv, ChatTabs),
            bundle.Metadata.PinnedPlayTabKey,
            StringComparison.OrdinalIgnoreCase);
    }
}
