using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

public sealed partial class PlaySettingsWorkbenchPage : UserControl, IPlaySettingsHost
{
    private readonly PlaySettingsInjectionTab _injectionTab = new();
    private readonly PlaySettingsNextSendTab _nextSendTab = new();
    private readonly PlaySettingsWorldTab _worldTab = new();
    private readonly PlaySettingsUtilityJobsTab _utilityJobsTab = new();
    private readonly PlaySettingsSessionTab _sessionTab = new();
    private readonly PlaySettingsPlaySurfaceTab _playSurfaceTab = new();
    private readonly PlaySettingsNarratorContractTab _narratorContractTab = new();
    private readonly PlaySettingsMemoryCardsTab _memoryCardsTab = new();
    private readonly PlaySettingsSourcesTab _sourcesTab = new();
    private readonly PlaySettingsHistoryTab _historyTab = new();
    private readonly PlaySettingsPreviewTab _previewTab = new();

    private readonly IReadOnlyList<IPlaySettingsTabPanel> _tabPanels;
    private PlaySettingsWorkbenchContext? _ctx;
    private PlaySettingsEditorSession? _playSession;
    private readonly System.Threading.Timer? _previewDebounce;
    private bool _playSettingsPersisted;

    public PlaySettingsWorkbenchPage(AdventureBundle bundle, string? previewPlayerLine, PlaySettingsTab initialTab)
    {
        _playSession = PlaySettingsEditorSession.Attach(bundle);
        _playSession.IsDirty = () => HasUnsavedPlaySettings();

        _ctx = new PlaySettingsWorkbenchContext(
            bundle,
            _playSession,
            NarratorSettingsSession.Attach(bundle),
            UiChromeStore.Load(),
            previewPlayerLine)
        {
            Host = this,
            NavigateToTab = SelectTab,
        };
        _ctx.ReviewQueueChanged += () => ReviewQueueChanged?.Invoke(this, EventArgs.Empty);
        _ctx.TransportSettingsCommitted += () => TransportSettingsCommitted?.Invoke(this, EventArgs.Empty);

        _tabPanels =
        [
            _injectionTab,
            _nextSendTab,
            _worldTab,
            _utilityJobsTab,
            _sessionTab,
            _playSurfaceTab,
            _narratorContractTab,
            _memoryCardsTab,
            _sourcesTab,
            _historyTab,
            _previewTab,
        ];

        InitializeComponent();
        AdventureTitleLine.Text = bundle.Metadata.Title;
        _previewTab.RefreshRequested += (_, _) => _ = RefreshMergedPreviewAsync();
        _previewTab.CopyRequested += (_, _) => CopyPacket();
        InitNavigation(initialTab);
        WireTabEvents();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => ApplyWorkbenchLayout();

        _previewDebounce = new System.Threading.Timer(
            _ => _ = WinUiShellHost.RunOnUiThreadAsync(RefreshMergedPreviewAsync),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public bool PlaySettingsPersisted => _playSettingsPersisted;

    public event EventHandler? PinPlayTabRequested;
    public event EventHandler? OpenPinnedPlayTabRequested;
    public event EventHandler? ClearPlayTabPinRequested;
    public event EventHandler? ReviewQueueChanged;
    public event EventHandler? TransportSettingsCommitted;

    public Func<string?>? ResolvePreviewComposerText { get; set; }
    public Func<AttachmentContext?>? ResolvePreviewAttachmentContext { get; set; }
    public Func<Task<int>>? ResolveThreadUserTurnCountAsync { get; set; }
    public Func<Task>? SyncSourcesAsync { get; set; }
    public Func<Task>? RefreshSourcesStatusAsync { get; set; }
    public Func<Task>? ReconcileDuplicatesAsync { get; set; }
    public Action? OpenThreadsHub { get; set; }
    public Func<PlayThreadStartRequest?, Task>? StartNewPlayThreadAsync { get; set; }
    public Action? OpenPlayHandoffDialog { get; set; }
    public Action<ProposalReviewCategory?>? OpenProposalReviewHub { get; set; }
    public Func<Task>? DraftNewProjectChatAsync { get; set; }
    public Action? CancelProjectChatDraft { get; set; }
    public Func<string, Task<UtilityStoryContextBuildResult>>? PreviewLiveStoryContextAsync { get; set; }
    public Func<string, IReadOnlyList<DomAttachmentPayload>?, string?, Task>? RunSourceEditJobAsync { get; set; }
    public Func<string, Task>? RunUtilityJobWithAttachmentsAsync { get; set; }
    public Func<Task<IReadOnlyList<ConversationFileRef>>>? ListThreadFilesAsync { get; set; }
    public Func<ConversationFileRef, Task<byte[]>>? DownloadThreadFileAsync { get; set; }
    public Func<Task>? OpenProjectSettingsAsync { get; set; }
    public Func<Task>? PushInstructionsNowAsync { get; set; }
    public Func<Task>? RefreshSummaryAsync { get; set; }
    public Func<Task>? SuggestMemoriesAsync { get; set; }
    public Func<Task>? GenerateCardsAsync { get; set; }
    public Func<Guid, Task>? ExpandStoryCardAsync { get; set; }
    public Func<Task>? SyncInstructionsAsync { get; set; }
    public Func<Task>? ProbeSourcesAsync { get; set; }
    public Func<Task>? OpenApiSyncDiagnosticsAsync { get; set; }
    public Func<string, Task>? ProbeSourceFileAsync { get; set; }
    public Func<string, string, Task<string?>>? SynthesizeSourceAsync { get; set; }
    public Func<Task>? PromptThreadLogSyncAsync { get; set; }
    public Func<Task>? PromptThreadLogSnapshotAsync { get; set; }
    public Func<Task>? PromptThreadLogDumpAsync { get; set; }

    public void RefreshHostDelegates() => _sourcesTab.RefreshHostDelegates();

    public async Task<bool> CommitAsync()
    {
        if (_ctx is null || _playSession is null)
            return false;

        try
        {
            FlushAllTabs();
            if (HasUnsavedPlaySettings())
                PersistPlaySettingsToDisk();

            if (_sessionTab.AutoSyncInstructions && SyncInstructionsAsync is not null)
                await SyncInstructionsAsync();

            ReloadBundleFromStore();
            RefreshAllTabs();
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("play_settings_commit_failed", ex);
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Play settings", ex.Message);
            return false;
        }
    }

    public bool HasUnsavedPlaySettings() =>
        CanEvaluateDirtyState() && BuildStagingEditsSummary().Count > 0;

    private bool CanEvaluateDirtyState() =>
        _ctx is not null && _ctx.PersistedBaseline is not null && !_ctx.Binding;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        _ctx.Binding = true;
        RefreshAllTabs();
        CapturePersistedBaseline();
        _ctx.Binding = false;
        _ = RefreshMergedPreviewAsync();
        UpdatePlaySettingsSaveUi();
        UpdateInjectionPresetChip();
        ApplyWorkbenchLayout();
    }

    private PlaySettingsNavItem? _selectedNav;
    private readonly IReadOnlyList<PlaySettingsNavItem> _navCatalog = PlaySettingsNavItem.BuildCatalog();
    private List<PlaySettingsNavItem> _filteredNav = [];
    private readonly Dictionary<PlaySettingsTab, UIElement> _tabContent = new();

    private void InitNavigation(PlaySettingsTab initialTab)
    {
        _tabContent[PlaySettingsTab.Injection] = _injectionTab;
        _tabContent[PlaySettingsTab.NextSend] = _nextSendTab;
        _tabContent[PlaySettingsTab.World] = _worldTab;
        _tabContent[PlaySettingsTab.UtilityJobs] = _utilityJobsTab;
        _tabContent[PlaySettingsTab.Session] = _sessionTab;
        _tabContent[PlaySettingsTab.PlaySurface] = _playSurfaceTab;
        _tabContent[PlaySettingsTab.Settings] = _narratorContractTab;
        _tabContent[PlaySettingsTab.MemoryCards] = _memoryCardsTab;
        _tabContent[PlaySettingsTab.Sources] = _sourcesTab;
        _tabContent[PlaySettingsTab.History] = _historyTab;
        _tabContent[PlaySettingsTab.Preview] = _previewTab;

        SettingsNavList.ItemsSource = _filteredNav;
        SettingsNavList.ContainerContentChanging += SettingsNavList_ContainerContentChanging;
        ApplyNavFilter("");
        SelectTab(initialTab);
    }

    private void NavSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyNavFilter(NavSearchBox.Text);

    private void ApplyNavFilter(string query)
    {
        var term = query.Trim();
        if (string.IsNullOrEmpty(term))
        {
            _filteredNav = _navCatalog.ToList();
        }
        else
        {
            _filteredNav = [];
            string? currentGroup = null;
            foreach (var item in _navCatalog)
            {
                if (item.IsHeader)
                {
                    currentGroup = item.Group;
                    continue;
                }

                if (MatchesNavFilter(item, term))
                {
                    if (currentGroup is not null &&
                        (_filteredNav.Count == 0 || _filteredNav[^1].Group != currentGroup))
                    {
                        _filteredNav.Add(_navCatalog.First(h => h.IsHeader && h.Group == currentGroup));
                    }

                    _filteredNav.Add(item);
                }
            }
        }

        SettingsNavList.ItemsSource = null;
        SettingsNavList.ItemsSource = _filteredNav;

        if (_selectedNav?.Tab is { } selectedTab &&
            _filteredNav.Any(i => i.Tab == selectedTab))
        {
            SettingsNavList.SelectedItem = _filteredNav.First(i => i.Tab == selectedTab);
        }
        else
        {
            var first = _filteredNav.FirstOrDefault(i => i.Tab is not null);
            if (first is not null)
            {
                SettingsNavList.SelectedItem = first;
                _selectedNav = first;
                ShowNavSection(first);
            }
        }
    }

    private static bool MatchesNavFilter(PlaySettingsNavItem item, string term) =>
        item.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.ScopeLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
        || item.Group.Contains(term, StringComparison.OrdinalIgnoreCase);

    private void SettingsNavList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
            return;

        var isHeader = args.Item is PlaySettingsNavItem { IsHeader: true };
        container.IsEnabled = !isHeader;
        container.IsHitTestVisible = !isHeader;

        if (!args.InRecycleQueue)
            UpdateNavItemVisualState(container);
    }

