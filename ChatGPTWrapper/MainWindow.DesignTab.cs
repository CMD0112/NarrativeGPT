using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Views;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private AdventureDesignView? _designView;
    private WebView2? _designWebView;

    public WebView2? GetDesignWebView() => _designWebView;

    public bool PinActiveTabForDesign(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || GetActiveWebView() is not { } active)
            return false;

        try
        {
            DesignTabPinService.PinDesignTab(bundle, active, ChatTabs);
        }
        catch (Exception ex)
        {
            var message = ex.Message.Contains("play thread", StringComparison.OrdinalIgnoreCase)
                ? "This conversation is the play thread. In the Project, create a New chat, open it in this tab, then pin it as the design tab."
                : ex.Message;
            MessageBox.Show(this, message, "Pin design tab", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _designWebView = active;
        GetOrRegisterAdventureBridge(active);
        SelectTabForWebView(active);
        UpdateDesignLinkStatus();
        MessageBox.Show(
            this,
            "Design thread pinned.\n\nSend step brief and Extract will use this chat.",
            "Pin design tab",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return true;
    }

    public void UpdateDesignLinkStatus()
    {
        if (_designView is null || _activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        _designView.SetThreadStatus(DesignTabPinService.FormatDesignThreadStatus(bundle));
    }

    private WebView2? ResolveDesignWebView(AdventureBundle? bundle)
    {
        if (bundle is null)
            return null;

        if (DesignTabPinService.TryFindWebViewOnEligibleDesignConversation(ChatTabs, bundle) is { } sessionTab)
            return sessionTab;

        if (_appMode == AppMode.Design && GetActiveWebView() is { } active)
        {
            var source = active.CoreWebView2?.Source;
            if (DesignTabPinService.IsOnDesignTarget(source, bundle)
                || (string.IsNullOrWhiteSpace(DesignTabPinService.GetDesignConversationId(bundle))
                    && AdventureNavigationService.IsOnLinkedProjectPage(source, bundle)))
            {
                return active;
            }
        }

        return null;
    }

    private async Task<WebView2?> ResolveDesignWebViewAsync(
        Guid adventureId,
        bool selectTab = true,
        bool ensureThread = false)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        var wv = ResolveDesignWebView(bundle);
        if (wv is not null)
        {
            _designWebView = wv;
            if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
                await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

            AdventureNavigationService.SyncLinkedFields(bundle);
            var browseUrl = AdventureNavigationService.ResolveDesignBrowseUrl(bundle, preferThread: ensureThread);
            if (browseUrl is not null
                && wv.CoreWebView2 is { } core
                && AdventureNavigationService.ShouldNavigateToDesignTarget(core.Source, bundle, browseUrl))
            {
                core.Navigate(browseUrl);
                await WaitForChatGptNavigationAsync(core);
            }

            await ApplyDesignWebViewNavigationAsync(bundle, wv, ensureThread);
            if (selectTab)
                SelectTabForWebView(wv);
            return wv;
        }

        wv = await RestoreDesignWebViewAsync(bundle, selectTab, ensureThread);
        if (wv is not null)
            _designWebView = wv;

        return wv;
    }

    private async Task ApplyDesignWebViewNavigationAsync(
        AdventureBundle bundle,
        WebView2 wv,
        bool ensureThread)
    {
        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);

        var targetUrl = AdventureNavigationService.ResolveDesignBrowseUrl(
            bundle,
            preferThread: ensureThread);

        if (ensureThread && wv.CoreWebView2 is { } core)
        {
            var jobService = GetOrCreateGenerationJobService(wv);
            var turnService = GetOrCreateTurnService(wv);
            var ctx = await AdventureDesignContextService.EnsureDesignThreadAsync(
                core,
                bundle,
                jobService,
                turnService);
            bundle = AdventureStore.Load(bundle.Metadata.Id) ?? bundle;
            targetUrl = ctx.IsReady
                ? DesignTabPinService.GetDesignTargetUrl(bundle)
                : AdventureNavigationService.ResolveDesignBrowseUrl(bundle, preferThread: true);
        }
        else if (wv.CoreWebView2 is { } browseCore)
        {
            await AdventureDesignContextService.PrepareDesignBrowserAsync(browseCore, bundle);
            bundle = AdventureStore.Load(bundle.Metadata.Id) ?? bundle;
            targetUrl = AdventureNavigationService.ResolveDesignBrowseUrl(bundle, preferThread: false);
        }

        if (wv.CoreWebView2 is { } navCore
            && !string.IsNullOrWhiteSpace(targetUrl)
            && AdventureNavigationService.ShouldNavigateToDesignTarget(navCore.Source, bundle, targetUrl))
        {
            navCore.Navigate(targetUrl);
            await WaitForChatGptNavigationAsync(navCore);
        }
    }

    private async Task<WebView2?> RestoreDesignWebViewAsync(
        AdventureBundle bundle,
        bool selectTab,
        bool ensureThread)
    {
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

            wv = await AddChatTabAsync("Design");
        }

        if (wv is null)
            return null;

        if (wv.CoreWebView2 is null && _chatWebViewEnvironment is not null)
            await wv.EnsureCoreWebView2Async(_chatWebViewEnvironment);

        await ApplyDesignWebViewNavigationAsync(bundle, wv, ensureThread);

        if (selectTab)
            SelectTabForWebView(wv);

        return wv;
    }

    private async Task PrepareDesignBrowserAsync(Guid adventureId, bool selectTab = true)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        if (!AdventureNavigationService.HasLinkedProject(bundle))
            return;

        AdventureNavigationService.SyncLinkedFields(bundle);
        AdventureStore.Save(bundle);

        var wv = await ResolveDesignWebViewAsync(
            adventureId,
            selectTab,
            ensureThread: AdventureDesignContextService.ShouldEnsureDesignThreadOnOpen);
        if (wv is null)
            return;

        _designWebView = wv;
        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);
    }

    private async Task<bool> TryEnsureDesignThreadForJobsAsync(Guid adventureId, bool selectTab = true)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return false;

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && !AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            if (_designView is not null && _activeAdventureId == adventureId)
                _designView.SetStatus("Link a ChatGPT Project before opening a design thread.");
            return false;
        }

        try
        {
            AdventureNavigationService.SyncLinkedFields(bundle);
            AdventureStore.Save(bundle);

            var wv = await ResolveDesignWebViewAsync(adventureId, selectTab, ensureThread: true);
            if (wv is null)
                return false;

            _designWebView = wv;
            GetOrRegisterAdventureBridge(wv);
            WireProjectServices(wv);

            if (_designView is not null && _activeAdventureId == adventureId)
            {
                var reloaded = AdventureStore.Load(adventureId);
                if (reloaded is not null
                    && DesignTabPinService.GetDesignConversationId(reloaded) is not null)
                {
                    _designView.SetStatus("Design thread ready.");
                    return true;
                }

                _designView.SetStatus(DesignTabPinService.DesignPinRequiredError);
            }

            return false;
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"EnsureDesignThread failed: {ex}");
            if (_designView is not null && _activeAdventureId == adventureId)
            {
                _designView.SetStatus(
                    AdventureNavigationService.FormatDesignSessionError(
                        new DesignContextResult
                        {
                            Status = DesignContextStatus.NavigationFailed,
                            Error = ex.Message,
                        }));
            }

            return false;
        }
    }

    public async Task StartNewDesignThreadAsync(Guid adventureId)
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
                "Link a ChatGPT Project before starting a new design thread.",
                "Start new design thread",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (MessageBox.Show(
                this,
                "This will release the current design thread binding (utility session and pinned design tab) "
                + "while keeping your linked Project.\n\n"
                + "The design thread start packet will be copied to your clipboard and your Design tab "
                + "will navigate to your Project.\n\n"
                + "Click New chat in ChatGPT, paste (Ctrl+V), and press Send. Then click "
                + "\"Use this tab as design thread\".",
                "Start new design thread",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await EnsureChatWebViewEnvironmentReadyAsync();

        var designTabForReuse = _designWebView ?? ResolveDesignWebView(bundle);

        DesignThreadRotationService.ReleaseDesignThread(bundle);
        DesignThreadRotationService.PersistRelease(bundle);

        bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var startPacket = DesignThreadRotationService.BuildStartPacket(bundle);
        if (!ClipboardCopy.TrySetText(startPacket, "StartNewDesignThread"))
        {
            MessageBox.Show(
                this,
                "Could not copy the design thread start packet to the clipboard.\n\n"
                + "Use Send step brief after pinning a tab, or try Start new design thread… again.",
                "Start new design thread",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var wv = ResolveExistingDesignWebView(designTabForReuse);
        if (wv is null)
        {
            bundle = AdventureStore.Load(adventureId);
            if (bundle is null)
                return;

            wv = await RestoreDesignWebViewAsync(bundle, selectTab: true, ensureThread: false);
        }

        if (wv is null)
        {
            MessageBox.Show(
                this,
                "No browser tab is available.\n\n"
                + "Open a ChatGPT tab, then try Start new design thread… again.",
                "Start new design thread",
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
                "Browser tab is still initializing — try again shortly.",
                "Start new design thread",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        GetOrRegisterAdventureBridge(wv);
        WireProjectServices(wv);
        SelectTabForWebView(wv);
        _designWebView = wv;

        var reloaded = AdventureStore.Load(adventureId);
        if (reloaded is null)
            return;

        if (PlayTabPinService.IsSameTabAsPlayPin(reloaded, wv, ChatTabs))
        {
            MessageBox.Show(
                this,
                "This tab is pinned for play. Open a different browser tab for design.\n\n"
                + "1. Click + to open a new ChatGPT tab.\n"
                + "2. Open your Project and click New chat.\n"
                + "3. Return here and click Start new design thread… again.",
                "Start new design thread",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, reloaded))
        {
            core.Navigate(projectUrl);
            await WaitForChatGptNavigationAsync(core);
        }

        reloaded = AdventureStore.Load(adventureId) ?? reloaded;
        UpdateDesignLinkStatus();
        _designView?.SetStatus(AdventureDesignContextService.FormatDesignModeOpenStatus(reloaded));

        MessageBox.Show(
            this,
            DesignThreadRotationService.FormatStartThreadReadyMessage(core.Source, reloaded),
            "Start new design thread",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private WebView2? ResolveExistingDesignWebView(WebView2? preferred)
    {
        if (preferred is not null && PlayTabPinService.GetTabKey(preferred, ChatTabs) is not null)
            return preferred;

        if (_appMode == AppMode.Design && GetActiveWebView() is { } active)
            return active;

        foreach (var item in ChatTabs.Items)
        {
            if (item is TabItem { Content: WebView2 wv })
                return wv;
        }

        return null;
    }

    public async Task StartDesignModeAsync(
        Guid adventureId,
        DesignModeEntryIntent entry = DesignModeEntryIntent.Default)
    {
        try
        {
            await StartDesignModeCoreAsync(adventureId, entry);
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"StartDesignMode failed: {ex}");
            MessageBox.Show(
                this,
                $"Could not open Design mode: {ex.Message}",
                "Design mode",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetAppMode(AppMode.Adventures);
        }
    }

    private async Task StartDesignModeCoreAsync(
        Guid adventureId,
        DesignModeEntryIntent entry = DesignModeEntryIntent.Default)
    {
        ChatGptWebViewFileDiagnostics.DownloadCompleted -= OnDesignChatDownloadCompleted;
        ChatGptWebViewFileDiagnostics.DownloadCompleted += OnDesignChatDownloadCompleted;

        _activeAdventureId = adventureId;
        SetAppMode(AppMode.Design);

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var localSourcesEdit = entry == DesignModeEntryIntent.LocalSourcesEdit;

        if (!localSourcesEdit && !AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            var link = MessageBox.Show(
                this,
                "Link a ChatGPT Project before designing with AI?\n\nYou can link now or continue with local editing only.",
                "Design mode",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (link == MessageBoxResult.Yes)
                await OpenProjectWorkspaceAsync(adventureId);

            bundle = AdventureStore.Load(adventureId);
        }

        if (bundle is null)
            return;

        if (localSourcesEdit)
            AdventureDesignContextService.ApplyLocalSourcesEditEntry(bundle);
        else
            AdventureDesignContextService.ApplyLocalSourcesResumeStep(bundle);
        AdventureStore.Save(bundle);

        _designView ??= new AdventureDesignView();
        WireDesignView(_designView);
        AdventureHost.Content = _designView;
        _designView.LoadAdventure(adventureId);

        ApplyDesignPanelLayout();
        _designView.SetStatus("Loading design workspace…");

        if (AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            try
            {
                await PrepareDesignBrowserAsync(adventureId);
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"Design browser prepare failed (non-fatal): {ex}");
                _designView.SetStatus(
                    $"Local sources ready. Design browser: {ex.Message}");
            }
        }

        bundle = AdventureStore.Load(adventureId) ?? bundle;
        if (_designView is not null)
        {
            _designView.SetStatus(localSourcesEdit
                ? AdventureDesignContextService.FormatLocalSourcesEditStatus(bundle)
                : AdventureDesignContextService.FormatDesignModeOpenStatus(bundle));
        }

        UpdateDesignLinkStatus();
    }

    private void ApplyDesignPanelLayout()
    {
        const double defaultWidth = 420;
        AdventureColumn.MinWidth = 280;
        AdventureColumn.Width = new GridLength(defaultWidth);
        AdventureHost.Visibility = Visibility.Visible;
        PlaySidePanelSplitterColumn.MinWidth = 4;
        PlaySidePanelSplitterColumn.Width = new GridLength(4);
        PlaySidePanelSplitter.Visibility = Visibility.Visible;
        NotesColumn.Width = new GridLength(0);
        NotesColumn.MinWidth = 0;
        NotesHost.Visibility = Visibility.Collapsed;
    }

    private void WireDesignView(AdventureDesignView view)
    {
        view.BackRequested -= OnDesignBack;
        view.BackRequested += OnDesignBack;
        view.LinkProjectRequested -= OnDesignLinkProjectRequested;
        view.LinkProjectRequested += OnDesignLinkProjectRequested;
        view.OpenDesignThreadRequested -= OnDesignOpenThreadRequested;
        view.OpenDesignThreadRequested += OnDesignOpenThreadRequested;
        view.PinDesignTabRequested -= OnDesignPinTabRequested;
        view.PinDesignTabRequested += OnDesignPinTabRequested;
        view.StartNewDesignThreadRequested -= OnDesignStartNewThreadRequested;
        view.StartNewDesignThreadRequested += OnDesignStartNewThreadRequested;
        view.SendStepBriefAsync = text => RunDesignChatAsync(view.AdventureId!.Value, text);
        view.SendSourceFilePromptAsync = path => RunDesignSourceFilePromptAsync(view.AdventureId!.Value, path);
        view.RefineInstructionsAsync = notes => RunRefineInstructionsAsync(view.AdventureId!.Value, notes);
        view.GenerateInstructionsFileAsync = () => RunGenerateInstructionsFileAsync(view.AdventureId!.Value);
        view.OpenInstructionDesignerAsync = () => RunOpenInstructionDesignerAsync(view.AdventureId!.Value);
        view.SendCombinedSourceFilePromptsAsync = paths =>
            RunDesignCombinedSourceFilePromptsAsync(view.AdventureId!.Value, paths);
        view.PullSourcesFromDesignThreadAsync = () =>
            RunPullSourcesFromDesignThreadAsync(view.AdventureId!.Value);
        view.ExtractStepAsync = step => RunDesignExtractStepAsync(view.AdventureId!.Value, step);
        view.ImportFrameworkDraftAsync = async () =>
        {
            _activeAdventureId = view.AdventureId;
            var result = await RunGenerationJobForActiveAdventureAsync(GenerationJobId.DraftFramework);
            return result?.DisplayText;
        };
        view.LaunchAdventureAsync = async (bootstrap, startPlay) =>
        {
            await LaunchDesignedAdventureAsync(view.AdventureId!.Value, bootstrap, startPlay);
            if (!startPlay)
                SetAppMode(AppMode.Adventures);
            _dashboardView?.RefreshList();
        };
    }

    private void OnDesignBack(object? sender, EventArgs e)
    {
        ChatGptWebViewFileDiagnostics.DownloadCompleted -= OnDesignChatDownloadCompleted;
        if (_activeAdventureId is { } prevAdventureId)
            AdventureLinkedNavigationGuard.Reset(prevAdventureId);
        _activeAdventureId = null;
        _designWebView = null;
        SetAppMode(AppMode.Adventures);
    }

    private void OnDesignChatDownloadCompleted(object? sender, ChatGptWebViewFileDiagnostics.ChatDownloadCompletedEventArgs e)
    {
        if (_appMode != AppMode.Design || _activeAdventureId is not { } id)
            return;

        if (!e.ResultFilePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return;

        Dispatcher.Invoke(() =>
        {
            var bundle = AdventureStore.Load(id);
            if (bundle is null)
                return;

            var result = AdventureSourceFileService.TryImportFromAbsolutePaths(
                bundle,
                [e.ResultFilePath],
                "chat-download");

            if (result.Imported <= 0)
                return;

            AdventureStore.Save(bundle);
            _designView?.RefreshAfterGenerationJob();
            _designView?.SetStatus(
                $"Imported chat download → {string.Join(", ", result.Messages.Where(m => m.Contains("→", StringComparison.Ordinal)))}");
        });
    }

    private async void OnDesignLinkProjectRequested(object? sender, EventArgs e)
    {
        if (_designView?.AdventureId is not { } id)
            return;

        await OpenProjectWorkspaceAsync(id);
    }

    private async void OnDesignOpenThreadRequested(object? sender, EventArgs e)
    {
        if (_designView?.AdventureId is not { } id)
            return;

        _designView.SetStatus("Opening design thread…");
        try
        {
            var ready = await TryEnsureDesignThreadForJobsAsync(id);
            UpdateDesignLinkStatus();
            if (!ready)
            {
                var bundle = AdventureStore.Load(id);
                if (bundle is not null
                    && DesignTabPinService.GetDesignConversationId(bundle) is null)
                {
                    _designView.SetStatus(DesignTabPinService.DesignPinRequiredError);
                }
            }
        }
        catch (Exception ex)
        {
            _designView.SetStatus(
                AdventureNavigationService.FormatDesignSessionError(
                    new DesignContextResult
                    {
                        Status = DesignContextStatus.NavigationFailed,
                        Error = ex.Message,
                    }));
        }

        if (_designWebView is not null)
            SelectTabForWebView(_designWebView);
    }

    private async Task RefreshProjectLinkUiAsync(Guid adventureId, bool linkedNow)
    {
        _dashboardView?.RefreshList();

        if (_appMode == AppMode.Play && _activeAdventureId == adventureId)
            RefreshPlayProjectLinkUi(adventureId);

        if (_appMode == AppMode.Design && _activeAdventureId == adventureId)
        {
            _designView?.LoadAdventure(adventureId);
            if (linkedNow)
            {
                try
                {
                    await PrepareDesignBrowserAsync(adventureId, selectTab: false);
                }
                catch (Exception ex)
                {
                    _designView?.SetStatus($"Session error: {ex.Message}");
                }
            }

            UpdateDesignLinkStatus();
        }
    }

    private void OnDesignPinTabRequested(object? sender, EventArgs e)
    {
        if (_designView?.AdventureId is not { } id)
            return;

        if (PinActiveTabForDesign(id))
        {
            _designView.SetStatus("Design thread pinned — you can Send step brief.");
            UpdateDesignLinkStatus();
        }
    }

    private async void OnDesignStartNewThreadRequested(object? sender, EventArgs e)
    {
        if (_designView?.AdventureId is not { } id)
            return;

        await StartNewDesignThreadAsync(id);
    }
}
