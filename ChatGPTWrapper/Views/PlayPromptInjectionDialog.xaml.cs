using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog : ShellDialogWindow
{
    public static readonly RoutedUICommand SavePlaySettingsCommand = new(nameof(SavePlaySettingsCommand), nameof(SavePlaySettingsCommand), typeof(PlayPromptInjectionDialog));

    private AdventureBundle _bundle;
    private readonly PlaySettingsEditorSession _playSettingsSession;
    private readonly NarratorSettingsSession _narratorSession;
    private readonly bool _sharedNarratorSession;
    private AdventureSettings _narratorBaselineAtOpen;
    private string _lastMergedText = "";
    private string _lastPreviewMetaLine = "";
    private InjectionPreviewSnapshot? _lastPreviewSnapshot;
    private InjectionSettingsStaging? _previewStaging;
    private bool _suppressInjectionPolicyEvents;
    private bool _suppressMaxPacketSlider;
    private bool _suppressPreviewPlayerLineSync;
    private readonly ObservableCollection<SourcePublishRowViewModel> _sourceRows = [];
    private DebouncedAdventureSaver? _sourceAutosave;
    private bool _playSettingsBinding;
    private string _previewPlayerLineBaseline;
    private DateTimeOffset? _lastPlaySettingsSaveAt;
    private readonly DispatcherTimer _previewDebounce;
    private Point _dragStartPoint;
    private bool _isDraggingSource;

    public Func<string?>? ResolvePreviewComposerText { get; set; }

    public Func<AttachmentContext?>? ResolvePreviewAttachmentContext { get; set; }

    public Func<Task<int>>? ResolveThreadUserTurnCountAsync { get; set; }

    public Func<Task>? SyncSourcesAsync { get; set; }

    public Func<Task>? RefreshSourcesStatusAsync { get; set; }

    public Func<Task>? ReconcileDuplicatesAsync { get; set; }

    public Action? OpenThreadsHub { get; set; }

    public event EventHandler? PinPlayTabRequested;

    public event EventHandler? OpenPinnedPlayTabRequested;

    public event EventHandler? ClearPlayTabPinRequested;

    /// <summary>Raised when a review queue changes so the play surface can refresh badges immediately.</summary>
    public event EventHandler? ReviewQueueChanged;

    public Func<PlayThreadStartRequest?, Task>? StartNewPlayThreadAsync { get; set; }

    public Action? OpenPlayHandoffDialog { get; set; }

    public Action<ProposalReviewCategory?>? OpenProposalReviewHub { get; set; }

    public Func<Task>? DraftNewProjectChatAsync { get; set; }

    public Action? CancelProjectChatDraft { get; set; }

    public Func<string, Task<UtilityStoryContextBuildResult>>? PreviewLiveStoryContextAsync { get; set; }

    public Func<string, string, Task>? RunSourceEditJobAsync { get; set; }

    public Func<Task<IReadOnlyList<ConversationFileRef>>>? ListThreadFilesAsync { get; set; }

    public Func<ConversationFileRef, Task<byte[]>>? DownloadThreadFileAsync { get; set; }

    public Func<Task>? OpenProjectSettingsAsync { get; set; }

    public Func<Task>? PushInstructionsNowAsync { get; set; }

    public Func<Task>? RefreshSummaryAsync { get; set; }

    public Func<Task>? SuggestMemoriesAsync { get; set; }

    public Func<Task>? GenerateCardsAsync { get; set; }

    public Func<Guid, Task>? ExpandStoryCardAsync { get; set; }

    public Func<Task>? SyncInstructionsAsync { get; set; }

    /// <summary>True after play/injection settings were written to disk (Save or OK).</summary>
    public bool PlaySettingsPersisted { get; private set; }

    public event EventHandler? TransportSettingsCommitted;

    private void NotifyTransportSettingsCommitted() =>
        TransportSettingsCommitted?.Invoke(this, EventArgs.Empty);

    public Func<Task>? ProbeSourcesAsync { get; set; }

    public string PreviewPlayerLine { get; private set; } = "";

    public PlayPromptInjectionDialog(
        AdventureBundle bundle,
        string? previewPlayerLine,
        PlaySettingsTab initialTab = PlaySettingsTab.Injection,
        NarratorSettingsSession? narratorSession = null)
    {
        _bundle = bundle;
        _playSettingsSession = PlaySettingsEditorSession.Attach(bundle);
        _playSettingsSession.IsDirty = HasUnsavedPlaySettings;
        _narratorSession = narratorSession ?? NarratorSettingsSession.Attach(bundle);
        _sharedNarratorSession = narratorSession is not null;
        _narratorSession.AutoCommitToDisk = false;
        _narratorBaselineAtOpen = NarratorSettingsSession.CaptureNarratorBaseline(_narratorSession.Bundle.Metadata.Settings);
        UtilityStoryContextSettingsService.EnsureDefaults(_bundle.Metadata);
        _previewPlayerLineBaseline = previewPlayerLine ?? "";
        _suppressStoryContextEvents = true;
        _playSettingsBinding = true;
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(
            SavePlaySettingsCommand,
            (_, _) => SavePlaySettings_Click(null!, null!),
            (_, e) => e.CanExecute = PlaySettingsSaveButton?.IsEnabled == true));
        InputBindings.Add(new KeyBinding(SavePlaySettingsCommand, Key.S, ModifierKeys.Control));
        ApplyPlaySettingsTabOrder();

        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            _ = RefreshMergedPreviewAsync();
        };

        QueueBox.Text = string.Join(Environment.NewLine, bundle.ContinuationQueue);
        PreviewPlayerLine = previewPlayerLine ?? "";
        PreviewPlayerLineBox.Text = PreviewPlayerLine;
        PreviewPlayerLinePanelBox.Text = PreviewPlayerLine;

        var hasPlayHistory = !AdventureBootstrapService.IsFreshAdventure(bundle);
        PreviewNarrativePacketButton.Visibility = Visibility.Visible;
        CopyNarrativePacketButton.Visibility = Visibility.Visible;
        var handoffVisible = hasPlayHistory ? Visibility.Visible : Visibility.Collapsed;
        PreviewHandoffPacketButton.Visibility = handoffVisible;
        CopyHandoffPacketButton.Visibility = handoffVisible;

        BindWorldPanel();
        BindNextSendPanel();
        BindInjectionPolicyPanel();
        BindAdventureSettings();
        BindAutomationPanel();
        BindUtilityDeliveryPanel();
        BindPlaySurfaceSettings();
        BindInjectionNarratorPanel();
        InitializeJobOverrideCombos();
        UpdateSessionStatusUi();
        BindAiActions();
        BindMemoryAndCards();
        BindHistory();
        _sourceAutosave = new DebouncedAdventureSaver(
            () => _bundle,
            at => SourceAutosaveLine.Text = $"Source changes saved automatically at {at.LocalDateTime:t}.",
            save: AdventureStore.SaveSourceManifestOnly);
        Closed += (_, _) =>
        {
            FlushPendingSourceManifest();
            _sourceAutosave?.Dispose();
            _previewDebounce.Stop();
        };

        BindSources();
        RefreshMergedPreview();
        SelectTab(initialTab);
        _playSettingsBinding = false;
        UpdatePlaySettingsSaveUi();
    }

    private void ApplyPlaySettingsTabOrder()
    {
        var ordered = new TabItem[]
        {
            InjectionTab,
            NextSendTab,
            WorldTab,
            AiActionsTab,
            PlaySurfaceTab,
            AdventureSettingsTab,
            SessionTab,
            SourcesTab,
            MemoryCardsTab,
            HistoryTab,
        };

        SettingsTabControl.Items.Clear();
        foreach (var tab in ordered)
            SettingsTabControl.Items.Add(tab);
    }

    public void SelectTab(PlaySettingsTab tab)
    {
        SettingsTabControl.SelectedItem = tab switch
        {
            PlaySettingsTab.Injection => InjectionTab,
            PlaySettingsTab.NextSend => NextSendTab,
            PlaySettingsTab.World => WorldTab,
            PlaySettingsTab.Session => SessionTab,
            PlaySettingsTab.AiTools => AiActionsTab,
            PlaySettingsTab.PlaySurface => PlaySurfaceTab,
            PlaySettingsTab.Settings => AdventureSettingsTab,
            PlaySettingsTab.Sources => SourcesTab,
            PlaySettingsTab.MemoryCards => MemoryCardsTab,
            _ => InjectionTab,
        };
    }

    public void UpdateSessionStatusUi()
    {
        AdventureThreadRegistryService.EnsureMigrated(_bundle);
        ThreadStatusBlock.Text = AdventureThreadRegistryService.FormatConnectionSummary(_bundle);
    }

    public void SetSessionLinkDetails(string threadLine, string sourcesLine)
    {
        ThreadStatusBlock.Text = threadLine;
        SourcesStatusBlock.Text = sourcesLine;
    }

    private string? _selectedAiActionJobId;
    private bool _suppressStoryContextEvents;

    private void BindAiActions()
    {
        InitializeStoryContextCombos();
        AiActionsList.ItemsSource = GenerationJobGuideService.EditableUtilityJobIds
            .Select(jobId => new AiActionRowViewModel(jobId))
            .ToList();
        if (AiActionsList.Items.Count > 0)
            AiActionsList.SelectedIndex = 0;
    }

    private void InitializeStoryContextCombos()
    {
        if (StoryContextSourceCombo.Items.Count > 0)
            return;

        StoryContextSourceCombo.ItemsSource = Enum.GetValues<UtilityStorySource>();
        StoryContextFormatCombo.ItemsSource = UtilityStoryContextSettingsNormalizer.LayoutFormats;
        StoryContextTrimCombo.ItemsSource = Enum.GetValues<UtilityTrimStrategy>();
        StoryContextAnchorCombo.ItemsSource = Enum.GetValues<UtilityLookbackAnchor>();
    }

    private void AiActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count > 0 && _selectedAiActionJobId is not null)
            FlushCurrentAiActionEdits();

        if (AiActionsList.SelectedItem is not AiActionRowViewModel row)
            return;

        _selectedAiActionJobId = row.JobId;
        AiActionTitleBlock.Text = row.DisplayLabel;
        AiActionInstructionBox.Text = GenerationJobGuideService.ResolveInstructionBody(_bundle, row.JobId);
        UpdateAiActionStatus(row.JobId);
        BindJobOverridePanel(row.JobId);
        BindStoryContextPanel(row.JobId);
    }

    private void AiActionInstructionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return;

        UpdateAiActionStatus(_selectedAiActionJobId);
        MarkPlaySettingsDirty();
    }

    private void BindStoryContextPanel(string jobId, UtilityStoryContextSettings? settingsOverride = null)
    {
        _suppressStoryContextEvents = true;
        try
        {
            var hasOverride = settingsOverride is null && UtilityStoryContextSettingsService.HasJobOverride(_bundle, jobId);
            StoryContextPerJobOverrideCheck.IsChecked = settingsOverride is null && hasOverride;
            var settings = settingsOverride ?? UtilityStoryContextSettingsService.Resolve(_bundle, jobId);
            StoryContextSourceCombo.SelectedItem = settings.Source;
            StoryContextMaxTurnsBox.Text = settings.MaxTurnPairs.ToString();
            StoryContextSkipNewestBox.Text = settings.SkipNewestTurnPairs.ToString();
            StoryContextMinTurnsBox.Text = settings.MinTurnPairs.ToString();
            StoryContextMaxTranscriptCharsBox.Text = settings.MaxTranscriptChars.ToString();
            StoryContextAnchorCombo.SelectedItem = settings.LookbackAnchor;
            StoryContextAnchorTurnIndexBox.Text = settings.AnchorTurnIndex.ToString();
            StoryContextMaxCharsBox.Text = settings.MaxContextChars.ToString();
            StoryContextFormatCombo.SelectedItem = settings.Format;
            StoryContextTrimCombo.SelectedItem = settings.Trim;
            StoryContextIncludePlayerCheck.IsChecked = settings.IncludePlayerMessages;
            StoryContextIncludeNarratorCheck.IsChecked = settings.IncludeNarratorMessages;
            StoryContextIncludePendingLocalCheck.IsChecked = settings.IncludePendingLocalTurns;
            StoryContextExcludeIncompleteCheck.IsChecked = settings.ExcludeIncompleteTrailingPair;
            StoryContextStripEmptyCheck.IsChecked = settings.StripEmptyTurnPairs;
            StoryContextMaxCharsPerPairBox.Text = settings.MaxCharsPerTurnPair.ToString();
            StoryContextOmitRedundantCheck.IsChecked = settings.OmitRedundantJobTurnSlices;
            StoryContextIncludeSummaryCheck.IsChecked = settings.IncludeRollingSummary;
            StoryContextIncludeStateCheck.IsChecked = settings.IncludeState;
            StoryContextIncludeMemoryCheck.IsChecked = settings.IncludePinnedMemory;
            StoryContextIncludeEntitiesCheck.IsChecked = settings.IncludeEntityIndex;
            StoryContextIncludeScenarioCheck.IsChecked = settings.IncludeScenarioExcerpt;
            StoryContextDirectionBox.Text = settings.DirectionPreamble ?? "";
            StoryContextPreviewMeta.Text = settingsOverride is null
                ? hasOverride
                    ? "Per-action override active."
                    : "Using adventure-wide story context defaults."
                : "Using adventure-wide story context defaults.";
        }
        finally
        {
            _suppressStoryContextEvents = false;
        }
    }

    private UtilityStoryContextSettings ReadStoryContextFromForm()
    {
        var settings = new UtilityStoryContextSettings();
        if (StoryContextSourceCombo.SelectedItem is UtilityStorySource source)
            settings.Source = source;
        if (int.TryParse(StoryContextMaxTurnsBox.Text, out var turns))
            settings.MaxTurnPairs = Math.Max(1, turns);
        if (int.TryParse(StoryContextSkipNewestBox.Text, out var skipNewest))
            settings.SkipNewestTurnPairs = Math.Max(0, skipNewest);
        if (int.TryParse(StoryContextMinTurnsBox.Text, out var minTurns))
            settings.MinTurnPairs = Math.Max(0, minTurns);
        if (int.TryParse(StoryContextMaxTranscriptCharsBox.Text, out var maxTranscript))
            settings.MaxTranscriptChars = Math.Max(0, maxTranscript);
        if (StoryContextAnchorCombo.SelectedItem is UtilityLookbackAnchor anchor)
            settings.LookbackAnchor = anchor;
        if (int.TryParse(StoryContextAnchorTurnIndexBox.Text, out var anchorIndex))
            settings.AnchorTurnIndex = Math.Max(0, anchorIndex);
        if (int.TryParse(StoryContextMaxCharsBox.Text, out var chars))
            settings.MaxContextChars = Math.Max(500, chars);
        if (StoryContextFormatCombo.SelectedItem is UtilityTranscriptFormat format)
            settings.Format = format;
        if (StoryContextTrimCombo.SelectedItem is UtilityTrimStrategy trim)
            settings.Trim = trim;
        settings.IncludePlayerMessages = StoryContextIncludePlayerCheck.IsChecked == true;
        settings.IncludeNarratorMessages = StoryContextIncludeNarratorCheck.IsChecked == true;
        settings.IncludePendingLocalTurns = StoryContextIncludePendingLocalCheck.IsChecked == true;
        settings.ExcludeIncompleteTrailingPair = StoryContextExcludeIncompleteCheck.IsChecked == true;
        settings.StripEmptyTurnPairs = StoryContextStripEmptyCheck.IsChecked == true;
        if (int.TryParse(StoryContextMaxCharsPerPairBox.Text, out var maxPerPair))
            settings.MaxCharsPerTurnPair = Math.Max(0, maxPerPair);
        settings.OmitRedundantJobTurnSlices = StoryContextOmitRedundantCheck.IsChecked == true;
        settings.IncludeRollingSummary = StoryContextIncludeSummaryCheck.IsChecked == true;
        settings.IncludeState = StoryContextIncludeStateCheck.IsChecked == true;
        settings.IncludePinnedMemory = StoryContextIncludeMemoryCheck.IsChecked == true;
        settings.IncludeEntityIndex = StoryContextIncludeEntitiesCheck.IsChecked == true;
        settings.IncludeScenarioExcerpt = StoryContextIncludeScenarioCheck.IsChecked == true;
        settings.DirectionPreamble = string.IsNullOrWhiteSpace(StoryContextDirectionBox.Text)
            ? null
            : StoryContextDirectionBox.Text.Trim();
        return settings;
    }

    private void SaveStoryContextSettings() => SaveStoryContextSettingsTo(_bundle);

    private void StoryContextSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressStoryContextEvents || StoryContextPreviewMeta is null)
            return;

        StoryContextPreviewMeta.Text = StoryContextPerJobOverrideCheck.IsChecked == true
            ? "Per-action override active."
            : "Using adventure-wide story context defaults.";
        MarkPlaySettingsDirty();
    }

    private void PreviewStoryContextLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        SaveStoryContextSettings();
        var preview = UtilityStoryContextBuilder.BuildPreviewFromLocal(_bundle, jobId);
        StoryContextPreviewMeta.Text = $"Preview (local): {preview.FormatStatusHint()}";

        var dlg = new RecapDialog(preview.Text) { Owner = this };
        dlg.Title = "Story context preview (local)";
        dlg.ShowDialog();
    }

    private async void PreviewStoryContextLive_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        SaveStoryContextSettings();
        if (PreviewLiveStoryContextAsync is null)
        {
            StoryContextPreviewMeta.Text = "Live preview unavailable — pin play tab and open from play cockpit.";
            return;
        }

        try
        {
            var preview = await PreviewLiveStoryContextAsync(jobId);
            StoryContextPreviewMeta.Text = $"Preview (live): {preview.FormatStatusHint()}";
            var dlg = new RecapDialog(preview.Text) { Owner = this };
            dlg.Title = "Story context preview (live)";
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            StoryContextPreviewMeta.Text = $"Live preview failed: {ex.Message}";
        }
    }

    private void ResetStoryContext_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        _suppressStoryContextEvents = true;
        StoryContextPerJobOverrideCheck.IsChecked = false;
        BindStoryContextPanel(jobId, new UtilityStoryContextSettings());
        StoryContextPreviewMeta.Text = "Using adventure-wide story context defaults.";
        _suppressStoryContextEvents = false;
        MarkPlaySettingsDirty();
    }

    private void UpdateAiActionStatus(string jobId)
    {
        if (HasAiActionGuideChanges())
        {
            AiActionStatusBlock.Text = "Unsaved edits — included when you Save play settings";
            return;
        }

        var isDefault = GenerationJobGuideService.IsUsingDefaultInstruction(_bundle, jobId);
        AiActionStatusBlock.Text = isDefault
            ? "Using built-in default"
            : "Customized — applies on the next inline job run";
    }

    private void ResetAiAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        AiActionInstructionBox.Text = GenerationJobGuideService.BuildDefaultInstructionBody(jobId);
        UpdateAiActionStatus(jobId);
        MarkPlaySettingsDirty();
    }

    private void SaveAiActionGuides()
    {
        SaveAiActionGuidesTo(_bundle);
        SaveStoryContextSettings();
    }

    private void BindWorldPanel()
    {
        SummaryBox.Text = _bundle.Summary.RollingSummary;
        LocationBox.Text = _bundle.State.CurrentLocation;
        ObjectivesBox.Text = _bundle.State.OpenObjectives;
        AuthorsNoteBox.Text = _bundle.Scenario.AuthorsNote;
        var pending = SummaryReviewService.IsPending(_bundle.Summary);
        SummaryReviewPanel.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ProposedSummaryBox.Text = pending ? _bundle.Summary.ProposedSummary ?? "" : "";
    }

    private bool _suppressPresetChange;
    private bool _playSettingsAutosaveHandlersAttached;

    private void BindPlaySurfaceSettings()
    {
        var s = _bundle.Metadata.Settings;
        AttachmentContextModeCombo.ItemsSource = Enum.GetValues<AttachmentContextMode>();
        AttachmentContextModeCombo.SelectedItem = s.AttachmentContextMode;
        AttachmentOnlyPlaceholderBox.Text = s.AttachmentOnlyPlaceholder;
        InjectAttachmentGuidanceCheck.IsChecked = s.InjectAttachmentGuidance;

        var actionKeys = PlaySurfaceActionSendHelper.DefaultActionKeys
            .Concat(s.PlaySurfaceActions.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        PlaySurfaceActionsGrid.ItemsSource = actionKeys
            .Select(key => new PlaySurfaceActionRow
            {
                ActionKey = key,
                Mode = s.PlaySurfaceActions.TryGetValue(key, out var mode) ? mode : "Visible",
            })
            .ToList();

        RebindPlayTabPlacementGrid();

        _suppressPresetChange = true;
        var presetItems = BuildPresetComboItems();
        PlayLayoutPresetCombo.ItemsSource = presetItems;
        PlayLayoutPresetCombo.DisplayMemberPath = nameof(PlayLayoutPresetComboItem.DisplayName);
        PlayLayoutPresetCombo.SelectedItem = presetItems.FirstOrDefault(i =>
            string.Equals(i.Id, s.PlayLayoutPresetId, StringComparison.OrdinalIgnoreCase))
            ?? presetItems[0];
        _suppressPresetChange = false;
    }

    private void RebindPlayTabPlacementGrid()
    {
        var s = _bundle.Metadata.Settings;
        PlayTabPlacementGrid.ItemsSource = PlayPanelSide.PlayTabs
            .Select(tab => new PlayTabPlacementRow
            {
                TabName = tab,
                Placement = PlayPanelLayoutService.ResolveTabPlacement(s, tab),
            })
            .ToList();
    }

    private static List<PlayLayoutPresetComboItem> BuildPresetComboItems()
    {
        var items = new List<PlayLayoutPresetComboItem> { new(null, "Custom") };
        items.AddRange(PlayLayoutPresetLibrary.All.Select(p => new PlayLayoutPresetComboItem(p.Id, p.DisplayName)));
        return items;
    }

    private void BindInjectionNarratorPanel() =>
        InjectionNarratorPanel.Bind(_narratorSession);

    public void SyncNarratorSession(NarratorSettingsSession session)
    {
        if (!ReferenceEquals(_narratorSession, session))
            return;

        InjectionNarratorPanel.Bind(_narratorSession);
        RefreshMergedPreview();
    }

    private void SaveNarratorBehaviorSettings()
    {
        InjectionNarratorPanel.FlushToSession();
        if (!ReferenceEquals(_narratorSession.Bundle, _bundle))
            NarratorSettingsSession.CopyNarratorSettings(_narratorSession.Bundle.Metadata.Settings, _bundle.Metadata.Settings);
    }

    private void InjectionNarratorPanel_SettingsChanged(object sender, EventArgs e)
    {
        MarkPlaySettingsDirty();
        RefreshMergedPreview();
    }

    private void PlayLayoutPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChange || !IsLoaded)
            return;

        if (PlayLayoutPresetCombo.SelectedItem is not PlayLayoutPresetComboItem item || item.Id is null)
            return;

        PlayPanelLayoutService.ApplyPreset(_bundle.Metadata.Settings, item.Id);
        RebindPlayTabPlacementGrid();
        MarkPlaySettingsDirty();
        RefreshMergedPreview();
    }

    private void SavePlaySurfaceSettings() => SavePlaySurfaceSettingsTo(_bundle);

    private void SavePlaySurfaceSettingsTo(AdventureBundle bundle)
    {
        var s = bundle.Metadata.Settings;
        if (AttachmentContextModeCombo.SelectedItem is AttachmentContextMode mode)
            s.AttachmentContextMode = mode;
        s.AttachmentOnlyPlaceholder = AttachmentOnlyPlaceholderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(s.AttachmentOnlyPlaceholder))
            s.AttachmentOnlyPlaceholder = "[Attached file]";
        s.InjectAttachmentGuidance = InjectAttachmentGuidanceCheck.IsChecked == true;

        s.PlaySurfaceActions.Clear();
        if (PlaySurfaceActionsGrid.ItemsSource is IEnumerable<PlaySurfaceActionRow> actionRows)
        {
            foreach (var row in actionRows)
            {
                if (!string.Equals(row.Mode, "Visible", StringComparison.OrdinalIgnoreCase))
                    s.PlaySurfaceActions[row.ActionKey] = row.Mode;
            }
        }

        s.PlayTabPlacement.Clear();
        if (PlayTabPlacementGrid.ItemsSource is IEnumerable<PlayTabPlacementRow> tabRows)
        {
            foreach (var row in tabRows)
            {
                var placement = PlayPanelLayoutService.NormalizeTabPlacement(row.TabName, row.Placement);
                if (!string.Equals(placement, PlayPanelSide.Left, StringComparison.OrdinalIgnoreCase))
                    s.PlayTabPlacement[row.TabName] = placement;
            }
        }
    }

    private void PlaySurfaceSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        if (sender == InjectAttachmentGuidanceCheck && !_suppressInjectionPolicyEvents)
        {
            _suppressInjectionPolicyEvents = true;
            InjectAttachmentGuidanceInjectionCheck.IsChecked = InjectAttachmentGuidanceCheck.IsChecked;
            _suppressInjectionPolicyEvents = false;
        }

        SchedulePreviewRefresh();
    }

    private void PlaySurfaceActionsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MarkPlaySettingsDirty();
            RefreshMergedPreview();
        }), DispatcherPriority.Background);
    }

    private void PlayTabPlacementGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PlayPanelLayoutService.MarkCustom(_bundle.Metadata.Settings);
            _suppressPresetChange = true;
            if (PlayLayoutPresetCombo.ItemsSource is IEnumerable<PlayLayoutPresetComboItem> items)
                PlayLayoutPresetCombo.SelectedItem = items.First();
            _suppressPresetChange = false;
            MarkPlaySettingsDirty();
            RefreshMergedPreview();
        }), DispatcherPriority.Background);
    }

    private void InitializeJobOverrideCombos()
    {
        JobOverrideResponseLengthCombo.ItemsSource = new[] { "normal", "short", "long" };
        JobOverrideResponseDetailCombo.ItemsSource = new[] { "standard", "brief", "deep" };
    }

    private void BindJobOverridePanel(string jobId)
    {
        var utilityId = GenerationJobHandlers.GetUtilityJobId(jobId);
        if (!_bundle.Metadata.Settings.UtilityJobOverrides.TryGetValue(utilityId, out var overrides))
            overrides = new UtilityJobOverrideSettings();

        JobOverrideResponseLengthCombo.SelectedItem = overrides.ResponseLength;
        JobOverrideResponseDetailCombo.SelectedItem = overrides.ResponseDetail;
    }

    private void SaveJobOverrideSettings() => SaveJobOverrideSettingsTo(_bundle.Metadata.Settings);

    private void JobOverrideSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return;

        MarkPlaySettingsDirty();
    }

    private void BindAdventureSettings()
    {
        var s = _bundle.Metadata.Settings;
        MaxPacketBox.Text = s.MaxPacketChars.ToString();
        PreferDomPlaySendCheck.IsChecked = s.PreferDomPlaySend;
        ForceFatPacketsCheck.IsChecked = s.ForceInlineLore;
        PerspectiveBox.Text = s.Perspective;
        BoundariesBox.Text = string.Join(Environment.NewLine, s.ContentBoundaries);
        CharacterPortrayalBox.Text = InstructionContractService.SerializeCharacterPortrayalRules(s.CharacterPortrayalRules);
        InstructionAddendumBox.Text = s.InstructionAddendum;
        s.SourcePublishMode = SourcePublishMode.Manual;
    }

    private void AttachPlaySettingsAutosaveHandlers()
    {
        if (_playSettingsAutosaveHandlersAttached)
            return;

        _playSettingsAutosaveHandlersAttached = true;
        AutomationCheck.Checked += PlaySettingsInputs_Changed;
        AutomationCheck.Unchecked += PlaySettingsInputs_Changed;
        PreferDomPlaySendCheck.Checked += PlaySettingsInputs_Changed;
        PreferDomPlaySendCheck.Unchecked += PlaySettingsInputs_Changed;
        AutoExtractEntitiesCheck.Checked += PlaySettingsInputs_Changed;
        AutoExtractEntitiesCheck.Unchecked += PlaySettingsInputs_Changed;
        AutoProposeMemoriesCheck.Checked += PlaySettingsInputs_Changed;
        AutoProposeMemoriesCheck.Unchecked += PlaySettingsInputs_Changed;
        AutoUpdateSummaryCheck.Checked += PlaySettingsInputs_Changed;
        AutoUpdateSummaryCheck.Unchecked += PlaySettingsInputs_Changed;
        AutoContinuityCheckCheck.Checked += PlaySettingsInputs_Changed;
        AutoContinuityCheckCheck.Unchecked += PlaySettingsInputs_Changed;
        AutoSyncInstructionsCheck.Checked += PlaySettingsInputs_Changed;
        AutoSyncInstructionsCheck.Unchecked += PlaySettingsInputs_Changed;
        SummaryIntervalBox.LostFocus += PlaySettingsInputs_Changed;
    }

    private void PlaySettingsInputs_Changed(object sender, RoutedEventArgs e) =>
        MarkPlaySettingsDirty();

    private void SaveWorldPanel()
    {
        _bundle.Summary.RollingSummary = SummaryBox.Text;
        _bundle.State.CurrentLocation = LocationBox.Text;
        _bundle.State.OpenObjectives = ObjectivesBox.Text;
        _bundle.Scenario.AuthorsNote = AuthorsNoteBox.Text;
    }

    private void SaveAdventureSettings()
    {
        var s = _bundle.Metadata.Settings;
        s.MaxPacketChars = ReadEffectiveMaxPacketChars();
        MaxPacketBox.Text = s.MaxPacketChars.ToString();
        s.PreferDomPlaySend = PreferDomPlaySendCheck.IsChecked == true;
        s.UseWrapperComposer = false;
        s.ForceInlineLore = ForceFatPacketsCheck.IsChecked == true;
        s.Perspective = PerspectiveBox.Text.Trim();
        s.ContentBoundaries = BoundariesBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        s.CharacterPortrayalRules = InstructionContractService.ParseCharacterPortrayalRules(CharacterPortrayalBox.Text) ?? [];
        s.InstructionAddendum = InstructionAddendumBox.Text.Trim();
        InstructionContractService.HydrateDesignInstructionFields(_bundle);
        s.SourcePublishMode = SourcePublishMode.Manual;
    }

    private void OpenThreadsHub_Click(object sender, RoutedEventArgs e) =>
        OpenThreadsHub?.Invoke();

    private void PinPlayTab_Click(object sender, RoutedEventArgs e) =>
        PinPlayTabRequested?.Invoke(this, EventArgs.Empty);

    private void OpenPinnedPlayTab_Click(object sender, RoutedEventArgs e) =>
        OpenPinnedPlayTabRequested?.Invoke(this, EventArgs.Empty);

    private void ClearPin_Click(object sender, RoutedEventArgs e) =>
        ClearPlayTabPinRequested?.Invoke(this, EventArgs.Empty);

    private async void StartNarrativeFromSources_Click(object sender, RoutedEventArgs e)
    {
        if (StartNewPlayThreadAsync is null)
            return;

        await StartNewPlayThreadAsync(new PlayThreadStartRequest { Kind = PlayThreadStartKind.FreshStart });
        ReplaceWorkingBundleFromDisk();
        UpdateSessionStatusUi();
    }

    private void HandOffToNewChat_Click(object sender, RoutedEventArgs e) =>
        OpenPlayHandoffDialog?.Invoke();

    private async void DraftNewProjectChat_Click(object sender, RoutedEventArgs e)
    {
        if (DraftNewProjectChatAsync is null)
            return;

        await DraftNewProjectChatAsync();
        ReplaceWorkingBundleFromDisk();
        UpdateSessionStatusUi();
    }

    private void CancelProjectChatDraft_Click(object sender, RoutedEventArgs e)
    {
        CancelProjectChatDraft?.Invoke();
        ReplaceWorkingBundleFromDisk();
        UpdateSessionStatusUi();
    }

    private void GoToSourcesTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(PlaySettingsTab.Sources);

    private void ReviewAllProposals_Click(object sender, RoutedEventArgs e) =>
        OpenProposalReviewHub?.Invoke(ResolveReviewCategoryFromSender(sender));

    private static ProposalReviewCategory? ResolveReviewCategoryFromSender(object sender) =>
        sender switch
        {
            FrameworkElement { Tag: ProposalReviewCategory category } => category,
            _ => null,
        };

    private void BindMemoryAndCards()
    {
        MemoryList.ItemsSource = null;
        MemoryList.ItemsSource = _bundle.Memory.Entries;
        MemoryReviewList.ItemsSource = _bundle.Memory.ReviewQueue;
        var memoryReviewCount = _bundle.Memory.ReviewQueue.Count;
        MemoryReviewExpander.IsExpanded = memoryReviewCount > 0;
        MemoryReviewHeader.Text = memoryReviewCount > 0
            ? $"{memoryReviewCount} memory proposal(s) awaiting review"
            : "";
        CardsList.ItemsSource = null;
        CardsList.ItemsSource = _bundle.Cards.Cards;
        CardReviewList.ItemsSource = _bundle.Cards.ReviewQueue
            .Select(c => new CardReviewListItem(c))
            .ToList();
        var cardReviewCount = _bundle.Cards.ReviewQueue.Count;
        CardReviewExpander.IsExpanded = cardReviewCount > 0;
        CardReviewHeader.Text = cardReviewCount > 0
            ? $"{cardReviewCount} card proposal(s) awaiting review"
            : "";
    }

    private void BindHistory()
    {
        var rows = _bundle.PromptHistory.Entries
            .OrderByDescending(e => e.At)
            .Take(40)
            .Select(e => new PromptHistoryListItem(e))
            .ToList();
        HistoryList.ItemsSource = rows;
        if (rows.Count > 0)
            HistoryList.SelectedIndex = 0;
    }

    private void BindSources()
    {
        ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(_bundle);
        var readiness = ProjectSourceInjectionService.Evaluate(_bundle);
        UpdateReadinessBanner(readiness);
        UpdatePublishModeUi();

        InstructionsPastedLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(_bundle);
        UpdateInstructionsUi();
        ProbeProjectButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        ProbeFileButton.IsEnabled = ProbeProjectButton.IsEnabled;
        ApiSyncDiagnosticsButton.IsEnabled = OpenApiSyncDiagnosticsAsync is not null;

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(_bundle);
        CanonicalPathLine.Text = $"Canonical folder: {sourcesDir}";
        BindSourceRepublishHints();
        _sourceRows.Clear();
        foreach (var entry in _bundle.SourceManifest.Entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SourcePublishRowViewModel(entry, sourcesDir, _bundle);
            row.ManifestEntryChanged += (_, _) => _sourceAutosave?.SaveNow();
            _sourceRows.Add(row);
        }

        SourcesGrid.ItemsSource = _sourceRows;
        if (_sourceRows.Count > 0 && SourcesGrid.SelectedItem is null)
            SourcesGrid.SelectedIndex = 0;
        UpdateCompareButton();
        BindSourceHistory();
        BindSourceEditReview();
    }

    private void SourcesGrid_CurrentCellChanged(object sender, EventArgs e) =>
        _sourceAutosave?.SaveNow();

    private void SourcesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column is DataGridCheckBoxColumn && e.EditAction == DataGridEditAction.Commit)
            Dispatcher.BeginInvoke(() => _sourceAutosave?.SaveNow(), DispatcherPriority.Background);
    }

    private void UpdateCompareButton()
    {
        CompareSourceButton.IsEnabled = SourcesGrid.SelectedItem is SourcePublishRowViewModel { HasMirror: true };
    }

    private void SourcesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCompareButton();
        BindSourceHistory();
    }

    private void BindSourceEditReview()
    {
        var pruned = ProjectSourceImportService.PruneStaleImportRemovalProposals(_bundle);
        ProjectSourceImportService.DeduplicateSourceEditReviewQueue(_bundle);
        if (pruned > 0)
            AdventureStore.Save(_bundle, AdventureSaveScope.Scenario);

        CanonCommitBar.Bind(_bundle);
        BindCanonDriftBanner();

        var visible = SourceEditReviewPresentationService.ListVisibleProposals(_bundle);
        var staged = EntityChangePlanQueueService.GetPending(_bundle).Count;
        var unresolved = CanonReconciliationService.HasUnresolvedDrift(_bundle);

        SourceEditReviewHeader.Text = SourceEditReviewPresentationService.FormatHeader(
            visible.Count,
            staged,
            unresolved);

        SourceEditReviewList.ItemsSource = visible
            .Select(e => new SourceEditReviewListItem(e))
            .ToList();
        if (SourceEditReviewList.Items.Count > 0)
            SourceEditReviewList.SelectedIndex = 0;
        else
            SourceEditDiffPreviewBox.Text = "";
    }

    private void BindCanonDriftBanner()
    {
        if (!CanonReconciliationService.HasUnresolvedDrift(_bundle))
        {
            CanonDriftBanner.Visibility = Visibility.Collapsed;
            return;
        }

        CanonDriftBanner.Visibility = Visibility.Visible;
        CanonDriftBannerText.Text =
            "entities.json / scenario.json differ from sources/*.md on disk. "
            + "JSON is the profile source of truth — use Sync sources from JSON to update markdown, "
            + "or Source Manager to pull from sources intentionally.";
    }

    private void CanonCommitBar_PlansChanged(object? sender, EventArgs e)
    {
        BindSourceEditReview();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncSourcesFromJson_Click(object sender, RoutedEventArgs e)
    {
        var result = EntityEditSourceSyncService.RepairFromJson(_bundle);
        AdventureStore.Save(_bundle, AdventureSaveScope.Scenario | AdventureSaveScope.Entities | AdventureSaveScope.SourceManifest);
        ReloadBundleFromStore();
        BindSourceEditReview();
        RefreshMergedPreview();
        MessageBox.Show(this,
            result.Summary ?? (result.Synced ? "Sources updated from JSON." : "No changes applied."),
            "Sync sources from JSON",
            MessageBoxButton.OK,
            result.Synced ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void SourceEditReviewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceEditReviewList.SelectedItem is not SourceEditReviewListItem row)
        {
            SourceEditDiffPreviewBox.Text = "";
            return;
        }

        SourceEditDiffPreviewBox.Text = SourceEditDiffPreviewService.BuildPreview(_bundle, row.Item);
    }

    private void UpdatePublishModeUi()
    {
        SourcesHintText.Text =
            "Canonical files live under adventures/{id}/sources/ (Open sources folder). "
            + "Drag each file onto ChatGPT Project → Files, then check Published. "
            + "Or use Copy selected file and paste/upload in the browser.";
    }

    private void UpdateReadinessBanner(ProjectSourceReadiness readiness)
    {
        var instructionBundle = BuildInstructionsStagingBundle();
        var instructionHint = InstructionSourcesPolicy.FormatInstructionDriftHint(instructionBundle);
        var instructionSuffix = string.IsNullOrWhiteSpace(instructionHint) ? "" : $"\n{instructionHint}";
        var probeSuffix = string.IsNullOrWhiteSpace(readiness.ProbeWarning) ? "" : $"\n{readiness.ProbeWarning}";
        if (readiness.CanDelegateStaticContent)
        {
            ReadinessBanner.Background = InstructionSourcesPolicy.InstructionDomainChanged(instructionBundle)
                ? (Brush)FindResource("WarningSubtleBrush")
                : (Brush)FindResource("SuccessSubtleBrush");
            ReadinessBannerText.Foreground = InstructionSourcesPolicy.InstructionDomainChanged(instructionBundle)
                ? UiBrushes.Warning(this)
                : UiBrushes.Success(this);
            ReadinessBannerText.Text =
                $"Source-delegated packets (manual publish) — {readiness.SyncedFiles.Count} file(s) published to Project.{instructionSuffix}{probeSuffix}";
            return;
        }

        if (!readiness.HasLinkedProject)
        {
            ReadinessBanner.Background = (Brush)FindResource("SurfaceSubtleBrush");
            ReadinessBannerText.Foreground = (Brush)FindResource("TextMutedBrush");
            ReadinessBannerText.Text =
                $"Minimal local packets — link a ChatGPT Project and publish sources for retrieval.{instructionSuffix}";
            SourceWalkthroughExpander.IsExpanded = true;
            return;
        }

        SourceWalkthroughExpander.IsExpanded = !readiness.CanDelegateStaticContent;

        ReadinessBanner.Background = (Brush)FindResource("WarningSubtleBrush");
        ReadinessBannerText.Foreground = UiBrushes.Warning(this);
        var reason = readiness.BlockingReason ?? "Using inline lore in packets";
        var action = string.IsNullOrWhiteSpace(readiness.SuggestedAction)
            ? ""
            : $" {readiness.SuggestedAction}.";
        var duplicateText = _bundle.SourceManifest.LastKnownDuplicateRemotes > 0
            ? $" {_bundle.SourceManifest.LastKnownDuplicateRemotes} duplicate remote file(s) detected — use Source Manager → Probe project."
            : "";
        ReadinessBannerText.Text = $"Publish sources to enable delegation — {reason}.{action}{duplicateText}{instructionSuffix}{probeSuffix}";
    }

    private AdventureBundle BuildInstructionsStagingBundle()
    {
        InjectionNarratorPanel.FlushToSession();
        var staging = InjectionSettingsStaging.CloneBundleForStaging(_bundle);
        staging.Scenario.AuthorsNote = AuthorsNoteBox.Text;
        SaveAdventureSettingsTo(staging.Metadata.Settings);
        SaveNarratorBehaviorSettingsTo(staging);
        return staging;
    }

    private void UpdateInstructionsUi()
    {
        if (!IsLoaded)
            return;

        var instructionBundle = BuildInstructionsStagingBundle();
        if (InstructionsPastedLine is not null)
            InstructionsPastedLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(instructionBundle);
        if (InstructionDriftLine is not null)
            InstructionDriftLine.Text = InstructionSourcesPolicy.FormatInstructionDriftHint(instructionBundle);

        var readiness = ProjectSourceInjectionService.Evaluate(_bundle);
        UpdateReadinessBanner(readiness);
    }

    private void DesignInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (InstructionDesignerDialog.Show(this, _bundle.Metadata.Id) != true)
            return;

        ReloadBundleFromStore();
        BindWorldPanel();
        BindAdventureSettings();
        UpdateInstructionsUi();
        MarkPlaySettingsDirty();
    }

    private void CopyInstructions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(InstructionSourcesPolicy.BuildStaticInstructionsBody(BuildInstructionsStagingBundle()));
            MessageBox.Show(this, "Instructions copied to clipboard. Paste into your ChatGPT Project settings.", "Copy instructions");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewInstructions_Click(object sender, RoutedEventArgs e)
    {
        var text = InstructionSourcesPolicy.BuildStaticInstructionsBody(BuildInstructionsStagingBundle());
        new ContextViewerDialog(text, "Project custom instructions preview")
        {
            Owner = this,
        }.ShowDialog();
    }

    private async void OpenProjectSettings_Click(object sender, RoutedEventArgs e)
    {
        if (OpenProjectSettingsAsync is not null)
            await OpenProjectSettingsAsync();
        else if (!string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId))
            MessageBox.Show(this,
                $"Open your ChatGPT Project settings in the browser and paste instructions.\nProject: {_bundle.Metadata.LinkedProjectId}",
                "Project settings");
    }

    private void MarkInstructionsPasted_Click(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedPlaySettings())
        {
            var result = MessageBox.Show(
                this,
                "Play settings have unsaved changes. Save first so the pasted marker matches what you copied.",
                "Mark instructions pasted",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
                return;

            PersistPlaySettingsToDisk();
        }

        var staged = BuildInstructionsStagingBundle();
        InstructionSourcesPolicy.RecordInstructionsManuallyPublished(staged);
        _bundle.Metadata.InstructionsManuallyPublishedAt = staged.Metadata.InstructionsManuallyPublishedAt;
        _bundle.Metadata.InstructionsManuallyPublishedHash = staged.Metadata.InstructionsManuallyPublishedHash;
        AdventureStore.Save(_bundle, AdventureSaveScope.Metadata);
        _sourceAutosave?.SaveNow();
        UpdateInstructionsUi();
    }

    private async void ProbeProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProbeSourcesAsync is not null)
            await ProbeSourcesAsync();
        ReloadBundleFromStore();
    }

    private void CompareSource_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesGrid.SelectedItem is not SourcePublishRowViewModel row || !row.HasMirror)
            return;

        var mirrorPath = ProjectSourceProbeService.MirrorFilePath(_bundle.Metadata.Id, row.RelativePath);
        var dialog = SourceCompareDialog.FromPaths(
            row.AbsolutePath,
            mirrorPath,
            "Canonical",
            "Project mirror",
            row.Entry.EffectiveLocalSha256,
            row.Entry.LastRemoteProbeSha256,
            row.Entry.ManuallyPublishedSha256);
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
    }

    private void RefreshExport_Click(object sender, RoutedEventArgs e)
    {
        ProjectSourceExportService.ExportForce(_bundle);
        AdventureStore.SaveSourceManifestOnly(_bundle);
        BindSources();
        RefreshMergedPreview();
    }

    private void CopySourceFile_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesGrid.SelectedItem is not SourcePublishRowViewModel row || !File.Exists(row.AbsolutePath))
            return;

        try
        {
            Clipboard.SetText(File.ReadAllText(row.AbsolutePath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewSourceFile_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesGrid.SelectedItem is not SourcePublishRowViewModel row || !File.Exists(row.AbsolutePath))
            return;

        new ContextViewerDialog(File.ReadAllText(row.AbsolutePath), row.RelativePath)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void MarkAllPublished_Click(object sender, RoutedEventArgs e)
    {
        ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(_bundle);
        SourceManifestHelper.RepublishAllCoreLore(_bundle);
        _sourceAutosave?.SaveNow();

        var readiness = ProjectSourceInjectionService.Evaluate(_bundle);
        UpdateReadinessBanner(readiness);
        foreach (var row in _sourceRows)
            row.RefreshDisplay();
        RefreshMergedPreview();
    }

    private async void EditSourcesAi_Click(object sender, RoutedEventArgs e)
    {
        if (RunSourceEditJobAsync is null)
            return;

        var dlg = new TextPromptDialog(
            "Edit sources with AI",
            "Describe what to change in scenario, world, plot, or instructions:",
            "Expand the world rules with more detail about magic.",
            confirmButtonText: "Continue",
            multiline: true)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText))
            return;

        var prompt = dlg.ResultText;

        await RunSourceEditJobAsync(prompt, "");
        ReloadBundleFromStore();
        BindSourceEditReview();
    }

    private void SourcesGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDraggingSource)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (SourcesGrid.SelectedItem is not SourcePublishRowViewModel row || !File.Exists(row.AbsolutePath))
            return;

        _isDraggingSource = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { row.AbsolutePath });
            DragDrop.DoDragDrop(SourcesGrid, data, DragDropEffects.Copy);
        }
        finally
        {
            _isDraggingSource = false;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void AcceptSourceEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SourceEditReviewList.SelectedItem is not SourceEditReviewListItem row)
        {
            MessageBox.Show(this, "Select a source edit proposal first.", "Accept edit",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SourceEditService.ApplyAcceptedEdit(_bundle, row.Item))
        {
            MessageBox.Show(this,
                "Could not apply the selected source edit. The proposal format may be unsupported.",
                "Accept edit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SourceEditService.RemoveMatchingReviewProposals(_bundle, row.Item);
        AdventureStore.Save(_bundle, AdventureSaveScope.Scenario | AdventureSaveScope.Entities | AdventureSaveScope.SourceManifest);
        BindSources();
        BindSourceEditReview();
        RefreshMergedPreview();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DismissSourceEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SourceEditReviewList.SelectedItem is not SourceEditReviewListItem row)
        {
            MessageBox.Show(this, "Select a source edit proposal first.", "Dismiss edit",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SourceEditService.RemoveMatchingReviewProposals(_bundle, row.Item);
        AdventureStore.Save(_bundle, AdventureSaveScope.Scenario);
        BindSourceEditReview();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadBundleFromStore()
    {
        _playSettingsSession.SyncFromDisk(
            preserveNarratorSettings: _sharedNarratorSession,
            rebindAllControls: () =>
            {
                _narratorSession.RepointWorkingBundle(_bundle);
                BindSources();
                RebindPlaySettingsFromBundle();
                RefreshMergedPreview();
            },
            refreshExternalOnly: () =>
            {
                RefreshUtilityWorkerStatusFromDisk();
                RefreshReviewPanels();
            });
    }

    public void RefreshUtilityWorkerStatusFromDisk()
    {
        _playSettingsSession.RefreshExternalOnly(UpdateUtilityWorkerStatusLine);
    }

    /// <summary>
    /// Reloads adventure documents from disk and keeps narrator session aligned with the dialog bundle.
    /// </summary>
    private void ReplaceWorkingBundleFromDisk()
    {
        InjectionNarratorPanel.FlushToSession();

        var fresh = AdventureStore.Load(_bundle.Metadata.Id);
        if (fresh is null)
            return;

        NarratorSettingsSession.CopyNarratorSettings(_narratorSession.Bundle.Metadata.Settings, fresh.Metadata.Settings);
        NarratorOverrideResolver.PersistScope(fresh.Metadata.Settings, _narratorSession.SelectedScope);
        _bundle = fresh;
        _playSettingsSession.RepointWorkingBundle(_bundle);
        _narratorSession.RepointWorkingBundle(_bundle);
    }

    private void RebindPlaySettingsFromBundle()
    {
        _playSettingsBinding = true;
        try
        {
            BindWorldPanel();
            BindNextSendPanel();
            BindInjectionPolicyPanel();
            BindAdventureSettings();
            BindAutomationPanel();
            BindUtilityDeliveryPanel();
            BindPlaySurfaceSettings();
            BindInjectionNarratorPanel();
            if (_selectedAiActionJobId is { } jobId)
            {
                AiActionInstructionBox.Text = GenerationJobGuideService.ResolveInstructionBody(_bundle, jobId);
                UpdateAiActionStatus(jobId);
                BindJobOverridePanel(jobId);
                BindStoryContextPanel(jobId);
            }
        }
        finally
        {
            _playSettingsBinding = false;
        }
    }

    public void RefreshReviewPanels()
    {
        BindWorldPanel();
        BindNextSendPanel();
        BindMemoryAndCards();
        BindSourceEditReview();
        RefreshUtilityParseLog();
    }

    private void RefreshUtilityParseLog()
    {
        if (UtilityParseLogBox is null)
            return;

        UtilityParseLogBox.Text = UtilityParseLogService.ReadRecentTail(_bundle.Metadata.Id);
    }

    private void RefreshUtilityParseLog_Click(object sender, RoutedEventArgs e) => RefreshUtilityParseLog();

    private async void ListThreadFiles_Click(object sender, RoutedEventArgs e)
    {
        if (ListThreadFilesAsync is null)
        {
            MessageBox.Show(this, "Thread file listing is not available.", "Conversation files",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var files = await ListThreadFilesAsync();
            new ConversationFilesDialog(files, DownloadThreadFileAsync) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Conversation files", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshMergedPreview() => _ = RefreshMergedPreviewAsync();

    private async Task RefreshMergedPreviewAsync()
    {
        var staging = BuildPreviewStagingBundle();
        var (playerLine, sourceLabel) = ResolvePreviewPlayerLine();
        PreviewSourceLine.Text = sourceLabel;

        var attachmentContext = ResolvePreviewAttachmentContext?.Invoke();
        var priorThreadUserMessageCount = await ResolvePriorThreadUserMessageCountAsync();
        var snapshot = InjectionPreviewCoordinator.Refresh(
            staging.Bundle,
            playerLine,
            attachmentContext,
            ResolvePreviewComposerText,
            PreviewPlayerLineBox.Text.Trim(),
            _lastPreviewSnapshot,
            priorThreadUserMessageCount);

        _lastPreviewSnapshot = snapshot;
        _lastMergedText = snapshot.MergedText;
        _lastPreviewMetaLine = snapshot.MetaLine;
        InjectionPreviewPanel.ApplySnapshot(snapshot);
        UpdatePreviewStagingHint();
    }

    private InjectionSettingsStaging BuildPreviewStagingBundle()
    {
        InjectionNarratorPanel.FlushToSession();
        _previewStaging = new InjectionSettingsStaging(_bundle);
        ApplyPendingUiToStagingBundle(_previewStaging.Bundle);
        return _previewStaging;
    }

    private void ApplyPendingUiToStagingBundle(AdventureBundle staging)
    {
        staging.ContinuationQueue = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        staging.Summary.RollingSummary = SummaryBox.Text;
        staging.State.CurrentLocation = LocationBox.Text;
        staging.State.OpenObjectives = ObjectivesBox.Text;
        staging.Scenario.AuthorsNote = AuthorsNoteBox.Text;

        SaveAdventureSettingsTo(staging.Metadata.Settings);
        SaveAutomationSettingsTo(staging.Metadata.Settings);
        SaveUtilityDeliverySettingsTo(staging.Metadata.Settings);
        SaveTurnOverrideSettingsTo(staging.Metadata.Settings);
        SavePlaySurfaceSettingsTo(staging);
        SaveNarratorBehaviorSettingsTo(staging);
        SaveInjectionPolicyTo(staging.Metadata.Settings, staging, syncUi: false);
        FlushCurrentAiActionEdits();
        SaveStoryContextSettingsTo(staging);
        SaveJobOverrideSettingsTo(staging.Metadata.Settings);
        SaveAiActionGuidesTo(staging);
    }

    private void SaveAdventureSettingsTo(AdventureSettings s)
    {
        s.MaxPacketChars = ReadEffectiveMaxPacketChars();
        s.PreferDomPlaySend = PreferDomPlaySendCheck.IsChecked == true;
        s.ForceInlineLore = ForceFatPacketsCheck.IsChecked == true;
        s.Perspective = PerspectiveBox.Text.Trim();
        s.ContentBoundaries = BoundariesBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        s.CharacterPortrayalRules = InstructionContractService.ParseCharacterPortrayalRules(CharacterPortrayalBox.Text) ?? [];
        s.InstructionAddendum = InstructionAddendumBox.Text.Trim();
    }

    private void SaveStoryContextSettingsTo(AdventureBundle target)
    {
        var settings = ReadStoryContextFromForm();
        if (StoryContextPerJobOverrideCheck.IsChecked == true && _selectedAiActionJobId is { } jobId)
        {
            UtilityStoryContextSettingsService.SetJobOverride(target, jobId, settings);
            return;
        }

        target.Metadata.Settings.UtilityStoryContext = settings;
        if (_selectedAiActionJobId is { } clearJobId)
            UtilityStoryContextSettingsService.SetJobOverride(target, clearJobId, null);
    }

    private void SaveJobOverrideSettingsTo(AdventureSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return;

        var utilityId = GenerationJobHandlers.GetUtilityJobId(_selectedAiActionJobId);
        var overrides = new UtilityJobOverrideSettings
        {
            ResponseLength = JobOverrideResponseLengthCombo.SelectedItem as string ?? "normal",
            ResponseDetail = JobOverrideResponseDetailCombo.SelectedItem as string ?? "standard",
        };

        if (string.Equals(overrides.ResponseLength, "normal", StringComparison.OrdinalIgnoreCase)
            && string.Equals(overrides.ResponseDetail, "standard", StringComparison.OrdinalIgnoreCase))
        {
            settings.UtilityJobOverrides.Remove(utilityId);
        }
        else
        {
            settings.UtilityJobOverrides[utilityId] = overrides;
        }
    }

    private void SaveAiActionGuidesTo(AdventureBundle target)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        GenerationJobGuideService.SetInstructionOverride(target, jobId, AiActionInstructionBox.Text);
    }

    private void SaveNarratorBehaviorSettingsTo(AdventureBundle staging)
    {
        NarratorSettingsSession.CopyNarratorSettings(_narratorSession.Bundle.Metadata.Settings, staging.Metadata.Settings);
        NarratorOverrideResolver.PersistScope(staging.Metadata.Settings, _narratorSession.SelectedScope);
    }

    private async Task<int> ResolvePriorThreadUserMessageCountAsync()
    {
        if (ResolveThreadUserTurnCountAsync is null)
            return 0;

        try
        {
            return Math.Max(0, await ResolveThreadUserTurnCountAsync());
        }
        catch
        {
            return 0;
        }
    }

    private (string Line, string SourceLabel) ResolvePreviewPlayerLine()
    {
        var compose = ResolvePreviewComposerText?.Invoke()?.Trim();
        if (!string.IsNullOrWhiteSpace(compose))
            return (compose, "Player line: in-page composer (Play mode)");

        var panelLine = PreviewPlayerLinePanelBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(panelLine))
            return (panelLine, "Player line: sample line (preview panel)");

        var fallback = PreviewPlayerLineBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return (fallback, "Player line: fallback (Play packet tab)");

        var queueLine = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queueLine))
            return (queueLine, "Player line: continuation queue line 1");

        return ("", "Player line: none — enter a sample line above");
    }

    private void UpdatePreviewStagingHint() => UpdatePlaySettingsSaveUi();

    private void MarkPlaySettingsDirty()
    {
        if (!IsLoaded || _playSettingsBinding)
            return;

        UpdatePlaySettingsSaveUi();
    }

    private void UpdatePlaySettingsSaveUi()
    {
        var hints = BuildStagingEditsSummary();
        var dirty = hints.Count > 0;

        if (PlaySettingsSaveButton is not null)
            PlaySettingsSaveButton.IsEnabled = dirty;

        if (PlaySettingsSaveLine is not null)
        {
            PlaySettingsSaveLine.Text = dirty
                ? "Unsaved changes — click Save (Ctrl+S) or OK."
                : _lastPlaySettingsSaveAt is { } at
                    ? $"Saved at {at.LocalDateTime:t}."
                    : "";
        }

        if (PreviewStagingHint is not null)
        {
            PreviewStagingHint.Text = hints.Count > 0
                ? "Unsaved edits in preview: " + string.Join(" · ", hints)
                : "";
            PreviewStagingHint.Visibility = hints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        CommandManager.InvalidateRequerySuggested();
        UpdateInstructionsUi();
    }

    private List<string> BuildStagingEditsSummary()
    {
        var hints = new List<string>();
        var s = _bundle.Metadata.Settings;

        var queueLines = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!queueLines.SequenceEqual(_bundle.ContinuationQueue))
            hints.Add("continuation queue");

        var previewPlayerLine = string.IsNullOrWhiteSpace(PreviewPlayerLinePanelBox.Text)
            ? PreviewPlayerLineBox.Text.Trim()
            : PreviewPlayerLinePanelBox.Text.Trim();
        if (!string.Equals(previewPlayerLine, _previewPlayerLineBaseline, StringComparison.Ordinal))
            hints.Add("preview player line");

        if (!string.Equals(SummaryBox.Text, _bundle.Summary.RollingSummary, StringComparison.Ordinal))
            hints.Add("rolling summary");
        if (!string.Equals(LocationBox.Text, _bundle.State.CurrentLocation, StringComparison.Ordinal))
            hints.Add("location");
        if (!string.Equals(ObjectivesBox.Text, _bundle.State.OpenObjectives, StringComparison.Ordinal))
            hints.Add("objectives");

        if (HasInstructionDomainChanges())
            hints.Add("project instructions");

        if (HasAutomationChanges())
            hints.Add("AI automation");

        if (PreferDomPlaySendCheck.IsChecked != s.PreferDomPlaySend
            || ForceFatPacketsCheck.IsChecked != s.ForceInlineLore)
        {
            hints.Add("developer send options");
        }

        if (HasUtilityDeliveryChanges())
            hints.Add("utility delivery");

        if (HasTurnOverrideChanges())
            hints.Add("next-send overrides");

        var policy = PlayInjectionPolicyService.Resolve(s);
        var presetId = InjectionPresetCombo.SelectedItem is InjectionPresetComboItem presetItem
            ? presetItem.Id
            : policy.InjectionPresetId;
        _ = int.TryParse(TranscriptMaxTurnsBox.Text, out var transcriptTurns);
        if (!string.Equals(presetId, policy.InjectionPresetId, StringComparison.OrdinalIgnoreCase)
            || IncludeSummaryCheck.IsChecked != policy.IncludeSummary
            || IncludeStateCheck.IsChecked != policy.IncludeState
            || IncludeMemoryCheck.IsChecked != policy.IncludePinnedMemory
            || IncludeTranscriptCheck.IsChecked != policy.IncludeTranscript
            || IncludeCardsCheck.IsChecked != policy.IncludeTriggeredCards
            || IncludeSourcesCheck.IsChecked != policy.IncludeSourcesPointers
            || ReadEffectiveMaxPacketChars() != s.MaxPacketChars
            || transcriptTurns != policy.TranscriptMaxTurns
            || InjectAttachmentGuidanceInjectionCheck.IsChecked != s.InjectAttachmentGuidance
            || UseContextTagsCheck.IsChecked != s.UseContextTags
            || UseSectionInjectionCheck.IsChecked != s.UseSectionInjection)
        {
            hints.Add("injection policy");
        }

        if (HasPlaySurfaceChanges())
            hints.Add("play surface");

        if (HasNarratorBehaviorChanges())
            hints.Add("narrator behavior");

        if (HasStoryContextChanges())
            hints.Add("story context");

        if (HasJobOverrideChanges())
            hints.Add("job overrides");

        if (HasAiActionGuideChanges())
            hints.Add("job guides");

        return hints;
    }

    private bool HasInstructionDomainChanges() =>
        !string.Equals(
            InstructionSourcesPolicy.ComputeInstructionDomainHash(BuildInstructionsStagingBundle()),
            InstructionSourcesPolicy.ComputeInstructionDomainHash(_bundle),
            StringComparison.OrdinalIgnoreCase);

    private bool HasAiActionGuideChanges()
    {
        if (string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return false;

        var current = AiActionInstructionBox.Text.Trim();
        var saved = GenerationJobGuideService.ResolveInstructionBody(_bundle, _selectedAiActionJobId).Trim();
        return !string.Equals(current, saved, StringComparison.Ordinal);
    }

    private int ReadSummaryIntervalTurns() =>
        int.TryParse(SummaryIntervalBox.Text, out var interval) ? Math.Max(1, interval) : _bundle.Metadata.Settings.SummaryUpdateIntervalTurns;

    private static string NormalizeAttachmentPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "[Attached file]" : value.Trim();

    private bool HasPlaySurfaceChanges()
    {
        var s = _bundle.Metadata.Settings;
        if (AttachmentContextModeCombo.SelectedItem is AttachmentContextMode mode && mode != s.AttachmentContextMode)
            return true;

        var placeholder = NormalizeAttachmentPlaceholder(AttachmentOnlyPlaceholderBox.Text);
        if (!string.Equals(placeholder, NormalizeAttachmentPlaceholder(s.AttachmentOnlyPlaceholder), StringComparison.Ordinal))
            return true;

        if ((InjectAttachmentGuidanceCheck.IsChecked == true) != s.InjectAttachmentGuidance)
            return true;

        var actions = ReadPlaySurfaceActionsFromUi();
        if (!DictionaryEquals(actions, s.PlaySurfaceActions))
            return true;

        var placement = ReadPlayTabPlacementFromUi();
        return !DictionaryEquals(placement, s.PlayTabPlacement);
    }

    private Dictionary<string, string> ReadPlaySurfaceActionsFromUi()
    {
        var actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (PlaySurfaceActionsGrid.ItemsSource is not IEnumerable<PlaySurfaceActionRow> actionRows)
            return actions;

        foreach (var row in actionRows)
        {
            if (!string.Equals(row.Mode, "Visible", StringComparison.OrdinalIgnoreCase))
                actions[row.ActionKey] = row.Mode;
        }

        return actions;
    }

    private Dictionary<string, string> ReadPlayTabPlacementFromUi()
    {
        var placement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (PlayTabPlacementGrid.ItemsSource is not IEnumerable<PlayTabPlacementRow> tabRows)
            return placement;

        foreach (var row in tabRows)
        {
            var normalized = PlayPanelLayoutService.NormalizeTabPlacement(row.TabName, row.Placement);
            if (!string.Equals(normalized, PlayPanelSide.Left, StringComparison.OrdinalIgnoreCase))
                placement[row.TabName] = normalized;
        }

        return placement;
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private bool HasNarratorBehaviorChanges()
    {
        if (!IsLoaded)
            return false;

        InjectionNarratorPanel.FlushToSession();
        return !NarratorSettingsSession.NarratorSettingsEqual(
            _narratorSession.Bundle.Metadata.Settings,
            _narratorBaselineAtOpen);
    }

    private bool HasStoryContextChanges()
    {
        if (!IsLoaded || StoryContextPreviewMeta is null)
            return false;

        var current = ReadStoryContextFromForm();
        if (StoryContextPerJobOverrideCheck.IsChecked == true && _selectedAiActionJobId is { } jobId)
        {
            var key = GenerationJobHandlers.GetUtilityJobId(jobId);
            if (_bundle.Metadata.UtilityJobGuideOverrides?.TryGetValue(key, out var over) == true && over.Context is { } saved)
                return !StoryContextSettingsEqual(saved, current);

            return !StoryContextSettingsEqual(new UtilityStoryContextSettings(), current);
        }

        return !StoryContextSettingsEqual(_bundle.Metadata.Settings.UtilityStoryContext, current);
    }

    private static bool StoryContextSettingsEqual(UtilityStoryContextSettings left, UtilityStoryContextSettings right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);

    private bool HasJobOverrideChanges()
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return false;

        var utilityId = GenerationJobHandlers.GetUtilityJobId(_selectedAiActionJobId);
        _bundle.Metadata.Settings.UtilityJobOverrides.TryGetValue(utilityId, out var saved);
        saved ??= new UtilityJobOverrideSettings();

        var current = new UtilityJobOverrideSettings
        {
            ResponseLength = JobOverrideResponseLengthCombo.SelectedItem as string ?? "normal",
            ResponseDetail = JobOverrideResponseDetailCombo.SelectedItem as string ?? "standard",
        };

        var isDefault = string.Equals(current.ResponseLength, "normal", StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.ResponseDetail, "standard", StringComparison.OrdinalIgnoreCase);
        if (isDefault)
            return _bundle.Metadata.Settings.UtilityJobOverrides.ContainsKey(utilityId);

        return !string.Equals(saved.ResponseLength, current.ResponseLength, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(saved.ResponseDetail, current.ResponseDetail, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncPreviewPlayerLineBoxes(string value, bool fromPanel)
    {
        if (_suppressPreviewPlayerLineSync)
            return;

        _suppressPreviewPlayerLineSync = true;
        if (fromPanel)
            PreviewPlayerLineBox.Text = value;
        else
            PreviewPlayerLinePanelBox.Text = value;
        _suppressPreviewPlayerLineSync = false;
    }

    private void PreviewPlayerLinePanelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPreviewPlayerLineSync)
            return;

        SyncPreviewPlayerLineBoxes(PreviewPlayerLinePanelBox.Text, fromPanel: true);
        SchedulePreviewRefresh();
        MarkPlaySettingsDirty();
    }

    private void PreviewPlayerLinePanelBox_LostFocus(object sender, RoutedEventArgs e) =>
        PreviewPlayerLine = PreviewPlayerLinePanelBox.Text.Trim();

    private void PreviewPlayerLineBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPreviewPlayerLineSync)
            return;

        SyncPreviewPlayerLineBoxes(PreviewPlayerLineBox.Text, fromPanel: false);
        SchedulePreviewRefresh();
        MarkPlaySettingsDirty();
    }

    private void SchedulePreviewRefresh()
    {
        _previewDebounce.Stop();
        _previewDebounce.Start();
        if (!_playSettingsBinding)
            UpdatePlaySettingsSaveUi();
    }

    private void PersistPendingPlaySettingsToBundle()
    {
        if (!IsLoaded)
            return;

        SaveQueueAndPreviewLine();
        SaveWorldPanel();
        SaveAdventureSettings();
        SaveAutomationSettingsTo(_bundle.Metadata.Settings);
        SaveUtilityDeliverySettings();
        SaveTurnOverrideSettings();
        SaveInjectionPolicyPanel();
        SavePlaySurfaceSettings();
        SaveNarratorBehaviorSettings();
        FlushCurrentAiActionEdits();
        SaveAiActionGuides();
        SaveStoryContextSettings();
    }

    private void FlushPendingSourceManifest() => _sourceAutosave?.SaveNow();

    private void PersistPlaySettingsToDisk()
    {
        FlushPendingSourceManifest();
        _playSettingsSession.Commit(
            PersistPendingPlaySettingsToBundle,
            preserveNarratorSettings: _sharedNarratorSession,
            rebindAllControls: () =>
            {
                _narratorSession.RepointWorkingBundle(_bundle);
                PlaySettingsPersisted = true;
                _narratorBaselineAtOpen = NarratorSettingsSession.CaptureNarratorBaseline(
                    _narratorSession.Bundle.Metadata.Settings);
                _lastPlaySettingsSaveAt = DateTimeOffset.Now;
                _previewPlayerLineBaseline = string.IsNullOrWhiteSpace(PreviewPlayerLinePanelBox.Text)
                    ? PreviewPlayerLineBox.Text.Trim()
                    : PreviewPlayerLinePanelBox.Text.Trim();
                RebindPlaySettingsFromBundle();
                UpdatePlaySettingsSaveUi();
                NotifyTransportSettingsCommitted();
            });
    }

    private void SavePlaySettings_Click(object sender, RoutedEventArgs e) => _ = FinalizePlaySettingsSaveAsync();

    private async Task FinalizePlaySettingsSaveAsync()
    {
        FlushPendingSourceManifest();
        if (HasUnsavedPlaySettings())
            PersistPlaySettingsToDisk();

        if (AutoSyncInstructionsCheck.IsChecked == true && SyncInstructionsAsync is not null)
            await SyncInstructionsAsync();

        ReloadBundleFromStore();
        UpdateInstructionsUi();
        if (!string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            UpdateAiActionStatus(_selectedAiActionJobId);
    }

    private void QueueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SchedulePreviewRefresh();
        MarkPlaySettingsDirty();
    }

    private void PreviewPlayerLineBox_LostFocus(object sender, RoutedEventArgs e) =>
        PreviewPlayerLine = PreviewPlayerLineBox.Text.Trim();

    private string FormatPreviewText(string mergedText) =>
        _bundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(mergedText)
            : mergedText;

    private void SaveQueueAndPreviewLine()
    {
        _bundle.ContinuationQueue = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        PlaySettingsStore.MirrorContinuationQueue(_bundle);
        PreviewPlayerLine = string.IsNullOrWhiteSpace(PreviewPlayerLinePanelBox.Text)
            ? PreviewPlayerLineBox.Text.Trim()
            : PreviewPlayerLinePanelBox.Text.Trim();
    }

    private void QueueBox_LostFocus(object sender, RoutedEventArgs e) => RefreshMergedPreview();

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => RefreshMergedPreview();

    private void CopyPacket_Click(object sender, RoutedEventArgs e) => _ = CopyPacketAsync();

    private async Task CopyPacketAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastMergedText))
        {
            await RefreshMergedPreviewAsync();
            if (string.IsNullOrWhiteSpace(_lastMergedText))
                return;
        }

        try
        {
            Clipboard.SetText(_lastMergedText);
            MessageBox.Show(this, "Play-turn packet copied to clipboard.", PlayPacketPanelCopy.CopyPlayTurnButton);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, PlayPacketPanelCopy.CopyPlayTurnButton, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CopyRepairPacket_Click(object sender, RoutedEventArgs e)
    {
        var (playerLine, _) = ResolvePreviewPlayerLine();
        if (string.IsNullOrWhiteSpace(playerLine))
        {
            MessageBox.Show(
                this,
                "Enter the player line to repair (composer, fallback line, or queue) before copying a repair packet.",
                PlaySendRepairService.CopyForEditRepairButton,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var threadUserCount = await ResolvePriorThreadUserMessageCountAsync();
        var repairTurnIndex = PlaySendRepairService.ResolveRepairTurnIndex(_bundle, threadUserCount);
        var attachmentContext = ResolvePreviewAttachmentContext?.Invoke();
        var previewBundle = BuildPreviewStagingBundle().Bundle;
        var prepared = PlaySendRepairService.PrepareRepairPacket(
            previewBundle,
            playerLine,
            repairTurnIndex,
            attachmentContext);
        var clipboardText = PlaySendRepairService.AssembleRepairClipboardText(
            prepared.MergedText,
            repairTurnIndex);

        try
        {
            Clipboard.SetText(clipboardText);
            MessageBox.Show(
                this,
                PlaySendRepairService.FormatRepairCopiedMessage(repairTurnIndex),
                PlaySendRepairService.CopyForEditRepairButton);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                PlaySendRepairService.CopyForEditRepairButton,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ViewFull_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastMergedText))
            RefreshMergedPreview();
        if (string.IsNullOrWhiteSpace(_lastMergedText))
            return;

        var dlg = new ContextViewerDialog(_lastMergedText, _lastPreviewMetaLine,
            useStructuredPreview: _bundle.Metadata.Settings.UseContextTags)
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    private void PreviewHandoffPacket_Click(object sender, RoutedEventArgs e) =>
        ShowHandoffPacketPreview();

    private void CopyNarrativePacket_Click(object sender, RoutedEventArgs e) =>
        CopyPacketToClipboard(
            BuildNarrativeStartPacketText(),
            PlayThreadRotationCopy.CopyNarrativePacketButton,
            "Narrative start packet copied to clipboard.\n\nOpen a new ChatGPT chat, paste (Ctrl+V), and press Send.");

    private void CopyHandoffPacket_Click(object sender, RoutedEventArgs e) =>
        CopyPacketToClipboard(
            BuildHandoffPacketText(),
            PlayThreadRotationCopy.CopyHandoffPacketButton,
            "Handoff packet copied to clipboard.\n\nOpen a new ChatGPT chat, paste (Ctrl+V), and press Send.");

    private void PreviewNarrativePacket_Click(object sender, RoutedEventArgs e) =>
        ShowNarrativeStartPacketPreview();

    private AdventureBundle ReloadFreshBundle() =>
        PlayThreadPacketService.ReloadFresh(_bundle.Metadata.Id) ?? _bundle;

    private string BuildNarrativeStartPacketText() =>
        PlayThreadPacketService.BuildStartPacket(ReloadFreshBundle());

    private string BuildHandoffPacketText()
    {
        var bundle = ReloadFreshBundle();
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        return PlayHandoffService.BuildHandoffPacket(bundle, snapshot, new PlayHandoffOptions());
    }

    private void CopyPacketToClipboard(string packetText, string title, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(packetText))
        {
            MessageBox.Show(this, "Packet text is empty.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Clipboard.SetText(packetText);
            MessageBox.Show(this, successMessage, title);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowNarrativeStartPacketPreview()
    {
        var bundle = ReloadFreshBundle();
        var packetText = PlayThreadPacketService.BuildStartPacket(bundle);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(packetText)))[..16];
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var profile = PacketProfileResolver.Resolve(bundle);
        var modeLabel = PacketProfileResolver.DisplayLabel(profile, readiness);
        var openingNote = string.IsNullOrWhiteSpace(bundle.Scenario.OpeningSituation)
            ? "Opening: (from sources)"
            : $"Opening in scenario.md ({bundle.Scenario.OpeningSituation.Trim().Length} chars author note)";
        new ContextViewerDialog(
            packetText,
            $"Narrative start packet preview | Mode: {modeLabel} | {openingNote} | Chars: {packetText.Length} | Hash: {hash}",
            useStructuredPreview: bundle.Metadata.Settings.UseContextTags)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void ShowHandoffPacketPreview()
    {
        var bundle = ReloadFreshBundle();
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var packetText = PlayHandoffService.BuildHandoffPacket(bundle, snapshot, new PlayHandoffOptions());
        var hash = PlayHandoffService.ComputePacketHash(packetText)[..16];
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var profile = PacketProfileResolver.Resolve(bundle);
        var modeLabel = PacketProfileResolver.DisplayLabel(profile, readiness);
        new ContextViewerDialog(
            packetText,
            $"Handoff packet preview | Mode: {modeLabel} | Adventure turns: {snapshot.AdventureTurnOrdinal} | Chars: {packetText.Length} | Hash: {hash}",
            useStructuredPreview: bundle.Metadata.Settings.UseContextTags)
        {
            Owner = this,
        }.ShowDialog();
    }

    private PromptHistoryEntry? SelectedHistoryEntry =>
        (HistoryList.SelectedItem as PromptHistoryListItem)?.Entry;

    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedHistoryEntry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.PacketText))
        {
            MessageBox.Show(this, "Select a history entry with packet text.", "History");
            return;
        }

        var meta = $"{entry.At:g} | Hash: {entry.PacketHash ?? "—"} | Chars: {entry.PacketText.Length}";
        new ContextViewerDialog(entry.PacketText, meta, useStructuredPreview: _bundle.Metadata.Settings.UseContextTags)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void CopyHistory_Click(object sender, RoutedEventArgs e)
    {
        var entry = SelectedHistoryEntry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.PacketText))
            return;

        try
        {
            Clipboard.SetText(entry.PacketText);
        }
        catch
        {
            /* ignore */
        }
    }

    private void AddMemory_Click(object sender, RoutedEventArgs e)
    {
        _bundle.Memory.Entries.Add(new MemoryEntry { Text = "New memory — edit in review." });
        AdventureStore.Save(_bundle, AdventureSaveScope.Memory);
        BindMemoryAndCards();
        SchedulePreviewRefresh();
    }

    private void PinMemory_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryList.SelectedItem is not MemoryEntry entry)
            return;

        entry.Pinned = !entry.Pinned;
        AdventureStore.Save(_bundle, AdventureSaveScope.Memory);
        BindMemoryAndCards();
        SchedulePreviewRefresh();
    }

    private void AcceptMemoryReview_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryReviewList.SelectedItem is not MemoryEntry item)
            return;

        _bundle.Memory.Entries.Add(new MemoryEntry
        {
            Text = item.Text,
            Tags = item.Tags,
            Pinned = item.Pinned,
        });
        _bundle.Memory.ReviewQueue.Remove(item);
        AdventureStore.Save(_bundle, AdventureSaveScope.Memory);
        BindMemoryAndCards();
        MemoryReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
        SchedulePreviewRefresh();
    }

    private void DismissMemoryReview_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryReviewList.SelectedItem is not MemoryEntry item)
            return;

        _bundle.Memory.ReviewQueue.Remove(item);
        AdventureStore.Save(_bundle, AdventureSaveScope.Memory);
        BindMemoryAndCards();
        MemoryReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AcceptCardReview_Click(object sender, RoutedEventArgs e)
    {
        if (CardReviewList.SelectedItem is not CardReviewListItem row)
            return;

        if (GenerationJobHandlers.ApplyAcceptedCardReviewItem(_bundle.Cards, row.Item))
            _bundle.Cards.ReviewQueue.Remove(row.Item);

        AdventureStore.Save(_bundle, AdventureSaveScope.Cards);
        BindMemoryAndCards();
        CardReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
        SchedulePreviewRefresh();
    }

    private void DismissCardReview_Click(object sender, RoutedEventArgs e)
    {
        if (CardReviewList.SelectedItem is not CardReviewListItem row)
            return;

        _bundle.Cards.ReviewQueue.Remove(row.Item);
        AdventureStore.Save(_bundle, AdventureSaveScope.Cards);
        BindMemoryAndCards();
        CardReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AcceptSummary_Click(object sender, RoutedEventArgs e)
    {
        SummaryReviewService.AcceptProposal(_bundle, ProposedSummaryBox.Text);
        SummaryBox.Text = _bundle.Summary.RollingSummary;
        AdventureStore.Save(_bundle, AdventureSaveScope.Summary);
        BindWorldPanel();
        SchedulePreviewRefresh();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DismissSummary_Click(object sender, RoutedEventArgs e)
    {
        SummaryReviewService.DismissProposal(_bundle);
        AdventureStore.Save(_bundle, AdventureSaveScope.Summary);
        BindWorldPanel();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void RefreshSummaryAi_Click(object sender, RoutedEventArgs e)
    {
        if (RefreshSummaryAsync is not null)
            await RefreshSummaryAsync();
        ReloadBundleFromStore();
        BindWorldPanel();
    }

    private async void SuggestMemoriesAi_Click(object sender, RoutedEventArgs e)
    {
        if (SuggestMemoriesAsync is not null)
            await SuggestMemoriesAsync();
        ReloadBundleFromStore();
        BindMemoryAndCards();
    }

    private async void GenerateCardsAi_Click(object sender, RoutedEventArgs e)
    {
        if (GenerateCardsAsync is not null)
            await GenerateCardsAsync();
        ReloadBundleFromStore();
        BindMemoryAndCards();
    }

    private async void ExpandCardAi_Click(object sender, RoutedEventArgs e)
    {
        if (CardsList.SelectedItem is not StoryCard card || ExpandStoryCardAsync is null)
        {
            MessageBox.Show(this, "Select a story card first.", "Expand card");
            return;
        }

        await ExpandStoryCardAsync(card.Id);
        ReloadBundleFromStore();
        BindMemoryAndCards();
    }

    private void AddCard_Click(object sender, RoutedEventArgs e)
    {
        _bundle.Cards.Cards.Add(new StoryCard
        {
            Name = "New card",
            Triggers = new List<string> { "keyword" },
            Content = "Lore text",
        });
        AdventureStore.Save(_bundle, AdventureSaveScope.Cards);
        BindMemoryAndCards();
        RefreshMergedPreview();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult != true && HasUnsavedPlaySettings())
        {
            var result = MessageBox.Show(
                this,
                "Save play settings before closing?",
                Title,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                PersistPlaySettingsToDisk();
                if (AutoSyncInstructionsCheck.IsChecked == true && SyncInstructionsAsync is not null)
                    _ = SyncInstructionsAsync();
            }
        }

        base.OnClosing(e);
    }

    private bool HasUnsavedPlaySettings() =>
        BuildStagingEditsSummary().Count > 0;

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        await FinalizePlaySettingsSaveAsync();
        DialogResult = true;
        Close();
    }

    private sealed class PromptHistoryListItem
    {
        public PromptHistoryListItem(PromptHistoryEntry entry)
        {
            Entry = entry;
        }

        public PromptHistoryEntry Entry { get; }

        public string DisplayLabel =>
            $"{Entry.At:g}  hash={Entry.PacketHash ?? "—"}  ({Entry.PacketText.Length} chars)";
    }

    private sealed class UtilityJobRowViewModel
    {
        public UtilityJobRowViewModel(AdventureBundle bundle, string jobId)
        {
            JobId = jobId;
            UtilityJobId = GenerationJobHandlers.GetUtilityJobId(jobId);
            DisplayLabel = GenerationUtilitySessionService.FormatUtilityStatus(bundle, jobId);
        }

        public string JobId { get; }

        public string UtilityJobId { get; }

        public string DisplayLabel { get; }
    }

    private sealed class AiActionRowViewModel
    {
        public AiActionRowViewModel(string jobId)
        {
            JobId = jobId;
            DisplayLabel = GenerationJobGuideService.GetDisplayLabel(jobId);
        }

        public string JobId { get; }

        public string DisplayLabel { get; }

        public override string ToString() => DisplayLabel;
    }

    private sealed class SourceEditReviewListItem
    {
        public SourceEditReviewListItem(SourceEditReviewItem item) => Item = item;

        public SourceEditReviewItem Item { get; }

        public string DisplayLabel => SourceEditReviewPresentationService.FormatListLabel(Item);

        public override string ToString() => DisplayLabel;
    }

    private sealed class CardReviewListItem
    {
        public CardReviewListItem(CardReviewItem item) => Item = item;

        public CardReviewItem Item { get; }

        public string DisplayLabel
        {
            get
            {
                var text = Item.ProposedChange ?? "";
                return text.Length <= 80 ? text : text[..80] + "…";
            }
        }
    }

    private sealed class PlaySurfaceActionRow
    {
        public string ActionKey { get; init; } = "";

        public string Mode { get; set; } = "Visible";

        public IReadOnlyList<string> ModeOptions { get; } = ["Visible", "Hidden", "InjectedOnly"];
    }

    private sealed class PlayTabPlacementRow
    {
        public string TabName { get; init; } = "";

        public string Placement { get; set; } = "Left";

        public IReadOnlyList<string> PlacementOptions =>
            TabName.Equals("Notes", StringComparison.OrdinalIgnoreCase)
                ? PlayPanelSide.NotesPlacement
                : PlayPanelSide.CompanionTabPlacement;
    }

    private sealed class PlayLayoutPresetComboItem(string? id, string displayName)
    {
        public string? Id { get; } = id;

        public string DisplayName { get; } = displayName;
    }
}
