using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog : Window
{
    private AdventureBundle _bundle;
    private string _lastMergedText = "";
    private readonly ObservableCollection<SourcePublishRowViewModel> _sourceRows = [];
    private DebouncedAdventureSaver? _sourceAutosave;
    private readonly DispatcherTimer _previewDebounce;
    private Point _dragStartPoint;
    private bool _isDraggingSource;

    public Func<string?>? ResolvePreviewComposerText { get; set; }

    public Func<AttachmentContext?>? ResolvePreviewAttachmentContext { get; set; }

    public Func<Task>? SyncSourcesAsync { get; set; }

    public Func<Task>? RefreshSourcesStatusAsync { get; set; }

    public Func<Task>? ReconcileDuplicatesAsync { get; set; }

    public event EventHandler? PinPlayTabRequested;

    public event EventHandler? OpenPinnedPlayTabRequested;

    public event EventHandler? ClearPlayTabPinRequested;

    public event EventHandler? PinUtilityTabRequested;

    public event EventHandler? OpenPinnedUtilityTabRequested;

    public event EventHandler? ClearUtilityTabPinRequested;

    /// <summary>Raised when a review queue changes so the play surface can refresh badges immediately.</summary>
    public event EventHandler? ReviewQueueChanged;

    public Func<string, Task>? OpenUtilityThreadAsync { get; set; }

    public Func<string, Task>? RotateUtilityThreadAsync { get; set; }

    public Func<Task>? StartNewPlayThreadAsync { get; set; }

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

    public Func<Task>? OpenSourceManagerAsync { get; set; }

    public Func<Task>? ProbeSourcesAsync { get; set; }

    public string PreviewPlayerLine { get; private set; } = "";

    public PlayPromptInjectionDialog(AdventureBundle bundle, string? previewPlayerLine, PlaySettingsTab initialTab = PlaySettingsTab.NextSend)
    {
        _bundle = bundle;
        UtilityStoryContextSettingsService.EnsureDefaults(_bundle.Metadata);
        _suppressStoryContextEvents = true;
        InitializeComponent();

        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            RefreshMergedPreview();
        };

        QueueBox.Text = string.Join(Environment.NewLine, bundle.ContinuationQueue);
        PreviewPlayerLine = previewPlayerLine ?? "";
        PreviewPlayerLineBox.Text = PreviewPlayerLine;

        var fresh = AdventureBootstrapService.IsFreshAdventure(bundle)
                    && bundle.Metadata.Settings.OfferStartOnPlay;
        PreviewStartPacketButton.Visibility = fresh ? Visibility.Visible : Visibility.Collapsed;
        if (fresh && string.IsNullOrWhiteSpace(PreviewPlayerLineBox.Text))
            PreviewPlayerLineBox.Text = AdventureBootstrapService.GetOpeningPlayerLine(bundle.Scenario);

        BindWorldPanel();
        BindAdventureSettings();
        BindPlaySurfaceSettings();
        InitializeJobOverrideCombos();
        BindUtilityDeliverySettings();
        UpdateSessionStatusUi();
        BindUtilityJobs();
        BindAiActions();
        BindMemoryAndCards();
        BindHistory();
        _sourceAutosave = new DebouncedAdventureSaver(() => _bundle, at =>
            SourceAutosaveLine.Text = $"Source changes saved automatically at {at.LocalDateTime:t}.");
        Closed += (_, _) =>
        {
            _sourceAutosave?.Dispose();
            _previewDebounce.Stop();
        };

        BindSources();
        RefreshMergedPreview();
        SelectTab(initialTab);
    }

    public void SelectTab(PlaySettingsTab tab)
    {
        SettingsTabControl.SelectedItem = tab switch
        {
            PlaySettingsTab.World => WorldTab,
            PlaySettingsTab.Session => SessionTab,
            PlaySettingsTab.AiActions => AiActionsTab,
            PlaySettingsTab.PlaySurface => PlaySurfaceTab,
            PlaySettingsTab.Settings => AdventureSettingsTab,
            PlaySettingsTab.Sources => SourcesTab,
            PlaySettingsTab.MemoryCards => MemoryCardsTab,
            _ => SettingsTabControl.Items[0],
        };
    }

    public void UpdateSessionStatusUi()
    {
        var pinned = !string.IsNullOrWhiteSpace(_bundle.Metadata.PinnedPlayTabKey);
        PlayTabStatusBlock.Text = pinned
            ? $"Play tab: {_bundle.Metadata.PinnedPlayTabTitle ?? "ChatGPT tab"}"
            : "Play tab: not linked — use Link to active browser tab before Send.";
        ClearPinButton.IsEnabled = pinned;
        OpenPinnedPlayTabButton.IsEnabled = pinned;
        StartNewPlayThreadButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        DraftNewProjectChatButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        CancelProjectChatDraftButton.Visibility = ProjectChatDraftService.IsActive(_bundle)
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelProjectChatDraftButton.IsEnabled = ProjectChatDraftService.IsActive(_bundle);
        var draftLine = ProjectChatDraftService.FormatStatusLine(_bundle);
        ProjectChatDraftStatusBlock.Text = draftLine;
        ProjectChatDraftStatusBlock.Visibility = string.IsNullOrWhiteSpace(draftLine)
            ? Visibility.Collapsed
            : Visibility.Visible;

        var utilityPinned = PlayTabPinService.HasUtilityPin(_bundle);
        UtilityTabStatusBlock.Text = utilityPinned
            ? FormatUtilityPinStatus(_bundle)
            : "Utility chat: auto-managed in background. Link a browser tab here only to override or inspect threads.";
        ClearUtilityPinButton.IsEnabled = utilityPinned;
        OpenPinnedUtilityTabButton.IsEnabled = utilityPinned;
        PinUtilityTabButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        UpdateUtilityDeliveryUi();
    }

    private void BindUtilityDeliverySettings()
    {
        var s = _bundle.Metadata.Settings;
        SelectUtilityDeliveryCombo(s.UtilityDeliveryMode);
        HideInlineUtilityCheck.IsChecked = s.HideInlineUtilityDuringPlay;
        ShowInlineUtilityTrafficCheck.IsChecked = s.ShowInlineUtilityTraffic;
        UpdateUtilityDeliveryUi();
    }

    private void SelectUtilityDeliveryCombo(UtilityDeliveryMode mode)
    {
        foreach (ComboBoxItem item in UtilityDeliveryModeCombo.Items)
        {
            if (item.Tag is string tag
                && string.Equals(tag, mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                UtilityDeliveryModeCombo.SelectedItem = item;
                return;
            }
        }

        UtilityDeliveryModeCombo.SelectedIndex = 0;
    }

    private void UpdateUtilityDeliveryUi()
    {
        var inline = _bundle.Metadata.Settings.UtilityDeliveryMode == UtilityDeliveryMode.InlinePlayThread;
        var hideSection = inline;
        UtilityBackgroundHeader.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        UtilityBackgroundHint.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        PinUtilityTabButton.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        OpenPinnedUtilityTabButton.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        ClearUtilityPinButton.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        UtilityTabStatusBlock.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        UtilityThreadsHeader.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        UtilityJobsList.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        OpenUtilityThreadButton.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;
        RotateUtilityThreadButton.Visibility = hideSection ? Visibility.Collapsed : Visibility.Visible;

        HideInlineUtilityCheck.IsEnabled = inline;
        ShowInlineUtilityTrafficCheck.IsEnabled = inline;

        UtilityDeliveryStatusBlock.Text = inline
            ? "Inline mode: AI jobs send on the play thread with [[cgw:utility]] tags. Utility traffic is hidden from the reading UI unless peek is enabled."
            : "Separate thread mode: AI jobs use dedicated utility conversations (background or pinned utility tab).";
    }

    private void UtilityDeliverySettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        SaveUtilityDeliverySettings();
        UpdateUtilityDeliveryUi();
    }

    private void SaveUtilityDeliverySettings()
    {
        var s = _bundle.Metadata.Settings;
        if (UtilityDeliveryModeCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<UtilityDeliveryMode>(tag, ignoreCase: true, out var mode))
        {
            s.UtilityDeliveryMode = mode;
        }

        s.HideInlineUtilityDuringPlay = HideInlineUtilityCheck.IsChecked == true;
        s.ShowInlineUtilityTraffic = ShowInlineUtilityTrafficCheck.IsChecked == true;
    }

    private static string FormatUtilityPinStatus(AdventureBundle bundle)
    {
        var title = bundle.Metadata.PinnedUtilityTabTitle ?? "ChatGPT tab";
        var convHint = "";
        foreach (var session in bundle.Metadata.UtilitySessions.Values)
        {
            if (session.ConversationId is { Length: >= 8 } id)
            {
                convHint = $" · c/{id[..8]}…";
                break;
            }
        }

        if (string.Equals(
                bundle.Metadata.PinnedUtilityTabKey,
                bundle.Metadata.PinnedPlayTabKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"Pinned: {title} · warning: same tab as play pin{convHint}";
        }

        return $"Pinned: {title}{convHint}";
    }

    public void SetSessionLinkDetails(string threadLine, string sourcesLine)
    {
        ThreadStatusBlock.Text = threadLine;
        SourcesStatusBlock.Text = sourcesLine;
    }

    public void BindUtilityJobs()
    {
        var hasProject = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        UtilityJobsList.ItemsSource = GenerationJobId.All
            .Select(jobId => new UtilityJobRowViewModel(_bundle, jobId))
            .ToList();
        OpenUtilityThreadButton.IsEnabled = hasProject;
        RotateUtilityThreadButton.IsEnabled = hasProject;
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
        if (AiActionsList.SelectedItem is not AiActionRowViewModel row)
            return;

        _selectedAiActionJobId = row.JobId;
        AiActionTitleBlock.Text = row.DisplayLabel;
        AiActionInstructionBox.Text = GenerationJobGuideService.ResolveInstructionBody(_bundle, row.JobId);
        UpdateAiActionStatus(row.JobId);
        BindJobOverridePanel(row.JobId);
        BindStoryContextPanel(row.JobId);
    }

    private void BindStoryContextPanel(string jobId)
    {
        _suppressStoryContextEvents = true;
        try
        {
            var hasOverride = UtilityStoryContextSettingsService.HasJobOverride(_bundle, jobId);
            StoryContextPerJobOverrideCheck.IsChecked = hasOverride;
            var settings = UtilityStoryContextSettingsService.Resolve(_bundle, jobId);
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
            StoryContextPreviewMeta.Text = hasOverride
                ? "Per-action override active."
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

    private void SaveStoryContextSettings()
    {
        var settings = ReadStoryContextFromForm();
        if (StoryContextPerJobOverrideCheck.IsChecked == true && _selectedAiActionJobId is { } jobId)
        {
            UtilityStoryContextSettingsService.SetJobOverride(_bundle, jobId, settings);
            return;
        }

        _bundle.Metadata.Settings.UtilityStoryContext = settings;
        if (_selectedAiActionJobId is { } clearJobId)
            UtilityStoryContextSettingsService.SetJobOverride(_bundle, clearJobId, null);
    }

    private void StoryContextSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressStoryContextEvents || StoryContextPreviewMeta is null)
            return;

        StoryContextPreviewMeta.Text = StoryContextPerJobOverrideCheck.IsChecked == true
            ? "Per-action override active."
            : "Using adventure-wide story context defaults.";
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

        if (StoryContextPerJobOverrideCheck.IsChecked == true)
            UtilityStoryContextSettingsService.SetJobOverride(_bundle, jobId, null);

        _bundle.Metadata.Settings.UtilityStoryContext = new UtilityStoryContextSettings();
        BindStoryContextPanel(jobId);
        AdventureStore.Save(_bundle);
    }

    private void UpdateAiActionStatus(string jobId)
    {
        var isDefault = GenerationJobGuideService.IsUsingDefaultInstruction(_bundle, jobId);
        var inline = _bundle.Metadata.Settings.UtilityDeliveryMode == UtilityDeliveryMode.InlinePlayThread;
        AiActionStatusBlock.Text = isDefault
            ? "Using built-in default"
            : inline
                ? "Customized — applies on the next inline job run"
                : "Customized — next job run may rotate the utility thread";
    }

    private void ApplyAiAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        GenerationJobGuideService.SetInstructionOverride(_bundle, jobId, AiActionInstructionBox.Text);
        AdventureStore.Save(_bundle);
        UpdateAiActionStatus(jobId);
    }

    private void ResetAiAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        GenerationJobGuideService.ResetInstructionOverride(_bundle, jobId);
        AiActionInstructionBox.Text = GenerationJobGuideService.BuildDefaultInstructionBody(jobId);
        AdventureStore.Save(_bundle);
        UpdateAiActionStatus(jobId);
    }

    private void SaveAiActionGuides()
    {
        if (_selectedAiActionJobId is not { } jobId)
            return;

        GenerationJobGuideService.SetInstructionOverride(_bundle, jobId, AiActionInstructionBox.Text);
        SaveStoryContextSettings();
    }

    private void BindWorldPanel()
    {
        SummaryBox.Text = _bundle.Summary.RollingSummary;
        LocationBox.Text = _bundle.State.CurrentLocation;
        ObjectivesBox.Text = _bundle.State.OpenObjectives;
        AuthorsNoteBox.Text = _bundle.Scenario.AuthorsNote;
        var pending = _bundle.Summary.PendingReview && !string.IsNullOrWhiteSpace(_bundle.Summary.ProposedSummary);
        SummaryReviewBanner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        ProposedSummaryBox.Text = pending ? _bundle.Summary.ProposedSummary ?? "" : "";
    }

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

        var tabNames = new[] { "Reference", "Warnings", "State" };
        PlayTabPlacementGrid.ItemsSource = tabNames
            .Select(tab => new PlayTabPlacementRow
            {
                TabName = tab,
                Placement = s.PlayTabPlacement.TryGetValue(tab, out var placement) ? placement : "Left",
            })
            .ToList();
    }

    private void SavePlaySurfaceSettings()
    {
        var s = _bundle.Metadata.Settings;
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
                if (!string.Equals(row.Placement, "Left", StringComparison.OrdinalIgnoreCase))
                    s.PlayTabPlacement[row.TabName] = row.Placement;
            }
        }
    }

    private void PlaySurfaceSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        SavePlaySurfaceSettings();
        RefreshMergedPreview();
    }

    private void PlaySurfaceActionsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SavePlaySurfaceSettings();
            RefreshMergedPreview();
        }), DispatcherPriority.Background);
    }

    private void PlayTabPlacementGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        PlaySurfaceActionsGrid_CellEditEnding(sender, e);

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

    private void SaveJobOverrideSettings()
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
            _bundle.Metadata.Settings.UtilityJobOverrides.Remove(utilityId);
        }
        else
        {
            _bundle.Metadata.Settings.UtilityJobOverrides[utilityId] = overrides;
        }
    }

    private void JobOverrideSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return;

        SaveJobOverrideSettings();
    }

    private void BindAdventureSettings()
    {
        var s = _bundle.Metadata.Settings;
        MaxPacketBox.Text = s.MaxPacketChars.ToString();
        AutomationCheck.IsChecked = s.AdventureAutomationEnabled;
        PreferDomPlaySendCheck.IsChecked = s.PreferDomPlaySend;
        UseWrapperComposerCheck.IsChecked = s.UseWrapperComposer;
        ForceFatPacketsCheck.IsChecked = s.ForceFatPackets;
        PerspectiveBox.Text = s.Perspective;
        BoundariesBox.Text = string.Join(Environment.NewLine, s.ContentBoundaries);
        CharacterPortrayalBox.Text = InstructionContractService.SerializeCharacterPortrayalRules(s.CharacterPortrayalRules);
        InstructionAddendumBox.Text = s.InstructionAddendum;
        AutoExtractEntitiesCheck.IsChecked = s.AutoExtractEntities;
        AutoProposeMemoriesCheck.IsChecked = s.AutoProposeMemories;
        AutoUpdateSummaryCheck.IsChecked = s.AutoUpdateSummary;
        SummaryIntervalBox.Text = s.SummaryUpdateIntervalTurns.ToString();
        AutoContinuityCheckCheck.IsChecked = s.AutoContinuityCheck;
        AutoSyncInstructionsCheck.IsChecked = s.AutoSyncProjectInstructions;
        s.SourcePublishMode = SourcePublishMode.Manual;
        var hasProject = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        var inlineDelivery = UtilityDeliveryModeService.UsesInlineDelivery(_bundle);
        var autoEnabled = hasProject && !inlineDelivery;
        AutoExtractEntitiesCheck.IsEnabled = autoEnabled;
        AutoProposeMemoriesCheck.IsEnabled = autoEnabled;
        AutoUpdateSummaryCheck.IsEnabled = autoEnabled;
        AutoContinuityCheckCheck.IsEnabled = autoEnabled;
        AutoSyncInstructionsCheck.IsEnabled = hasProject;
        AutoExtractEntitiesHint.Text = inlineDelivery
            ? "Auto AI is disabled in inline play-thread mode. Use Process last exchange (AI) on the play panel."
            : hasProject
                ? "Proposals appear in Reference → review queue after each accepted turn."
                : "Link a Project to enable auto entity extraction.";
    }

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
        if (int.TryParse(MaxPacketBox.Text, out var max))
            s.MaxPacketChars = Math.Max(4000, max);
        s.AdventureAutomationEnabled = AutomationCheck.IsChecked == true;
        s.PreferDomPlaySend = PreferDomPlaySendCheck.IsChecked == true;
        s.UseWrapperComposer = UseWrapperComposerCheck.IsChecked == true;
        s.ForceFatPackets = ForceFatPacketsCheck.IsChecked == true;
        s.Perspective = PerspectiveBox.Text.Trim();
        s.ContentBoundaries = BoundariesBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        s.CharacterPortrayalRules = InstructionContractService.ParseCharacterPortrayalRules(CharacterPortrayalBox.Text) ?? [];
        s.InstructionAddendum = InstructionAddendumBox.Text.Trim();
        InstructionContractService.HydrateDesignInstructionFields(_bundle);
        s.AutoExtractEntities = AutoExtractEntitiesCheck.IsChecked == true;
        s.AutoProposeMemories = AutoProposeMemoriesCheck.IsChecked == true;
        s.AutoUpdateSummary = AutoUpdateSummaryCheck.IsChecked == true;
        if (int.TryParse(SummaryIntervalBox.Text, out var interval))
            s.SummaryUpdateIntervalTurns = Math.Max(1, interval);
        s.AutoContinuityCheck = AutoContinuityCheckCheck.IsChecked == true;
        s.AutoSyncProjectInstructions = AutoSyncInstructionsCheck.IsChecked == true;
        s.SourcePublishMode = SourcePublishMode.Manual;
        SaveUtilityDeliverySettings();
    }

    private void PinPlayTab_Click(object sender, RoutedEventArgs e) =>
        PinPlayTabRequested?.Invoke(this, EventArgs.Empty);

    private void OpenPinnedPlayTab_Click(object sender, RoutedEventArgs e) =>
        OpenPinnedPlayTabRequested?.Invoke(this, EventArgs.Empty);

    private void ClearPin_Click(object sender, RoutedEventArgs e) =>
        ClearPlayTabPinRequested?.Invoke(this, EventArgs.Empty);

    private async void StartNewPlayThread_Click(object sender, RoutedEventArgs e)
    {
        if (StartNewPlayThreadAsync is null)
            return;

        StartNewPlayThreadButton.IsEnabled = false;
        try
        {
            await StartNewPlayThreadAsync();
            _bundle = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
            UpdateSessionStatusUi();
        }
        finally
        {
            StartNewPlayThreadButton.IsEnabled =
                !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        }
    }

    private async void DraftNewProjectChat_Click(object sender, RoutedEventArgs e)
    {
        if (DraftNewProjectChatAsync is null)
            return;

        DraftNewProjectChatButton.IsEnabled = false;
        try
        {
            await DraftNewProjectChatAsync();
            _bundle = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
            UpdateSessionStatusUi();
        }
        finally
        {
            DraftNewProjectChatButton.IsEnabled =
                !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        }
    }

    private void CancelProjectChatDraft_Click(object sender, RoutedEventArgs e)
    {
        CancelProjectChatDraft?.Invoke();
        _bundle = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
        UpdateSessionStatusUi();
    }

    private void PinUtilityTab_Click(object sender, RoutedEventArgs e) =>
        PinUtilityTabRequested?.Invoke(this, EventArgs.Empty);

    private void OpenPinnedUtilityTab_Click(object sender, RoutedEventArgs e) =>
        OpenPinnedUtilityTabRequested?.Invoke(this, EventArgs.Empty);

    private void ClearUtilityPin_Click(object sender, RoutedEventArgs e) =>
        ClearUtilityTabPinRequested?.Invoke(this, EventArgs.Empty);

    private void GoToSourcesTab_Click(object sender, RoutedEventArgs e) =>
        SelectTab(PlaySettingsTab.Sources);

    private async void OpenUtilityThread_Click(object sender, RoutedEventArgs e)
    {
        if (UtilityJobsList.SelectedItem is not UtilityJobRowViewModel row || OpenUtilityThreadAsync is null)
            return;

        await OpenUtilityThreadAsync(row.UtilityJobId);
    }

    private async void RotateUtilityThread_Click(object sender, RoutedEventArgs e)
    {
        if (UtilityJobsList.SelectedItem is not UtilityJobRowViewModel row || RotateUtilityThreadAsync is null)
            return;

        await RotateUtilityThreadAsync(row.UtilityJobId);
        ReloadBundleFromStore();
        BindUtilityJobs();
    }

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
        var readiness = ProjectSourceInjectionService.Evaluate(_bundle);
        UpdateReadinessBanner(readiness);
        UpdatePublishModeUi();

        InstructionsPastedLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(_bundle);
        ProbeProjectButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(_bundle);
        _sourceRows.Clear();
        foreach (var entry in _bundle.SourceManifest.Entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SourcePublishRowViewModel(entry, sourcesDir, _bundle.Metadata.Id);
            row.ManifestEntryChanged += (_, _) => _sourceAutosave?.ScheduleSave();
            _sourceRows.Add(row);
        }

        SourcesGrid.ItemsSource = _sourceRows;
        UpdateCompareButton();
        BindSourceEditReview();
    }

    private void UpdateCompareButton()
    {
        CompareSourceButton.IsEnabled = SourcesGrid.SelectedItem is SourcePublishRowViewModel { HasMirror: true };
    }

    private void SourcesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCompareButton();

    private void BindSourceEditReview()
    {
        var before = _bundle.Scenario.SourceEditReviewQueue.Count;
        ProjectSourceImportService.DeduplicateSourceEditReviewQueue(_bundle);
        if (_bundle.Scenario.SourceEditReviewQueue.Count != before)
            AdventureStore.Save(_bundle);

        var queue = _bundle.Scenario.SourceEditReviewQueue;
        SourceEditReviewHeader.Text = queue.Count > 0
            ? $"{queue.Count} source edit proposal(s) awaiting review"
            : "";
        SourceEditReviewList.ItemsSource = queue
            .Select(e => new SourceEditReviewListItem(e))
            .ToList();
        if (SourceEditReviewList.Items.Count > 0)
            SourceEditReviewList.SelectedIndex = 0;
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
        var instructionHint = InstructionSourcesPolicy.FormatInstructionDriftHint(_bundle);
        var instructionSuffix = string.IsNullOrWhiteSpace(instructionHint) ? "" : $"\n{instructionHint}";
        var probeSuffix = string.IsNullOrWhiteSpace(readiness.ProbeWarning) ? "" : $"\n{readiness.ProbeWarning}";
        if (readiness.CanDelegateStaticContent)
        {
            ReadinessBanner.Background = InstructionSourcesPolicy.InstructionDomainChanged(_bundle)
                ? (Brush)FindResource("WarningSubtleBrush")
                : (Brush)FindResource("SuccessSubtleBrush");
            ReadinessBannerText.Foreground = InstructionSourcesPolicy.InstructionDomainChanged(_bundle)
                ? UiBrushes.Warning(this)
                : UiBrushes.Success(this);
            ReadinessBannerText.Text =
                $"Source-delegated packets (manual publish) — {readiness.SyncedFiles.Count} file(s) published to Project.{instructionSuffix}{probeSuffix}";
            return;
        }

        ReadinessBanner.Background = (Brush)FindResource("WarningSubtleBrush");
        ReadinessBannerText.Foreground = UiBrushes.Warning(this);
        var reason = readiness.BlockingReason ?? "Using inline lore in packets";
        var action = string.IsNullOrWhiteSpace(readiness.SuggestedAction)
            ? ""
            : $" {readiness.SuggestedAction}.";
        var duplicateText = _bundle.SourceManifest.LastKnownDuplicateRemotes > 0
            ? $" {_bundle.SourceManifest.LastKnownDuplicateRemotes} duplicate remote file(s) detected — use Source Manager → Probe project."
            : "";
        ReadinessBannerText.Text = $"Fat fallback (manual publish) — {reason}.{action}{duplicateText}{instructionSuffix}{probeSuffix}";
    }

    private void DesignInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (InstructionDesignerDialog.Show(this, _bundle.Metadata.Id) != true)
            return;

        ReloadBundleFromStore();
        BindWorldPanel();
        BindAdventureSettings();
    }

    private void CopyInstructions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(InstructionSourcesPolicy.BuildStaticInstructionsBody(_bundle));
            MessageBox.Show(this, "Instructions copied to clipboard. Paste into your ChatGPT Project settings.", "Copy instructions");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewInstructions_Click(object sender, RoutedEventArgs e)
    {
        var text = InstructionSourcesPolicy.BuildStaticInstructionsBody(_bundle);
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
        InstructionSourcesPolicy.RecordInstructionsManuallyPublished(_bundle);
        _sourceAutosave?.SaveNow();
        InstructionsPastedLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(_bundle);
    }

    private async void ManageSources_Click(object sender, RoutedEventArgs e)
    {
        if (OpenSourceManagerAsync is not null)
            await OpenSourceManagerAsync();
        ReloadBundleFromStore();
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
        AdventureStore.Save(_bundle);
        BindSources();
        RefreshMergedPreview();
    }

    private void OpenSourcesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(_bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
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
        foreach (var row in _sourceRows)
            row.IsPublished = true;

        AdventureStore.Save(_bundle);
        BindSources();
        RefreshMergedPreview();
    }

    private async void EditSourcesAi_Click(object sender, RoutedEventArgs e)
    {
        if (RunSourceEditJobAsync is null)
            return;

        var dlg = new TextPromptDialog(
            "Edit sources with AI",
            "Describe what to change in scenario, world, plot, or instructions:",
            "Expand the world rules with more detail about magic.")
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
        AdventureStore.Save(_bundle);
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
        AdventureStore.Save(_bundle);
        BindSourceEditReview();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadBundleFromStore()
    {
        var reloaded = AdventureStore.Load(_bundle.Metadata.Id);
        if (reloaded is null)
            return;

        _bundle = reloaded;
        BindSources();
        RefreshMergedPreview();
    }

    public void RefreshReviewPanels()
    {
        BindWorldPanel();
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

    private void RefreshMergedPreview()
    {
        var (playerLine, sourceLabel) = ResolvePreviewPlayerLine();
        PreviewSourceLine.Text = $"Preview uses: {sourceLabel}";

        if (string.IsNullOrWhiteSpace(playerLine))
        {
            PacketMetaLine.Text = "Enter a fallback line, queue line, or composer text to preview the merged packet.";
            MergedPreviewBox.Text = "";
            _lastMergedText = "";
            return;
        }

        var attachmentContext = ResolvePreviewAttachmentContext?.Invoke();
        var prepared = PromptInjectionService.PrepareSend(_bundle, playerLine, attachmentContext);
        _lastMergedText = prepared.MergedText;
        MergedPreviewBox.Text = FormatPreviewText(prepared.MergedText);

        var readiness = ProjectSourceInjectionService.Evaluate(_bundle);
        var modePart = readiness.CanDelegateStaticContent
            ? $"Source-delegated ({readiness.SyncedFiles.Count} files)"
            : $"Fat fallback — {readiness.BlockingReason ?? "inline lore"}";
        var pointers = prepared.ResolvedSectionPointers.Count > 0
            ? prepared.ResolvedSectionPointers
            : prepared.TriggeredCardNames;
        var pointerLabel = _bundle.Metadata.Settings.UseSectionInjection ? "Sections" : "Triggered cards";
        var pointerText = pointers.Count > 0 ? string.Join(", ", pointers) : "none";
        var attachMode = _bundle.Metadata.Settings.AttachmentContextMode;
        var attachNote = attachmentContext is { HasAttachments: true }
            ? $" | Attachments: {attachmentContext.Attachments.Count} ({attachMode})"
            : "";
        var injectionNote = _bundle.Metadata.Settings.UseSectionInjection ? " | Section injection v2" : "";
        PacketMetaLine.Text =
            $"Packet: {prepared.Mode} ({modePart}) | Chars: {prepared.MergedText.Length} | Hash: {prepared.Hash} | Trimmed: {prepared.WasTrimmed}{attachNote}{injectionNote}\n" +
            $"Project: {_bundle.Metadata.LinkedProjectId ?? "none"} | {pointerLabel}: {pointerText}";
    }

    private (string Line, string SourceLabel) ResolvePreviewPlayerLine()
    {
        var compose = ResolvePreviewComposerText?.Invoke()?.Trim();
        if (!string.IsNullOrWhiteSpace(compose))
            return (compose, "in-page composer (Play mode)");

        var fallback = PreviewPlayerLineBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return (fallback, "fallback player line");

        var queueLine = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queueLine))
            return (queueLine, "continuation queue line 1");

        return ("", "none");
    }

    private void SchedulePreviewRefresh()
    {
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void QueueBox_TextChanged(object sender, TextChangedEventArgs e) => SchedulePreviewRefresh();

    private void PreviewPlayerLineBox_TextChanged(object sender, TextChangedEventArgs e) => SchedulePreviewRefresh();

    private string FormatPreviewText(string mergedText) =>
        _bundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(mergedText)
            : mergedText;

    private void SaveQueueAndPreviewLine()
    {
        _bundle.ContinuationQueue = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        PreviewPlayerLine = PreviewPlayerLineBox.Text.Trim();
    }

    private void QueueBox_LostFocus(object sender, RoutedEventArgs e) => RefreshMergedPreview();

    private void PreviewPlayerLineBox_LostFocus(object sender, RoutedEventArgs e) => RefreshMergedPreview();

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => RefreshMergedPreview();

    private void CopyPacket_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastMergedText))
        {
            RefreshMergedPreview();
            if (string.IsNullOrWhiteSpace(_lastMergedText))
                return;
        }

        try
        {
            Clipboard.SetText(_lastMergedText);
            MessageBox.Show(this, "Merged packet copied to clipboard.", "Copy packet");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewFull_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastMergedText))
            RefreshMergedPreview();
        if (string.IsNullOrWhiteSpace(_lastMergedText))
            return;

        var dlg = new ContextViewerDialog(_lastMergedText, PacketMetaLine.Text,
            useStructuredPreview: _bundle.Metadata.Settings.UseContextTags)
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    private void PreviewStartPacket_Click(object sender, RoutedEventArgs e)
    {
        var packetText = AdventureBootstrapService.BuildStartPacket(_bundle);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(packetText)))[..16];
        var readiness = ProjectSourceInjectionService.Evaluate(_bundle);
        var modeLabel = readiness.CanDelegateStaticContent ? "Source-delegated" : "Fat";
        var dlg = new ContextViewerDialog(
            packetText,
            $"Start packet preview | Mode: {modeLabel} | Chars: {packetText.Length} | Hash: {hash}",
            useStructuredPreview: _bundle.Metadata.Settings.UseContextTags)
        {
            Owner = this,
        };
        dlg.ShowDialog();
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
        AdventureStore.Save(_bundle);
        BindMemoryAndCards();
    }

    private void PinMemory_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryList.SelectedItem is not MemoryEntry entry)
            return;

        entry.Pinned = !entry.Pinned;
        AdventureStore.Save(_bundle);
        BindMemoryAndCards();
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
        AdventureStore.Save(_bundle);
        BindMemoryAndCards();
        MemoryReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DismissMemoryReview_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryReviewList.SelectedItem is not MemoryEntry item)
            return;

        _bundle.Memory.ReviewQueue.Remove(item);
        AdventureStore.Save(_bundle);
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

        AdventureStore.Save(_bundle);
        BindMemoryAndCards();
        CardReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DismissCardReview_Click(object sender, RoutedEventArgs e)
    {
        if (CardReviewList.SelectedItem is not CardReviewListItem row)
            return;

        _bundle.Cards.ReviewQueue.Remove(row.Item);
        AdventureStore.Save(_bundle);
        BindMemoryAndCards();
        CardReviewList.SelectedItem = null;
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AcceptSummary_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ProposedSummaryBox.Text))
            _bundle.Summary.RollingSummary = ProposedSummaryBox.Text.Trim();

        _bundle.Summary.ProposedSummary = null;
        _bundle.Summary.PendingReview = false;
        SummaryBox.Text = _bundle.Summary.RollingSummary;
        AdventureStore.Save(_bundle);
        BindWorldPanel();
        ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DismissSummary_Click(object sender, RoutedEventArgs e)
    {
        _bundle.Summary.ProposedSummary = null;
        _bundle.Summary.PendingReview = false;
        AdventureStore.Save(_bundle);
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
            Triggers = ["keyword"],
            Content = "Lore text",
        });
        AdventureStore.Save(_bundle);
        BindMemoryAndCards();
        RefreshMergedPreview();
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        SaveQueueAndPreviewLine();
        SaveWorldPanel();
        SaveAdventureSettings();
        SavePlaySurfaceSettings();
        SaveJobOverrideSettings();
        SaveAiActionGuides();
        SaveStoryContextSettings();
        AdventureStore.Save(_bundle);
        if (AutoSyncInstructionsCheck.IsChecked == true && SyncInstructionsAsync is not null)
            await SyncInstructionsAsync();
        DialogResult = true;
        Close();
    }

    private sealed class PromptHistoryListItem(PromptHistoryEntry entry)
    {
        public PromptHistoryEntry Entry { get; } = entry;

        public string DisplayLabel =>
            $"{Entry.At:g}  hash={Entry.PacketHash ?? "—"}  ({Entry.PacketText.Length} chars)";
    }

    private sealed class UtilityJobRowViewModel(AdventureBundle bundle, string jobId)
    {
        public string JobId { get; } = jobId;

        public string UtilityJobId { get; } = GenerationJobHandlers.GetUtilityJobId(jobId);

        public string DisplayLabel { get; } = GenerationUtilitySessionService.FormatUtilityStatus(bundle, jobId);
    }

    private sealed class AiActionRowViewModel(string jobId)
    {
        public string JobId { get; } = jobId;

        public string DisplayLabel { get; } = GenerationJobGuideService.GetDisplayLabel(jobId);

        public override string ToString() => DisplayLabel;
    }

    private sealed class SourceEditReviewListItem
    {
        public SourceEditReviewListItem(SourceEditReviewItem item) => Item = item;

        public SourceEditReviewItem Item { get; }

        public string DisplayLabel =>
            $"{Item.TargetFile} · {Item.Operation}: {(Item.Content.Length <= 72 ? Item.Content : Item.Content[..72] + "…")}";

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

        public IReadOnlyList<string> PlacementOptions { get; } = ["Left", "Hidden"];
    }
}
