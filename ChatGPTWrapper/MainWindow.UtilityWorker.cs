using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow : IUtilityWorkerHost
{
    private WebView2? _utilityWorkerWebView;
    private int _utilityWorkerSetupDepth;

    ChatGptConversationSendService IUtilityWorkerHost.ConversationSend =>
        _conversationSendService
        ?? throw new InvalidOperationException("Conversation send service not ready.");

    ChatGptProjectApiService? IUtilityWorkerHost.ProjectApi => _projectApiService;

    AdventureTurnService IUtilityWorkerHost.GetTurnService(object webView) =>
        GetOrCreateTurnService((WebView2)webView);

    void IUtilityWorkerHost.RegisterWorkerTab(object webView) =>
        RegisterWorkerTabCore((WebView2)webView);

    private void RegisterWorkerTabCore(WebView2 webView)
    {
        GetOrRegisterAdventureBridge(webView);
        WireProjectServices(webView);
        _utilityWorkerWebView = webView;
        if (webView.CoreWebView2 is { } core)
            _ = ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);
    }

    async Task<object?> IUtilityWorkerHost.ResolveWorkerWebViewAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken) =>
        await ResolveUtilityWorkerWebViewAsync(bundle);

    async Task<object?> IUtilityWorkerHost.EnsureWorkerTabReadyAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken) =>
        await EnsureUtilityWorkerTabReadyAsync(bundle);

    object? IUtilityWorkerHost.GetPlayWebView() => GetPlayWebView();

    object? IUtilityWorkerHost.GetWorkerCookieSource()
    {
        if (Dispatcher.CheckAccess())
            return ResolveWorkerCookieSource();

        return Dispatcher.Invoke(ResolveWorkerCookieSource);
    }

    async Task<IReadOnlyList<object>> IUtilityWorkerHost.GetWorkerChatGptCookiesAsync(
        CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
            return (await ReadWorkerChatGptCookiesAsync(cancellationToken)).Cast<object>().ToList();

        return (await Dispatcher.InvokeAsync(() => ReadWorkerChatGptCookiesAsync(cancellationToken))
            .Task.Unwrap()
            .WaitAsync(cancellationToken)).Cast<object>().ToList();
    }

    private CoreWebView2? ResolveWorkerCookieSource()
    {
        if (_utilityWorkerWebView?.CoreWebView2 is { } workerCore)
            return workerCore;

        return GetPlayWebView()?.CoreWebView2;
    }

    private async Task<IReadOnlyList<CoreWebView2Cookie>> ReadWorkerChatGptCookiesAsync(
        CancellationToken cancellationToken)
    {
        var core = ResolveWorkerCookieSource();
        if (core is null)
            return Array.Empty<CoreWebView2Cookie>();

        return await WebViewCookieSync.GetChatGptCookiesAsync(core, cancellationToken);
    }

    void IUtilityWorkerHost.SetStatus(string message)
    {
        if (Dispatcher.CheckAccess())
        {
            SetPlayComposeStatus(message);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => SetPlayComposeStatus(message));
    }

    void IUtilityWorkerHost.OnOutboxBatchCompleted(
        Guid adventureId,
        IReadOnlyList<UtilityOutboxJobResult> results)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ReloadPlayAdventure(adventureId);
            _playView?.RefreshAfterGenerationJob();
            _playView?.RefreshUtilityWorkerStatusFromDisk();
            _playView?.UpdateJobButtonStates();
            UpdatePlayLinkStatus();

            if (_utilityWorkerWebView?.CoreWebView2 is { } workerCore)
                _ = ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(workerCore);

            foreach (var item in results)
            {
                if (item.Result is not null)
                    HandleGenerationJobUiResult(item.JobId, item.Result);
            }
        });
    }

    void IUtilityWorkerHost.RefreshPlayJobButtons()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_activeAdventureId is { } adventureId)
            {
                ReloadPlayAdventure(adventureId);
                _playView?.RefreshUtilityWorkerStatusFromDisk();
            }
            else
            {
                _playView?.UpdateJobButtonStates();
            }

            UpdatePlayLinkStatus();
        });
    }

    public WebView2? GetUtilityWorkerWebView() => _utilityWorkerWebView;

    private UtilityWorkerCoordinator WorkerCoordinator(Guid adventureId) =>
        UtilityWorkerCoordinator.For(adventureId);

    public async Task<bool> SetupUtilityWorkerAsync(Guid adventureId, bool replaceExisting = false)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return false;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            MessageBox.Show(
                this,
                UtilityWorkerSetupCopy.LinkProjectFirstMessage,
                UtilityWorkerSetupCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (!replaceExisting)
        {
            var setupStatus = UtilityWorkerSetupService.Evaluate(bundle);
            if (setupStatus.WorkerPinned && setupStatus.ConnectionGreen)
                return true;
        }

        if (Volatile.Read(ref _utilityWorkerSetupDepth) > 0)
        {
            SetPlayComposeStatus("Utility worker: setup already in progress.");
            return false;
        }

        await EnsureChatWebViewEnvironmentReadyAsync();
        SetPlayComposeStatus(UtilityWorkerSetupCopy.SetupInProgressStatus);
        ProjectChatDraftService.BeginUtilityDraft(bundle);
        Interlocked.Increment(ref _utilityWorkerSetupDepth);

        try
        {
            var wv = await ResolveUtilityWorkerSetupWebViewAsync(bundle, gizmoId, replaceExisting);
            if (wv is null)
            {
                SetPlayComposeStatus("Utility worker: browser tab not ready.");
                MessageBox.Show(
                    this,
                    UtilityWorkerSetupCopy.SetupFailedMessage,
                    UtilityWorkerSetupCopy.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            if (wv.CoreWebView2 is not { } core)
            {
                SetPlayComposeStatus("Utility worker: browser tab not ready.");
                MessageBox.Show(
                    this,
                    UtilityWorkerSetupCopy.SetupFailedMessage,
                    UtilityWorkerSetupCopy.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            ((IUtilityWorkerHost)this).RegisterWorkerTab(wv);
            SelectTabForWebView(wv);
            ProjectChatDraftService.NoteDraftTab(bundle, wv, ChatTabs);
            await SuspendPlayAutomationForDraftTabAsync(wv);

            var conversationId = await CreateWorkerConversationAsync(
                adventureId,
                bundle,
                core,
                gizmoId,
                wv,
                replaceExisting);

            bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return false;

            if (string.IsNullOrWhiteSpace(conversationId)
                || !IsAcceptableUtilityUiConversation(bundle, core.Source, conversationId))
            {
                SetPlayComposeStatus("Utility worker: could not open a separate Project chat.");
                MessageBox.Show(
                    this,
                    UtilityWorkerSetupCopy.ManualCreateTimeoutMessage,
                    UtilityWorkerSetupCopy.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var tabKey = ThreadTabBindingService.GetTabKey(wv, ChatTabs);
            var tabTitle = ThreadTabBindingService.GetTabTitle(wv, ChatTabs);
            if (!UtilityWorkerPinService.BindWorkerPinFromWebView(bundle, wv, tabKey, tabTitle))
            {
                SetPlayComposeStatus("Utility worker: could not pin new chat.");
                MessageBox.Show(
                    this,
                    UtilityWorkerSetupCopy.SetupFailedMessage,
                    UtilityWorkerSetupCopy.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            ProjectChatDraftService.Complete(bundle);
            UpdatePlayLinkStatus();
            SetPlayComposeStatus(UtilityWorkerSetupCopy.RegisteringWorkerStatus);
            var verified = await ProbeUtilityWorkerCapabilitiesAsync(adventureId);
            bundle = AdventureStore.Load(adventureId);
            var probeError = bundle?.Metadata.UtilityWorkerCapabilities?.LastProbeError;
            await Dispatcher.InvokeAsync(() =>
            {
                if (verified)
                {
                    SetPlayComposeStatus("Utility worker: ready.");
                    MessageBox.Show(
                        this,
                        UtilityWorkerSetupCopy.SetupSuccessMessage,
                        UtilityWorkerSetupCopy.DialogTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    SetPlayComposeStatus(
                        probeError is { Length: > 0 }
                            ? $"Utility worker: verify failed ({probeError})"
                            : "Utility worker: pinned — verify connection.");
                    MessageBox.Show(
                        this,
                        UtilityWorkerSetupCopy.SetupPartialMessage(probeError),
                        UtilityWorkerSetupCopy.DialogTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });

            return verified;
        }
        finally
        {
            Interlocked.Decrement(ref _utilityWorkerSetupDepth);
            if (ProjectChatDraftService.IsActive(adventureId))
            {
                var loaded = AdventureStore.Load(adventureId);
                if (loaded is not null)
                    ProjectChatDraftService.Complete(loaded);
            }
        }
    }

    private static readonly TimeSpan ManualWorkerChatWaitTimeout = TimeSpan.FromMinutes(3);

    private async Task<string?> CreateWorkerConversationAsync(
        Guid adventureId,
        AdventureBundle bundle,
        CoreWebView2 core,
        string gizmoId,
        WebView2 workerWebView,
        bool replaceExisting)
    {
        var autonomous = await TryCreateWorkerConversationAutonomousAsync(
            adventureId,
            bundle,
            core,
            gizmoId);
        if (!string.IsNullOrWhiteSpace(autonomous))
            return autonomous;

        ProjectLinkDiagnostics.Log(
            $"Utility worker autonomous create failed for project {gizmoId}; waiting for manual New chat");

        return await WaitForManualWorkerConversationAsync(
            adventureId,
            bundle,
            core,
            gizmoId,
            workerWebView,
            replaceExisting);
    }

    private async Task<string?> TryCreateWorkerConversationAutonomousAsync(
        Guid adventureId,
        AdventureBundle bundle,
        CoreWebView2 core,
        string gizmoId)
    {
        if (_projectApiService is null || _conversationSendService is null)
            return null;

        if (UtilityEphemeralWorkerPolicy.IsEnabled(bundle))
        {
            var workerWebView = FindWebViewForCore(core);
            if (workerWebView is null)
                return null;

            var ephemeral = GetOrCreateEphemeralProjectChatService(workerWebView);
            var turnService = GetOrCreateTurnService(workerWebView);

            var provision = await ephemeral.ProvisionComposerAsync(
                new EphemeralProjectChatRequest
                {
                    Core = core,
                    GizmoId = gizmoId,
                    MessageText = " ",
                    WarmSession = true,
                    TurnService = turnService,
                    DeleteAfterCapture = false,
                    TryUiCreate = (c, ct) => TryCreateWorkerConversationViaUiAsync(adventureId, c, ct),
                });

            if (!provision.Success)
                return null;

            var ephemeralConversationId = provision.ConversationId?.Trim();
            if (string.IsNullOrWhiteSpace(ephemeralConversationId) && provision.DomComposerReady)
                ephemeralConversationId = await turnService.GetConversationIdAsync(core);

            if (string.IsNullOrWhiteSpace(ephemeralConversationId))
                return null;

            if (!PlayTabPinService.IsAcceptableUtilityConversationId(bundle, ephemeralConversationId))
                return null;

            var ephemeralTargetUrl = ChatGptUrls.ResolveProjectConversationUrl(ephemeralConversationId, gizmoId, core.Source);
            if (!AdventurePlayContextService.IsOnPlayConversationPage(core.Source, ephemeralConversationId, gizmoId))
            {
                core.Navigate(ephemeralTargetUrl);
                await WaitForChatGptNavigationAsync(core, expectedDestination: ephemeralTargetUrl);
            }

            return ephemeralConversationId;
        }

        var created = await _projectApiService.CreateProjectConversationDetailedAsync(
            core,
            gizmoId,
            new ProjectConversationCreateOptions
            {
                SkipClientBootstrap = true,
                SkipLegacyApiCreate = true,
                TryUiCreate = (c, ct) => TryCreateWorkerConversationViaUiAsync(adventureId, c, ct),
            });

        var conversationId = created.ConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
            return null;

        if (!PlayTabPinService.IsAcceptableUtilityConversationId(bundle, conversationId))
            return null;

        var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(conversationId, gizmoId, core.Source);
        if (!AdventurePlayContextService.IsOnPlayConversationPage(core.Source, conversationId, gizmoId))
        {
            core.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: targetUrl);
        }

        return conversationId;
    }

    private async Task<string?> WaitForManualWorkerConversationAsync(
        Guid adventureId,
        AdventureBundle bundle,
        CoreWebView2 core,
        string gizmoId,
        WebView2 workerWebView,
        bool replaceExisting)
    {
        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var previousWorkerId = replaceExisting
            ? UtilityWorkerSessionService.GetWorkerConversationId(bundle)
            : null;

        await Dispatcher.InvokeAsync(() => SelectTabForWebView(workerWebView));
        var turnService = GetOrCreateTurnService(workerWebView);

        await _projectApiService!.EnsureProjectPageAsync(core, gizmoId);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: projectUrl);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            SetPlayComposeStatus(UtilityWorkerSetupCopy.ManualCreateWaitingStatus);
            MessageBox.Show(
                this,
                UtilityWorkerSetupCopy.ManualCreatePromptMessage,
                UtilityWorkerSetupCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });

        var deadline = DateTimeOffset.UtcNow.Add(ManualWorkerChatWaitTimeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var loaded = AdventureStore.Load(adventureId);
            if (loaded is null)
                return null;

            var conversationId = await TryReadWorkerConversationFromPageAsync(
                turnService,
                core,
                loaded,
                gizmoId,
                previousWorkerId);
            if (!string.IsNullOrWhiteSpace(conversationId))
                return conversationId;

            await Task.Delay(500);
        }

        await Dispatcher.InvokeAsync(() =>
            SetPlayComposeStatus("Utility worker: timed out waiting for New chat."));

        return null;
    }

    private async Task<string?> TryReadWorkerConversationFromPageAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        AdventureBundle bundle,
        string gizmoId,
        string? rejectConversationId)
    {
        string? conversationId = null;
        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.TryParseConversationId(uri, out var fromUrl))
        {
            conversationId = fromUrl;
        }

        conversationId ??= await turnService.GetConversationIdAsync(core);

        if (string.IsNullOrWhiteSpace(conversationId)
            || !PlayTabPinService.IsAcceptableUtilityConversationId(bundle, conversationId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(rejectConversationId)
            && string.Equals(conversationId, rejectConversationId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsAcceptableUtilityUiConversation(bundle, core.Source, conversationId))
            return null;

        var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(conversationId, gizmoId, core.Source);
        if (!AdventurePlayContextService.IsOnPlayConversationPage(core.Source, conversationId, gizmoId))
        {
            core.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: targetUrl);
        }

        return IsAcceptableUtilityUiConversation(bundle, core.Source, conversationId)
            ? conversationId
            : null;
    }

    private async Task<string?> TryCreateWorkerConversationViaUiAsync(
        Guid adventureId,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || _projectApiService is null)
            return null;

        var wv = FindWebViewForCore(core) ?? _utilityWorkerWebView;
        if (wv is null)
            return null;

        await Dispatcher.InvokeAsync(() => SelectTabForWebView(wv));
        var turnService = GetOrCreateTurnService(wv);

        var conversationId = await UtilityEphemeralUiCreateService.TryOpenComposerAsync(
            bundle,
            core,
            _projectApiService,
            turnService,
            cancellationToken,
            async (url, ct) => await WaitForChatGptNavigationAsync(core, expectedDestination: url));

        if (string.IsNullOrWhiteSpace(conversationId))
            return null;

        return IsAcceptableUtilityUiConversation(bundle, core.Source, conversationId)
            ? conversationId
            : null;
    }

    async Task<string?> IUtilityWorkerHost.TryCreateEphemeralConversationViaUiAsync(
        AdventureBundle bundle,
        object core,
        CancellationToken cancellationToken)
    {
        if (_projectApiService is null)
            return null;

        var coreWebView = UtilityWebViewBridge.AsCoreWebView2(core);
        if (coreWebView is null)
            return null;

        var wv = FindWebViewForCore(coreWebView) ?? _utilityWorkerWebView;
        if (wv is null)
            return null;

        var turnService = GetOrCreateTurnService(wv);
        return await ((IUtilityWorkerHost)this).WithUtilityWebViewActivatedAsync(
            core,
            () => UtilityEphemeralUiCreateService.TryOpenComposerAsync(
                bundle,
                coreWebView,
                _projectApiService,
                turnService,
                cancellationToken,
                async (url, ct) => await WaitForChatGptNavigationAsync(coreWebView, expectedDestination: url)),
            cancellationToken);
    }

    private WebView2? FindWebViewForCore(CoreWebView2 core)
    {
        foreach (var item in ChatTabs.Items)
        {
            if (item is TabItem { Content: WebView2 wv }
                && ReferenceEquals(wv.CoreWebView2, core))
            {
                return wv;
            }
        }

        if (UtilityWorkerBackgroundHost.Children.Count > 0
            && UtilityWorkerBackgroundHost.Children[0] is WebView2 background
            && ReferenceEquals(background.CoreWebView2, core))
        {
            return background;
        }

        return null;
    }

    public async Task<bool> PinCurrentTabAsUtilityWorkerAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || GetActiveWebView() is not { } active)
            return false;

        if (!UtilityWorkerSetupService.Evaluate(bundle).ProjectLinked)
        {
            MessageBox.Show(
                this,
                UtilityWorkerSetupCopy.LinkProjectFirstMessage,
                UtilityWorkerSetupCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var tabKey = ThreadTabBindingService.GetTabKey(active, ChatTabs);
        var tabTitle = ThreadTabBindingService.GetTabTitle(active, ChatTabs);
        if (!UtilityWorkerPinService.BindWorkerPinFromWebView(bundle, active, tabKey, tabTitle))
        {
            MessageBox.Show(
                this,
                UtilityWorkerSetupCopy.PinCurrentTabFailedMessage,
                UtilityWorkerSetupCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        ((IUtilityWorkerHost)this).RegisterWorkerTab(active);
        SelectTabForWebView(active);
        UpdatePlayLinkStatus();
        SetPlayComposeStatus(UtilityWorkerSetupCopy.RegisteringWorkerStatus);

        var verified = await ProbeUtilityWorkerCapabilitiesAsync(adventureId);
        if (!verified)
        {
            var probeError = AdventureStore.Load(adventureId)?.Metadata.UtilityWorkerCapabilities?.LastProbeError;
            MessageBox.Show(
                this,
                UtilityWorkerSetupCopy.SetupPartialMessage(probeError),
                UtilityWorkerSetupCopy.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return verified;
    }

    public Task<bool> PinActiveTabForUtilityWorkerAsync(Guid adventureId) =>
        PinCurrentTabAsUtilityWorkerAsync(adventureId);

    private async Task<WebView2?> ResolveUtilityWorkerSetupWebViewAsync(
        AdventureBundle bundle,
        string gizmoId,
        bool replaceExisting)
    {
        if (_utilityWorkerWebView?.CoreWebView2 is not null)
            return _utilityWorkerWebView;

        var fromHeader = await Dispatcher.InvokeAsync(() => FindUtilityWorkerTabByHeader());
        if (fromHeader is not null)
            return fromHeader;

        if (!replaceExisting
            && UtilityWorkerPinService.TryFindWebViewForWorkerSession(ChatTabs, bundle) is { } pinned)
        {
            return pinned;
        }

        return await Dispatcher.InvokeAsync(async () =>
            await AddChatTabAsync("Utility worker", new Uri(ChatGptUrls.BuildProjectUrl(gizmoId)))).Task.Unwrap();
    }

    private WebView2? FindUtilityWorkerTabByHeader()
    {
        foreach (var item in ChatTabs.Items)
        {
            if (item is not TabItem { Header: var header } tab)
                continue;

            if (header is not string title || !title.Contains("Utility worker", StringComparison.OrdinalIgnoreCase))
                continue;

            if (tab.Content is WebView2 wv)
                return wv;

            if (_parkedUtilityWorkerTab == tab && _utilityWorkerWebView is not null)
                return _utilityWorkerWebView;
        }

        return null;
    }

    public Task<bool> ProbeUtilityWorkerCapabilitiesAsync(Guid adventureId) =>
        WorkerCoordinator(adventureId).ProbeAsync(this);

    private async Task<WebView2?> EnsureUtilityWorkerTabReadyAsync(AdventureBundle bundle)
    {
        UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle);

        var existing = await ResolveUtilityWorkerWebViewAsync(bundle);
        if (existing?.CoreWebView2 is not null)
        {
            await Dispatcher.InvokeAsync(SyncUtilityWorkerWebViewParking);
            return existing;
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var ephemeral = UtilityEphemeralWorkerPolicy.IsEnabled(bundle);
        var conversationId = UtilityWorkerPinService.GetWorkerConversationId(bundle);
        if (!ephemeral && string.IsNullOrWhiteSpace(conversationId))
            return null;

        var url = ephemeral || string.IsNullOrWhiteSpace(conversationId)
            ? new Uri(ChatGptUrls.BuildProjectUrl(gizmoId))
            : new Uri(ChatGptUrls.BuildProjectConversationUrl(conversationId, gizmoId));

        ProjectLinkDiagnostics.Log(
            ephemeral
                ? $"Utility worker tab missing; opening project {gizmoId} for ephemeral jobs"
                : $"Utility worker tab missing; opening {url}");
        SetPlayComposeStatus(
            ephemeral
                ? "Utility worker: opening project tab for ephemeral jobs…"
                : "Utility worker: opening background tab…");

        WebView2? wv;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            wv = await CreateUtilityWorkerWebViewInBackgroundHostAsync(url)
                .WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            SetPlayComposeStatus("Utility worker: timed out opening background tab.");
            return null;
        }

        if (wv is null)
            return null;

        if (wv.CoreWebView2 is not { } core)
            return null;

        ((IUtilityWorkerHost)this).RegisterWorkerTab(wv);

        if (ephemeral || string.IsNullOrWhiteSpace(conversationId))
        {
            if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
            {
                SetPlayComposeStatus("Utility worker: loading linked project…");
                core.Navigate(url.ToString());
                await WaitForChatGptNavigationAsync(core, expectedDestination: url.ToString());
            }
        }
        else if (!UtilityConversationPageService.MatchesTargetConversation(core.Source, conversationId, gizmoId))
        {
            SetPlayComposeStatus("Utility worker: loading conversation…");
            core.Navigate(url.ToString());
            await WaitForChatGptNavigationAsync(core, expectedDestination: url.ToString());
        }

        await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);

        return wv;
    }

    private async Task<GenerationJobResult?> EnqueueWorkerUtilityJobAsync(
        Guid adventureId,
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        IReadOnlyList<DomAttachmentPayload>? domAttachments = null)
    {
        if (UtilityEphemeralWorkerPolicy.ShouldUseEphemeralLane(bundle, jobId))
        {
            if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
            {
                await Dispatcher.InvokeAsync(() =>
                    SetPlayComposeStatus($"{jobId}: link a ChatGPT Project first."));
                return new GenerationJobResult
                {
                    Success = false,
                    Error = "no_linked_project",
                };
            }
        }
        else
        {
            if (UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle))
                AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

            if (UtilityEphemeralWorkerPolicy.RequiresWorkerPin(bundle, jobId))
            {
                await Dispatcher.InvokeAsync(() =>
                    SetPlayComposeStatus($"{jobId}: set up utility worker in Threads first."));
                return new GenerationJobResult
                {
                    Success = false,
                    Error = UtilityWorkerPinService.WorkerPinRequiredError,
                };
            }
        }

        UtilityOutboxService.Enqueue(
            bundle,
            jobId,
            UtilityExecutionChannel.WorkerBackground,
            context,
            domAttachments);
        AdventureStore.Save(bundle);

        var attachNote = domAttachments is { Count: > 0 }
            ? $" with {domAttachments.Count} attachment(s)"
            : "";
        await Dispatcher.InvokeAsync(() =>
            SetPlayComposeStatus(
                $"{jobId}: queued on utility worker{attachNote} ({UtilityOutboxService.PendingCount(adventureId)} pending)."));

        WorkerCoordinator(adventureId).RequestOutboxPump(this);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = 0,
            DisplayText = "queued_on_worker",
        };
    }

    private async Task<GenerationJobResult?> EnqueueWorkerUtilityJobsBatchAsync(
        Guid adventureId,
        AdventureBundle bundle,
        IReadOnlyList<(string JobId, GenerationJobContext Context)> jobs)
    {
        if (jobs.Count == 0)
        {
            return new GenerationJobResult
            {
                Success = false,
                Error = "no_jobs_selected",
            };
        }

        foreach (var (jobId, context) in jobs)
        {
            if (UtilityEphemeralWorkerPolicy.ShouldUseEphemeralLane(bundle, jobId))
            {
                if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
                {
                    await Dispatcher.InvokeAsync(() =>
                        SetPlayComposeStatus($"{jobId}: link a ChatGPT Project first."));
                    return new GenerationJobResult
                    {
                        Success = false,
                        Error = "no_linked_project",
                    };
                }
            }
            else if (UtilityEphemeralWorkerPolicy.RequiresWorkerPin(bundle, jobId))
            {
                if (UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle))
                    AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

                await Dispatcher.InvokeAsync(() =>
                    SetPlayComposeStatus($"{jobId}: set up utility worker in Threads first."));
                return new GenerationJobResult
                {
                    Success = false,
                    Error = UtilityWorkerPinService.WorkerPinRequiredError,
                };
            }

            UtilityOutboxService.Enqueue(
                bundle,
                jobId,
                UtilityExecutionChannel.WorkerBackground,
                context);
        }

        AdventureStore.Save(bundle);

        WorkerCoordinator(adventureId).RequestOutboxPump(this);
        await Dispatcher.InvokeAsync(() =>
            SetPlayComposeStatus(
                $"Queued {jobs.Count} utility worker job(s) ({UtilityOutboxService.PendingCount(adventureId)} pending)."));

        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = 0,
            DisplayText = "queued_on_worker",
        };
    }

    private Task ProcessWorkerOutboxAsync(Guid adventureId)
    {
        WorkerCoordinator(adventureId).RequestOutboxPump(this);
        return Task.CompletedTask;
    }

    private async Task<WebView2?> ResolveUtilityWorkerWebViewAsync(AdventureBundle bundle)
    {
        if (_utilityWorkerWebView?.CoreWebView2 is not null)
            return _utilityWorkerWebView;

        var resolved = await Dispatcher.InvokeAsync(() =>
            _utilityWorkerWebView
            ?? UtilityWorkerPinService.TryFindWebViewForWorkerSession(ChatTabs, bundle)
            ?? FindUtilityWorkerTabByHeader()
            ?? ThreadWebViewResolver.TryFindExisting(ChatTabs, bundle, AdventureThreadKind.UtilityWorker));

        if (resolved is not null)
            _utilityWorkerWebView = resolved;
        return resolved;
    }
}
