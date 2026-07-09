using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private enum AppMode
    {
        Browse,
        Adventures,
        Design,
        Play,
    }

    private AppMode _appMode = AppMode.Browse;
    private AdventureDashboardView? _dashboardView;
    private AdventurePlayView? _playView;
    private AdventureNotesPanel? _notesPanel;
    private bool _notesPanelWired;
    private PlayRightCompanionHost? _rightCompanionHost;
    private readonly Dictionary<WebView2, ChatGptApiBridgeInjection> _apiBridges = new();
    private ChatGptApiBridgeInjection? _apiBridge;
    private WebView2? _projectApiWebView;
    private ChatGptProjectApiService? _projectApiService;
    private ChatGptConversationSendService? _conversationSendService;
    private ChatGptChatFileService? _chatFileService;
    private PlaySendWarmupService? _playSendWarmupService;
    private AdventureProjectBindingService? _projectBindingService;
    private ProjectSourceSyncService? _sourceSyncService;
    private AdventureTurnService? _turnService;
    private Guid? _activeAdventureId;
    private readonly SemaphoreSlim _playContextGate = new(1, 1);
    private bool _playPanelFocusModeActive;
    private PlayPanelFocusSnapshot? _playPanelFocusSnapshot;

    private sealed class PlayPanelFocusSnapshot
    {
        public required bool LeftCollapsed { get; init; }

        public required bool RightCollapsed { get; init; }

        public required double LeftWidth { get; init; }

        public required double RightWidth { get; init; }
    }

    private Guid? ResolveActiveAdventureIdForFormatImport() =>
        _activeAdventureId is { } id && _appMode is AppMode.Play or AppMode.Design ? id : null;

    public async Task<BridgeHealthStatus?> GetAdventureBridgeHealthAsync()
    {
        if (_turnService is null || _playWebView?.CoreWebView2 is not { } core)
            return null;

        if (_activeAdventureId is { } id)
        {
            var bundle = AdventureStore.Load(id);
            if (bundle is not null && !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            {
                var ctx = await EnsureLinkedPlayContextForBundleAsync(bundle);
                if (ctx is not null && !ctx.IsReady)
                {
                    return new BridgeHealthStatus
                    {
                        BridgeReachable = false,
                        Error = ctx.Error ?? ctx.Status.ToString(),
                    };
                }
            }
        }

        return await _turnService.GetHealthAsync(core);
    }

    public void UpdatePlayLinkStatus()
    {
        if (_playView is null || _activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        AdventureNavigationService.SyncLinkedFields(bundle);
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var connectionSummary = AdventureThreadRegistryService.FormatConnectionSummary(bundle);
        var sourceReadiness = ProjectSourceInjectionService.Evaluate(bundle);
        var sourcesLine = ProjectSourceInjectionService.FormatLinkStatusSources(sourceReadiness);

        var duplicateHint = bundle.SourceManifest.LastKnownDuplicateRemotes > 0
            ? $" ({bundle.SourceManifest.LastKnownDuplicateRemotes} duplicate remote(s))"
            : "";
        var instructionsLine = InstructionSourcesPolicy.FormatInstructionSyncStatus(bundle);
        var sourcesWithInstructions = string.IsNullOrWhiteSpace(instructionsLine)
            ? $"{sourcesLine}{duplicateHint}"
            : $"{sourcesLine} | {instructionsLine}{duplicateHint}";
        var canonStatus = CanonReconciliationPromptService.FormatUnresolvedStatus(bundle);
        if (!string.IsNullOrWhiteSpace(canonStatus))
            sourcesWithInstructions += " | " + canonStatus;

        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            var packet = sourceReadiness.CanDelegateStaticContent
                ? "source-delegated packets"
                : sourceReadiness.HasLinkedProject
                    ? "inline fallback"
                    : "minimal local";
            sourcesWithInstructions = $"No Project — {packet}" + (canonStatus is not null ? " | " + canonStatus : "");
        }

        _playView.SetConnectionSummary(connectionSummary, sourcesWithInstructions);

        _playView.SetPlayTabPinStatus(
            AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.PinnedTabKey is not null,
            AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play)?.PinnedTabTitle);
        _playView.UpdateJobButtonStates();
        RefreshAllChatTabHeaders();
        UpdateShellStatusBar();
        UpdateSessionStatusChips();
    }

    public WebView2? GetAdventureWebView() => GetPlayWebView();

    public WebView2? GetProjectApiWebView() => _projectApiWebView ?? _playWebView;

    public AdventureProjectBindingService? GetProjectBindingService() => _projectBindingService;

    public ProjectSourceSyncService? GetProjectSyncService() => _sourceSyncService;

    public ChatGptChatFileService? GetChatFileService() => _chatFileService;

    public async Task<bool> OpenSourceManagerDialogAsync(Guid adventureId)
    {
        await ProjectHost.EnsureReadyAsync(adventureId, showBrowserPane: true);

        if (_playView is not null && _activeAdventureId == adventureId)
        {
            _playView.OpenPublishSourcesSettings();
            return true;
        }

        return await OpenPlaySettingsSourcesDialogAsync(adventureId);
    }

    private Task<bool> OpenPlaySettingsSourcesDialogAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return Task.FromResult(false);

        var dlg = new PlayPromptInjectionDialog(bundle, previewPlayerLine: null, PlaySettingsTab.Sources)
        {
            Owner = this,
        };
        WireStandalonePlaySettingsDialog(dlg, adventureId);
        if (dlg.ShowDialog() == true)
        {
            ReloadPlayAdventure(adventureId);
            UpdatePlayLinkStatus();
        }

        return Task.FromResult(true);
    }

    public void WireStandalonePlaySettingsDialog(PlayPromptInjectionDialog dlg, Guid adventureId)
    {
        dlg.OpenThreadsHub = () => OpenThreadManagerDialog(adventureId, AdventureThreadKind.Play);
        dlg.ProbeSourcesAsync = () => ProbeProjectSourcesAsync(adventureId);
        dlg.ProbeSourceFileAsync = path => ProbeProjectSourceFileAsync(adventureId, path);
        dlg.OpenProjectSettingsAsync = OpenProjectSettingsAsync;
        dlg.OpenApiSyncDiagnosticsAsync = () => OpenSourceSyncDialogAsync(adventureId);
        dlg.SynthesizeSourceAsync = (targetPath, parsed) =>
            SynthesizeSourceContentAsync(adventureId, targetPath, parsed);
        dlg.RunSourceEditJobAsync = (prompt, attachments, referenceNote) =>
            RunSourceEditJobAsync(prompt, attachments, referenceNote);
        dlg.RunUtilityJobWithAttachmentsAsync = jobId => RunUtilityJobWithAttachmentsPromptAsync(jobId);
        dlg.RefreshHostDelegates();
    }

    public async Task<bool> OpenSourceSyncDialogAsync(Guid adventureId)
    {
        if (!ProjectHost.TryEnterOperation())
            return false;

        try
        {
            await ProjectHost.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var dlg = new SourceSyncDialog(adventureId, ProjectHost)
            {
                Owner = this,
            };

            dlg.ShowDialog();
            ReloadPlayAdventure(adventureId);
            UpdatePlayLinkStatus();
            return dlg.SyncCompleted;
        }
        finally
        {
            ProjectHost.ExitOperation();
        }
    }

    public async Task ProbeProjectSourcesAsync(Guid adventureId)
    {
        if (!ProjectHost.TryEnterOperation())
            return;

        try
        {
            await ProjectHost.EnsureReadyAsync(adventureId, showBrowserPane: true);
            if (ProjectHost.ApiCore is not { } core)
                return;

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
                return;

            await ProjectSourceProbeService.ProbeAllAsync(core, bundle, ProjectHost.Api);
            AdventureStore.SaveSourceManifestOnly(bundle);
            ReloadPlayAdventure(adventureId);
            UpdatePlayLinkStatus();
        }
        finally
        {
            ProjectHost.ExitOperation();
        }
    }

    public async Task ProbeProjectSourceFileAsync(Guid adventureId, string relativePath)
    {
        if (!ProjectHost.TryEnterOperation())
            return;

        try
        {
            await ProjectHost.EnsureReadyAsync(adventureId, showBrowserPane: true);
            if (ProjectHost.ApiCore is not { } core)
                return;

            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
                return;

            await ProjectSourceProbeService.ProbeFileAsync(core, bundle, ProjectHost.Api, relativePath);
            AdventureStore.SaveSourceManifestOnly(bundle);
            ReloadPlayAdventure(adventureId);
            UpdatePlayLinkStatus();
        }
        finally
        {
            ProjectHost.ExitOperation();
        }
    }

    public Task<bool> OpenProjectLinkWizardAsync(Guid adventureId) =>
        OpenProjectWorkspaceAsync(adventureId);

    public async Task<bool> OpenProjectWorkspaceAsync(Guid adventureId)
    {
        if (!ProjectHost.TryEnterOperation())
            return false;

        try
        {
            var dlg = new ProjectWorkspaceDialog(adventureId, ProjectHost)
            {
                Owner = this,
            };

            dlg.ShowDialog();

            var bundle = AdventureStore.Load(adventureId);
            var linkedNow = bundle is not null && AdventureProjectBindingService.HasLinkedProject(bundle);

            if (!dlg.LinkStateChanged)
            {
                await RefreshProjectLinkUiAsync(adventureId, linkedNow);
                return linkedNow || dlg.LinkedSuccessfully;
            }

            bundle = AdventureStore.Load(adventureId);
            linkedNow = bundle is not null && AdventureProjectBindingService.HasLinkedProject(bundle);

            // Refresh Play UI immediately so the Link now banner hides before play-context prep.
            await RefreshProjectLinkUiAsync(adventureId, linkedNow);

            var skipPlayThread = bundle?.Metadata.Status == AdventureStatus.Designing
                                 || _appMode == AppMode.Design;

            if (!skipPlayThread)
            {
                bundle = AdventureStore.Load(adventureId);
                var deferPlayContext = bundle is not null
                    && AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle);

                await EnsurePlaySessionAsync(
                    adventureId,
                    selectTab: _appMode == AppMode.Play,
                    prepareContext: !deferPlayContext);

                bundle = AdventureStore.Load(adventureId);
                linkedNow = bundle is not null && AdventureProjectBindingService.HasLinkedProject(bundle);
                if (!deferPlayContext
                    && bundle is not null
                    && AdventureProjectBindingService.HasLinkedProject(bundle)
                    && _playWebView?.CoreWebView2 is not null)
                {
                    var ctx = await EnsureLinkedPlayContextForBundleAsync(bundle);
                    if (ctx is not null && !ctx.IsReady)
                    {
                        MessageBox.Show(
                            this,
                            ctx.Error ?? "Could not open the Project play thread.",
                            "Linked Project",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }

            _dashboardView?.RefreshList();
            await RefreshProjectLinkUiAsync(adventureId, linkedNow);
            return dlg.LinkedSuccessfully || linkedNow;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Projects workspace",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            ProjectHost.ExitOperation();
        }
    }

    private void InitializeAdventureUi()
    {
        BodyGrid.SizeChanged += OnBodyGridSizeChanged;
        SizeChanged += OnMainWindowSizeChanged;
        SetAppMode(AppMode.Browse);
        InitializePlayPromptComposer();
    }

    private void OnMainWindowSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyAllPlayPanelLayouts();

    private void OnBodyGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_appMode != AppMode.Play || _activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var settings = bundle.Metadata.Settings;
        var changed = false;

        if (!settings.PlaySidePanelCollapsed
            && AdventureColumn.Width.IsAbsolute
            && AdventureColumn.Width.Value > 0)
        {
            var max = GetMaxPlaySidePanelWidth(settings);
            if (AdventureColumn.Width.Value > max)
            {
                var clamped = Math.Clamp(AdventureColumn.Width.Value, MinPlaySidePanelWidth, max);
                AdventureColumn.Width = new GridLength(clamped);
                settings.PlaySidePanelWidth = clamped;
                changed = true;
            }
        }

        if (!settings.PlayNotesPanelCollapsed
            && NotesColumn.Width.IsAbsolute
            && NotesColumn.Width.Value > 0)
        {
            var max = GetMaxPlayNotesPanelWidth(settings);
            if (NotesColumn.Width.Value > max)
            {
                var clamped = Math.Clamp(NotesColumn.Width.Value, MinPlayNotesPanelWidth, max);
                NotesColumn.Width = new GridLength(clamped);
                settings.PlayNotesPanelWidth = clamped;
                changed = true;
            }
        }

        if (changed)
            AdventureStore.Save(bundle);

        ApplyPlayPanelResponsiveLayouts(bundle);
    }

    private void BrowseModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appMode is AppMode.Play or AppMode.Design)
            LeaveActiveAdventureSession();

        SetAppMode(AppMode.Browse);
    }

    private void AdventuresModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appMode is AppMode.Play or AppMode.Design)
            LeaveActiveAdventureSession();

        SetAppMode(AppMode.Adventures);
    }

    /// <summary>
    /// Drops cached play/navigation state when switching from one adventure to another without leaving Play.
    /// </summary>
    private void PrepareAdventureSwitch(Guid previousAdventureId, Guid newAdventureId)
    {
        PlayContextSessionCache.Invalidate(previousAdventureId);
        PlayContextSessionCache.Invalidate(newAdventureId);
        AdventureLinkedNavigationGuard.Reset(previousAdventureId);
        AdventureLinkedNavigationGuard.Reset(newAdventureId);
        ClearPlayPanelFocusMode();
        _playWebView = null;
    }

    /// <summary>
    /// Clears the active adventure session when leaving Play or Design (library, Browse, or shell back).
    /// </summary>
    private void LeaveActiveAdventureSession(bool refreshDashboardList = false)
    {
        if (_appMode == AppMode.Design)
        {
            ChatGptWebViewFileDiagnostics.DownloadCompleted -= OnDesignChatDownloadCompleted;
            _designWebView = null;
        }

        if (_activeAdventureId is { } prevAdventureId)
        {
            UiEventLogger.Info(
                "adventure_session_end",
                "Leaving adventure session",
                new { adventureId = prevAdventureId, appMode = _appMode.ToString() });
            PlayContextSessionCache.Invalidate(prevAdventureId);
            AdventureLinkedNavigationGuard.Reset(prevAdventureId);
        }

        ClearPlayPanelFocusMode();
        _activeAdventureId = null;
        ClearPlayComposePrompt();
        _preparedSendArtifactStore.Invalidate();
        PlayPromptComposer?.SetMergedPreview(null);
        SetPlayComposeStatus("");

        ScheduleStaleInjectionComposerCleanupOnAllTabs();

        if (refreshDashboardList)
            _dashboardView?.RefreshList();
    }

    private void ShowProjectBrowserPane(Guid? adventureId)
    {
        switch (_appMode)
        {
            case AppMode.Adventures when adventureId is { } id:
                UiAsyncTasks.Run(() => StartPlayModeAsync(id), "start_play_mode", adventureId: id);
                break;
            case AppMode.Play:
                if (FindProjectApiWebView() is { } playWv)
                    SelectTabForWebView(playWv);
                break;
            case AppMode.Design:
                if (_designWebView is not null)
                    SelectTabForWebView(_designWebView);
                else if (FindDesignApiWebView() is { } designWv)
                    SelectTabForWebView(designWv);
                break;
            case AppMode.Browse:
                if (FindProjectApiWebView() is { } browseWv)
                    SelectTabForWebView(browseWv);
                break;
        }
    }

    private void SetAppMode(AppMode mode)
    {
        _appMode = mode;
        UpdatePlayPromptComposerVisibility();
        UpdateModeButtonStyles();
        UpdateShellContext();
        UpdateTranscriptSettingsVisibility();

        switch (mode)
        {
            case AppMode.Browse:
                ResetSidePanelLayoutForNonPlay();
                ChatTabs.Visibility = Visibility.Visible;
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                ChatChromePanel.Visibility = Visibility.Visible;
                break;
            case AppMode.Adventures:
                AdventureColumn.MinWidth = 0;
                AdventureColumn.Width = new GridLength(1, GridUnitType.Star);
                PlaySidePanelSplitterColumn.MinWidth = 0;
                PlaySidePanelSplitterColumn.Width = new GridLength(0);
                PlaySidePanelSplitter.Visibility = Visibility.Collapsed;
                CollapsePlaySidePanelButton.Visibility = Visibility.Collapsed;
                ExpandPlaySidePanelButton.Visibility = Visibility.Collapsed;
                NotesColumn.MinWidth = 0;
                NotesColumn.Width = new GridLength(0);
                PlayNotesPanelSplitterColumn.MinWidth = 0;
                PlayNotesPanelSplitterColumn.Width = new GridLength(0);
                NotesHost.Visibility = Visibility.Collapsed;
                PlayNotesPanelSplitter.Visibility = Visibility.Collapsed;
                CollapsePlayNotesPanelButton.Visibility = Visibility.Collapsed;
                ExpandPlayNotesPanelButton.Visibility = Visibility.Collapsed;
                AdventureHost.Visibility = Visibility.Visible;
                ChatTabs.Visibility = Visibility.Collapsed;
                ChatColumn.Width = new GridLength(0);
                ChatColumn.MinWidth = 0;
                ChatChromePanel.Visibility = Visibility.Visible;
                EnsureDashboard();
                AdventureHost.Content = _dashboardView;
                if (_dashboardView is not null)
                    _ = _dashboardView.RefreshOnEnterAsync();
                break;
            case AppMode.Play:
                PrepareBodyGridForPlayMode();
                ApplyAllPlayPanelLayouts();
                EnsureAdventureHostPlayContent();
                ChatTabs.Visibility = Visibility.Visible;
                ChatChromePanel.Visibility = Visibility.Visible;
                break;
            case AppMode.Design:
                ApplyDesignPanelLayout();
                ChatTabs.Visibility = Visibility.Visible;
                ChatColumn.Width = new GridLength(1, GridUnitType.Star);
                ChatChromePanel.Visibility = Visibility.Visible;
                if (_designView is not null && _activeAdventureId is { } designId)
                    AdventureHost.Content = _designView;
                break;
        }

        UpdateAdventureNavigationWatchdog();
        UpdateShellStatusBar();

        var layout = ShellLayoutDiagnostics.Capture(
            AdventureHost.Content?.GetType().Name,
            AdventureHost.Visibility,
            AdventureColumn.Width,
            ChatColumn.Width,
            NotesColumn.Width);

        UiEventLogger.Debug(
            "app_mode_changed",
            $"App mode set to {mode}",
            new { mode = mode.ToString(), layout },
            adventureId: _activeAdventureId);

        if (_activeAdventureId is not null
            && mode is not AppMode.Play
            && mode is not AppMode.Design)
        {
            UiEventLogger.Warn(
                "orphaned_adventure_session",
                "Active adventure id without Play/Design shell mode",
                new { mode = mode.ToString(), layout },
                adventureId: _activeAdventureId);
        }
    }

    private void UpdateTranscriptSettingsVisibility()
    {
        var showChatChrome = _appMode != AppMode.Adventures;
        ChatTabs.Visibility = showChatChrome ? Visibility.Visible : Visibility.Collapsed;
        ShellViewMenu.Visibility = showChatChrome ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateModeButtonStyles() => SyncAppModeSegmentSelection();

    private void UpdateShellContext()
    {
        if (_appMode is AppMode.Play or AppMode.Design)
        {
            ShellContextPanel.Visibility = Visibility.Visible;
            var title = "Adventure";
            if (_activeAdventureId is { } id)
            {
                var bundle = AdventureStore.Load(id);
                if (!string.IsNullOrWhiteSpace(bundle?.Metadata.Title))
                    title = bundle.Metadata.Title;
            }

            ShellContextTitle.Text = title;
            UpdateAdventureSessionToggleStyles();
            UpdatePlaySessionChrome();
            UpdateSessionStatusChips();
            _playView?.SetShellChromeState(true);
            _designView?.SetShellChromeState(true);
            return;
        }

        ShellContextPanel.Visibility = Visibility.Collapsed;
        ShellContextTitle.Text = string.Empty;
        UpdatePlaySessionChrome();
        UpdateSessionStatusChips();
        _playView?.SetShellChromeState(false);
        _designView?.SetShellChromeState(false);
    }

    private void UpdatePlaySessionChrome()
    {
        var inPlay = _appMode == AppMode.Play;
        var hasAdventureSession = _appMode is AppMode.Play or AppMode.Design && _activeAdventureId is not null;

        if (ShellPlayFocusButton is not null)
        {
            ShellPlayFocusButton.Visibility = inPlay ? Visibility.Visible : Visibility.Collapsed;
            ShellPlayFocusButton.ToolTip = _playPanelFocusModeActive
                ? "Show companion panels"
                : "Focus chat — hide companion columns for maximum chat width";
        }

        if (FocusChatMenuItem is not null)
            FocusChatMenuItem.Visibility = Visibility.Collapsed;

        if (PlaySettingsMenuItem is not null)
            PlaySettingsMenuItem.Visibility = hasAdventureSession ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearPlayPanelFocusMode()
    {
        _playPanelFocusModeActive = false;
        _playPanelFocusSnapshot = null;
        UpdatePlaySessionChrome();
    }

    private void TogglePlayPanelFocusMode()
    {
        if (_appMode != AppMode.Play || _activeAdventureId is not { } id)
            return;

        if (_playPanelFocusModeActive)
        {
            RestorePlayPanelFocusSnapshot();
            return;
        }

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        _playPanelFocusSnapshot = CapturePlayPanelFocusSnapshot(bundle);
        _playPanelFocusModeActive = true;

        bundle.Metadata.Settings.PlaySidePanelCollapsed = true;
        if (ShouldShowRightCompanionColumn(bundle))
            bundle.Metadata.Settings.PlayNotesPanelCollapsed = true;

        AdventureStore.Save(bundle);
        ApplyAllPlayPanelLayouts();
        UpdatePlaySessionChrome();
    }

    private PlayPanelFocusSnapshot CapturePlayPanelFocusSnapshot(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;
        var leftWidth = AdventureColumn.Width.IsAbsolute && AdventureColumn.Width.Value > 0
            ? AdventureColumn.Width.Value
            : GetPlaySidePanelWidth(settings);
        var rightWidth = NotesColumn.Width.IsAbsolute && NotesColumn.Width.Value > 0
            ? NotesColumn.Width.Value
            : GetPlayNotesPanelWidth(settings);

        return new PlayPanelFocusSnapshot
        {
            LeftCollapsed = settings.PlaySidePanelCollapsed,
            RightCollapsed = settings.PlayNotesPanelCollapsed,
            LeftWidth = leftWidth,
            RightWidth = rightWidth,
        };
    }

    private void RestorePlayPanelFocusSnapshot()
    {
        if (_activeAdventureId is not { } id || _playPanelFocusSnapshot is not { } snapshot)
        {
            ClearPlayPanelFocusMode();
            return;
        }

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
        {
            ClearPlayPanelFocusMode();
            return;
        }

        bundle.Metadata.Settings.PlaySidePanelCollapsed = snapshot.LeftCollapsed;
        bundle.Metadata.Settings.PlayNotesPanelCollapsed = snapshot.RightCollapsed;
        bundle.Metadata.Settings.PlaySidePanelWidth = snapshot.LeftWidth;
        bundle.Metadata.Settings.PlayNotesPanelWidth = snapshot.RightWidth;
        AdventureStore.Save(bundle);

        ClearPlayPanelFocusMode();
        ApplyAllPlayPanelLayouts();
    }

    private void ShellBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_appMode is AppMode.Play or AppMode.Design)
            LeaveActiveAdventureSession();

        SetAppMode(AppMode.Adventures);
        if (_dashboardView is not null)
            _ = _dashboardView.RefreshOnEnterAsync();
    }

    private void EnsureDashboard()
    {
        if (_dashboardView is not null)
            return;

        _dashboardView = new AdventureDashboardView();
        _dashboardView.IsAdventureActiveInPlay = id =>
            _appMode == AppMode.Play && _activeAdventureId == id;
        _dashboardView.PlayRequested += OnDashboardPlayRequested;
        _dashboardView.LinkProjectRequested += OnDashboardLinkProjectRequested;
        _dashboardView.DraftFrameworkRequested += async (_, adventureId) =>
        {
            _activeAdventureId = adventureId;
            await RunDraftFrameworkAsync();
        };
        _dashboardView.DesignWithAiRequested += async (_, _) => await OpenAdventureDesignWizardAsync();
        _dashboardView.ContinueDesignRequested += async (_, adventureId) =>
            await OpenContinueDesignWizardAsync(adventureId);
        _dashboardView.OpenDesignWizardFromDialogAsync = () => OpenAdventureDesignWizardAsync();
        _dashboardView.RenameCompleted += OnAdventureRenamed;
        _dashboardView.PreferencesRequested += (_, _) =>
        {
            OpenPreferencesHub();
            _dashboardView.RefreshAfterPreferencesClosed();
        };
    }

    private void OnAdventureRenamed(object? sender, Guid adventureId) =>
        AfterAdventureRenamed(adventureId);

    private void OnPlayTitleRenamed(object? sender, Guid adventureId) =>
        AfterAdventureRenamed(adventureId);

    private void AfterAdventureRenamed(Guid adventureId)
    {
        _dashboardView?.RefreshList();
        ReloadPlayAdventure(adventureId);
        if (_designView?.AdventureId == adventureId)
            _designView.LoadAdventure(adventureId);
        if (_activeAdventureId == adventureId)
            UpdateShellContext();
    }

    private void ReloadPlayAdventure(Guid adventureId)
    {
        _playView?.LoadAdventure(adventureId);
        if (_activeAdventureId != adventureId)
            return;

        _notesPanel?.LoadAdventure(adventureId);
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        ApplyPlayTabHostsLayout(bundle);
        UpdatePlayNotesRailTooltip(bundle);
    }

    private void RefreshPlayProjectLinkUi(Guid adventureId)
    {
        if (_playView is null || _activeAdventureId != adventureId || _appMode != AppMode.Play)
            return;

        ReloadPlayAdventure(adventureId);
        UpdatePlayLinkStatus();
    }

    private async void OnDashboardPlayRequested(object? sender, Guid adventureId) =>
        await StartPlayModeAsync(adventureId);

    private async Task StartPlayModeAsync(Guid adventureId)
    {
        UiEventLogger.Info(
            "play_requested",
            "Dashboard requested play mode",
            adventureId: adventureId);

        try
        {
            if (_activeAdventureId is { } previousId && previousId != adventureId)
                PrepareAdventureSwitch(previousId, adventureId);

            _activeAdventureId = adventureId;
            SetAppMode(AppMode.Play);

            var playView = EnsurePlayViewWired(adventureId);
            EnsurePlayCompanionHosts();
            EnsureAdventureHostPlayContent();

            UiEventLogger.Info(
                "play_session_start",
                "Entering play mode",
                adventureId: adventureId);

            var enterBundle = AdventureStore.Load(adventureId);
            if (enterBundle is not null)
            {
                PlayCompanionRestoreService.ApplyEnterPlayPreferences(
                    enterBundle.Metadata.Settings,
                    _chrome.PlaySurface);
                AdventureStore.Save(enterBundle, AdventureSaveScope.Metadata);
            }

            ReloadPlayAdventure(adventureId);

            var loadedBundle = AdventureStore.Load(adventureId);
            if (loadedBundle is not null)
            {
                if (UtilityWorkerPinService.TryReconcilePinFromCapabilities(loadedBundle))
                    AdventureStore.Save(loadedBundle, AdventureSaveScope.Metadata);
                WorkerCoordinator(adventureId).ResumeIncompleteOutbox(this);
            }

            ApplyAllPlayPanelLayouts();
            SyncPlayComposerFromAdventurePanel();
            playView.SetSessionLoading(true, "Preparing play session…");
            try
            {
                await BrowserTabsReadyTask;
                await EnsurePlaySessionAsync(adventureId);
                ApplyPlaySurfaceActionsToPlayTab();
                _ = ApplyThreadOrdinalMapToPlayTabAsync();
                await CheckThreadLogDriftOnLoadAsync(adventureId);
            }
            catch (Exception ex)
            {
                var bundle = AdventureStore.Load(adventureId);
                var message = bundle is not null
                    ? AdventureNavigationService.FormatPlaySessionError(
                        new PlayContextResult
                        {
                            Status = PlayContextStatus.NavigationFailed,
                            Error = ex.Message,
                        })
                    : $"Session error: {ex.Message}";
                playView.SetSessionError(message);
            }
            finally
            {
                playView.SetSessionLoading(false);
            }

            UpdatePlayLinkStatus();
            UpdatePlayMergedPreview();
        }
        catch (Exception ex)
        {
            ReportPlaySessionStartFailure(adventureId, ex);
        }
    }

    private void ReportPlaySessionStartFailure(Guid adventureId, Exception ex)
    {
        DiagnosticsMirror.LogException("play_start", ex, adventureId: adventureId);
        UiEventLogger.Error(
            "play_session_start_failed",
            ex.Message,
            new { exceptionType = ex.GetType().Name },
            adventureId: adventureId);

        if (_playView is not null)
        {
            _playView.SetSessionError($"Could not open play cockpit: {ex.Message}");
            return;
        }

        if (_appMode == AppMode.Play)
        {
            LeaveActiveAdventureSession();
            SetAppMode(AppMode.Adventures);
        }
        else
        {
            _activeAdventureId = null;
        }

        MessageBox.Show(
            this,
            ex.Message,
            "Play mode",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void SyncPlayComposerFromAdventurePanel()
    {
        if (PlayPromptComposer is null || _playView is null)
            return;

        if (!string.IsNullOrWhiteSpace(GetPlayPlayerLineText()))
            return;

        var preview = _playView.GetPreviewPlayerLineText();
        if (!string.IsNullOrWhiteSpace(preview))
            SetPlayPlayerLineText(preview);
    }

    private void OnPlayBack(object? sender, EventArgs e)
    {
        LeaveActiveAdventureSession(refreshDashboardList: true);
        SetAppMode(AppMode.Adventures);
    }

    private ChatGptApiBridgeInjection GetOrRegisterApiBridge(WebView2 wv)
    {
        if (!_apiBridges.TryGetValue(wv, out var bridge))
        {
            bridge = new ChatGptApiBridgeInjection(wv);
            _apiBridges[wv] = bridge;
        }

        if (wv.CoreWebView2 is not null)
            bridge.Register();

        return bridge;
    }

    private void WireProjectServices(WebView2 wv)
    {
        _apiBridge = GetOrRegisterApiBridge(wv);
        if (wv.CoreWebView2 is not null && !_apiBridge.IsRegistered)
            _apiBridge.Register();

        _projectApiWebView = wv;
        _projectApiService = new ChatGptProjectApiService(_apiBridge);
        _conversationSendService = new ChatGptConversationSendService(_apiBridge);
        _chatFileService = new ChatGptChatFileService(_apiBridge, _projectApiService, _conversationSendService);
        _playSendWarmupService = new PlaySendWarmupService(_apiBridge, _chatFileService.Transport.Warmup);
        _sourceSyncService = new ProjectSourceSyncService(_projectApiService);
        _projectBindingService = new AdventureProjectBindingService(_projectApiService, _sourceSyncService);
        _generationJobService = new GenerationJobService(
            _projectApiService,
            _conversationSendService,
            TryCreateProjectConversationViaUiAsync);
        if (_sessionHost is not null)
            _sessionHost.SetConversationSendService(_conversationSendService);
    }

    private static bool IsChatGptPage(CoreWebView2? core)
    {
        if (core is null)
            return false;

        return Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
               && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri);
    }

    private WebView2? FindProjectApiWebView()
    {
        if (_appMode == AppMode.Play
            && _activeAdventureId is { } activeId)
        {
            var bundle = AdventureStore.Load(activeId);
            if (PlayTabPinService.PreferPinnedPlayWebView(true, bundle))
            {
                var pinned = PlayTabPinService.FindWebViewByPinKey(
                    ChatTabs,
                    PlayTabPinService.GetPlayPinKey(bundle!));
                if (pinned is not null)
                    return pinned;
            }

            if (_playWebView is not null)
                return _playWebView;
        }

        WebView2? firstCandidate = null;

        foreach (var item in ChatTabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            firstCandidate ??= wv;
            if (IsChatGptPage(wv.CoreWebView2))
                return wv;
        }

        if (GetActiveWebView() is { } active)
        {
            if (IsChatGptPage(active.CoreWebView2))
                return active;

            firstCandidate ??= active;
        }

        return firstCandidate;
    }

    private WebView2? FindDesignApiWebView()
    {
        if (_activeAdventureId is { } activeId)
        {
            var bundle = AdventureStore.Load(activeId);
            if (bundle is not null)
            {
                var pinned = DesignTabPinService.TryFindWebViewForDesignSession(ChatTabs, bundle);
                if (pinned is not null)
                    return pinned;
            }
        }

        return _designWebView;
    }

    private void SelectTabForWebView(WebView2 wv)
    {
        if (_utilityWorkerWebView is not null
            && ReferenceEquals(wv, _utilityWorkerWebView)
            && FindUtilityWorkerTabItem(wv) is { } utilityTab)
        {
            if (Volatile.Read(ref _utilityWorkerDomSendInFlight) > 0)
                return;

            UnparkUtilityWorkerWebView(wv, utilityTab);
        }

        foreach (var item in ChatTabs.Items)
        {
            if (item is TabItem tab && tab.Content == wv)
            {
                ChatTabs.SelectedItem = tab;
                break;
            }
        }
    }

    private static bool IsAtNavigationDestination(CoreWebView2 core, string? expectedDestination)
    {
        if (string.IsNullOrWhiteSpace(expectedDestination))
            return IsChatGptPage(core);

        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var current)
            || !Uri.TryCreate(expectedDestination, UriKind.Absolute, out var expected))
        {
            return string.Equals(core.Source, expectedDestination, StringComparison.OrdinalIgnoreCase);
        }

        if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(current))
            return false;

        var currentPath = current.AbsolutePath.TrimEnd('/');
        var expectedPath = expected.AbsolutePath.TrimEnd('/');
        if (string.Equals(currentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            return true;

        if (ChatGptUrls.TryParseConversationId(expected, out var expectedConversationId)
            && ChatGptUrls.TryParseConversationId(current, out var currentConversationId)
            && string.Equals(expectedConversationId, currentConversationId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static async Task WaitForChatGptNavigationAsync(
        CoreWebView2 core,
        int timeoutMs = 20000,
        CancellationToken cancellationToken = default,
        string? expectedDestination = null)
    {
        if (IsAtNavigationDestination(core, expectedDestination))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && IsAtNavigationDestination(core, expectedDestination))
                tcs.TrySetResult();
        }

        core.NavigationCompleted += Handler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        }
        catch (TimeoutException)
        {
            /* best effort */
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private async void OnDashboardLinkProjectRequested(object? sender, Guid adventureId) =>
        await RunLinkProjectFlowAsync(adventureId);

    private async void OnPlayLinkProjectRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is not { } adventureId)
            return;

        await RunLinkProjectFlowAsync(adventureId);
    }

    private void OnPlayManageThreadsRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is not { } adventureId)
            return;

        OpenThreadManagerDialog(adventureId, AdventureThreadKind.Play);
    }

    private async Task RunLinkProjectFlowAsync(Guid adventureId)
    {
        _dashboardView?.SetLinkProjectBusy(true);
        try
        {
            await OpenProjectLinkWizardAsync(adventureId);
        }
        finally
        {
            _dashboardView?.SetLinkProjectBusy(false);
        }
    }

    public Task PrepareProjectLinkSessionAsync(Guid adventureId) =>
        ProjectHost.EnsureReadyAsync(adventureId, showBrowserPane: true);

    private async Task<PlayContextResult?> EnsureLinkedPlayContextForBundleAsync(AdventureBundle bundle)
    {
        AdventureNavigationService.SyncLinkedFields(bundle);

        if (!AdventureNavigationService.HasLinkedProject(bundle))
            return new PlayContextResult { Status = PlayContextStatus.Legacy };

        var wv = _playWebView ?? ResolvePlayWebView(bundle);
        if (wv?.CoreWebView2 is not { } core)
        {
            return new PlayContextResult
            {
                Status = PlayContextStatus.NavigationFailed,
                Error = "Pinned play tab is not ready. Pin a ChatGPT tab or open Play again.",
            };
        }

        _playWebView = wv;
        if (_projectApiService is null)
            WireProjectServices(wv);

        if (_projectApiService is null)
        {
            return new PlayContextResult
            {
                Status = PlayContextStatus.NavigationFailed,
                Error = "Project API services are not ready.",
            };
        }

        if (_turnService is null)
            GetOrCreateTurnService(wv);

        if (PlayContextSessionCache.TryBindConversationFromUrl(bundle, core.Source)
            || PlayContextSessionCache.TrySyncConversationFromUrl(bundle, core.Source))
        {
            AdventureStore.Save(bundle);
        }

        await _playContextGate.WaitAsync();
        try
        {
            if (await PlayContextSessionCache.ShouldSkipReensureAsync(bundle, core, _turnService!))
            {
                _playSendWarmupService?.PrefetchFireAndForget(core, bundle);
                return new PlayContextResult
                {
                    Status = PlayContextStatus.Ready,
                    ConversationId = bundle.Metadata.LinkedConversationId,
                };
            }

            var result = await AdventurePlayContextService.EnsureLinkedProjectPlayContextAsync(
                core,
                bundle,
                _projectApiService,
                _turnService);

            if (result.IsReady && _turnService is not null)
            {
                var health = await _turnService.GetHealthAsync(core);
                PlayContextSessionCache.Record(
                    bundle.Metadata.Id,
                    core.Source,
                    result.ConversationId ?? bundle.Metadata.LinkedConversationId,
                    health.ComposerFound);
                _playSendWarmupService?.PrefetchFireAndForget(core, bundle);
            }
            else
            {
                PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
            }

            return result;
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"EnsureLinkedProjectPlayContext failed: {ex.Message}");
            return new PlayContextResult
            {
                Status = PlayContextStatus.NavigationFailed,
                Error = ex.Message,
            };
        }
        finally
        {
            _playContextGate.Release();
        }
    }

    private static AdventureTurnResult PlayContextFailureResult(PlayContextResult ctx, string? packetText = null) =>
        new()
        {
            Success = false,
            Error = $"linked_project_context_failed: {ctx.Error ?? ctx.Status.ToString()}",
            RequiresManualFallback = true,
            PacketText = packetText,
        };

    private async Task<AdventureTurnResult> SendTurnWithLinkedContextAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        TurnRecord turn,
        string packetText,
        bool regenerate)
    {
        var ctx = await EnsureLinkedPlayContextForBundleAsync(bundle);
        if (ctx is not null && !ctx.IsReady)
            return PlayContextFailureResult(ctx, packetText);

        var result = await _turnService!.SendTurnAsync(
            core,
            bundle,
            turn,
            packetText,
            regenerate: regenerate);

        if (!result.Success
            && !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && result.Error is "project_context_required")
        {
            ctx = await EnsureLinkedPlayContextForBundleAsync(bundle);
            if (ctx is not null && ctx.IsReady)
            {
                result = await _turnService.SendTurnAsync(
                    core,
                    bundle,
                    turn,
                    packetText,
                    regenerate: regenerate);
            }
        }

        return result;
    }

    private const double DefaultPlaySidePanelWidth = 300;

    private static double ResolveDefaultPlaySidePanelWidth() =>
        ThemeRuntime.Current.CompanionDefaultWidth > 0
            ? ThemeRuntime.Current.CompanionDefaultWidth
            : DefaultPlaySidePanelWidth;
    private const double MinPlaySidePanelWidth = 200;
    private const double MaxPlaySidePanelWidth = 640;
    private const double DefaultPlayNotesPanelWidth = 240;
    private const double MinPlayNotesPanelWidth = 180;
    private const double MaxPlayNotesPanelWidth = 480;
    private const double MinChatColumnWidth = 400;
    private const double NarrowMinChatColumnWidth = 280;
    private const double NarrowWindowWidthThreshold = 900;

    private double GetEffectiveMinChatColumnWidth()
    {
        var windowWidth = ActualWidth > 0 ? ActualWidth : MinWidth;
        return windowWidth < NarrowWindowWidthThreshold
            ? NarrowMinChatColumnWidth
            : MinChatColumnWidth;
    }
    private const double SplitterColumnWidth = 12;
    private const double ExpandColumnWidth = 12;

    /// <summary>
    /// Breaks Adventures-mode body grid sizing (star-width library column, zero-width chat)
    /// before play panel layout runs.
    /// </summary>
    private void PrepareBodyGridForPlayMode()
    {
        ChatColumn.MinWidth = GetEffectiveMinChatColumnWidth();
        ChatColumn.Width = new GridLength(1, GridUnitType.Star);

        if (AdventureColumn.Width.IsStar)
        {
            AdventureColumn.MinWidth = MinPlaySidePanelWidth;
            AdventureColumn.Width = new GridLength(ResolveDefaultPlaySidePanelWidth());
        }

        AdventureHost.Visibility = Visibility.Visible;
    }

    private void ResetSidePanelLayoutForNonPlay()
    {
        AdventureColumn.MinWidth = 0;
        AdventureColumn.Width = new GridLength(0);
        PlaySidePanelSplitterColumn.MinWidth = 0;
        PlaySidePanelSplitterColumn.Width = new GridLength(0);
        AdventureHost.Visibility = Visibility.Collapsed;
        PlaySidePanelSplitter.Visibility = Visibility.Collapsed;
        CollapsePlaySidePanelButton.Visibility = Visibility.Collapsed;
        ExpandPlaySidePanelButton.Visibility = Visibility.Collapsed;

        NotesColumn.MinWidth = 0;
        NotesColumn.Width = new GridLength(0);
        PlayNotesPanelSplitterColumn.MinWidth = 0;
        PlayNotesPanelSplitterColumn.Width = new GridLength(0);
        NotesHost.Visibility = Visibility.Collapsed;
        PlayNotesPanelSplitter.Visibility = Visibility.Collapsed;
        CollapsePlayNotesPanelButton.Visibility = Visibility.Collapsed;
        ExpandPlayNotesPanelButton.Visibility = Visibility.Collapsed;
    }

    private static double GetStoredPlaySidePanelWidth(AdventureSettings settings) =>
        settings.PlaySidePanelWidth > 0 ? settings.PlaySidePanelWidth : ResolveDefaultPlaySidePanelWidth();

    private static double GetStoredPlayNotesPanelWidth(AdventureSettings settings) =>
        settings.PlayNotesPanelWidth > 0 ? settings.PlayNotesPanelWidth : DefaultPlayNotesPanelWidth;

    private double GetRightPanelReservedWidth(AdventureSettings settings) =>
        settings.PlayNotesPanelCollapsed
            ? ExpandColumnWidth
            : Math.Clamp(GetStoredPlayNotesPanelWidth(settings), MinPlayNotesPanelWidth, MaxPlayNotesPanelWidth)
              + SplitterColumnWidth;

    private double GetLeftPanelReservedWidth(AdventureSettings settings) =>
        settings.PlaySidePanelCollapsed
            ? ExpandColumnWidth
            : Math.Clamp(GetStoredPlaySidePanelWidth(settings), MinPlaySidePanelWidth, MaxPlaySidePanelWidth)
              + SplitterColumnWidth;

    private double GetMaxPlaySidePanelWidth(AdventureSettings settings)
    {
        var available = BodyGrid.ActualWidth > 0 ? BodyGrid.ActualWidth : ActualWidth;
        var reserved = GetEffectiveMinChatColumnWidth()
            + GetRightPanelReservedWidth(settings)
            + SplitterColumnWidth;
        return Math.Max(
            MinPlaySidePanelWidth,
            Math.Min(MaxPlaySidePanelWidth, available - reserved));
    }

    private double GetMaxPlayNotesPanelWidth(AdventureSettings settings)
    {
        var available = BodyGrid.ActualWidth > 0 ? BodyGrid.ActualWidth : ActualWidth;
        var reserved = GetEffectiveMinChatColumnWidth()
            + GetLeftPanelReservedWidth(settings)
            + SplitterColumnWidth;
        return Math.Max(
            MinPlayNotesPanelWidth,
            Math.Min(MaxPlayNotesPanelWidth, available - reserved));
    }

    private double GetPlaySidePanelWidth(AdventureSettings settings)
    {
        var width = GetStoredPlaySidePanelWidth(settings);
        return Math.Clamp(width, MinPlaySidePanelWidth, GetMaxPlaySidePanelWidth(settings));
    }

    private double GetPlayNotesPanelWidth(AdventureSettings settings)
    {
        var width = GetStoredPlayNotesPanelWidth(settings);
        return Math.Clamp(width, MinPlayNotesPanelWidth, GetMaxPlayNotesPanelWidth(settings));
    }

    private void ApplyAllPlayPanelLayouts()
    {
        ChatColumn.MinWidth = GetEffectiveMinChatColumnWidth();

        if (_appMode != AppMode.Play || _activeAdventureId is not { } id)
        {
            ResetSidePanelLayoutForNonPlay();
            return;
        }

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
        {
            ResetSidePanelLayoutForNonPlay();
            return;
        }

        ApplyPlaySidePanelLayout(bundle);
        ApplyPlayTabHostsLayout(bundle);
        ApplyPlayNotesPanelLayout(bundle);
        ApplyPlayPanelResponsiveLayouts(bundle);
    }

    private void ApplyPlayPanelResponsiveLayouts(AdventureBundle bundle)
    {
        var shellWidth = 0.0;
        if (!bundle.Metadata.Settings.PlaySidePanelCollapsed
            && AdventureColumn.Width.IsAbsolute
            && AdventureColumn.Width.Value > 0)
        {
            shellWidth = AdventureColumn.Width.Value;
        }

        var companionWidth = 0.0;
        if (!bundle.Metadata.Settings.PlayNotesPanelCollapsed
            && NotesColumn.Width.IsAbsolute
            && NotesColumn.Width.Value > 0)
        {
            companionWidth = NotesColumn.Width.Value;
        }

        var snapshot = PlayLayoutCoordinator.CreateSnapshot(shellWidth, companionWidth);

        if (shellWidth > 0)
            _playView?.ApplyLayout(snapshot);

        if (companionWidth > 0)
            _rightCompanionHost?.ApplyLayout(snapshot.Companion);
    }

    private void EnsurePlayCompanionHosts()
    {
        _notesPanel ??= new AdventureNotesPanel();
        _rightCompanionHost ??= new PlayRightCompanionHost();
        NotesHost.Content = _rightCompanionHost;
        _playView?.SetRightTabControl(_rightCompanionHost.RightTabControl);
        WireNotesPanel();
    }

    private void EnsureAdventureHostPlayContent()
    {
        if (_appMode != AppMode.Play)
            return;

        if (_activeAdventureId is not { } id)
        {
            DiagnosticsLog.Write(
                DiagnosticsChannel.Ui,
                DiagnosticsLevel.Warn,
                "play_host_content_skipped",
                "Play mode active without an adventure id; left panel left unchanged.",
                data: new { hostContent = AdventureHost.Content?.GetType().Name });
            return;
        }

        AdventurePlayView playView;
        try
        {
            playView = EnsurePlayViewWired(id);
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write(
                DiagnosticsChannel.Ui,
                DiagnosticsLevel.Error,
                "play_host_content_failed",
                ex.Message,
                adventureId: id,
                data: new { ex.GetType().Name });
            return;
        }

        if (AdventureHost.Content == playView)
            return;

        var previous = AdventureHost.Content?.GetType().Name ?? "(null)";
        AdventureHost.Content = playView;
        UiEventLogger.Debug(
            "play_host_content_set",
            "AdventureHost switched to play cockpit",
            new { previousContent = previous },
            adventureId: id);
    }

    private void WireNotesPanel()
    {
        if (_notesPanel is null || _playView is null)
            return;

        if (!_notesPanelWired)
        {
            _notesPanelWired = true;
            _notesPanel.NotesContentChanged += OnNotesContentChanged;
            _playView.FocusNotesEditor = () => _notesPanel.FocusEditor();
            _playView.SaveNotesAction = () => _notesPanel.SaveConfiguration();
            _notesPanel.ResolveNotesInsertContext = ResolveNotesInsertContext;
        }

        EnsureAdventureHostPlayContent();
    }

    private void OnNotesContentChanged(object? sender, EventArgs e)
    {
        if (_activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is not null)
            UpdatePlayNotesRailTooltip(bundle);
    }

    private NotesInsertContext ResolveNotesInsertContext()
    {
        if (_activeAdventureId is not { } id)
            return new NotesInsertContext(0, null);

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return new NotesInsertContext(0, null);

        var accepted = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
        return new NotesInsertContext(accepted, _playView?.GetSelectedEntityName());
    }

    private void ApplyPlayTabHostsLayout(AdventureBundle bundle)
    {
        EnsurePlayCompanionHosts();
        var settings = bundle.Metadata.Settings;
        var notesSide = PlayPanelLayoutService.ResolveTabPlacement(settings, "Notes");

        _playView?.SetRightTabControl(_rightCompanionHost!.RightTabControl);
        _playView?.ApplyTabPlacementFromSettings();

        if (notesSide == PlayPanelSide.Hidden)
        {
            _rightCompanionHost!.NotesSlot.Content = null;
        }
        else
        {
            _rightCompanionHost!.NotesSlot.Content = _notesPanel;
        }

        var hasRightTabs = _rightCompanionHost!.RightTabControl.Items.Count > 0;
        _rightCompanionHost.SetRightTabsVisible(hasRightTabs);
        UpdatePlayNotesRailTooltip(bundle);
    }

    private static bool ShouldShowRightCompanionColumn(AdventureBundle bundle)
    {
        var settings = bundle.Metadata.Settings;
        if (PlayPanelLayoutService.ResolveTabPlacement(settings, "Notes") == PlayPanelSide.Right)
            return true;

        foreach (var tab in new[] { "Reference", "Warnings", "State" })
        {
            if (PlayPanelLayoutService.ResolveTabPlacement(settings, tab) == PlayPanelSide.Right)
                return true;
        }

        return false;
    }

    private void UpdatePlayNotesRailTooltip(AdventureBundle bundle)
    {
        var notesSide = PlayPanelLayoutService.ResolveTabPlacement(bundle.Metadata.Settings, "Notes");
        if (notesSide == PlayPanelSide.Hidden)
        {
            ExpandPlayNotesPanelButton.ToolTip = "Notes hidden — Play settings → Play surface";
            return;
        }

        ExpandPlayNotesPanelButton.ToolTip = _notesPanel?.GetNotesPreviewLine() ?? "Show notes panel";
    }

    private void ApplyPlaySidePanelLayout(AdventureBundle bundle)
    {
        var collapsed = bundle.Metadata.Settings.PlaySidePanelCollapsed;

        if (collapsed)
        {
            AdventureColumn.MinWidth = 0;
            AdventureColumn.Width = new GridLength(0);
            PlaySidePanelSplitterColumn.MinWidth = 0;
            PlaySidePanelSplitterColumn.Width = new GridLength(ExpandColumnWidth);
            AdventureHost.Visibility = Visibility.Collapsed;
            PlaySidePanelSplitter.Visibility = Visibility.Collapsed;
            CollapsePlaySidePanelButton.Visibility = Visibility.Collapsed;
            ExpandPlaySidePanelButton.Visibility = Visibility.Visible;
        }
        else
        {
            var width = GetPlaySidePanelWidth(bundle.Metadata.Settings);
            AdventureColumn.MinWidth = MinPlaySidePanelWidth;
            AdventureColumn.Width = new GridLength(width);
            PlaySidePanelSplitterColumn.MinWidth = SplitterColumnWidth;
            PlaySidePanelSplitterColumn.Width = new GridLength(SplitterColumnWidth);
            AdventureHost.Visibility = Visibility.Visible;
            PlaySidePanelSplitter.Visibility = Visibility.Visible;
            CollapsePlaySidePanelButton.Visibility = Visibility.Visible;
            ExpandPlaySidePanelButton.Visibility = Visibility.Collapsed;
        }

        _playView?.SetSidePanelCollapsed(collapsed);
    }

    private void SnapPlaySidePanelToOptimalWidth()
    {
        if (_activeAdventureId is not { } id || _appMode != AppMode.Play)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        if (bundle.Metadata.Settings.PlaySidePanelCollapsed)
            bundle.Metadata.Settings.PlaySidePanelCollapsed = false;

        var optimal = PlayPanelOptimalWidthCalculator.Resolve(
            bundle.Metadata.Settings,
            GetMaxPlaySidePanelWidth(bundle.Metadata.Settings),
            GetMaxPlayNotesPanelWidth(bundle.Metadata.Settings));

        var width = Math.Clamp(
            optimal.LeftWidth,
            MinPlaySidePanelWidth,
            GetMaxPlaySidePanelWidth(bundle.Metadata.Settings));
        bundle.Metadata.Settings.PlaySidePanelWidth = width;
        AdventureStore.Save(bundle);
        ApplyAllPlayPanelLayouts();
    }

    private void SnapPlayNotesPanelToOptimalWidth()
    {
        if (_activeAdventureId is not { } id || _appMode != AppMode.Play)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null || !ShouldShowRightCompanionColumn(bundle))
            return;

        if (bundle.Metadata.Settings.PlayNotesPanelCollapsed)
            bundle.Metadata.Settings.PlayNotesPanelCollapsed = false;

        var optimal = PlayPanelOptimalWidthCalculator.Resolve(
            bundle.Metadata.Settings,
            GetMaxPlaySidePanelWidth(bundle.Metadata.Settings),
            GetMaxPlayNotesPanelWidth(bundle.Metadata.Settings));

        var width = Math.Clamp(
            optimal.RightWidth,
            MinPlayNotesPanelWidth,
            GetMaxPlayNotesPanelWidth(bundle.Metadata.Settings));
        bundle.Metadata.Settings.PlayNotesPanelWidth = width;
        AdventureStore.Save(bundle);
        ApplyAllPlayPanelLayouts();
    }

    private void ApplyPlayNotesPanelLayout(AdventureBundle bundle)
    {
        if (!ShouldShowRightCompanionColumn(bundle))
        {
            SetPlayNotesColumnCollapsed(collapsed: true);
            return;
        }

        var collapsed = bundle.Metadata.Settings.PlayNotesPanelCollapsed;

        SetPlayNotesColumnCollapsed(collapsed);
    }

    private void SetPlayNotesColumnCollapsed(bool collapsed)
    {
        if (collapsed)
        {
            NotesColumn.MinWidth = 0;
            NotesColumn.Width = new GridLength(0);
            PlayNotesPanelSplitterColumn.MinWidth = 0;
            PlayNotesPanelSplitterColumn.Width = new GridLength(ExpandColumnWidth);
            NotesHost.Visibility = Visibility.Collapsed;
            PlayNotesPanelSplitter.Visibility = Visibility.Collapsed;
            CollapsePlayNotesPanelButton.Visibility = Visibility.Collapsed;
            ExpandPlayNotesPanelButton.Visibility = Visibility.Visible;
        }
        else
        {
            if (_activeAdventureId is { } id)
            {
                var bundle = AdventureStore.Load(id);
                if (bundle is not null)
                {
                    var width = GetPlayNotesPanelWidth(bundle.Metadata.Settings);
                    NotesColumn.MinWidth = MinPlayNotesPanelWidth;
                    NotesColumn.Width = new GridLength(width);
                }
            }

            PlayNotesPanelSplitterColumn.MinWidth = SplitterColumnWidth;
            PlayNotesPanelSplitterColumn.Width = new GridLength(SplitterColumnWidth);
            NotesHost.Visibility = Visibility.Visible;
            PlayNotesPanelSplitter.Visibility = Visibility.Visible;
            CollapsePlayNotesPanelButton.Visibility = Visibility.Visible;
            ExpandPlayNotesPanelButton.Visibility = Visibility.Collapsed;
        }

        if (!collapsed
            && NotesColumn.Width.IsAbsolute
            && NotesColumn.Width.Value > 0
            && _activeAdventureId is { } notesLayoutId)
        {
            var notesBundle = AdventureStore.Load(notesLayoutId);
            if (notesBundle is not null)
                ApplyPlayPanelResponsiveLayouts(notesBundle);
        }
    }

    private void PlaySidePanelSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        SnapPlaySidePanelToOptimalWidth();

    private void PlayNotesPanelSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        SnapPlayNotesPanelToOptimalWidth();

    private void ExpandPlaySidePanelButton_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        SnapPlaySidePanelToOptimalWidth();

    private void ExpandPlayNotesPanelButton_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        SnapPlayNotesPanelToOptimalWidth();

    private void PlaySidePanelSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (_appMode != AppMode.Play || _activeAdventureId is not { } id || !AdventureColumn.Width.IsAbsolute)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var width = Math.Clamp(
            AdventureColumn.Width.Value,
            MinPlaySidePanelWidth,
            GetMaxPlaySidePanelWidth(bundle.Metadata.Settings));
        if (Math.Abs(AdventureColumn.Width.Value - width) > 0.5)
            AdventureColumn.Width = new GridLength(width);

        ApplyPlayPanelResponsiveLayouts(bundle);
    }

    private void PlaySidePanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_activeAdventureId is not { } id || _appMode != AppMode.Play)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null || !AdventureColumn.Width.IsAbsolute)
            return;

        var width = Math.Clamp(
            AdventureColumn.Width.Value,
            MinPlaySidePanelWidth,
            GetMaxPlaySidePanelWidth(bundle.Metadata.Settings));
        bundle.Metadata.Settings.PlaySidePanelWidth = width;
        AdventureStore.Save(bundle);
        AdventureColumn.Width = new GridLength(width);
        ApplyPlayPanelResponsiveLayouts(bundle);
    }

    private void PlayNotesPanelSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (_appMode != AppMode.Play || _activeAdventureId is not { } id || !NotesColumn.Width.IsAbsolute)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        var width = Math.Clamp(
            NotesColumn.Width.Value,
            MinPlayNotesPanelWidth,
            GetMaxPlayNotesPanelWidth(bundle.Metadata.Settings));
        if (Math.Abs(NotesColumn.Width.Value - width) > 0.5)
            NotesColumn.Width = new GridLength(width);

        ApplyPlayPanelResponsiveLayouts(bundle);
    }

    private void PlayNotesPanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_activeAdventureId is not { } id || _appMode != AppMode.Play)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null || !NotesColumn.Width.IsAbsolute)
            return;

        var width = Math.Clamp(
            NotesColumn.Width.Value,
            MinPlayNotesPanelWidth,
            GetMaxPlayNotesPanelWidth(bundle.Metadata.Settings));
        bundle.Metadata.Settings.PlayNotesPanelWidth = width;
        AdventureStore.Save(bundle);
        NotesColumn.Width = new GridLength(width);
        ApplyPlayPanelResponsiveLayouts(bundle);
    }

    private void CollapsePlaySidePanelButton_Click(object sender, RoutedEventArgs e) =>
        TogglePlaySidePanelCollapsed(collapse: true);

    private void TogglePlaySidePanelCollapsed(bool collapse)
    {
        if (_activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        ClearPlayPanelFocusMode();
        bundle.Metadata.Settings.PlaySidePanelCollapsed = collapse;
        AdventureStore.Save(bundle);
        ApplyAllPlayPanelLayouts();
    }

    private void ExpandPlaySidePanelButton_Click(object sender, RoutedEventArgs e) =>
        TogglePlaySidePanelCollapsed(collapse: false);

    private void OnExpandPlaySidePanelRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is null)
            return;

        var bundle = AdventureStore.Load(_activeAdventureId.Value);
        if (bundle is null || !bundle.Metadata.Settings.PlaySidePanelCollapsed)
            return;

        TogglePlaySidePanelCollapsed(collapse: false);
    }

    private void OnExpandPlayNotesPanelRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is null)
            return;

        var bundle = AdventureStore.Load(_activeAdventureId.Value);
        if (bundle is null || !ShouldShowRightCompanionColumn(bundle))
            return;

        if (!bundle.Metadata.Settings.PlayNotesPanelCollapsed)
            return;

        TogglePlayNotesPanelCollapsed(collapse: false);
    }

    private void CollapsePlayNotesPanelButton_Click(object sender, RoutedEventArgs e) =>
        TogglePlayNotesPanelCollapsed(collapse: true);

    private void ExpandPlayNotesPanelButton_Click(object sender, RoutedEventArgs e) =>
        TogglePlayNotesPanelCollapsed(collapse: false);

    private void TogglePlayNotesPanelCollapsed(bool collapse)
    {
        if (_activeAdventureId is not { } id)
            return;

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return;

        ClearPlayPanelFocusMode();
        bundle.Metadata.Settings.PlayNotesPanelCollapsed = collapse;
        AdventureStore.Save(bundle);
        ApplyAllPlayPanelLayouts();
    }

    private void OnPinPlayTabRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is not { } id)
            return;

        if (PinActiveTabForPlay(id))
            ReloadPlayAdventure(id);
    }

    private void OnOpenPinnedPlayTabRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is not { } id)
            return;

        if (!SelectPinnedPlayTab(id))
            MessageBox.Show(this, "No pinned play tab found. Link the active browser tab first.", "Play tab",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnClearPlayTabPinRequested(object? sender, EventArgs e)
    {
        if (_activeAdventureId is not { } id)
            return;

        ClearPlayTabPin(id);
        ReloadPlayAdventure(id);
    }

    private void OnPlaySettingsSaved(object? sender, EventArgs e)
    {
        var freshChrome = UiChromeStore.Load();
        _chrome.PlaySurface = freshChrome.PlaySurface;
        _playView?.ConfigurePlayChrome(_chrome.PlaySurface);
        _playView?.RefreshPlayChromeFromSettings();

        ApplyWrapperComposerToPlayTab(_appMode == AppMode.Play);
        ApplyInlineUtilityToPlayTab();
        ApplyContextTagsToPlayTab();
        ApplyPlaySurfaceActionsToPlayTab();
        if (_activeAdventureId is { } id)
        {
            ApplyAllPlayPanelLayouts();
            ReloadPlayAdventure(id);
            UpdatePlayMergedPreview();
            UpdatePlayLinkStatus();
        }
    }

    private void OnPlayStatusRefreshRequested(object? sender, EventArgs e) =>
        UpdatePlayLinkStatus();

    private async Task RefreshPlaySourcesStatusAsync(Guid adventureId)
    {
        if (!ProjectHost.TryEnterOperation())
            return;

        try
        {
            await ProjectHost.EnsureReadyAsync(adventureId);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || ProjectHost.ApiCore is not { } core)
                return;

            await ProjectHost.FileSync.BuildStatusPlanAsync(core, bundle);
            AdventureStore.Save(bundle);
            ReloadPlayAdventure(adventureId);
            UpdatePlayLinkStatus();
        }
        finally
        {
            ProjectHost.ExitOperation();
        }
    }

    private async Task<IReadOnlyList<ConversationFileRef>> ListPlayThreadFilesAsync(Guid adventureId)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            return [];

        var wv = await ResolvePlayWebViewAsync(adventureId, promptToPinIfMissing: false, selectTab: false);
        if (wv?.CoreWebView2 is not { } core || _chatFileService is null)
            return [];

        return await _chatFileService.ListConversationFilesAsync(core, bundle.Metadata.LinkedConversationId);
    }

    private async Task<byte[]> DownloadPlayThreadFileAsync(Guid adventureId, ConversationFileRef file)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return [];

        var wv = await ResolvePlayWebViewAsync(adventureId, promptToPinIfMissing: false, selectTab: false);
        if (wv?.CoreWebView2 is not { } core || _chatFileService is null)
            return [];

        return await _chatFileService.DownloadConversationFileAsync(
            core,
            file,
            bundle.Metadata.LinkedProjectId,
            bundle.Metadata.LinkedConversationId);
    }

    private async Task ReconcilePlaySourcesAsync(Guid adventureId)
    {
        if (!ProjectHost.TryEnterOperation())
            return;

        try
        {
            await ProjectHost.EnsureReadyAsync(adventureId, showBrowserPane: true);
            var bundle = AdventureStore.Load(adventureId);
            if (bundle is null || ProjectHost.ApiCore is not { } core)
                return;

            var plan = await ProjectHost.FileSync.BuildPlanAsync(core, bundle, ensureProjectPage: false);
            AdventureStore.Save(bundle);

            await SourceSyncUiHelper.ConfirmAndReconcileDuplicatesAsync(
                this,
                core,
                bundle,
                plan,
                ProjectHost.Sync,
                ProjectHost.FileSync);

            ReloadPlayAdventure(adventureId);
            UpdatePlayLinkStatus();
        }
        finally
        {
            ProjectHost.ExitOperation();
        }
    }

    private void OnRollIntoPlayerLineRequested(object? sender, string rollText)
    {
        if (string.IsNullOrWhiteSpace(rollText))
            return;

        AppendPlayPlayerLineText(rollText);
    }

    private void OnReplacePlayerLineRequested(object? sender, string rollText)
    {
        if (string.IsNullOrWhiteSpace(rollText))
            return;

        SetPlayPlayerLineText(rollText);
    }

    private void OnInsertIntoComposerRequested(object? sender, string text) =>
        AppendPlayPlayerLineText(text);

    private void OnPlayBranchCreated(object? sender, Guid branchId) =>
        UiAsyncTasks.Run(() => StartPlayModeAsync(branchId), "start_play_mode_branch", adventureId: branchId);
}
