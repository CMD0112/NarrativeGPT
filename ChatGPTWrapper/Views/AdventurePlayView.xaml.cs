using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class AdventurePlayView : UserControl
{
    public event EventHandler? BackRequested;

    public event EventHandler? LinkProjectRequested;

    public event EventHandler? ManageThreadsRequested;

    public event EventHandler? PinPlayTabRequested;

    public event EventHandler? OpenPinnedPlayTabRequested;

    public event EventHandler? ClearPlayTabPinRequested;

    public event EventHandler? PlaySettingsSaved;

    public event EventHandler? PlayStatusRefreshRequested;

    public event EventHandler<Guid>? TitleRenamed;

    public Func<string?>? ResolvePreviewComposerText { get; set; }

    public Func<AttachmentContext?>? ResolvePreviewAttachmentContext { get; set; }

    public Func<Task>? OpenSourceManagerAsync { get; set; }

    public Func<Task>? ProbeSourcesAsync { get; set; }

    public Func<string, Task>? ProbeSourceFileAsync { get; set; }

    public Func<Task>? OpenApiSyncDiagnosticsAsync { get; set; }

    public Func<string, string, Task<string?>>? SynthesizeSourceAsync { get; set; }

    public Func<Task>? RefreshSourcesStatusAsync { get; set; }

    public Func<IReadOnlyList<PhraseHighlightRule>>? GetPhraseHighlightRules { get; set; }

    public Action<IReadOnlyList<PhraseHighlightRule>>? CommitPhraseHighlightRules { get; set; }

    public Func<Task<int>>? ResolveThreadUserTurnCountAsync { get; set; }

    public event EventHandler<string>? InsertIntoComposerRequested;

    public void RefreshActiveEntityHighlightState() =>
        EntityReferencePanel.RefreshActiveHighlightState();

    public Func<Task>? ReconcileDuplicatesAsync { get; set; }

    public Func<Task>? SuggestEntitiesAsync { get; set; }

    public Func<Task>? SuggestMemoriesAsync { get; set; }

    public Func<Task>? RefreshSummaryAsync { get; set; }

    public Func<Task>? GenerateCardsAsync { get; set; }

    public Func<Guid, Task>? ExpandStoryCardAsync { get; set; }

    public Func<Task>? RunContinuityCheckAsync { get; set; }

    public Func<bool, Task>? ProcessLastExchangeAsync { get; set; }

    public Func<string, Guid, Task>? ExpandEntityAsync { get; set; }

    public Func<Task>? SyncInstructionsAsync { get; set; }

    public Func<PlayThreadStartRequest?, Task>? StartNewPlayThreadAsync { get; set; }

    public Func<Task>? DraftNewProjectChatAsync { get; set; }

    public Action? CancelProjectChatDraft { get; set; }

    public Func<string, Task>? RunSourceEditJobAsync { get; set; }

    public Func<Task>? ContinueDesignAsync { get; set; }

    public Func<Task>? PromptThreadLogSyncAsync { get; set; }

    public Func<Task<IReadOnlyList<ConversationFileRef>>>? ListThreadFilesAsync { get; set; }

    public Func<ConversationFileRef, Task<byte[]>>? DownloadThreadFileAsync { get; set; }

    public Func<Task>? OpenProjectSettingsAsync { get; set; }

    public Func<string, Task<UtilityStoryContextBuildResult>>? PreviewLiveStoryContextAsync { get; set; }

    public Action? SaveNotesAction { get; set; }

    public Action? FocusNotesEditor { get; set; }

    public event EventHandler<string>? RollIntoPlayerLineRequested;

    public event EventHandler<string>? ReplacePlayerLineRequested;

    public event EventHandler<Guid>? BranchCreated;

    public event EventHandler? ExpandPlaySidePanelRequested;

    public event EventHandler? ExpandPlayNotesPanelRequested;

    public TabControl LeftTabControl => PlaySideTabControl;

    private AdventureBundle? _bundle;
    private string _previewPlayerLine = "";
    private bool _shellBreadcrumbVisible;
    private bool _showReferenceReviewQueue;
    private DispatcherTimer? _canonSyncNoticeTimer;
    private EntityEditSourceSyncResult? _lastCanonSyncResult;
    private TabControl? _rightTabControl;
    private PlayLayoutSnapshot _layoutSnapshot = new(
        PlayLayoutContext.Empty(PlayPanelSide.Left),
        PlayLayoutContext.Empty(PlayPanelSide.Right));

    public void SetRightTabControl(TabControl? rightTabControl) =>
        _rightTabControl = rightTabControl;

    public TabItem? GetTabByName(string tabName) =>
        tabName switch
        {
            "Reference" => ReferenceSideTab,
            "Warnings" => WarningsSideTab,
            "State" => StateSideTab,
            _ => null,
        };

    public void ReparentTab(TabItem tab, TabControl target)
    {
        if (tab.Parent is TabControl current && !ReferenceEquals(current, target))
            current.Items.Remove(tab);

        if (!target.Items.Contains(tab))
            target.Items.Add(tab);
    }

    public void ApplyTabPlacementFromSettings()
    {
        if (_bundle is null)
            return;

        EnsureTabsOnLeft();

        foreach (var tabName in new[] { "Reference", "Warnings", "State" })
        {
            var tab = GetTabByName(tabName);
            if (tab is null)
                continue;

            var side = PlayPanelLayoutService.ResolveTabPlacement(_bundle.Metadata.Settings, tabName);
            tab.Visibility = side == PlayPanelSide.Hidden ? Visibility.Collapsed : Visibility.Visible;

            if (side == PlayPanelSide.Right && _rightTabControl is not null)
                ReparentTab(tab, _rightTabControl);
            else
                ReparentTab(tab, PlaySideTabControl);
        }

        if (_rightTabControl is not null)
        {
            _rightTabControl.Visibility = _rightTabControl.Items.Cast<TabItem>()
                .Any(t => t.Visibility == Visibility.Visible)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void EnsureTabsOnLeft()
    {
        foreach (var tabName in new[] { "Reference", "Warnings", "State" })
        {
            var tab = GetTabByName(tabName);
            if (tab is not null)
                ReparentTab(tab, PlaySideTabControl);
        }
    }

    public bool NavigateToPlayTab(string tabName, bool scrollIntoView = true)
    {
        if (_bundle is null)
            return false;

        var side = PlayPanelLayoutService.ResolveTabPlacement(_bundle.Metadata.Settings, tabName);
        if (side == PlayPanelSide.Hidden)
        {
            MessageBox.Show(
                $"The {tabName} tab is hidden in Play settings → Play surface. "
                    + "Set visibility to Left or Right to open it from the play companion.",
                $"{tabName} tab hidden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (tabName.Equals("Notes", StringComparison.OrdinalIgnoreCase))
        {
            ExpandPlayNotesPanelRequested?.Invoke(this, EventArgs.Empty);
            FocusNotesEditor?.Invoke();
            return true;
        }

        if (side == PlayPanelSide.Right)
        {
            ExpandPlayNotesPanelRequested?.Invoke(this, EventArgs.Empty);
            var tab = GetTabByName(tabName);
            if (tab is not null && _rightTabControl is not null)
            {
                ReparentTab(tab, _rightTabControl);
                _rightTabControl.SelectedItem = tab;
            }
        }
        else
        {
            if (IsSidePanelCollapsed)
                ExpandPlaySidePanelRequested?.Invoke(this, EventArgs.Empty);

            var tab = GetTabByName(tabName);
            if (tab is not null)
            {
                ReparentTab(tab, PlaySideTabControl);
                PlaySideTabControl.SelectedItem = tab;
            }
        }

        if (tabName.Equals("Reference", StringComparison.OrdinalIgnoreCase) && scrollIntoView)
            FocusEntityReviewQueueInner(scrollOnly: true);

        return true;
    }

    public AdventurePlayView()
    {
        _suppressNarratorControls = true;
        InitializeComponent();
        _suppressNarratorControls = false;
        WireEntityReferencePanel();
        WarningsList.PreviewMouseRightButtonDown += WarningsList_PreviewMouseRightButtonDown;
        SizeChanged += (_, _) => ApplyLayout(
            PlayLayoutCoordinator.CreateSnapshot(ActualWidth, _layoutSnapshot.Companion.PanelWidth));
        Loaded += (_, _) => ApplyLayout(
            PlayLayoutCoordinator.CreateSnapshot(ActualWidth, _layoutSnapshot.Companion.PanelWidth));
    }

    private void WireEntityReferencePanel()
    {
        EntityReferencePanel.Configure(
            new EntityReferencePanelOptions { EditMode = EntityReferenceEditMode.Modal },
            new EntityReferenceEditCallbacks
            {
                GetPhraseHighlightRules = () => GetPhraseHighlightRules?.Invoke(),
                CommitPhraseHighlightRules = rules => CommitPhraseHighlightRules?.Invoke(rules),
                InsertIntoComposer = text => InsertIntoComposerRequested?.Invoke(this, text),
                OpenSourceManagerAsync = () => OpenSourceManagerAsync?.Invoke() ?? Task.CompletedTask,
                OnBundleReloaded = bundle =>
                {
                    _bundle = bundle;
                    BindStateTable();
                    BindReviewQueue();
                    BindPendingReview();
                    BindWarnings();
                    EntityReferencePanel.LoadBundle(bundle);
                    CanonCommitBar.Bind(bundle);
                },
                OnStatusRefreshRequested = () => PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty),
                OnSourceSyncCompleted = result =>
                {
                    _lastCanonSyncResult = result;
                    if ((result.Synced || result.Staged) && !string.IsNullOrWhiteSpace(result.Summary))
                        ShowCanonSyncNotice(result.Summary!);
                    CanonCommitBar.Bind(_bundle);
                    PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
                },
            });
        EntityReferencePanel.SelectionChanged += (_, _) => UpdateJobButtonStates();
        EntityReferencePanel.EntitiesChanged += (_, _) =>
        {
            BindReviewQueue();
            BindPendingReview();
        };
        EntityReferencePanel.SuggestEntitiesRequested += (_, _) =>
            SuggestEntities_Click(EntityReferencePanel, new RoutedEventArgs());
        EntityReferencePanel.ExpandEntityRequested += (_, row) => _ = ExpandEntityForRowAsync(row);
    }

    private async Task ExpandEntityForRowAsync(EntityReferenceRow row)
    {
        if (_bundle is null || ExpandEntityAsync is null)
            return;

        await RunJobButtonAsync(
            () => ExpandEntityAsync(EntityReferencePanel.CurrentFilter, row.Id),
            () =>
            {
                BindReviewQueue();
                BindPendingReview();
                EntityReferencePanel.RefreshList();
            });
    }

    public Guid? AdventureId => _bundle?.Metadata.Id;

    public bool IsSidePanelCollapsed =>
        _bundle?.Metadata.Settings.PlaySidePanelCollapsed == true;

    private bool _settingsCommitNotifierAttached;

    public void SyncTransportSettingsFromDisk()
    {
        if (_bundle is null)
            return;

        var meta = AdventureStore.ReadMetadataFromDisk(_bundle.Metadata.Id);
        if (meta?.Settings is null)
            return;

        TransportSettingsStore.ApplyToBundle(_bundle, meta.Settings);
    }

    private void EnsureSettingsCommitNotifierAttached()
    {
        if (_settingsCommitNotifierAttached)
            return;

        _settingsCommitNotifierAttached = true;
        AdventureSettingsCommitNotifier.SettingsCommitted += OnAdventureSettingsCommitted;
    }

    private void OnAdventureSettingsCommitted(object? sender, Guid adventureId)
    {
        if (_bundle?.Metadata.Id != adventureId)
            return;

        SyncTransportSettingsFromDisk();
        PlaySettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    public void LoadAdventure(Guid id)
    {
        EnsureSettingsCommitNotifierAttached();
        _bundle = AdventureStore.Load(id);
        if (_bundle is null)
            return;

        AdventureNavigationService.SyncLinkedFields(_bundle);
        var reconcile = ThreadMetadataReconcileService.Reconcile(_bundle);
        var normalized = PlayTurnScopeService.NormalizeIncompleteCaptureTurns(_bundle);
        if (reconcile.Changed || normalized)
            AdventureStore.Save(_bundle);

        FinalizeLegacyPendingTurns();
        TitleBlock.Text = _bundle.Metadata.Title;
        UpdatePreviewPlayerLineFromBootstrap();
        UpdatePlayTabPinUi();
        BindStateTable();
        BindEntityGrid();
        BindReviewQueue();
        BindPendingReview();
        BindWarnings();
        UpdatePlayTabBadges();
        ApplyTabPlacementFromSettings();
        UpdateLinkProjectUi();
        BindNarratorControls();
        UpdateJobButtonStates();
        ApplyLayout(
            PlayLayoutCoordinator.CreateSnapshot(ActualWidth, _layoutSnapshot.Companion.PanelWidth));
        SetShellChromeState(_shellBreadcrumbVisible);
    }

    public void SetShellChromeState(bool shellBreadcrumbVisible)
    {
        _shellBreadcrumbVisible = shellBreadcrumbVisible;
        BackButton.Visibility = shellBreadcrumbVisible ? Visibility.Collapsed : Visibility.Visible;
        if (shellBreadcrumbVisible)
            Grid.SetColumn(TitleBlock, 0);
        else
            Grid.SetColumn(TitleBlock, 1);
        TitleBlock.Margin = shellBreadcrumbVisible ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 8, 0);
        if (_layoutSnapshot.Shell.IsUsable)
            ApplyShellLayout(_layoutSnapshot.Shell);
    }

    private void ApplyPlayTabPlacement() => ApplyTabPlacementFromSettings();

    private void UpdatePlayTabBadges()
    {
        var reviewCount = _bundle?.Entities.ReviewQueue.Count ?? 0;
        var warningCount = _bundle is null
            ? 0
            : ContinuityWarningDismissalService.FilterActive(_bundle.Continuity).Count;
        var pendingCount = _bundle is null ? 0 : PendingReviewService.GetCounts(_bundle).Total;

        SetSideTabHeader(ReferenceSideTab, "Reference", reviewCount > 0 ? reviewCount : null);
        SetSideTabHeader(WarningsSideTab, "Warnings", warningCount > 0 ? warningCount : null);
        SetSideTabHeader(StateSideTab, "State", null);
        UpdatePlaySettingsButtonBadge(pendingCount);
    }

    private void UpdatePlaySettingsButtonBadge(int pendingCount)
    {
        if (pendingCount <= 0)
        {
            PlaySettingsButton.Content = "Play settings…";
            return;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = "Play settings…",
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(CreateBadgeBorder(pendingCount));
        PlaySettingsButton.Content = panel;
    }

    private void SetSideTabHeader(TabItem tab, string title, int? badgeCount)
    {
        if (badgeCount is null or 0)
        {
            tab.Header = title;
            return;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(CreateBadgeBorder(badgeCount.Value));
        tab.Header = panel;
    }

    private Border CreateBadgeBorder(int count)
    {
        var badge = new Border();
        if (TryFindResource("ShellBadgeStyle") is Style badgeStyle)
            badge.Style = badgeStyle;

        badge.Margin = new Thickness(6, 0, 0, 0);
        badge.Child = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White,
        };
        return badge;
    }

    public void ApplyLayout(PlayLayoutSnapshot snapshot)
    {
        _layoutSnapshot = snapshot;

        if (snapshot.Shell.IsUsable)
            ApplyShellLayout(snapshot.Shell);

        if (_bundle is not null)
        {
            ApplyReferenceLayout(ResolveTabContext("Reference"));
            ApplyWarningsLayout(ResolveTabContext("Warnings"));
            ApplyStateLayout(ResolveTabContext("State"));
        }
    }

    /// <summary>Legacy entry point — prefer <see cref="ApplyLayout"/> with a full snapshot.</summary>
    public void UpdateResponsiveLayout(double panelWidth) =>
        ApplyLayout(PlayLayoutCoordinator.CreateSnapshot(panelWidth, _layoutSnapshot.Companion.PanelWidth));

    private PlayLayoutContext ResolveTabContext(string tabName) =>
        _bundle is null
            ? _layoutSnapshot.Shell
            : PlayLayoutCoordinator.ResolveTabContext(_layoutSnapshot, _bundle.Metadata.Settings, tabName);

    private void ApplyShellLayout(PlayLayoutContext context)
    {
        var cap = context.Capabilities;
        RootGrid.Margin = new Thickness(context.Margin);

        BackButton.Content = cap.ShowFullBackLabel ? "← Dashboard" : "←";
        BackButton.Visibility = _shellBreadcrumbVisible ? Visibility.Collapsed : Visibility.Visible;

        PlaySettingsButton.Content = cap.ShowFullPlaySettingsLabel
            ? "Play settings…"
            : cap.ShowIntermediatePlaySettingsLabel
                ? "Settings"
                : "⚙";
        PlaySettingsButton.Padding = cap.ShowFullBackLabel ? new Thickness(10, 4, 10, 4) : new Thickness(8, 4, 8, 4);
        PlaySettingsButton.MinWidth = cap.ShowFullBackLabel ? 0 : 32;

        SourcesButton.Visibility = cap.ShowSourcesButton ? Visibility.Visible : Visibility.Collapsed;
        HeaderMoreMenu.Visibility = Visibility.Visible;

        AiToolsFlyoutMenu.Visibility = cap.UseShellHeaderFlyouts ? Visibility.Visible : Visibility.Collapsed;
        JobActionsPanel.Visibility = cap.UseShellHeaderFlyouts ? Visibility.Collapsed : Visibility.Visible;

        NarratorFlyoutMenu.Visibility = cap.UseShellHeaderFlyouts ? Visibility.Visible : Visibility.Collapsed;
        NarratorControlsPanel.Visibility = cap.UseShellHeaderFlyouts ? Visibility.Collapsed : Visibility.Visible;
        NarratorExpander.Visibility = Visibility.Visible;

        var footerPadding = cap.UseFullFooterLabels
            ? new Thickness(10, 5, 10, 5)
            : new Thickness(8, 5, 8, 5);
        FooterSearchButton.Padding = footerPadding;
        FooterExportButton.Padding = footerPadding;
        MoreActionsButton.Padding = footerPadding;
        FooterSearchButton.Content = cap.UseFullFooterLabels ? "Search…" : "Search";
        FooterExportButton.Content = cap.UseFullFooterLabels ? "Export…" : "Export";
        FooterExportButton.ToolTip = "Save adventure to a file — Markdown, HTML, JSON, or ZIP archive";
        MoreActionsButton.Content = cap.UseCompactFooterMore ? "More…" : "More actions…";

        SessionCockpit.Padding = cap.UseCompactSessionPadding ? new Thickness(6) : new Thickness(8);

        EditWorldButton.Content = cap.UseFullFooterLabels
            ? "Edit in Play settings → World"
            : "Edit world in settings";
    }

    private void ApplyReferenceLayout(PlayLayoutContext context)
    {
        var cap = context.Capabilities;
        ReferenceTabRoot.Margin = cap.UseCompactSessionPadding ? new Thickness(2) : new Thickness(4);
        EntityReferencePanel.ApplyLayout(cap);
    }

    private void ApplyWarningsLayout(PlayLayoutContext context)
    {
        if (_bundle is not null)
            BindWarnings(context);
    }

    private void ApplyStateLayout(PlayLayoutContext context)
    {
        var cap = context.Capabilities;
        StateAllFieldsExpander.Visibility = cap.ShowStateAllFields
            ? Visibility.Visible
            : Visibility.Collapsed;
        StateFieldColumn.Width = new DataGridLength(cap.StateFieldColumnWidth);
        StatePreviewPanel.Columns = cap.UseWideStatePreview ? 2 : 1;
    }

    public void SetSidePanelCollapsed(bool collapsed)
    {
        if (_bundle is null)
            return;

        _bundle.Metadata.Settings.PlaySidePanelCollapsed = collapsed;
    }

    public void SaveConfiguration() => SaveNotesAction?.Invoke();

    public string? GetSelectedEntityName() => EntityReferencePanel.SelectedRow?.Name;

    public string GetPreviewPlayerLineText() => _previewPlayerLine;

    public void SetPreviewPlayerLine(string line) => _previewPlayerLine = line;

    private void FinalizeLegacyPendingTurns()
    {
        if (_bundle is null)
            return;

        var pending = _bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Pending)
            .ToList();
        if (pending.Count == 0)
            return;

        foreach (var turn in pending)
        {
            if (PlayTurnScopeService.IsIncompleteNarratorCapture(turn.NarratorText))
                continue;

            TurnTimelineService.AcceptTurn(turn, turn.NarratorText ?? "");
        }

        AdventureStore.Save(_bundle);
    }

    public void SetSessionLinkDetails(string threadLine, string sourcesLine)
    {
        ThreadStatusBlock.Text = threadLine;
        SourcesStatusBlock.Text = sourcesLine;
        UpdateLinkProjectUi();
    }

    public void SetConnectionSummary(string threadLine, string sourcesLine) =>
        SetSessionLinkDetails(threadLine, sourcesLine);

    public void ShowCanonSyncNotice(string message)
    {
        CanonSyncNoticeText.Text = message;
        CanonSyncNoticeBanner.Visibility = Visibility.Visible;

        _canonSyncNoticeTimer?.Stop();
        _canonSyncNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
        _canonSyncNoticeTimer.Tick += (_, _) => HideCanonSyncNotice();
        _canonSyncNoticeTimer.Start();
    }

    private void HideCanonSyncNotice()
    {
        _canonSyncNoticeTimer?.Stop();
        CanonSyncNoticeBanner.Visibility = Visibility.Collapsed;
    }

    private void DismissCanonSyncNotice_Click(object sender, RoutedEventArgs e) =>
        HideCanonSyncNotice();

    private void ViewLastCanonSyncDiff_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _lastCanonSyncResult is null)
            return;

        var dlg = new EntityChangePlanDiffPreviewDialog(_bundle, _lastCanonSyncResult)
        {
            Owner = Window.GetWindow(this),
        };
        dlg.ShowDialog();
    }

    private async void OpenSourceManagerFromBanner_Click(object sender, RoutedEventArgs e)
    {
        if (OpenSourceManagerAsync is not null)
            await OpenSourceManagerAsync();
        else
            OpenPlaySettings(PlaySettingsTab.Sources);
    }

    private void CanonCommitBar_PlansChanged(object? sender, EventArgs e)
    {
        if (_bundle is not null)
            _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        BindEntityGrid();
        PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CanonInbox_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        if (ProposalReviewService.HasAny(_bundle))
        {
            OpenProposalReviewHub();
            return;
        }

        var dlg = new CanonInboxDialog(_bundle) { Owner = Window.GetWindow(this) };
        dlg.NavigateRequested += (_, item) => NavigateCanonInboxItem(item);
        dlg.ShowDialog();
    }

    private async void NavigateCanonInboxItem(CanonInboxItem item)
    {
        switch (item.Destination)
        {
            case CanonInboxDestination.ReferenceTab:
                PlaySideTabControl.SelectedItem = ReferenceSideTab;
                break;
            case CanonInboxDestination.SourcesSettings:
            case CanonInboxDestination.SourceManager:
                if (OpenSourceManagerAsync is not null)
                    await OpenSourceManagerAsync();
                else
                    OpenPlaySettings(PlaySettingsTab.Sources);
                break;
            case CanonInboxDestination.CommitBar:
                CanonCommitBar.Bind(_bundle);
                break;
            case CanonInboxDestination.JsonImportReview:
                OpenProposalReviewHub(ProposalReviewCategory.JsonImport);
                break;
        }
    }

    private bool _suppressNarratorControls;
    private NarratorOverrideScope _lastNarratorScope = NarratorOverrideScope.Turn;

    private void BindNarratorControls(bool resetSceneProfile = true)
    {
        if (_bundle is null)
            return;

        _suppressNarratorControls = true;
        var scope = GetSelectedNarratorScope();

        NarratorBehaviorPanelBinder.BindSceneProfile(NarratorSceneProfileCombo, _bundle, selectInherit: resetSceneProfile);
        RefreshNarratorParameterCombos(scope);
        _lastNarratorScope = scope;
        UpdateNarratorOverrideChips();
        _suppressNarratorControls = false;
    }

    private NarratorOverrideScope GetSelectedNarratorScope()
    {
        if (NarratorScopeSessionRadio?.IsChecked == true)
            return NarratorOverrideScope.Session;
        if (NarratorScopeAdventureRadio?.IsChecked == true)
            return NarratorOverrideScope.Adventure;
        return NarratorOverrideScope.Turn;
    }

    private void NarratorScope_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressNarratorControls || _bundle is null)
            return;

        var newScope = GetSelectedNarratorScope();
        if (newScope != _lastNarratorScope)
            FlushNarratorParameterCombos(_lastNarratorScope);

        _lastNarratorScope = newScope;

        _suppressNarratorControls = true;
        NarratorBehaviorPanelBinder.SelectSceneProfileInherit(NarratorSceneProfileCombo);
        RefreshNarratorParameterCombos(newScope);
        UpdateNarratorOverrideChips();
        _suppressNarratorControls = false;
    }

    private void FlushNarratorParameterCombos(NarratorOverrideScope scope)
    {
        if (_bundle is null)
            return;

        NarratorControlsService.SaveComboValue(_bundle, ResponseLengthCombo, NarratorParameter.ResponseLength, scope);
        NarratorControlsService.SaveComboValue(_bundle, DetailLevelCombo, NarratorParameter.DetailLevel, scope);
        NarratorControlsService.SaveComboValue(_bundle, ToneQuickCombo, NarratorParameter.Tone, scope);
        NarratorControlsService.SaveComboValue(_bundle, NarrativePacingCombo, NarratorParameter.NarrativePacing, scope);
        NarratorControlsService.SaveComboValue(_bundle, DifficultyQuickCombo, NarratorParameter.Difficulty, scope);
        NarratorControlsService.SaveComboValue(_bundle, ViolenceCombo, NarratorParameter.ViolenceLevel, scope);
        NarratorControlsService.SaveComboValue(_bundle, ConsequenceWeightCombo, NarratorParameter.ConsequenceWeight, scope);
    }

    private void NarratorParameter_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressNarratorControls || _bundle is null)
            return;

        FlushNarratorParameterCombos(GetSelectedNarratorScope());
        UpdateNarratorOverrideChips();
        AdventureStore.Save(_bundle);
    }

    private void NarratorSceneProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNarratorControls || _bundle is null)
            return;

        if (NarratorSceneProfileCombo.SelectedItem is not NarratorPresetComboItem item)
            return;

        var scope = GetSelectedNarratorScope();
        if (item.IsInherit)
        {
            NarratorOverrideResolver.ResetScope(_bundle, scope);
            RefreshNarratorParameterCombos(scope);
            UpdateNarratorOverrideChips();
            AdventureStore.Save(_bundle);
            return;
        }

        if (item.Id is null)
            return;

        NarratorPresetLibrary.ApplySceneProfile(_bundle, item.Id, scope);
        RefreshNarratorParameterCombos(scope);
        UpdateNarratorOverrideChips();
        AdventureStore.Save(_bundle);
    }

    private void RefreshNarratorParameterCombos(NarratorOverrideScope scope)
    {
        if (_bundle is null)
            return;

        NarratorControlsService.PopulateCombo(ResponseLengthCombo, _bundle, NarratorParameter.ResponseLength, scope);
        NarratorControlsService.PopulateCombo(DetailLevelCombo, _bundle, NarratorParameter.DetailLevel, scope);
        NarratorControlsService.PopulateCombo(ToneQuickCombo, _bundle, NarratorParameter.Tone, scope, isEditable: true);
        NarratorControlsService.PopulateCombo(NarrativePacingCombo, _bundle, NarratorParameter.NarrativePacing, scope);
        NarratorControlsService.PopulateCombo(DifficultyQuickCombo, _bundle, NarratorParameter.Difficulty, scope, isEditable: true);
        NarratorControlsService.PopulateCombo(ViolenceCombo, _bundle, NarratorParameter.ViolenceLevel, scope);
        NarratorControlsService.PopulateCombo(ConsequenceWeightCombo, _bundle, NarratorParameter.ConsequenceWeight, scope);
    }

    private void ResetNarratorScope_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        NarratorOverrideResolver.ResetScope(_bundle, GetSelectedNarratorScope());
        BindNarratorControls();
        AdventureStore.Save(_bundle);
    }

    private void NarratorAdvanced_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var dialog = new NarratorAdvancedDialog(_bundle, GetSelectedNarratorScope())
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
        {
            BindNarratorControls();
            AdventureStore.Save(_bundle);
        }
    }

    private void NarratorFlyoutExpand_Click(object sender, RoutedEventArgs e)
    {
        NarratorExpander.IsExpanded = true;
    }

    private void UpdateNarratorOverrideChips()
    {
        if (_bundle is null)
            return;

        var chips = NarratorOverrideResolver.GetActiveOverrideChips(_bundle);
        NarratorOverrideChips.Text = chips.Count == 0
            ? "No active overrides."
            : $"Active: {string.Join(" · ", chips)}";
    }

    private void UpdateLinkProjectUi()
    {
        var showBanner = AdventureProjectBindingService.ShouldShowLinkProjectBanner(_bundle);
        LinkProjectBanner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
        var hasProject = !showBanner;
        HeaderLinkProjectMenuItem.Visibility = showBanner ? Visibility.Collapsed : Visibility.Visible;
        HeaderLinkProjectMenuItem.Header = hasProject ? "Change Project…" : "Link Project…";
        HeaderLinkProjectMenuItem.ToolTip = hasProject
            ? "Switch to a different ChatGPT Project or unlink"
            : "Connect this adventure to a ChatGPT Project";
        LinkProjectButton.Visibility = Visibility.Collapsed;
        ThreadStatusBlock.Cursor = System.Windows.Input.Cursors.Hand;
        ThreadStatusBlock.ToolTip = hasProject
            ? "Click to manage play threads"
            : "Click to manage play threads (link a Project first for new threads)";
    }

    private void LinkProject_Click(object sender, RoutedEventArgs e) =>
        LinkProjectRequested?.Invoke(this, EventArgs.Empty);

    private void ThreadStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ManageThreads_Click(sender, e);

    private void ManageThreads_Click(object sender, RoutedEventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    public void SetSessionError(string message) =>
        ThreadStatusBlock.Text = message;

    public void SetSessionLoading(bool loading, string? message = null)
    {
        if (loading)
        {
            SessionLoadingBlock.Text = message ?? "Preparing play session…";
            SessionLoadingBlock.Visibility = Visibility.Visible;
            JobActionsPanel.IsEnabled = false;
            PlaySettingsButton.IsEnabled = false;
        }
        else
        {
            SessionLoadingBlock.Visibility = Visibility.Collapsed;
            PlaySettingsButton.IsEnabled = true;
            JobActionsPanel.IsEnabled = true;
            UpdateJobButtonStates();
        }
    }

    public void UpdateJobButtonStates()
    {
        if (_bundle is null)
        {
            ProcessLastExchangeButton.IsEnabled = false;
            SuggestMemoriesButton.IsEnabled = false;
            RefreshSummaryButton.IsEnabled = false;
            GenerateCardsButton.IsEnabled = false;
            SourcesButton.IsEnabled = false;
            GenerateRecapButton.IsEnabled = false;
            RunContinuityButton.IsEnabled = false;
            RunContinuityCockpitButton.IsEnabled = false;
            AiToolsFlyoutButton.IsEnabled = false;
            AiToolsExpander.IsEnabled = false;
            return;
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(_bundle.Metadata);
        var hasProject = AdventureProjectBindingService.HasLinkedProject(_bundle);
        var hasExchange = UtilityTranscriptScopeService.ResolveFromLocalLog(_bundle) is not null
                          || UtilityTranscriptScopeService.ResolveFallbackTurn(_bundle) is not null;
        var workerReady = UtilityWorkerCapabilityGate.IsGreen(_bundle);
        var canRunScopedJobs = hasProject && (hasExchange || workerReady);

        var scopedTooltip = hasExchange
            ? "Bundled memories + entities for the latest play exchange"
            : workerReady
                ? "Uses live play thread context via the utility worker lane"
                : "Send a play turn first, or verify the utility worker in Threads";

        ProcessLastExchangeButton.IsEnabled = canRunScopedJobs;
        ProcessLastExchangeButton.ToolTip = scopedTooltip;
        EntityReferencePanel.UpdateSecondaryActionStates(hasProject, canRunScopedJobs);
        SuggestMemoriesButton.IsEnabled = canRunScopedJobs;
        SuggestMemoriesButton.ToolTip = workerReady && !hasExchange
            ? "Propose memories from live play context via utility worker"
            : "Propose memories from the latest logged exchange";
        RefreshSummaryButton.IsEnabled = hasProject;
        GenerateCardsButton.IsEnabled = hasProject;
        SourcesButton.IsEnabled = true;
        GenerateRecapButton.IsEnabled = hasProject;
        RunContinuityButton.IsEnabled = hasProject;
        RunContinuityCockpitButton.IsEnabled = hasProject;

        var aiEnabled = canRunScopedJobs
                        || RefreshSummaryButton.IsEnabled
                        || GenerateCardsButton.IsEnabled
                        || GenerateRecapButton.IsEnabled
                        || RunContinuityCockpitButton.IsEnabled;
        AiToolsFlyoutButton.IsEnabled = aiEnabled;
        AiToolsExpander.IsEnabled = hasProject || workerReady;
    }

    private void BindWarnings()
    {
        BindWarnings(ResolveTabContext("Warnings"));
    }

    private void BindWarnings(PlayLayoutContext context)
    {
        if (_bundle is null)
            return;

        var warnings = ContinuityWarningDismissalService.FilterActive(_bundle.Continuity)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => CreateWarningDisplayItem(w, context.ContentWidth))
            .ToList();

        WarningsList.ItemsSource = warnings;
        WarningsEmptyState.Visibility = warnings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningsList.Visibility = warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (_bundle.Continuity.LastCheckedAt is { } checkedAt)
        {
            WarningsLastCheckedBlock.Text = $"Last checked {FormatRelativeTime(checkedAt)}";
            WarningsLastCheckedBlock.Visibility = Visibility.Visible;
        }
        else
        {
            WarningsLastCheckedBlock.Visibility = Visibility.Collapsed;
        }

        UpdatePlayTabBadges();
    }

    private WarningDisplayItem CreateWarningDisplayItem(ContinuityWarningEntry entry, double contentWidth)
    {
        var (label, brushKey) = entry.Severity.ToLowerInvariant() switch
        {
            "high" or "error" => ("Error", "ErrorSubtleBrush"),
            "info" => ("Info", "AccentSubtleBrush"),
            _ => ("Warning", "WarningSubtleBrush"),
        };

        return new WarningDisplayItem
        {
            Entry = entry,
            Message = entry.Message,
            Source = entry.Source,
            SeverityLabel = label,
            SeverityBrush = TryFindResource(brushKey) as Brush ?? Brushes.Transparent,
            SourceVisibility = PlayResponsiveTiers.ShowWarningSource(contentWidth)
                ? Visibility.Visible
                : Visibility.Collapsed,
        };
    }

    private static string FormatRelativeTime(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalMinutes < 1)
            return "just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours} hr ago";
        if (delta.TotalDays < 7)
            return $"{(int)delta.TotalDays} days ago";
        return when.LocalDateTime.ToString("MMM d, yyyy");
    }

    private void WarningsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is ListBoxItem item)
            WarningsList.SelectedItem = item.DataContext;
    }

    private WarningDisplayItem? ResolveWarningMenuItem(object sender)
    {
        if (SelectedWarningItem is { } selected)
            return selected;

        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: DependencyObject target } })
        {
            var container = ItemsControl.ContainerFromElement(WarningsList, target) as ListBoxItem;
            return container?.DataContext as WarningDisplayItem;
        }

        return null;
    }

    private WarningDisplayItem? SelectedWarningItem => WarningsList.SelectedItem as WarningDisplayItem;

    private void WarningDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var item = ResolveWarningMenuItem(sender);
        if (item is null)
            return;

        ContinuityWarningDismissalService.Dismiss(_bundle.Continuity, item.Entry.Message);
        AdventureStore.Save(_bundle);
        BindWarnings();
    }

    private void WarningOpenInReference_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var item = ResolveWarningMenuItem(sender);
        if (item is null)
            return;

        if (!TryResolveEntityFromWarning(item.Entry.Message, out var filter, out var entityId))
        {
            NavigateToPlayTab("Reference");
            return;
        }

        NavigateToPlayTab("Reference");
        EntityReferencePanel.SelectEntity(filter, entityId);
    }

    private bool TryResolveEntityFromWarning(string message, out string filter, out Guid entityId)
    {
        filter = "Characters";
        entityId = Guid.Empty;
        if (_bundle is null || string.IsNullOrWhiteSpace(message))
            return false;

        foreach (var loc in _bundle.Entities.Locations)
        {
            if (!string.IsNullOrWhiteSpace(loc.Name)
                && message.Contains(loc.Name, StringComparison.OrdinalIgnoreCase))
            {
                filter = "Locations";
                entityId = loc.Id;
                return true;
            }
        }

        foreach (var character in _bundle.Entities.Characters)
        {
            if (!string.IsNullOrWhiteSpace(character.Name)
                && message.Contains(character.Name, StringComparison.OrdinalIgnoreCase))
            {
                filter = "Characters";
                entityId = character.Id;
                return true;
            }
        }

        foreach (var item in _bundle.Entities.Inventory)
        {
            if (!string.IsNullOrWhiteSpace(item.Name)
                && message.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
            {
                filter = "Things";
                entityId = item.Id;
                return true;
            }
        }

        return false;
    }

    private void StateLocationReference_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var locationName = _bundle.State.CurrentLocation?.Trim();
        if (string.IsNullOrWhiteSpace(locationName))
            return;

        var match = _bundle.Entities.Locations.FirstOrDefault(l =>
            l.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;

        NavigateToPlayTab("Reference");
        EntityReferencePanel.SelectEntity("Locations", match.Id);
    }

    public void SetPlayTabPinStatus(bool pinned, string? tabTitle)
    {
        PlayTabStatusBlock.Text = pinned
            ? $"Play tab: {tabTitle ?? "ChatGPT tab"}"
            : "Play tab: not linked — open Play settings → Session before Send.";
    }

    private void UpdatePlayTabPinUi()
    {
        if (_bundle is null)
            return;

        SetPlayTabPinStatus(
            !string.IsNullOrWhiteSpace(_bundle.Metadata.PinnedPlayTabKey),
            _bundle.Metadata.PinnedPlayTabTitle);
    }

    private void UpdatePreviewPlayerLineFromBootstrap()
    {
        if (_bundle is null)
            return;

        if (!string.IsNullOrWhiteSpace(_previewPlayerLine))
            return;

        if (AdventureBootstrapService.IsFreshAdventure(_bundle)
            && _bundle.Metadata.Settings.OfferStartOnPlay)
        {
            _previewPlayerLine = AdventureBootstrapService.BuildStartPlayerDirective(_bundle);
        }
    }

    private void BindStateTable()
    {
        if (_bundle is null)
            return;

        var rows = StateTableHelper.BuildRows(_bundle);
        StateGrid.ItemsSource = rows;
        var summary = TruncatePreview(FindStateValue(rows, "Rolling summary"));
        var location = TruncatePreview(FindStateValue(rows, "Location", "Scene location"));
        var objectives = TruncatePreview(FindStateValue(rows, "Objectives"));
        StateSummaryPreview.Text = summary;
        StateLocationPreview.Text = location;
        StateObjectivesPreview.Text = objectives;

        var allUnset = summary == "(not set)" && location == "(not set)" && objectives == "(not set)";
        StateEmptyStateCard.Visibility = allUnset ? Visibility.Visible : Visibility.Collapsed;

        var locationName = _bundle.State.CurrentLocation?.Trim();
        StateLocationReferenceButton.Visibility =
            !string.IsNullOrWhiteSpace(locationName)
            && _bundle.Entities.Locations.Any(l =>
                l.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                ? Visibility.Visible
                : Visibility.Collapsed;
        StateLocationReferenceMenuItem.Visibility = StateLocationReferenceButton.Visibility;

        if (_bundle.Metadata.LastPlayedAt != default)
        {
            StateLastUpdatedBlock.Text = $"Last updated {FormatRelativeTime(_bundle.Metadata.LastPlayedAt)}";
            StateLastUpdatedBlock.Visibility = Visibility.Visible;
        }
        else
        {
            StateLastUpdatedBlock.Visibility = Visibility.Collapsed;
        }
    }

    private static string FindStateValue(IReadOnlyList<StateTableRow> rows, params string[] fields)
    {
        foreach (var field in fields)
        {
            var row = rows.FirstOrDefault(r => r.Field.Equals(field, StringComparison.OrdinalIgnoreCase));
            if (row is not null && !string.IsNullOrWhiteSpace(row.Value))
                return row.Value;
        }

        return "(not set)";
    }

    private static string TruncatePreview(string value) =>
        value.Length <= 220 ? value : value[..217] + "…";

    private void BindEntityGrid()
    {
        if (_bundle is null)
            return;

        EntityReferencePanel.LoadBundle(_bundle);
        CanonCommitBar.Bind(_bundle);
    }

    private PlayPromptInjectionDialog? _openPlaySettingsDialog;

    public void RefreshAfterGenerationJob()
    {
        if (_bundle is null)
            return;

        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        if (_bundle is null)
            return;

        BindReviewQueue();
        BindPendingReview();
        _openPlaySettingsDialog?.ReloadBundleFromStore();
        _openPlaySettingsDialog?.RefreshUtilityWorkerStatusFromDisk();
        _openPlaySettingsDialog?.RefreshReviewPanels();
        UpdateJobButtonStates();
    }

    public void RefreshUtilityWorkerStatusFromDisk()
    {
        if (_bundle is null)
            return;

        AdventureStore.SyncUtilityWorkerCapabilitiesFromDisk(_bundle);
        _openPlaySettingsDialog?.RefreshUtilityWorkerStatusFromDisk();
        UpdateJobButtonStates();
    }

    private void BindPendingReview()
    {
        if (_bundle is null)
            return;

        var counts = PendingReviewService.GetCounts(_bundle);
        var visible = counts.Total > 0;
        PendingReviewBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PendingReviewText.Text = visible
            ? PendingReviewService.FormatSummaryLine(counts)
            : "";
        UpdatePlayTabBadges();
    }

    private void BindReviewQueue()
    {
        if (_bundle is null)
            return;

        var queue = _bundle.Entities.ReviewQueue;
        ReviewQueueBanner.Visibility = queue.Count > 0 && _showReferenceReviewQueue
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReviewQueueList.ItemsSource = queue
            .Select(q => new ReviewQueueListItem(q))
            .ToList();
        if (ReviewQueueList.Items.Count > 0)
            ReviewQueueList.SelectedIndex = 0;
        UpdatePlayTabBadges();
    }

    private EntityReviewItem? SelectedReviewItem =>
        (ReviewQueueList.SelectedItem as ReviewQueueListItem)?.Item;

    private void AcceptReviewItem_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedReviewItem is not { } item)
            return;

        if (EntityExtractionService.ApplyAcceptedReviewItem(_bundle.Entities, item))
            _bundle.Entities.ReviewQueue.Remove(item);

        AdventureStore.Save(_bundle);
        TryPromptCanonReconcile(new CanonEditContext
        {
            Category = MapReviewEntityCategory(item.EntityType),
            IsReviewAccept = true,
        });
        BindEntityGrid();
        BindReviewQueue();
    }

    private async void SuggestEntities_Click(object sender, RoutedEventArgs e) =>
        await RunJobButtonAsync(SuggestEntitiesAsync, () =>
        {
            BindReviewQueue();
            BindPendingReview();
        });

    private async void SuggestMemories_Click(object sender, RoutedEventArgs e) =>
        await RunJobButtonAsync(SuggestMemoriesAsync, RefreshAfterGenerationJob);

    private async void RefreshSummary_Click(object sender, RoutedEventArgs e) =>
        await RunJobButtonAsync(RefreshSummaryAsync, RefreshAfterGenerationJob);

    private async void GenerateCards_Click(object sender, RoutedEventArgs e) =>
        await RunJobButtonAsync(GenerateCardsAsync, RefreshAfterGenerationJob);

    private async void Sources_Click(object sender, RoutedEventArgs e) =>
        await OpenSourceManagerOrFallbackAsync();

    private async void SourcesStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_bundle is not null
            && CanonReconciliationService.HasUnresolvedDrift(_bundle)
            && MessageBox.Show(
                Window.GetWindow(this),
                "Local JSON and sources/ are out of sync.\n\n"
                + "Sync now to rewrite sources/*.md from current JSON?\n"
                + "(Use Source Manager if you need to pull from sources instead.)",
                "Repair source files",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var result = EntityEditSourceSyncService.RepairFromJson(_bundle);
            AdventureStore.Save(_bundle);
            if (result.Synced && !string.IsNullOrWhiteSpace(result.Summary))
                ShowCanonSyncNotice(result.Summary!);
            PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
            _bundle = AdventureStore.Load(_bundle.Metadata.Id);
            BindEntityGrid();
            return;
        }

        await OpenSourceManagerOrFallbackAsync();
    }

    private async Task OpenSourceManagerOrFallbackAsync()
    {
        if (OpenSourceManagerAsync is not null)
            await OpenSourceManagerAsync();
        else
            OpenPlaySettings(PlaySettingsTab.Sources);
    }

    private void GenerateRecap_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var text = RecapFormatter.Format(_bundle, RecapDisplayStyle.Brief);
        var dlg = new RecapDialog(text) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private async void ProcessLastExchange_Click(object sender, RoutedEventArgs e) =>
        await RunJobButtonAsync(
            () => ProcessLastExchangeAsync?.Invoke(false) ?? Task.CompletedTask,
            RefreshAfterGenerationJob);

    private async void RunContinuityCheck_Click(object sender, RoutedEventArgs e) =>
        await RunJobButtonAsync(RunContinuityCheckAsync, BindWarnings);

    private async Task RunJobButtonAsync(Func<Task>? action, Action? refresh = null)
    {
        if (action is null)
            return;

        UpdateJobButtonStates();
        try
        {
            await action();
            if (_bundle is not null)
            {
                _bundle = AdventureStore.Load(_bundle.Metadata.Id);
                refresh?.Invoke();
            }
        }
        finally
        {
            UpdateJobButtonStates();
        }
    }

    private void DismissReviewItem_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedReviewItem is not { } item)
            return;

        _bundle.Entities.ReviewQueue.Remove(item);
        AdventureStore.Save(_bundle);
        BindReviewQueue();
    }

    private void EditReviewItem_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedReviewItem is not { } item)
            return;

        if (EntityExtractionService.ApplyAcceptedReviewItem(_bundle.Entities, item))
            _bundle.Entities.ReviewQueue.Remove(item);

        AdventureStore.Save(_bundle);
        TryPromptCanonReconcile(new CanonEditContext
        {
            Category = MapReviewEntityCategory(item.EntityType),
            IsReviewAccept = true,
        });
        BindEntityGrid();
        BindReviewQueue();

        var last = _bundle.Entities.Characters.LastOrDefault()
                   ?? (object?)_bundle.Entities.Locations.LastOrDefault()
                   ?? _bundle.Entities.Quests.LastOrDefault();
        if (last is CharacterEntry character)
        {
            EntityReferencePanel.SelectEntity("Characters", character.Id);
            EntityReferencePanel.TryOpenEditor(EntityReferencePanel.SelectedRow);
        }
        else if (last is LocationEntry location)
        {
            EntityReferencePanel.SelectEntity("Locations", location.Id);
            EntityReferencePanel.TryOpenEditor(EntityReferencePanel.SelectedRow);
        }
        else if (last is QuestEntry quest)
        {
            EntityReferencePanel.SelectEntity("Quests", quest.Id);
            EntityReferencePanel.TryOpenEditor(EntityReferencePanel.SelectedRow);
        }
    }

    private void OpenSessionSettings_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.Session);

    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.PlacementTarget = (Button)sender;
            menu.IsOpen = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void PlaySettings_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.NextSend);

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var owner = Window.GetWindow(this);
        var dlg = new AdventureRenameDialog(_bundle.Metadata.Title)
        {
            Owner = owner,
        };
        if (dlg.ShowDialog() != true)
            return;

        if (!AdventureRenameService.TryRename(_bundle, dlg.NewTitle, out var error))
        {
            MessageBox.Show(owner, error ?? "Could not rename adventure.", "Rename adventure",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TitleBlock.Text = _bundle.Metadata.Title;
        TitleRenamed?.Invoke(this, _bundle.Metadata.Id);
    }

    private void EditWorldInSettings_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.World);

    public void OpenPublishSourcesSettings() =>
        OpenPlaySettings(PlaySettingsTab.Sources);

    public void OpenPlaySettings(PlaySettingsTab tab)
    {
        if (_bundle is null)
            return;

        SaveNotesAction?.Invoke();
        var fresh = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
        var dlg = new PlayPromptInjectionDialog(fresh, _previewPlayerLine, tab)
        {
            Owner = Window.GetWindow(this),
        };
        WirePlaySettingsDialog(dlg);
        _openPlaySettingsDialog = dlg;
        dlg.Closed += (_, _) => _openPlaySettingsDialog = null;
        if (dlg.ShowDialog() == true)
        {
            _previewPlayerLine = dlg.PreviewPlayerLine;
            LoadAdventure(_bundle.Metadata.Id);
            PlaySettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReviewProposals_Click(object sender, RoutedEventArgs e) =>
        OpenProposalReviewHub();

    public void OpenProposalReviewHub(ProposalReviewCategory? focusCategory = null)
    {
        if (_bundle is null)
            return;

        var fresh = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
        var dlg = new ProposalReviewHubDialog(fresh, focusCategory)
        {
            Owner = _openPlaySettingsDialog ?? Window.GetWindow(this),
        };
        dlg.EntityAccepted += (_, item) =>
            TryPromptCanonReconcile(new CanonEditContext
            {
                Category = MapReviewEntityCategory(item.EntityType),
                IsReviewAccept = true,
            });
        dlg.ItemsChanged += (_, _) =>
        {
            if (_bundle is null)
                return;

            _bundle = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
            BindEntityGrid();
            BindReviewQueue();
            BindPendingReview();
            BindWarnings();
            _openPlaySettingsDialog?.ReloadBundleFromStore();
            _openPlaySettingsDialog?.RefreshReviewPanels();
            PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
        };

        dlg.ShowDialog();
        if (dlg.ChangesSaved)
        {
            _bundle = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
            BindEntityGrid();
            BindReviewQueue();
            BindPendingReview();
            BindWarnings();
            UpdateJobButtonStates();
        }
    }

    public void TryOpenProposalReviewHubAfterJob(string jobId, int proposalCount)
    {
        if (_bundle is null || proposalCount <= 0)
            return;

        if (string.Equals(jobId, GenerationJobId.ProcessTurn, StringComparison.OrdinalIgnoreCase))
            return;

        OpenProposalReviewHub(ProposalReviewService.ResolveCategoryForJob(jobId));
    }

    private void OpenPlaySettingsForFirstPending()
    {
        if (_bundle is null)
            return;

        OpenProposalReviewHub(ProposalReviewService.ListCategories(_bundle).FirstOrDefault()?.Category);
    }

    private void FocusReferenceTab() => FocusEntityReviewQueue(scrollOnly: false);

    private void FocusEntityReviewQueue(bool scrollOnly = true)
    {
        if (_bundle is null || _bundle.Entities.ReviewQueue.Count == 0)
            return;

        if (!NavigateToPlayTab("Reference", scrollIntoView: false))
            return;

        FocusEntityReviewQueueInner(scrollOnly);
    }

    private void FocusEntityReviewQueueInner(bool scrollOnly)
    {
        _showReferenceReviewQueue = true;
        BindReviewQueue();
        if (ReviewQueueList.Items.Count > 0)
        {
            ReviewQueueList.SelectedIndex = 0;
            ReviewQueueList.ScrollIntoView(ReviewQueueList.SelectedItem);
        }

        if (scrollOnly)
            ReviewQueueBanner.BringIntoView();
    }

    private void TryPromptCanonReconcile(CanonEditContext context)
    {
        if (_bundle is null)
            return;

        var adventureId = _bundle.Metadata.Id;
        var owner = Window.GetWindow(this);
        var phraseRules = GetPhraseHighlightRules?.Invoke();
        var syncResult = EntityEditSourceSyncService.TrySyncAfterEntityEdit(_bundle, context, phraseRules);
        AdventureStore.Save(_bundle);

        if (syncResult.Synced && !string.IsNullOrWhiteSpace(syncResult.Summary))
            ShowCanonSyncNotice(syncResult.Summary!);

        CanonReconcileResult? result = null;
        if (syncResult.RequiresManualReconcile)
        {
            result = CanonReconciliationPromptService.TryPromptAfterSave(
                owner,
                _bundle,
                context,
                phraseRules,
                OpenSourceManagerAsync);
        }

        if (result is CanonReconcileResult.Pushed or CanonReconcileResult.Pulled)
        {
            _bundle = AdventureStore.Load(adventureId);
            if (_bundle is null)
                return;

            BindStateTable();
            BindReviewQueue();
            BindPendingReview();
            BindWarnings();
        }
        else if (syncResult.Synced)
        {
            _bundle = AdventureStore.Load(adventureId);
        }

        BindEntityGrid();

        if (result is not null
            || syncResult.Synced
            || (_bundle is not null && CanonReconciliationService.HasUnresolvedDrift(_bundle))
            || (_bundle is not null && CanonReconciliationService.HasPendingNotify(_bundle)))
        {
            PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string MapReviewEntityCategory(string? entityType) =>
        EntityTypeNormalizer.Normalize(entityType) switch
        {
            "person" => "Characters",
            "place" => "Locations",
            "concept" => "Concepts",
            "quest" => "Quests",
            "faction" => "Factions",
            _ => "Characters",
        };

    public void WirePlaySettingsDialog(PlayPromptInjectionDialog dlg)
    {
        dlg.OpenThreadsHub = () => ManageThreadsRequested?.Invoke(this, EventArgs.Empty);
        dlg.ResolvePreviewComposerText = ResolvePreviewComposerText;
        dlg.ResolvePreviewAttachmentContext = ResolvePreviewAttachmentContext;
        dlg.ProbeSourcesAsync = ProbeSourcesAsync;
        dlg.ProbeSourceFileAsync = ProbeSourceFileAsync;
        dlg.OpenApiSyncDiagnosticsAsync = OpenApiSyncDiagnosticsAsync;
        dlg.SynthesizeSourceAsync = SynthesizeSourceAsync;
        dlg.RefreshSourcesStatusAsync = RefreshSourcesStatusAsync;
        dlg.ReconcileDuplicatesAsync = ReconcileDuplicatesAsync;
        dlg.SetSessionLinkDetails(ThreadStatusBlock.Text, SourcesStatusBlock.Text);
        dlg.StartNewPlayThreadAsync = request => StartNewPlayThreadAsync?.Invoke(request) ?? Task.CompletedTask;
        dlg.OpenPlayHandoffDialog = () => OpenPlayHandoffWizard();
        dlg.OpenProposalReviewHub = category => OpenProposalReviewHub(category);
        dlg.DraftNewProjectChatAsync = () => DraftNewProjectChatAsync?.Invoke() ?? Task.CompletedTask;
        dlg.CancelProjectChatDraft = () => CancelProjectChatDraft?.Invoke();
        dlg.RunSourceEditJobAsync = (prompt, _) => RunSourceEditJobAsync?.Invoke(prompt) ?? Task.CompletedTask;
        dlg.ListThreadFilesAsync = () => ListThreadFilesAsync?.Invoke() ?? Task.FromResult<IReadOnlyList<ConversationFileRef>>([]);
        dlg.DownloadThreadFileAsync = file =>
            DownloadThreadFileAsync?.Invoke(file) ?? Task.FromResult(Array.Empty<byte>());
        dlg.OpenProjectSettingsAsync = OpenProjectSettingsAsync;
        dlg.PushInstructionsNowAsync = SyncInstructionsAsync;
        dlg.RefreshSummaryAsync = RefreshSummaryAsync;
        dlg.SuggestMemoriesAsync = SuggestMemoriesAsync;
        dlg.GenerateCardsAsync = GenerateCardsAsync;
        dlg.ExpandStoryCardAsync = cardId => ExpandStoryCardAsync?.Invoke(cardId) ?? Task.CompletedTask;
        dlg.SyncInstructionsAsync = SyncInstructionsAsync;
        dlg.PreviewLiveStoryContextAsync = PreviewLiveStoryContextAsync;
        dlg.PinPlayTabRequested += (_, _) => PinPlayTabRequested?.Invoke(this, EventArgs.Empty);
        dlg.OpenPinnedPlayTabRequested += (_, _) => OpenPinnedPlayTabRequested?.Invoke(this, EventArgs.Empty);
        dlg.ClearPlayTabPinRequested += (_, _) => ClearPlayTabPinRequested?.Invoke(this, EventArgs.Empty);
        dlg.ReviewQueueChanged += (_, _) =>
        {
            if (_bundle is null)
                return;

            var reloaded = AdventureStore.Load(_bundle.Metadata.Id);
            if (reloaded is null)
                return;

            _bundle = reloaded;
            BindPendingReview();
            BindReviewQueue();
            BindEntityGrid();
        };

        dlg.TransportSettingsCommitted += (_, _) =>
        {
            SyncTransportSettingsFromDisk();
            PlaySettingsSaved?.Invoke(this, EventArgs.Empty);
        };

        if (_bundle is not null)
        {
            var bundle = _bundle;
            void RefreshDialogSessionStatus()
            {
                var reloaded = AdventureStore.Load(bundle.Metadata.Id);
                if (reloaded is null)
                    return;

                dlg.UpdateSessionStatusUi();
                var thread = ThreadStatusBlock.Text;
                var sources = SourcesStatusBlock.Text;
                if (!string.IsNullOrWhiteSpace(thread))
                    dlg.SetSessionLinkDetails(thread, sources);
            }

            dlg.PinPlayTabRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.OpenPinnedPlayTabRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.ClearPlayTabPinRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.StartNewPlayThreadAsync = async request =>
            {
                if (StartNewPlayThreadAsync is not null)
                    await StartNewPlayThreadAsync(request);
                RefreshDialogSessionStatus();
            };
        }
    }

    private void Branch_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var last = _bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted).OrderByDescending(t => t.Index).FirstOrDefault();
        if (last is null)
            return;

        var name = _bundle.Metadata.Title + " (branch)";
        var br = TurnTimelineService.BranchFrom(_bundle, last.Index, name);
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Created branch: {br.Metadata.Title}\n\nOpen the new branch now?",
            "Branch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            BranchCreated?.Invoke(this, br.Metadata.Id);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var dlg = new SaveFileDialog { Filter = "Markdown|*.md|Plain text|*.txt|HTML|*.html|JSON|*.json|Archive|*.zip" };
        if (dlg.ShowDialog() != true)
            return;

        if (dlg.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ExportService.ExportJsonArchive(_bundle, dlg.FileName);
        else if (dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(dlg.FileName, ExportService.ExportPlainText(_bundle, polishedOnly: true));
        else if (dlg.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(dlg.FileName, ExportService.ExportHtml(_bundle, polishedOnly: true));
        else if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(dlg.FileName, ExportService.ExportFullJson(_bundle));
        else
            File.WriteAllText(dlg.FileName, ExportService.ExportStoryMarkdown(_bundle, polishedOnly: true));
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        new SearchDialog(_bundle) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Roll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var dlg = new RandomTableDialog(_bundle) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.LastRoll))
            return;

        if (dlg.AppendToComposer)
            RollIntoPlayerLineRequested?.Invoke(this, dlg.LastRoll);
        else
            ReplacePlayerLineRequested?.Invoke(this, dlg.LastRoll);
    }

    private void PlayHandoff_Click(object sender, RoutedEventArgs e) =>
        OpenPlayHandoffWizard();

    private async void StartNarrativeFromSources_Click(object sender, RoutedEventArgs e)
    {
        if (StartNewPlayThreadAsync is null)
            return;

        await StartNewPlayThreadAsync(new PlayThreadStartRequest { Kind = PlayThreadStartKind.FreshStart });
    }

    private void SyncFromThread_Click(object sender, RoutedEventArgs e)
    {
        if (PromptThreadLogSyncAsync is null)
            return;

        _ = PromptThreadLogSyncAsync();
    }

    public void OpenPlayHandoffWizard()
    {
        if (_bundle is null)
            return;

        var snapshot = PlayHandoffService.CaptureSnapshot(_bundle);
        var checkpoint = PlayHandoffService.BuildCheckpoint(_bundle, snapshot, new PlayHandoffOptions());
        new PlayHandoffDialog(_bundle, snapshot, checkpoint)
        {
            Owner = Window.GetWindow(this),
            StartNewPlayThreadAsync = request => StartNewPlayThreadAsync?.Invoke(request) ?? Task.CompletedTask,
            RollbackHandoffAsync = () =>
            {
                var id = _bundle.Metadata.Id;
                var reloaded = AdventureStore.Load(id);
                if (reloaded is not null && PlayHandoffService.TryRollbackPendingHandoff(reloaded))
                {
                    LoadAdventure(id);
                    PlayStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
                }

                return Task.CompletedTask;
            },
        }.ShowDialog();
    }

    private void ContinueDesign_Click(object sender, RoutedEventArgs e)
    {
        if (ContinueDesignAsync is null)
            return;

        _ = ContinueDesignAsync();
    }

    private sealed class WarningDisplayItem
    {
        public required ContinuityWarningEntry Entry { get; init; }

        public string Message { get; init; } = "";

        public string Source { get; init; } = "";

        public string SeverityLabel { get; init; } = "";

        public Brush SeverityBrush { get; init; } = Brushes.Transparent;

        public Visibility SourceVisibility { get; init; } = Visibility.Visible;
    }

    private sealed class ReviewQueueListItem(EntityReviewItem item)
    {
        public EntityReviewItem Item { get; } = item;

        public string DisplayLabel =>
            $"{EntityTypeNormalizer.DisplayLabel(Item.EntityType)}: {SummarizeProposal(Item.ProposedChange)}";

        private static string SummarizeProposal(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "(empty)";

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("name", out var nameEl))
                    return nameEl.GetString() ?? json;
            }
            catch
            {
                /* fall through */
            }

            return json.Length <= 60 ? json : json[..60] + "…";
        }
    }
}
