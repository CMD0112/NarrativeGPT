using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Helpers;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class AdventureDashboardPage : Page
{
    private AdventureLibraryFilter _filter = new();
    private IReadOnlyList<AdventureLibraryRowDto> _allRows = [];
    private CancellationTokenSource? _filterDebounceCts;

    public AdventureDashboardPage()
    {
        InitializeComponent();
        InitializeFilters();
        Loaded += OnLoaded;
    }

    public event EventHandler<Guid>? PlayRequested;

    public event EventHandler<Guid>? ContinueDesignRequested;

    public event EventHandler? DesignWithAiRequested;

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        _ = RefreshAsync();

    private void InitializeFilters()
    {
        SortCombo.Items.Add(new ComboBoxItem { Content = "Last played", Tag = AdventureSort.LastPlayed });
        SortCombo.Items.Add(new ComboBoxItem { Content = "Title A–Z", Tag = AdventureSort.Title });
        SortCombo.Items.Add(new ComboBoxItem { Content = "Date created", Tag = AdventureSort.Created });
        SortCombo.Items.Add(new ComboBoxItem { Content = "Status", Tag = AdventureSort.Status });
        SortCombo.SelectedIndex = 0;

        GenreFilter.Items.Add(new ComboBoxItem { Content = "All genres", Tag = string.Empty });
        foreach (var genre in new[] { "Fantasy", "Sci-Fi", "Horror", "Mystery", "Romance", "Other" })
            GenreFilter.Items.Add(new ComboBoxItem { Content = genre, Tag = genre });
        GenreFilter.SelectedIndex = 0;
    }

    public async Task RefreshAsync()
    {
        try
        {
            _allRows = await Task.Run(AdventureLibraryBridge.LoadAllRows);
            await EnqueueUiAsync(() =>
            {
                PopulateGenreFilterFromData();
                ApplyFilter();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsMirror.LogException("adventure_library_refresh", ex);
        }
    }

    private Task EnqueueUiAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private void PopulateGenreFilterFromData()
    {
        var selected = (GenreFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        var genres = _allRows
            .Select(r => r.Genre)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GenreFilter.Items.Clear();
        GenreFilter.Items.Add(new ComboBoxItem { Content = "All genres", Tag = string.Empty });
        foreach (var genre in genres)
            GenreFilter.Items.Add(new ComboBoxItem { Content = genre, Tag = genre });

        var matchIndex = genres.FindIndex(g => string.Equals(g, selected, StringComparison.OrdinalIgnoreCase));
        GenreFilter.SelectedIndex = matchIndex >= 0 ? matchIndex + 1 : 0;
    }

    private void ApplyFilter()
    {
        ReadFilterFromUi();
        var rows = AdventureLibraryService.Apply(_allRows, _filter)
            .Select(AdventureLibraryRowVm.FromDto)
            .ToList();

        AdventureList.ItemsSource = rows;
        UpdateEmptyState(rows.Count);
        UpdateSelectionActions();
    }

    private void ReadFilterFromUi()
    {
        _filter.SearchQuery = SearchBox.Text ?? string.Empty;
        _filter.ShowArchived = ArchivedToggle.IsOn;
        _filter.Sort = SortCombo.SelectedItem is ComboBoxItem { Tag: AdventureSort sort }
            ? sort
            : AdventureSort.LastPlayed;
        _filter.GenreFilter = (GenreFilter.SelectedItem as ComboBoxItem)?.Tag as string;
    }

    private void DebouncedApplyFilter()
    {
        _filterDebounceCts?.Cancel();
        _filterDebounceCts = new CancellationTokenSource();
        var token = _filterDebounceCts.Token;
        _ = DebouncedApplyFilterCoreAsync(token);
    }

    private async Task DebouncedApplyFilterCoreAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            /* superseded */
        }
    }

    private void UpdateEmptyState(int visibleCount)
    {
        if (visibleCount > 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            AdventureList.Visibility = Visibility.Visible;
            return;
        }

        EmptyStatePanel.Visibility = Visibility.Visible;
        AdventureList.Visibility = Visibility.Collapsed;

        var libraryEmpty = _allRows.Count == 0;
        var hasActiveFilter = !string.IsNullOrWhiteSpace(SearchBox.Text) || ArchivedToggle.IsOn;

        if (libraryEmpty && !hasActiveFilter)
        {
            EmptyStateTitle.Text = "No adventures yet";
            EmptyStateHint.Text = "Create a new adventure, import a backup, or start designing with AI.";
            EmptyStateActions.Visibility = Visibility.Visible;
            return;
        }

        EmptyStateTitle.Text = "No matching adventures";
        EmptyStateHint.Text = hasActiveFilter
            ? "Try clearing search or showing archived adventures."
            : "All adventures are hidden by the current filters.";
        EmptyStateActions.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectionActions()
    {
        var selected = AdventureList.SelectedItems
            .OfType<AdventureLibraryRowVm>()
            .ToList();

        var playButton = DashboardCommandBar.PrimaryCommands
            .OfType<AppBarButton>()
            .FirstOrDefault(b => b.Label == "Play");
        if (playButton is not null)
            playButton.IsEnabled = selected.Count == 1;
    }

    private IReadOnlyList<AdventureLibraryRowVm> GetSelectedRows() =>
        AdventureList.SelectedItems.OfType<AdventureLibraryRowVm>().ToList();

    private void AdventureList_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        WinUiListFlyoutHelper.SelectItemUnderPointer(sender, e);

    private void AdventureContextFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
            return;

        var selected = GetSelectedRows();
        var count = selected.Count;
        var hasSelection = count > 0;
        var single = count == 1 ? selected[0] : null;
        var singleMeta = single is not null
            ? AdventureStore.ListIndex().FirstOrDefault(a => a.Id == single.Id)
            : null;

        var archivedCount = selected.Count(r =>
            AdventureStore.ListIndex().FirstOrDefault(a => a.Id == r.Id) is { Archived: true });
        var activeCount = count - archivedCount;

        var projectLabel = count == 1
                             && singleMeta is not null
                             && AdventureProjectBindingService.HasLinkedProject(
                                 new AdventureBundle { Metadata = singleMeta })
            ? "Change Project…"
            : "Link Project…";

        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            switch (item.Tag as string)
            {
                case "Play":
                    item.IsEnabled = count == 1;
                    break;
                case "LinkProject":
                    item.Text = projectLabel;
                    item.IsEnabled = count == 1;
                    break;
                case "ContinueDesign":
                    item.IsEnabled = count == 1
                        && singleMeta is not null
                        && (singleMeta.Status == AdventureStatus.Designing
                            || IsBlankAdventure(singleMeta.Id)
                            || HasLocalSources(singleMeta.Id));
                    break;
                case "Rename":
                case "OpenFolder":
                case "CreateFolder":
                    item.IsEnabled = count == 1;
                    break;
                case "Archive":
                    item.Text = count <= 1 ? "Archive" : $"Archive ({activeCount})";
                    item.IsEnabled = hasSelection && activeCount > 0;
                    break;
                case "Unarchive":
                    item.Text = count <= 1 ? "Unarchive" : $"Unarchive ({archivedCount})";
                    item.IsEnabled = hasSelection && archivedCount > 0;
                    break;
                case "Backup":
                    item.Text = count <= 1 ? "Backup selected" : $"Backup selected ({count})";
                    item.IsEnabled = hasSelection;
                    break;
                case "Delete":
                    item.Text = count <= 1 ? "Delete" : $"Delete ({count})";
                    item.IsEnabled = hasSelection;
                    break;
            }
        }
    }

    private static bool IsBlankAdventure(Guid id)
    {
        var bundle = AdventureStore.Load(id);
        return bundle is not null
               && bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted) == 0;
    }

    private static bool HasLocalSources(Guid id)
    {
        var bundle = AdventureStore.Load(id);
        return bundle is not null && AdventureDesignContextService.CanOpenLocalSourcesEdit(bundle);
    }

    private AdventureLibraryRowVm? SelectedRow =>
        AdventureList.SelectedItem as AdventureLibraryRowVm;

    private void AdventureList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionActions();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        DebouncedApplyFilter();

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        ApplyFilter();

    private void Filter_Changed(object sender, RoutedEventArgs e) =>
        ApplyFilter();

    private void RowPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
            PlayRequested?.Invoke(this, id);
    }

    private async void NewAdventure_Click(object sender, RoutedEventArgs e)
    {
        var outcome = await WpfDialogHostService.ShowScenarioCreationAsync(App.CurrentMainWindow);
        if (!outcome.Confirmed)
            return;

        if (outcome.RequestDesignWithAi)
        {
            await WpfDialogHostService.ShowDesignWizardAsync(App.CurrentMainWindow);
            await RefreshAsync();
            return;
        }

        if (outcome.Scenario is null)
            return;

        var bundle = AdventureStore.CreateNew(
            string.IsNullOrWhiteSpace(outcome.AdventureTitle) ? "Untitled adventure" : outcome.AdventureTitle,
            outcome.Scenario);

        bundle.Metadata.Settings.OfferStartOnPlay = outcome.StartWithOpeningNarration;
        AdventureStore.Save(bundle);
        await RefreshAsync();
        PlayRequested?.Invoke(this, bundle.Metadata.Id);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
            PlayRequested?.Invoke(this, row.Id);
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        if (await WpfDialogHostService.ShowRenameAsync(App.CurrentMainWindow, row.Id))
            await RefreshAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = AdventureList.SelectedItems.OfType<AdventureLibraryRowVm>().ToList();
        if (selected.Count == 0)
            return;

        var confirm = new ContentDialog
        {
            Title = "Delete adventures?",
            Content = $"Delete {selected.Count} adventure(s)? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        foreach (var row in selected)
            AdventureStore.DeleteMany([row.Id]);

        await RefreshAsync();
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var selected = AdventureList.SelectedItems.OfType<AdventureLibraryRowVm>().Select(r => r.Id).ToList();
        if (selected.Count == 0)
            return;

        await Task.Run(() =>
        {
            foreach (var id in selected)
                BackupService.CreateBackup(id);
        });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        await WpfDialogHostService.ShowExportAsync(App.CurrentMainWindow, row.Id);
    }

    private async void Archive_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Select(r => r.Id).ToList();
        if (selected.Count == 0)
            return;

        var anyActive = selected.Any(id =>
            AdventureStore.ListIndex().FirstOrDefault(a => a.Id == id) is { Archived: false });
        AdventureStore.SetArchivedMany(selected, anyActive);
        await RefreshAsync();
    }

    private void CtxPlay_Click(object sender, RoutedEventArgs e) =>
        Play_Click(sender, e);

    private async void CtxLinkProject_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        await WpfDialogHostService.ShowProjectWorkspaceAsync(App.CurrentMainWindow, row.Id);
        await RefreshAsync();
    }

    private void CtxContinueDesign_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        ContinueDesignRequested?.Invoke(this, row.Id);
    }

    private void CtxOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        if (AdventureDirectoryService.TryOpenInShell(row.Id, out var error))
            return;

        _ = ShowSimpleDialogAsync("Open adventure folder", error ?? "Could not open the adventure folder.");
    }

    private async void CtxCreateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        if (!AdventureStore.MaterializeDirectory(row.Id))
        {
            await ShowSimpleDialogAsync(
                "Create folder on disk",
                "Could not create the adventure folder. The adventure may be missing or inaccessible.");
            return;
        }

        var path = AppDirectories.AdventureDirectory(row.Id);
        await ShowSimpleDialogAsync("Create folder on disk", $"Adventure folder ready:\n{path}");
    }

    private async void CtxArchive_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Select(r => r.Id).ToList();
        if (selected.Count == 0)
            return;

        var ids = selected.Where(id =>
            AdventureStore.ListIndex().FirstOrDefault(a => a.Id == id) is { Archived: false })
            .Select(id => id)
            .ToList();
        if (ids.Count == 0)
            return;

        AdventureStore.SetArchivedMany(ids, archived: true);
        await RefreshAsync();
    }

    private async void CtxUnarchive_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Select(r => r.Id).ToList();
        if (selected.Count == 0)
            return;

        var ids = selected.Where(id =>
            AdventureStore.ListIndex().FirstOrDefault(a => a.Id == id) is { Archived: true })
            .Select(id => id)
            .ToList();
        if (ids.Count == 0)
            return;

        AdventureStore.SetArchivedMany(ids, archived: false);
        await RefreshAsync();
    }

    private async Task ShowSimpleDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (await WpfDialogHostService.ShowImportBackupAsync(App.CurrentMainWindow))
            await RefreshAsync();
    }

    private void DesignWithAi_Click(object sender, RoutedEventArgs e) =>
        DesignWithAiRequested?.Invoke(this, EventArgs.Empty);
}