    private void SettingsNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var removed in e.RemovedItems)
        {
            if (SettingsNavList.ContainerFromItem(removed) is ListViewItem container)
                UpdateNavItemVisualState(container);
        }

        foreach (var added in e.AddedItems)
        {
            if (SettingsNavList.ContainerFromItem(added) is ListViewItem container)
                UpdateNavItemVisualState(container);
        }

        if (SettingsNavList.SelectedItem is not PlaySettingsNavItem nav || nav.IsHeader || nav.Tab is null)
        {
            if (_selectedNav is not null)
                SettingsNavList.SelectedItem = _selectedNav;
            return;
        }

        _selectedNav = nav;
        ShowNavSection(nav);
    }

    private static void UpdateNavItemVisualState(ListViewItem container)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root)
            return;

        var bar = root.FindName("SelectionBar") as Border;
        var background = root.FindName("NavBackground") as Border;
        if (bar is null || background is null)
            return;

        if (container.IsSelected)
        {
            bar.Opacity = 1;
            background.Background = (Brush)Application.Current.Resources["AccentSubtleBrush"];
        }
        else
        {
            bar.Opacity = 0;
            background.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private void ShowNavSection(PlaySettingsNavItem nav)
    {
        SectionTitleText.Text = nav.Label;
        SectionHintText.Text = nav.Description;
        SectionScopeBadge.ScopeLabel = nav.ScopeLabel ?? "";

        if (nav.Tab is { } tab && _tabContent.TryGetValue(tab, out var content))
            SettingsContentHost.Content = content;
        else
            SettingsContentHost.Content = null;

        var isPreview = nav.Tab == PlaySettingsTab.Preview;
        SectionHeaderPanel.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;

        if (isPreview)
            _ = RefreshMergedPreviewAsync();

        ApplyWorkbenchLayout();
    }

    private void BodyGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyWorkbenchLayout();

    private void ApplyWorkbenchLayout()
    {
        if (BodyGrid is null || SectionContentPanel is null || SettingsScrollViewer is null)
            return;

        var shellWidth = ActualWidth;
        if (shellWidth > 0)
            NavColumnDefinition.Width = new GridLength(PlaySettingsWorkbenchLayout.ResolveNavRailWidth(shellWidth));

        var contentWidth = SettingsScrollViewer.ActualWidth;
        if (contentWidth <= 0 && shellWidth > 0)
        {
            var navWidth = PlaySettingsWorkbenchLayout.ResolveNavRailWidth(shellWidth);
            contentWidth = Math.Max(0, shellWidth - navWidth - 1 - SettingsScrollViewer.Padding.Left - SettingsScrollViewer.Padding.Right);
        }

        var mode = _selectedNav?.Tab is { } tab
            ? PlaySettingsWorkbenchLayout.GetLayoutMode(tab)
            : PlaySettingsContentLayoutMode.FormColumn;
        var snapshot = PlaySettingsWorkbenchLayout.Resolve(mode, contentWidth);

        SectionContentPanel.MaxWidth = snapshot.ContentMaxWidth;
        SectionContentPanel.HorizontalAlignment = snapshot.ContentHorizontalAlignment;
        SettingsScrollViewer.VerticalScrollBarVisibility = snapshot.OuterVerticalScroll;
        HeaderHintText.MaxWidth = mode == PlaySettingsContentLayoutMode.FormColumn
            ? PlaySettingsWorkbenchLayout.CurrentViewport.FormColumnMaxWidth
            : double.PositiveInfinity;
    }

    public void SelectTab(PlaySettingsTab tab)
    {
        foreach (var item in _filteredNav.Count > 0 ? _filteredNav : _navCatalog)
        {
            if (item.Tab == tab)
            {
                SettingsNavList.SelectedItem = item;
                _selectedNav = item;
                ShowNavSection(item);
                return;
            }
        }

        foreach (var item in _navCatalog)
        {
            if (item.Tab == tab)
            {
                ApplyNavFilter("");
                SettingsNavList.SelectedItem = item;
                _selectedNav = item;
                ShowNavSection(item);
                return;
            }
        }

        var first = (_filteredNav.Count > 0 ? _filteredNav : _navCatalog).FirstOrDefault(i => i.Tab is not null);
        if (first is not null)
        {
            SettingsNavList.SelectedItem = first;
            _selectedNav = first;
            ShowNavSection(first);
        }
    }

    public void RequestPinPlayTab() => PinPlayTabRequested?.Invoke(this, EventArgs.Empty);

    public void RequestOpenPinnedPlayTab() => OpenPinnedPlayTabRequested?.Invoke(this, EventArgs.Empty);

    public void RequestClearPlayTabPin() => ClearPlayTabPinRequested?.Invoke(this, EventArgs.Empty);

    private void WireTabEvents()
    {
        foreach (var panel in _tabPanels)
            panel.SettingsChanged += (_, _) =>
            {
                if (_ctx is null || _ctx.Binding)
                    return;

                SchedulePreviewRefresh();
                UpdatePlaySettingsSaveUi();
                UpdateInjectionPresetChip();
            };

        ReviewQueueChanged += (_, _) => _ctx?.RaiseReviewQueueChanged();
        TransportSettingsCommitted += (_, _) => _ctx?.RaiseTransportSettingsCommitted();
    }

    private void RefreshAllTabs()
    {
        if (_ctx is null)
            return;

        foreach (var panel in _tabPanels)
            panel.Bind(_ctx);
    }

    private void FlushAllTabs()
    {
        if (_ctx is null)
            return;

        foreach (var panel in _tabPanels)
            panel.Flush(_ctx);

        _injectionTab.FlushNarratorToSession();
        if (!ReferenceEquals(_ctx.NarratorSession.Bundle, _ctx.Bundle))
            NarratorSettingsSession.CopyNarratorSettings(
                _ctx.NarratorSession.Bundle.Metadata.Settings,
                _ctx.Bundle.Metadata.Settings);

        _ctx.PreviewPlayerLine = _previewTab.GetSampleLineText();
        _playSurfaceTab.SaveChrome(_ctx);
    }

    private void CapturePersistedBaseline()
    {
        if (_ctx is null)
            return;

        FlushAllTabs();
        _ctx.PersistedBaseline = PlaySettingsEditorBaseline.Capture(
            _ctx.Bundle,
            _ctx.ChromeSettings,
            _ctx.PreviewPlayerLine,
            _ctx.NarratorSession.Bundle.Metadata.Settings);
        _ctx.PreviewPlayerLineBaseline = _ctx.PreviewPlayerLine;
    }

    private IReadOnlyList<string> BuildStagingEditsSummary()
    {
        if (_ctx?.PersistedBaseline is null)
            return [];

        FlushAllTabs();
        return _ctx.PersistedBaseline.Diff(
            _ctx.Bundle,
            _ctx.ChromeSettings,
            _ctx.PreviewPlayerLine,
            _ctx.NarratorSession.Bundle.Metadata.Settings);
    }

    private void PersistPlaySettingsToDisk()
    {
        if (_ctx is null || _playSession is null)
            return;

        _sourcesTab.Flush(_ctx);
        _playSession.Commit(
            FlushAllTabs,
            preserveNarratorSettings: false,
            rebindAllControls: () =>
            {
                _ctx.NarratorSession.RepointWorkingBundle(_ctx.Bundle);
                _playSettingsPersisted = true;
                CapturePersistedBaseline();
                RefreshAllTabs();
                UpdatePlaySettingsSaveUi();
                TransportSettingsCommitted?.Invoke(this, EventArgs.Empty);
            });

        TransportSettingsStore.Commit(_ctx.Bundle, caller: nameof(PlaySettingsWorkbenchPage));
    }

    private void ReloadBundleFromStore()
    {
        if (_ctx is null || _playSession is null)
            return;

        _playSession.SyncFromDisk(
            preserveNarratorSettings: false,
            rebindAllControls: RefreshAllTabs,
            refreshExternalOnly: RefreshAllTabs);
    }

    private void UpdatePlaySettingsSaveUi()
    {
        if (!CanEvaluateDirtyState())
            return;

        var dirty = HasUnsavedPlaySettings();
        DirtyBadge.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;

        var resources = Application.Current.Resources;
        if (!dirty)
        {
            SaveStatusIcon.Glyph = "\uE73E";
            SaveStatusIcon.Foreground = (Brush)resources["TextMutedBrush"];
            PlaySettingsSaveLine.Text = _playSettingsPersisted
                ? "All changes saved."
                : "No unsaved changes.";
            EditCountLine.Visibility = Visibility.Collapsed;
            DirtyEditsPanel.Visibility = Visibility.Collapsed;
            DirtyEditsPanel.Items.Clear();
            return;
        }

        var edits = BuildStagingEditsSummary();
        SaveStatusIcon.Glyph = "\uE7BA";
        SaveStatusIcon.Foreground = (Brush)resources["WarningBrush"];
        PlaySettingsSaveLine.Text = edits.Count == 0
            ? "Unsaved edits"
            : $"Unsaved: {string.Join("; ", edits.Take(3))}{(edits.Count > 3 ? "…" : "")}";
        EditCountLine.Text = edits.Count > 0 ? $"{edits.Count} field{(edits.Count == 1 ? "" : "s")}" : "";
        EditCountLine.Visibility = edits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PopulateDirtyEditLinks(edits);
    }

    private void PopulateDirtyEditLinks(IReadOnlyList<string> edits)
    {
        DirtyEditsPanel.Items.Clear();
        if (edits.Count == 0)
        {
            DirtyEditsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var edit in edits.Take(6))
        {
            var tab = MapEditHintToTab(edit);
            var link = new HyperlinkButton
            {
                Content = edit,
                Padding = new Thickness(0),
                FontSize = 11,
                Tag = tab,
            };
            link.Click += DirtyEditLink_Click;
            DirtyEditsPanel.Items.Add(link);
        }

        DirtyEditsPanel.Visibility = Visibility.Visible;
    }

    private void DirtyEditLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: PlaySettingsTab tab })
            SelectTab(tab);
    }

    private static PlaySettingsTab MapEditHintToTab(string hint)
    {
        var h = hint.ToLowerInvariant();
        if (h.Contains("job") || h.Contains("utility") || h.Contains("story context") || h.Contains("override"))
            return PlaySettingsTab.UtilityJobs;
        if (h.Contains("automation"))
            return PlaySettingsTab.Session;
        if (h.Contains("injection") || h.Contains("policy"))
            return PlaySettingsTab.Injection;
        if (h.Contains("preview") || h.Contains("player line"))
            return PlaySettingsTab.Preview;
        if (h.Contains("continuation") || h.Contains("next-send") || h.Contains("turn override"))
            return PlaySettingsTab.NextSend;
        if (h.Contains("summary") || h.Contains("location") || h.Contains("objective") || h.Contains("author"))
            return PlaySettingsTab.World;
        if (h.Contains("instruction") || h.Contains("project"))
            return PlaySettingsTab.Sources;
        if (h.Contains("narrator") || h.Contains("contract"))
            return PlaySettingsTab.Settings;
        if (h.Contains("play surface") || h.Contains("layout") || h.Contains("chrome"))
            return PlaySettingsTab.PlaySurface;
        if (h.Contains("memory") || h.Contains("card"))
            return PlaySettingsTab.MemoryCards;
        return PlaySettingsTab.Injection;
    }

    private void UpdateInjectionPresetChip()
    {
        if (_ctx is null)
            return;

        PlayInjectionPolicyService.EnsureDefaults(_ctx.Bundle.Metadata);
        var policy = PlayInjectionPolicyService.Resolve(_ctx.Bundle.Metadata.Settings);
        var presetId = policy.InjectionPresetId ?? InjectionPresetIds.Standard;
        var spec = InjectionPresetLibrary.Find(presetId);
        var label = spec?.DisplayName ?? "Custom";
        InjectionPresetChipText.Text = $"Preset: {label}";
        InjectionPresetChip.Visibility = Visibility.Visible;
    }

    private void SchedulePreviewRefresh() =>
        _previewDebounce?.Change(300, Timeout.Infinite);

    private async Task RefreshMergedPreviewAsync()
    {
        if (_ctx is null)
            return;

        FlushAllTabs();
        var staging = InjectionSettingsStaging.CloneBundleForStaging(_ctx.Bundle);
        ApplyPendingUiToStagingBundle(staging);

        var (playerLine, sourceLabel) = ResolvePreviewPlayerLine();
        _previewTab.SetSourceLine(sourceLabel);

        var attachmentContext = ResolvePreviewAttachmentContext?.Invoke();
        var priorCount = ResolveThreadUserTurnCountAsync is not null
            ? await ResolveThreadUserTurnCountAsync()
            : 0;

        var snapshot = InjectionPreviewCoordinator.Refresh(
            staging,
            playerLine,
            attachmentContext,
            ResolvePreviewComposerText,
            _previewTab.GetSampleLineText(),
            _ctx.LastPreviewSnapshot,
            priorCount);

        _ctx.LastPreviewSnapshot = snapshot;
        _ctx.LastMergedText = snapshot.MergedText;
        _previewTab.PreviewPanel.ApplySnapshot(snapshot);
        UpdatePreviewStagingHint();
    }

    private void ApplyPendingUiToStagingBundle(AdventureBundle staging)
    {
        foreach (var panel in _tabPanels)
            panel.Flush(_ctx!);
    }

    private (string PlayerLine, string SourceLabel) ResolvePreviewPlayerLine()
    {
        if (_ctx is null)
            return ("", "");

        var composer = ResolvePreviewComposerText?.Invoke();
        if (!string.IsNullOrWhiteSpace(composer))
            return (composer.Trim(), "Source: in-page composer");

        var fallback = _previewTab.GetSampleLineText();
        if (!string.IsNullOrWhiteSpace(fallback))
            return (fallback, "Source: fallback player line");

        var queueLine = _ctx.Bundle.ContinuationQueue.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queueLine))
            return (queueLine.Trim(), "Source: continuation queue (first line)");

        return ("", "Source: none — enter a sample line");
    }

    private void UpdatePreviewStagingHint()
    {
        if (_ctx is null)
            return;

        var edits = BuildStagingEditsSummary();
        var hint = edits.Count == 0
            ? ""
            : $"Preview includes {edits.Count} unsaved edit(s) — Send uses last saved settings until you save.";
        _previewTab.SetStagingHint(hint, edits.Count > 0);
    }

    private async void CopyPacket()
    {
        if (_ctx is null)
            return;

        if (string.IsNullOrWhiteSpace(_ctx.LastMergedText))
            await RefreshMergedPreviewAsync();

        if (string.IsNullOrWhiteSpace(_ctx.LastMergedText))
            return;

        var package = new DataPackage();
        package.SetText(_ctx.LastMergedText);
        Clipboard.SetContent(package);
        await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Copy packet", "Play-turn packet copied to clipboard.");
    }
}
