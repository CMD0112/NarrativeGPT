using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class AdventurePlayView : UserControl
{
    public event EventHandler? BackRequested;

    public event EventHandler? LinkProjectRequested;

    public event EventHandler? PinPlayTabRequested;

    public event EventHandler? OpenPinnedPlayTabRequested;

    public event EventHandler? ClearPlayTabPinRequested;

    public event EventHandler? PinUtilityTabRequested;

    public event EventHandler? OpenPinnedUtilityTabRequested;

    public event EventHandler? ClearUtilityTabPinRequested;

    public event EventHandler? PlaySettingsSaved;

    public event EventHandler<Guid>? TitleRenamed;

    public Func<string?>? ResolvePreviewComposerText { get; set; }

    public Func<AttachmentContext?>? ResolvePreviewAttachmentContext { get; set; }

    public Func<Task>? OpenSourceManagerAsync { get; set; }

    public Func<Task>? ProbeSourcesAsync { get; set; }

    public Func<Task>? RefreshSourcesStatusAsync { get; set; }

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

    public Func<string, Task>? OpenUtilityThreadAsync { get; set; }

    public Func<string, Task>? RotateUtilityThreadAsync { get; set; }

    public Func<Task>? StartNewPlayThreadAsync { get; set; }

    public Func<string, Task>? RunSourceEditJobAsync { get; set; }

    public Func<Task>? RunDraftFrameworkAsync { get; set; }

    public Func<Task>? ContinueDesignAsync { get; set; }

    public Func<Task<IReadOnlyList<ConversationFileRef>>>? ListThreadFilesAsync { get; set; }

    public Func<ConversationFileRef, Task<byte[]>>? DownloadThreadFileAsync { get; set; }

    public Func<Task>? OpenProjectSettingsAsync { get; set; }

    public Func<string, Task<UtilityStoryContextBuildResult>>? PreviewLiveStoryContextAsync { get; set; }

    public Action? SaveNotesAction { get; set; }

    public event EventHandler<string>? RollIntoPlayerLineRequested;

    public event EventHandler? ExpandPlaySidePanelRequested;

    private AdventureBundle? _bundle;
    private string _previewPlayerLine = "";
    private string _entityFilter = "Characters";

    public AdventurePlayView()
    {
        InitializeComponent();
        EntityFilterBox.ItemsSource = new[] { "Characters", "Locations", "Things", "Factions", "Quests", "Concepts" };
        EntityFilterBox.SelectedIndex = 0;
        SizeChanged += (_, _) => UpdateResponsiveLayout(ActualWidth);
        Loaded += (_, _) => UpdateResponsiveLayout(ActualWidth);
    }

    public Guid? AdventureId => _bundle?.Metadata.Id;

    public bool IsSidePanelCollapsed =>
        _bundle?.Metadata.Settings.PlaySidePanelCollapsed == true;

    public void LoadAdventure(Guid id)
    {
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
        ApplyPlayTabPlacement();
        UpdateLinkProjectUi();
        UpdateJobButtonStates();
        UpdateResponsiveLayout(ActualWidth);
    }

    private void ApplyPlayTabPlacement()
    {
        if (_bundle is null)
            return;

        var placement = _bundle.Metadata.Settings.PlayTabPlacement;
        foreach (TabItem tab in PlaySideTabControl.Items)
        {
            if (tab.Header is not string header)
                continue;

            if (!placement.TryGetValue(header, out var where) || string.IsNullOrWhiteSpace(where))
                continue;

            tab.Visibility = string.Equals(where, "Hidden", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    public void UpdateResponsiveLayout(double panelWidth)
    {
        if (panelWidth <= 0)
            return;

        RootGrid.Margin = panelWidth < 264 ? new Thickness(8) : new Thickness(12);
        var contentWidth = Math.Max(0, panelWidth - RootGrid.Margin.Left - RootGrid.Margin.Right);

        BackButton.Content = contentWidth < 220 ? "←" : "← Dashboard";
        PlaySettingsButton.Content = contentWidth switch
        {
            < 220 => "⚙",
            < 260 => "Settings",
            _ => "Play settings…",
        };
        PlaySettingsButton.Padding = contentWidth < 220 ? new Thickness(8, 4, 8, 4) : new Thickness(10, 4, 10, 4);
        PlaySettingsButton.MinWidth = contentWidth < 220 ? 32 : 0;

        SessionCockpit.Padding = contentWidth < 240 ? new Thickness(6) : new Thickness(8);

        EntityRoleColumn.Visibility = contentWidth >= 250 ? Visibility.Visible : Visibility.Collapsed;
        EntityPinnedColumn.Visibility = contentWidth >= 290 ? Visibility.Visible : Visibility.Collapsed;
        EntityDescriptionColumn.Visibility = contentWidth >= 330 ? Visibility.Visible : Visibility.Collapsed;
        PinEntityButton.Visibility = contentWidth >= 290 ? Visibility.Visible : Visibility.Collapsed;
        SuggestEntitiesButton.Visibility = contentWidth >= 360 ? Visibility.Visible : Visibility.Collapsed;

        StateFieldColumn.Width = contentWidth < 260
            ? new DataGridLength(96)
            : new DataGridLength(140);

        EditWorldButton.Content = contentWidth < 280
            ? "Edit world in settings"
            : "Edit in Play settings → World";
    }

    public void SetSidePanelCollapsed(bool collapsed)
    {
        if (_bundle is null)
            return;

        _bundle.Metadata.Settings.PlaySidePanelCollapsed = collapsed;
    }

    public void SaveConfiguration() => SaveNotesAction?.Invoke();

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

    private void UpdateLinkProjectUi()
    {
        var showBanner = AdventureProjectBindingService.ShouldShowLinkProjectBanner(_bundle);
        LinkProjectBanner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
        var hasProject = !showBanner;
        LinkProjectButton.Visibility = Visibility.Visible;
        LinkProjectButton.Content = hasProject ? "Change Project…" : "Link Project…";
        LinkProjectButton.ToolTip = hasProject
            ? "Switch to a different ChatGPT Project or unlink"
            : "Connect this adventure to a ChatGPT Project";
        ThreadStatusBlock.Cursor = System.Windows.Input.Cursors.Hand;
        ThreadStatusBlock.ToolTip = hasProject
            ? "Click to change or unlink the linked ChatGPT Project"
            : "Click to link a ChatGPT Project";
    }

    private void LinkProject_Click(object sender, RoutedEventArgs e) =>
        LinkProjectRequested?.Invoke(this, EventArgs.Empty);

    private void ThreadStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        LinkProjectRequested?.Invoke(this, EventArgs.Empty);

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
            UpdateJobButtonStates();
        }
    }

    public void UpdateJobButtonStates()
    {
        if (_bundle is null)
        {
            ProcessLastExchangeButton.IsEnabled = false;
            SuggestEntitiesButton.IsEnabled = false;
            ExpandEntityButton.IsEnabled = false;
            SuggestMemoriesButton.IsEnabled = false;
            RefreshSummaryButton.IsEnabled = false;
            GenerateCardsButton.IsEnabled = false;
            SourcesButton.IsEnabled = false;
            GenerateRecapButton.IsEnabled = false;
            RunContinuityButton.IsEnabled = false;
            return;
        }

        var hasProject = AdventureProjectBindingService.HasLinkedProject(_bundle);
        var hasExchange = UtilityTranscriptScopeService.ResolveFromLocalLog(_bundle) is not null
                          || UtilityTranscriptScopeService.ResolveFallbackTurn(_bundle) is not null;

        ProcessLastExchangeButton.IsEnabled = hasProject && hasExchange;
        SuggestEntitiesButton.IsEnabled = hasProject && hasExchange;
        ExpandEntityButton.IsEnabled = hasProject && SelectedEntityRow is not null;
        SuggestMemoriesButton.IsEnabled = hasProject && hasExchange;
        RefreshSummaryButton.IsEnabled = hasProject;
        GenerateCardsButton.IsEnabled = hasProject;
        SourcesButton.IsEnabled = true;
        GenerateRecapButton.IsEnabled = hasProject;
        RunContinuityButton.IsEnabled = hasProject;
    }

    private void BindWarnings()
    {
        if (_bundle is null)
            return;

        WarningsGrid.ItemsSource = _bundle.Continuity.Warnings
            .OrderByDescending(w => w.CreatedAt)
            .ToList();
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
            _previewPlayerLine = AdventureBootstrapService.GetOpeningPlayerLine(_bundle.Scenario);
        }
    }

    private void BindStateTable()
    {
        if (_bundle is null)
            return;

        StateGrid.ItemsSource = StateTableHelper.BuildRows(_bundle);
    }

    private void BindEntityGrid()
    {
        if (_bundle is null)
            return;

        EntityGrid.ItemsSource = BuildEntityRows(_entityFilter);
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
        _openPlaySettingsDialog?.RefreshReviewPanels();
    }

    private void BindPendingReview()
    {
        if (_bundle is null)
            return;

        var counts = PendingReviewService.GetCounts(_bundle);
        PendingReviewBanner.Visibility = counts.Total > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingReviewHeader.Text = PendingReviewService.FormatSummaryLine(counts);
        ReviewMemoriesButton.Visibility = counts.Memories > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewSummaryButton.Visibility = counts.Summary > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewEntitiesButton.Visibility = counts.Entities > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewCardsButton.Visibility = counts.Cards > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BindReviewQueue()
    {
        if (_bundle is null)
            return;

        var queue = _bundle.Entities.ReviewQueue;
        ReviewQueueBanner.Visibility = queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewQueueHeader.Text = queue.Count > 0
            ? $"{queue.Count} proposed entit{(queue.Count == 1 ? "y" : "ies")} awaiting review"
            : "";
        ReviewQueueList.ItemsSource = queue
            .Select(q => new ReviewQueueListItem(q))
            .ToList();
        if (ReviewQueueList.Items.Count > 0)
            ReviewQueueList.SelectedIndex = 0;
    }

    private List<EntityGridRow> BuildEntityRows(string filter)
    {
        if (_bundle is null)
            return [];

        return filter switch
        {
            "Locations" => _bundle.Entities.Locations
                .Select(e => new EntityGridRow
                {
                    Id = e.Id,
                    Kind = EntityKind.Location,
                    Name = e.Name,
                    RoleOrStatus = e.Status,
                    Pinned = e.Pinned,
                    DescriptionSnippet = Truncate(e.Description, 80),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "Quests" => _bundle.Entities.Quests
                .Select(e => new EntityGridRow
                {
                    Id = e.Id,
                    Kind = EntityKind.Quest,
                    Name = e.Title,
                    RoleOrStatus = e.Status.ToString(),
                    Pinned = false,
                    DescriptionSnippet = Truncate(e.Description, 80),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "Things" => _bundle.Entities.Inventory
                .Select(e => new EntityGridRow
                {
                    Id = e.Id,
                    Kind = EntityKind.Thing,
                    Name = e.Name,
                    RoleOrStatus = e.Status,
                    Pinned = false,
                    DescriptionSnippet = Truncate(e.Description, 80),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "Factions" => _bundle.Entities.Factions
                .Select(e => new EntityGridRow
                {
                    Id = e.Id,
                    Kind = EntityKind.Faction,
                    Name = e.Name,
                    RoleOrStatus = e.Reputation,
                    Pinned = false,
                    DescriptionSnippet = Truncate(e.Goals, 80),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            "Concepts" => _bundle.Entities.Concepts
                .Select(e => new EntityGridRow
                {
                    Id = e.Id,
                    Kind = EntityKind.Concept,
                    Name = e.Name,
                    RoleOrStatus = e.Category,
                    Pinned = e.Pinned,
                    DescriptionSnippet = Truncate(e.Description, 80),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => _bundle.Entities.Characters
                .Select(e => new EntityGridRow
                {
                    Id = e.Id,
                    Kind = EntityKind.Character,
                    Name = e.Name,
                    RoleOrStatus = string.IsNullOrWhiteSpace(e.Role) ? e.Status : e.Role,
                    Pinned = e.Pinned,
                    DescriptionSnippet = Truncate(e.Description, 80),
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }

    private EntityGridRow? SelectedEntityRow => EntityGrid.SelectedItem as EntityGridRow;

    private EntityReviewItem? SelectedReviewItem =>
        (ReviewQueueList.SelectedItem as ReviewQueueListItem)?.Item;

    private void EntityFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntityFilterBox.SelectedItem is string filter)
        {
            _entityFilter = filter;
            BindEntityGrid();
        }
    }

    private void AddEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var showPinned = _entityFilter != "Quests";
        var dlg = new EntityEditDialog("", "", "", pinned: false, showPinned) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.EntityName))
            return;

        switch (_entityFilter)
        {
            case "Locations":
                _bundle.Entities.Locations.Add(new LocationEntry
                {
                    Name = dlg.EntityName,
                    Status = dlg.EntityRole,
                    Description = dlg.EntityDescription,
                    Pinned = dlg.EntityPinned,
                });
                break;
            case "Quests":
                _bundle.Entities.Quests.Add(new QuestEntry
                {
                    Title = dlg.EntityName,
                    Description = dlg.EntityDescription,
                    Notes = dlg.EntityRole,
                });
                break;
            default:
                _bundle.Entities.Characters.Add(new CharacterEntry
                {
                    Name = dlg.EntityName,
                    Role = dlg.EntityRole,
                    Description = dlg.EntityDescription,
                    Pinned = dlg.EntityPinned,
                });
                break;
        }

        AdventureStore.Save(_bundle);
        BindEntityGrid();
    }

    private void EditEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedEntityRow is not { } row)
            return;

        EditEntityRow(row);
    }

    private void EditEntityRow(EntityGridRow row)
    {
        if (_bundle is null)
            return;

        string name;
        string role;
        string description;
        bool pinned;
        var showPinned = row.Kind != EntityKind.Quest;

        switch (row.Kind)
        {
            case EntityKind.Location:
            {
                var location = _bundle.Entities.Locations.First(e => e.Id == row.Id);
                name = location.Name;
                role = location.Status;
                description = location.Description;
                pinned = location.Pinned;
                var dlg = new EntityEditDialog(name, role, description, pinned, showPinned)
                {
                    Owner = Window.GetWindow(this),
                };
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.EntityName))
                    return;

                location.Name = dlg.EntityName;
                location.Status = dlg.EntityRole;
                location.Description = dlg.EntityDescription;
                location.Pinned = dlg.EntityPinned;
                break;
            }
            case EntityKind.Quest:
            {
                var quest = _bundle.Entities.Quests.First(e => e.Id == row.Id);
                name = quest.Title;
                role = quest.Notes;
                description = quest.Description;
                var dlg = new EntityEditDialog(name, role, description, pinned: false, showPinned: false)
                {
                    Owner = Window.GetWindow(this),
                };
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.EntityName))
                    return;

                quest.Title = dlg.EntityName;
                quest.Notes = dlg.EntityRole;
                quest.Description = dlg.EntityDescription;
                break;
            }
            default:
            {
                var character = _bundle.Entities.Characters.First(e => e.Id == row.Id);
                name = character.Name;
                role = string.IsNullOrWhiteSpace(character.Role) ? character.Status : character.Role;
                description = character.Description;
                pinned = character.Pinned;
                var dlg = new EntityEditDialog(name, role, description, pinned, showPinned)
                {
                    Owner = Window.GetWindow(this),
                };
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.EntityName))
                    return;

                character.Name = dlg.EntityName;
                character.Role = dlg.EntityRole;
                character.Description = dlg.EntityDescription;
                character.Pinned = dlg.EntityPinned;
                break;
            }
        }

        AdventureStore.Save(_bundle);
        BindEntityGrid();
    }

    private void DeleteEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedEntityRow is not { } row)
            return;

        if (MessageBox.Show(Window.GetWindow(this), $"Delete “{row.Name}”?", "Delete entity",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        switch (row.Kind)
        {
            case EntityKind.Location:
                _bundle.Entities.Locations.RemoveAll(e => e.Id == row.Id);
                break;
            case EntityKind.Quest:
                _bundle.Entities.Quests.RemoveAll(e => e.Id == row.Id);
                break;
            default:
                _bundle.Entities.Characters.RemoveAll(e => e.Id == row.Id);
                break;
        }

        AdventureStore.Save(_bundle);
        BindEntityGrid();
    }

    private void TogglePinEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedEntityRow is not { } row)
            return;

        if (row.Kind == EntityKind.Quest)
            return;

        switch (row.Kind)
        {
            case EntityKind.Location:
                if (_bundle.Entities.Locations.FirstOrDefault(e => e.Id == row.Id) is { } location)
                    location.Pinned = !location.Pinned;
                break;
            default:
                if (_bundle.Entities.Characters.FirstOrDefault(e => e.Id == row.Id) is { } character)
                    character.Pinned = !character.Pinned;
                break;
        }

        AdventureStore.Save(_bundle);
        BindEntityGrid();
    }

    private void AcceptReviewItem_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedReviewItem is not { } item)
            return;

        if (EntityExtractionService.ApplyAcceptedReviewItem(_bundle.Entities, item))
            _bundle.Entities.ReviewQueue.Remove(item);

        AdventureStore.Save(_bundle);
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

    private async void SourcesStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        await OpenSourceManagerOrFallbackAsync();

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

    private async void ExpandEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedEntityRow is not { } row || ExpandEntityAsync is null)
            return;

        await RunJobButtonAsync(
            () => ExpandEntityAsync(_entityFilter, row.Id),
            () =>
            {
                BindReviewQueue();
                BindPendingReview();
            });
    }

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
        BindEntityGrid();
        BindReviewQueue();

        var last = _bundle.Entities.Characters.LastOrDefault()
                   ?? (object?)_bundle.Entities.Locations.LastOrDefault()
                   ?? _bundle.Entities.Quests.LastOrDefault();
        if (last is CharacterEntry character)
            EditEntityRow(BuildEntityRows("Characters").First(r => r.Id == character.Id));
        else if (last is LocationEntry location)
            EditEntityRow(BuildEntityRows("Locations").First(r => r.Id == location.Id));
        else if (last is QuestEntry quest)
            EditEntityRow(BuildEntityRows("Quests").First(r => r.Id == quest.Id));
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
        var dlg = new PlayPromptInjectionDialog(_bundle, _previewPlayerLine, tab)
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
        OpenPlaySettingsForFirstPending();

    private void ReviewMemories_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.MemoryCards);

    private void ReviewSummary_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.World);

    private void OpenPlaySettingsForFirstPending()
    {
        if (_bundle is null)
            return;

        var counts = PendingReviewService.GetCounts(_bundle);
        if (counts.Memories > 0)
            OpenPlaySettings(PlaySettingsTab.MemoryCards);
        else if (counts.Summary > 0)
            OpenPlaySettings(PlaySettingsTab.World);
        else if (counts.Entities > 0)
            FocusEntityReviewQueue();
        else if (counts.Cards > 0)
            OpenPlaySettings(PlaySettingsTab.MemoryCards);
        else if (counts.SourceEdits > 0)
            OpenPlaySettings(PlaySettingsTab.Sources);
    }

    private void ReviewCards_Click(object sender, RoutedEventArgs e) =>
        OpenPlaySettings(PlaySettingsTab.MemoryCards);

    private void ReviewEntities_Click(object sender, RoutedEventArgs e) =>
        FocusEntityReviewQueue();

    private bool IsReferenceTabHidden()
    {
        foreach (TabItem tab in PlaySideTabControl.Items)
        {
            if (tab.Header is not string header
                || !string.Equals(header, "Reference", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return tab.Visibility != Visibility.Visible;
        }

        return false;
    }

    private void FocusReferenceTab() => FocusEntityReviewQueue(scrollOnly: false);

    private void FocusEntityReviewQueue(bool scrollOnly = true)
    {
        if (_bundle is null || _bundle.Entities.ReviewQueue.Count == 0)
            return;

        if (IsReferenceTabHidden())
        {
            MessageBox.Show(
                "The Reference tab is hidden in Play settings → Layout. "
                    + "Set Reference visibility to Visible to review entities from the play surface.",
                "Reference tab hidden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsSidePanelCollapsed)
            ExpandPlaySidePanelRequested?.Invoke(this, EventArgs.Empty);

        if (PlaySideTabControl.Items.Count > 0)
            PlaySideTabControl.SelectedIndex = 0;

        BindReviewQueue();
        if (ReviewQueueList.Items.Count > 0)
        {
            ReviewQueueList.SelectedIndex = 0;
            ReviewQueueList.ScrollIntoView(ReviewQueueList.SelectedItem);
        }

        if (scrollOnly)
            ReviewQueueBanner.BringIntoView();
    }

    public void WirePlaySettingsDialog(PlayPromptInjectionDialog dlg)
    {
        dlg.ResolvePreviewComposerText = ResolvePreviewComposerText;
        dlg.ResolvePreviewAttachmentContext = ResolvePreviewAttachmentContext;
        dlg.OpenSourceManagerAsync = OpenSourceManagerAsync;
        dlg.ProbeSourcesAsync = ProbeSourcesAsync;
        dlg.RefreshSourcesStatusAsync = RefreshSourcesStatusAsync;
        dlg.ReconcileDuplicatesAsync = ReconcileDuplicatesAsync;
        dlg.SetSessionLinkDetails(ThreadStatusBlock.Text, SourcesStatusBlock.Text);
        dlg.OpenUtilityThreadAsync = jobId => OpenUtilityThreadAsync?.Invoke(jobId) ?? Task.CompletedTask;
        dlg.RotateUtilityThreadAsync = jobId => RotateUtilityThreadAsync?.Invoke(jobId) ?? Task.CompletedTask;
        dlg.StartNewPlayThreadAsync = () => StartNewPlayThreadAsync?.Invoke() ?? Task.CompletedTask;
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
        dlg.PinUtilityTabRequested += (_, _) => PinUtilityTabRequested?.Invoke(this, EventArgs.Empty);
        dlg.OpenPinnedUtilityTabRequested += (_, _) => OpenPinnedUtilityTabRequested?.Invoke(this, EventArgs.Empty);
        dlg.ClearUtilityTabPinRequested += (_, _) => ClearUtilityTabPinRequested?.Invoke(this, EventArgs.Empty);
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
                dlg.BindUtilityJobs();
            }

            dlg.PinPlayTabRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.OpenPinnedPlayTabRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.ClearPlayTabPinRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.StartNewPlayThreadAsync = async () =>
            {
                if (StartNewPlayThreadAsync is not null)
                    await StartNewPlayThreadAsync();
                RefreshDialogSessionStatus();
            };
            dlg.PinUtilityTabRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.OpenPinnedUtilityTabRequested += (_, _) => RefreshDialogSessionStatus();
            dlg.ClearUtilityTabPinRequested += (_, _) => RefreshDialogSessionStatus();
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        if (TurnTimelineService.UndoLast(_bundle))
            AdventureStore.Save(_bundle);
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
        MessageBox.Show(Window.GetWindow(this), $"Created branch: {br.Metadata.Title}", "Branch");
    }

    private void SaveState_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        SaveConfiguration();
        var path = TurnTimelineService.CreateSaveState(_bundle, "manual");
        MessageBox.Show(Window.GetWindow(this), $"Save state:\n{path}", "Saved");
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

    private void EditTurn_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var turn = _bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted).OrderByDescending(t => t.Index).FirstOrDefault();
        if (turn is null)
            return;

        var dlg = new EditTurnDialog(turn.PlayerText, turn.NarratorText ?? "") { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;

        ThreadMetadataService.MarkTurnSuperseded(_bundle, turn.Id);
        TurnTimelineService.EditTurn(turn, dlg.PlayerText, dlg.NarratorText);
        ThreadMetadataService.RecordPlayTurnExchange(
            _bundle,
            turn,
            turn.PlayerText,
            turn.NarratorText);
        _bundle.Summary.PendingReview = true;
        AdventureStore.Save(_bundle);
    }

    private void Roll_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RandomTableDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.LastRoll))
            RollIntoPlayerLineRequested?.Invoke(this, dlg.LastRoll);
    }

    private void MigrationCheckpoint_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var checkpoint = SummarizationMigrationService.BuildCheckpoint(_bundle);
        SummarizationMigrationService.SaveCheckpointSidecar(_bundle, checkpoint);
        new SummarizationMigrationDialog(checkpoint)
        {
            Owner = Window.GetWindow(this),
            CreateNewPlayThreadAsync = StartNewPlayThreadAsync,
        }.ShowDialog();
    }

    private void ContinueDesign_Click(object sender, RoutedEventArgs e)
    {
        if (ContinueDesignAsync is null)
            return;

        _ = ContinueDesignAsync();
    }

    private void DraftFramework_Click(object sender, RoutedEventArgs e)
    {
        if (RunDraftFrameworkAsync is null)
            return;

        _ = RunDraftFrameworkAsync();
    }

    private enum EntityKind
    {
        Character,
        Location,
        Quest,
        Thing,
        Faction,
        Concept,
    }

    private sealed class EntityGridRow
    {
        public Guid Id { get; init; }

        public EntityKind Kind { get; init; }

        public string Name { get; set; } = "";

        public string RoleOrStatus { get; set; } = "";

        public bool Pinned { get; set; }

        public string DescriptionSnippet { get; set; } = "";
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
