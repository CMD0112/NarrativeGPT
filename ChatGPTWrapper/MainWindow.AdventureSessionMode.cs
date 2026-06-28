using System.Linq;
using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private bool _adventureSessionSwitchInProgress;

    private void ShellPlayModeButton_Click(object sender, RoutedEventArgs e) =>
        _ = SwitchToPlaySessionAsync();

    private void ShellDesignModeButton_Click(object sender, RoutedEventArgs e) =>
        _ = SwitchToDesignSessionAsync();

    private void UpdateAdventureSessionToggleStyles()
    {
        if (ShellPlayModeButton is null || ShellDesignModeButton is null)
            return;

        var selected = (Style)FindResource("ModeButtonSelectedStyle");
        var normal = (Style)FindResource("ModeButtonStyle");
        ShellPlayModeButton.Style = _appMode == AppMode.Play ? selected : normal;
        ShellDesignModeButton.Style = _appMode == AppMode.Design ? selected : normal;

        var bundle = _activeAdventureId is { } id ? AdventureStore.Load(id) : null;
        var canDesign = AdventureSessionModePolicy.CanSwitchToDesign(bundle);
        ShellDesignModeButton.IsEnabled = canDesign && !_adventureSessionSwitchInProgress;
        ShellDesignModeButton.ToolTip = canDesign
            ? "Switch to Design mode"
            : "Design unavailable while play is in progress without local sources";
        ShellPlayModeButton.IsEnabled = !_adventureSessionSwitchInProgress;
        ShellPlayModeButton.ToolTip = "Switch to Play mode";
    }

    public async Task SwitchToPlaySessionAsync()
    {
        if (_appMode == AppMode.Play || _adventureSessionSwitchInProgress)
            return;

        if (_activeAdventureId is not { } adventureId)
        {
            return;
        }

        _adventureSessionSwitchInProgress = true;
        UpdateAdventureSessionToggleStyles();
        try
        {
            EnsurePlayViewWired(adventureId);
            EnsurePlayCompanionHosts();
            ReloadPlayAdventure(adventureId);
            SetAppMode(AppMode.Play);
            EnsureAdventureHostPlayContent();
            SyncPlayComposerFromAdventurePanel();

            await BrowserTabsReadyTask;
            await EnsurePlaySessionAsync(
                adventureId,
                selectTab: true,
                prepareContext: false,
                navigateToBrowseTarget: false);
            ApplyPlaySurfaceActionsToPlayTab();
            UpdatePlayLinkStatus();
            UpdatePlayMergedPreview();
        }
        finally
        {
            _adventureSessionSwitchInProgress = false;
            UpdateAdventureSessionToggleStyles();
        }
    }

    public async Task SwitchToDesignSessionAsync()
    {
        if (_appMode == AppMode.Design || _adventureSessionSwitchInProgress)
            return;

        if (_activeAdventureId is not { } adventureId)
            return;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        var availability = AdventureSessionModePolicy.GetDesignAvailability(bundle);
        if (availability == AdventureSessionDesignAvailability.UnavailableHasPlayTurns)
        {
            MessageBox.Show(
                this,
                "This adventure already has play turns and no local source files.\n\n"
                + "Use Play settings to edit scenario, or export sources first.",
                "Design mode",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (availability == AdventureSessionDesignAvailability.NeedsWizard)
        {
            await OpenAdventureDesignWizardAsync(adventureId);
            return;
        }

        _adventureSessionSwitchInProgress = true;
        UpdateAdventureSessionToggleStyles();
        try
        {
            if (AdventureSessionModePolicy.ShouldPromoteToDesigning(bundle))
            {
                bundle.Metadata.Status = AdventureStatus.Designing;
                AdventureDesignService.EnsureWorkspace(bundle);
                AdventureDesignService.HydrateFromScenario(bundle);
                AdventureStore.Save(bundle, AdventureSaveScope.DesignSessionSwitch);
            }

            await SwitchToDesignSessionCoreAsync(
                adventureId,
                AdventureSessionModePolicy.ResolveDesignEntryIntent(bundle));
        }
        finally
        {
            _adventureSessionSwitchInProgress = false;
            UpdateAdventureSessionToggleStyles();
        }
    }

    private async Task SwitchToDesignSessionCoreAsync(
        Guid adventureId,
        DesignModeEntryIntent entry)
    {
        ChatGptWebViewFileDiagnostics.DownloadCompleted -= OnDesignChatDownloadCompleted;
        ChatGptWebViewFileDiagnostics.DownloadCompleted += OnDesignChatDownloadCompleted;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return;

        if (entry == DesignModeEntryIntent.LocalSourcesEdit)
            AdventureDesignContextService.ApplyLocalSourcesEditEntry(bundle);
        else
            AdventureDesignContextService.ApplyLocalSourcesResumeStep(bundle);
        AdventureStore.Save(bundle, AdventureSaveScope.DesignSessionSwitch);

        _designView ??= new AdventureDesignView();
        WireDesignView(_designView);
        _designView.LoadAdventure(adventureId);
        SetAppMode(AppMode.Design);
        AdventureHost.Content = _designView;

        if (AdventureProjectBindingService.HasLinkedProject(bundle))
        {
            try
            {
                await PrepareDesignBrowserAsync(adventureId);
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"Design browser prepare failed (non-fatal): {ex}");
                _designView.SetStatus($"Local sources ready. Design browser: {ex.Message}");
            }
        }

        bundle = AdventureStore.Load(adventureId) ?? bundle;
        var status = entry == DesignModeEntryIntent.LocalSourcesEdit
            ? AdventureDesignContextService.FormatLocalSourcesEditStatus(bundle)
            : AdventureDesignContextService.FormatDesignModeOpenStatus(bundle);
        if (!string.IsNullOrWhiteSpace(bundle.DesignWorkspace.PendingBootstrapNotice))
            status = $"{bundle.DesignWorkspace.PendingBootstrapNotice} {status}";
        _designView.SetStatus(status);
        UpdateDesignLinkStatus();
    }

    private AdventurePlayView EnsurePlayViewWired(Guid adventureId)
    {
        _playView ??= new AdventurePlayView();
        _playView.ResolvePreviewComposerText = () => GetPlayPlayerLineText();
        _playView.ResolvePreviewAttachmentContext = () =>
            GetActivePlayComposeInjection()?.GetLastAttachmentContext();
        _playView.ResolveThreadUserTurnCountAsync = GetPlayThreadUserMessageCountAsync;
        _playView.BackRequested -= OnPlayBack;
        _playView.BackRequested += OnPlayBack;
        _playView.LinkProjectRequested -= OnPlayLinkProjectRequested;
        _playView.LinkProjectRequested += OnPlayLinkProjectRequested;
        _playView.ManageThreadsRequested -= OnPlayManageThreadsRequested;
        _playView.ManageThreadsRequested += OnPlayManageThreadsRequested;
        _playView.PinPlayTabRequested -= OnPinPlayTabRequested;
        _playView.PinPlayTabRequested += OnPinPlayTabRequested;
        _playView.OpenPinnedPlayTabRequested -= OnOpenPinnedPlayTabRequested;
        _playView.OpenPinnedPlayTabRequested += OnOpenPinnedPlayTabRequested;
        _playView.ClearPlayTabPinRequested -= OnClearPlayTabPinRequested;
        _playView.ClearPlayTabPinRequested += OnClearPlayTabPinRequested;
        _playView.PlaySettingsSaved -= OnPlaySettingsSaved;
        _playView.PlaySettingsSaved += OnPlaySettingsSaved;
        _playView.PlayStatusRefreshRequested -= OnPlayStatusRefreshRequested;
        _playView.PlayStatusRefreshRequested += OnPlayStatusRefreshRequested;
        _playView.TitleRenamed -= OnPlayTitleRenamed;
        _playView.TitleRenamed += OnPlayTitleRenamed;
        _playView.RollIntoPlayerLineRequested -= OnRollIntoPlayerLineRequested;
        _playView.RollIntoPlayerLineRequested += OnRollIntoPlayerLineRequested;
        _playView.ReplacePlayerLineRequested -= OnReplacePlayerLineRequested;
        _playView.ReplacePlayerLineRequested += OnReplacePlayerLineRequested;
        _playView.InsertIntoComposerRequested -= OnInsertIntoComposerRequested;
        _playView.InsertIntoComposerRequested += OnInsertIntoComposerRequested;
        _playView.BranchCreated -= OnPlayBranchCreated;
        _playView.BranchCreated += OnPlayBranchCreated;
        _playView.ExpandPlaySidePanelRequested -= OnExpandPlaySidePanelRequested;
        _playView.ExpandPlaySidePanelRequested += OnExpandPlaySidePanelRequested;
        _playView.ExpandPlayNotesPanelRequested -= OnExpandPlayNotesPanelRequested;
        _playView.ExpandPlayNotesPanelRequested += OnExpandPlayNotesPanelRequested;
        _playView.OpenSourceManagerAsync = () => OpenSourceManagerDialogAsync(adventureId);
        _playView.ProbeSourcesAsync = () => ProbeProjectSourcesAsync(adventureId);
        _playView.ProbeSourceFileAsync = path => ProbeProjectSourceFileAsync(adventureId, path);
        _playView.OpenApiSyncDiagnosticsAsync = () => OpenSourceSyncDialogAsync(adventureId);
        _playView.SynthesizeSourceAsync = (targetPath, parsed) =>
            SynthesizeSourceContentAsync(adventureId, targetPath, parsed);
        _playView.RefreshSourcesStatusAsync = () => RefreshPlaySourcesStatusAsync(adventureId);
        _playView.GetPhraseHighlightRules = () => _chrome.PhraseHighlightRules;
        _playView.CommitPhraseHighlightRules = CommitPhraseHighlightRules;
        _playView.ReconcileDuplicatesAsync = () => ReconcilePlaySourcesAsync(adventureId);
        _playView.SuggestEntitiesAsync = () => RunEntityExtractionForActiveAdventureAsync();
        _playView.SuggestMemoriesAsync = () => RunProposeMemoriesAsync();
        _playView.RefreshSummaryAsync = () => RunUpdateSummaryAsync();
        _playView.GenerateCardsAsync = () =>
        {
            var b = AdventureStore.Load(adventureId);
            return b?.Metadata.Settings.UseSectionInjection == true
                ? RunBootstrapSectionsAsync()
                : RunBootstrapLoreAsync();
        };
        _playView.ExpandStoryCardAsync = cardId =>
        {
            var b = AdventureStore.Load(adventureId);
            if (b?.Metadata.Settings.UseSectionInjection == true)
            {
                var card = b.Cards.Cards.FirstOrDefault(c => c.Id == cardId);
                var entity = card is not null
                    ? b.Entities.Characters.FirstOrDefault(c =>
                        string.Equals(c.Name, card.Name, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (entity is not null)
                    return RunExpandSectionAsync(entity.Id);
            }

            return RunExpandStoryCardAsync(cardId);
        };
        _playView.RunContinuityCheckAsync = () => RunContinuityCheckAsync();
        _playView.ProcessLastExchangeAsync = includeSummary => RunProcessLastExchangeAsync(includeSummary);
        _playView.ExpandEntityAsync = (kind, id) => RunExpandEntityAsync(kind, id);
        _playView.SyncInstructionsAsync = async () =>
        {
            var b = AdventureStore.Load(adventureId);
            if (b is not null)
                await SyncProjectInstructionsIfEnabledAsync(b);
        };
        _playView.StartNewPlayThreadAsync = request => StartNewPlayThreadAsync(adventureId, request);
        _playView.DraftNewProjectChatAsync = () => DraftNewProjectChatAsync(adventureId);
        _playView.CancelProjectChatDraft = () => CancelProjectChatDraft(adventureId);
        _playView.RunSourceEditJobAsync = prompt => RunSourceEditJobAsync(prompt);
        _playView.ContinueDesignAsync = () => SwitchToDesignSessionAsync();
        _playView.PromptThreadLogSyncAsync = () => PromptThreadLogSyncFromMenuAsync(adventureId);
        _playView.ListThreadFilesAsync = () => ListPlayThreadFilesAsync(adventureId);
        _playView.DownloadThreadFileAsync = file => DownloadPlayThreadFileAsync(adventureId, file);
        _playView.OpenProjectSettingsAsync = () => OpenProjectSettingsAsync();
        _playView.PreviewLiveStoryContextAsync = jobId => BuildLiveStoryContextPreviewAsync(adventureId, jobId);
        return _playView;
    }
}
