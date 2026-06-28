using System.Windows;
using System.Windows.Controls;
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

    AdventureTurnService IUtilityWorkerHost.GetTurnService(WebView2 webView) =>
        GetOrCreateTurnService(webView);

    void IUtilityWorkerHost.RegisterWorkerTab(WebView2 webView)
    {
        GetOrRegisterAdventureBridge(webView);
        WireProjectServices(webView);
        _utilityWorkerWebView = webView;
        if (webView.CoreWebView2 is { } core)
            _ = ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);
    }

    async Task<WebView2?> IUtilityWorkerHost.ResolveWorkerWebViewAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken) =>
        await ResolveUtilityWorkerWebViewAsync(bundle);

    async Task<WebView2?> IUtilityWorkerHost.EnsureWorkerTabReadyAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken) =>
        await EnsureUtilityWorkerTabReadyAsync(bundle);

    WebView2? IUtilityWorkerHost.GetPlayWebView() => GetPlayWebView();

    void IUtilityWorkerHost.SetStatus(string message) =>
        SetPlayComposeStatus(message);

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
        if (_projectApiService is null)
            return null;

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
        if (bundle is null)
            return null;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        var wv = FindWebViewForCore(core) ?? _utilityWorkerWebView;
        if (wv is null)
            return null;

        await Dispatcher.InvokeAsync(() => SelectTabForWebView(wv));
        var turnService = GetOrCreateTurnService(wv);

        await _projectApiService!.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core, expectedDestination: projectUrl);
        }

        if (!await turnService.EnsureUtilityBridgeReadyAsync(core, cancellationToken))
            return null;

        for (var warmup = 0; warmup < 12; warmup++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
            if (health.ComposerFound)
                break;

            await Task.Delay(500, cancellationToken);
        }

        const int maxUiAttempts = 2;
        for (var attempt = 0; attempt < maxUiAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
            var conversationId = ui.ConversationId ?? await turnService.GetConversationIdAsync(core);

            if (!string.IsNullOrWhiteSpace(conversationId)
                && PlayTabPinService.IsAcceptableUtilityConversationId(bundle, conversationId))
            {
                var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(conversationId, gizmoId, core.Source);
                if (!AdventurePlayContextService.IsOnPlayConversationPage(core.Source, conversationId, gizmoId))
                {
                    core.Navigate(targetUrl);
                    await WaitForChatGptNavigationAsync(core, expectedDestination: targetUrl);
                }

                if (IsAcceptableUtilityUiConversation(bundle, core.Source, conversationId))
                {
                    ProjectLinkDiagnostics.Log($"Utility worker UI create succeeded: {conversationId}");
                    return conversationId;
                }
            }

            if (string.Equals(ui.Error, "project_new_chat_not_found", StringComparison.OrdinalIgnoreCase))
            {
                ProjectLinkDiagnostics.Log("Utility worker UI New chat button not found; deferring to manual");
                break;
            }

            if (attempt + 1 >= maxUiAttempts
                || string.Equals(ui.Error, "project_chat_not_ready", StringComparison.OrdinalIgnoreCase))
            {
                ProjectLinkDiagnostics.Log(
                    $"Utility worker UI create gave up ({ui.Error ?? "no_conversation_id"}); deferring to manual");
                break;
            }

            await Task.Delay(1000, cancellationToken);
        }

        return null;
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
        var existing = await ResolveUtilityWorkerWebViewAsync(bundle);
        if (existing?.CoreWebView2 is not null)
        {
            await Dispatcher.InvokeAsync(SyncUtilityWorkerWebViewParking);
            return existing;
        }

        var conversationId = UtilityWorkerSessionService.GetWorkerConversationId(bundle);
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(gizmoId))
            return null;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var url = ChatGptUrls.BuildProjectConversationUrl(conversationId, gizmoId);

        ProjectLinkDiagnostics.Log($"Utility worker tab missing; opening {url}");
        SetPlayComposeStatus("Utility worker: opening background tab…");

        WebView2? wv;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            wv = await CreateUtilityWorkerWebViewInBackgroundHostAsync(new Uri(url))
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

        if (!UtilityConversationPageService.MatchesTargetConversation(core.Source, conversationId, gizmoId))
        {
            SetPlayComposeStatus("Utility worker: loading conversation…");
            core.Navigate(url);
            await WaitForChatGptNavigationAsync(core, expectedDestination: url);
        }

        await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);

        return wv;
    }

    private async Task<GenerationJobResult?> EnqueueWorkerUtilityJobAsync(
        Guid adventureId,
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context)
    {
        if (UtilityWorkerPinService.TryReconcilePinFromCapabilities(bundle))
            AdventureStore.Save(bundle, AdventureSaveScope.Metadata);

        if (!UtilityWorkerPinService.HasWorkerPin(bundle))
        {
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
        AdventureStore.Save(bundle);

        await Dispatcher.InvokeAsync(() =>
            SetPlayComposeStatus(
                $"{jobId}: queued on utility worker ({UtilityOutboxService.PendingCount(adventureId)} pending)."));

        WorkerCoordinator(adventureId).RequestOutboxPump(this);
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
