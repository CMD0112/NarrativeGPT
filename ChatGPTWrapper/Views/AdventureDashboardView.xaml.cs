using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class AdventureDashboardView : UserControl
{
    public event EventHandler<Guid>? PlayRequested;

    public event EventHandler<Guid>? LinkProjectRequested;

    public event EventHandler<Guid>? DraftFrameworkRequested;

    public event EventHandler? DesignWithAiRequested;

    public event EventHandler<Guid>? ContinueDesignRequested;

    public event EventHandler<Guid>? RenameCompleted;

    public Func<Guid, bool>? IsAdventureActiveInPlay { get; set; }

    private List<AdventureMetadata> _all = [];
    private bool _showArchived;
    private bool _refreshInFlight;

    public AdventureDashboardView()
    {
        InitializeComponent();
        UpdateStorageHint();
        RefreshList();
    }

    private void UpdateStorageHint()
    {
        LocalOnlyHint.Text =
            $"Adventures: {AppDirectories.AdventuresDirectory} · Config: {AppDirectories.ConfigRoot}. "
            + "Only prompt packets are sent to ChatGPT when you play.";
    }

    public void RefreshList()
    {
        _all = AdventureStore.ListIndex();
        ApplyFilter();
    }

    public async Task RefreshOnEnterAsync()
    {
        if (_refreshInFlight)
            return;

        _refreshInFlight = true;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingStatusBlock.Text = "Loading adventures…";
        NewAdventureButton.IsEnabled = false;
        PlayButton.IsEnabled = false;

        try
        {
            await Task.Run(RefreshList);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            NewAdventureButton.IsEnabled = true;
            UpdateSelectionActions();
            _refreshInFlight = false;
        }
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim().ToLowerInvariant();
        IEnumerable<AdventureMetadata> filtered = _all;

        if (!_showArchived)
            filtered = filtered.Where(a => !a.Archived);

        if (!string.IsNullOrEmpty(q))
        {
            filtered = filtered.Where(a =>
                a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Genre.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        AdventureGrid.ItemsSource = filtered.Select(m => new AdventureGridRow(m)).ToList();
        UpdateSelectionActions();
    }

    private AdventureGridRow? SelectedRow =>
        AdventureGrid.SelectedItem as AdventureGridRow;

    private AdventureMetadata? PrimarySelectedMeta => GetSelectedMetas().FirstOrDefault();

    private IReadOnlyList<AdventureMetadata> GetSelectedMetas() =>
        AdventureGrid.SelectedItems
            .Cast<AdventureGridRow>()
            .Select(r => r.Meta)
            .ToList();

    private int VisibleAdventureCount =>
        AdventureGrid.Items.Count;

    private void AdventureGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionActions();

    private void UpdateSelectionActions()
    {
        var selected = GetSelectedMetas();
        var count = selected.Count;
        var hasSelection = count > 0;
        var single = count == 1 ? selected[0] : null;

        PlayButton.IsEnabled = count == 1;
        LinkProjectButton.IsEnabled = count == 1;
        LinkProjectContextMenuItem.IsEnabled = count == 1;
        RenameButton.IsEnabled = count == 1;
        RenameContextMenuItem.IsEnabled = count == 1;
        var projectLabel = count == 1
                           && single is not null
                           && AdventureProjectBindingService.HasLinkedProject(
                               new AdventureBundle { Metadata = single })
            ? "Change Project…"
            : "Link Project…";
        LinkProjectButton.Content = projectLabel;
        LinkProjectMenuItem.Header = projectLabel;
        LinkProjectContextMenuItem.Header = projectLabel;

        DeleteButton.IsEnabled = hasSelection;
        DeleteButton.Content = count <= 1 ? "Delete" : $"Delete ({count})";
        DeleteContextMenuItem.Header = count <= 1 ? "Delete" : $"Delete ({count})";

        var archivedCount = selected.Count(m => m.Archived);
        var activeCount = count - archivedCount;
        ArchiveButton.IsEnabled = hasSelection && activeCount > 0;
        ArchiveButton.Content = count <= 1 ? "Archive" : $"Archive ({activeCount})";
        ArchiveContextMenuItem.Header = count <= 1 ? "Archive" : $"Archive ({activeCount})";
        UnarchiveButton.IsEnabled = hasSelection && archivedCount > 0;
        UnarchiveButton.Content = count <= 1 ? "Unarchive" : $"Unarchive ({archivedCount})";
        UnarchiveContextMenuItem.IsEnabled = hasSelection && archivedCount > 0;
        UnarchiveContextMenuItem.Header = count <= 1 ? "Unarchive" : $"Unarchive ({archivedCount})";

        BackupSelectedButton.IsEnabled = hasSelection;
        BackupSelectedButton.Content = count <= 1 ? "Backup" : $"Backup ({count})";
        BackupMenuItem.Header = count <= 1 ? "Backup selected" : $"Backup selected ({count})";
        BackupContextMenuItem.Header = BackupMenuItem.Header;

        DraftFrameworkMenuItem.IsEnabled = count == 1
            && !string.IsNullOrWhiteSpace(single?.LinkedProjectId);
        ContinueDesignMenuItem.IsEnabled = count == 1
            && single is not null
            && (single.Status == AdventureStatus.Designing
                || IsBlankAdventure(single.Id)
                || HasLocalSources(single.Id));

        CreateFolderMenuItem.IsEnabled = count == 1;
        CreateFolderContextMenuItem.IsEnabled = count == 1;

        ClearSelectionButton.IsEnabled = hasSelection;
        SelectAllButton.IsEnabled = VisibleAdventureCount > 0;

        UpdateSelectionStatus(count);
    }

    private void UpdateSelectionStatus(int selectedCount)
    {
        if (selectedCount == 0)
        {
            SelectionStatusBlock.Text = VisibleAdventureCount == 1
                ? "1 adventure shown"
                : $"{VisibleAdventureCount} adventures shown";
            return;
        }

        SelectionStatusBlock.Text = selectedCount == 1
            ? "1 adventure selected"
            : $"{selectedCount} adventures selected";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        AdventureGrid.SelectAll();
        UpdateSelectionActions();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        AdventureGrid.UnselectAll();
        UpdateSelectionActions();
    }

    private void AdventureDashboardView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.A
            && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            AdventureGrid.SelectAll();
            UpdateSelectionActions();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.F2 && PrimarySelectedMeta is not null)
        {
            RenameSelectedAdventure();
            e.Handled = true;
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e) => RenameSelectedAdventure();

    private void RenameSelectedAdventure()
    {
        if (PrimarySelectedMeta is null)
            return;

        var bundle = AdventureStore.Load(PrimarySelectedMeta.Id);
        if (bundle is null)
            return;

        var owner = Window.GetWindow(this);
        var dlg = new AdventureRenameDialog(bundle.Metadata.Title)
        {
            Owner = owner,
        };
        if (dlg.ShowDialog() != true)
            return;

        if (!AdventureRenameService.TryRename(bundle, dlg.NewTitle, out var error))
        {
            MessageBox.Show(owner, error ?? "Could not rename adventure.", "Rename adventure",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshList();
        RenameCompleted?.Invoke(this, bundle.Metadata.Id);
    }

    private static bool IsBlankAdventure(Guid id)
    {
        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return false;

        return bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted) == 0;
    }

    private static bool HasLocalSources(Guid id)
    {
        var bundle = AdventureStore.Load(id);
        return bundle is not null && AdventureDesignContextService.CanOpenLocalSourcesEdit(bundle);
    }

    private void DraftFramework_Click(object sender, RoutedEventArgs e)
    {
        if (PrimarySelectedMeta is null)
            return;

        DraftFrameworkRequested?.Invoke(this, PrimarySelectedMeta.Id);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ShowArchived_Changed(object sender, RoutedEventArgs e)
    {
        _showArchived = ShowArchivedCheck.IsChecked == true;
        ApplyFilter();
    }

    private void DesignWithAi_Click(object sender, RoutedEventArgs e) =>
        DesignWithAiRequested?.Invoke(this, EventArgs.Empty);

    private void ContinueDesign_Click(object sender, RoutedEventArgs e)
    {
        if (PrimarySelectedMeta is null)
            return;

        ContinueDesignRequested?.Invoke(this, PrimarySelectedMeta.Id);
    }

    public Func<Task>? OpenDesignWizardFromDialogAsync { get; set; }

    private async void NewAdventure_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ScenarioCreationDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true)
            return;

        if (dlg.RequestDesignWithAi)
        {
            if (OpenDesignWizardFromDialogAsync is not null)
                await OpenDesignWizardFromDialogAsync();
            else
                DesignWithAiRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (dlg.ResultScenario is null)
            return;

        var bundle = AdventureStore.CreateNew(
            string.IsNullOrWhiteSpace(dlg.AdventureTitle) ? "Untitled adventure" : dlg.AdventureTitle,
            dlg.ResultScenario);

        bundle.Metadata.Settings.OfferStartOnPlay = dlg.StartWithOpeningNarration;
        AdventureStore.Save(bundle);

        RefreshList();
        PlayRequested?.Invoke(this, bundle.Metadata.Id);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (PrimarySelectedMeta is null)
            return;

        if (PrimarySelectedMeta.Status == AdventureStatus.Designing)
        {
            var choice = MessageBox.Show(Window.GetWindow(this),
                $"\"{PrimarySelectedMeta.Title}\" is still in design.\n\n"
                + "Yes = continue design wizard\n"
                + "No = play anyway (finalize design first recommended)\n"
                + "Cancel = stay here",
                "In design",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel)
                return;

            if (choice == MessageBoxResult.Yes)
            {
                ContinueDesignRequested?.Invoke(this, PrimarySelectedMeta.Id);
                return;
            }
        }

        PlayRequested?.Invoke(this, PrimarySelectedMeta.Id);
    }

    public void SetLinkProjectBusy(bool busy)
    {
        var canLink = !busy && GetSelectedMetas().Count == 1;
        LinkProjectMenuItem.IsEnabled = canLink;
        LinkProjectButton.IsEnabled = canLink;
        LinkProjectContextMenuItem.IsEnabled = canLink;
    }

    private void LinkProject_Click(object sender, RoutedEventArgs e)
    {
        if (PrimarySelectedMeta is null || !LinkProjectMenuItem.IsEnabled)
            return;

        LinkProjectRequested?.Invoke(this, PrimarySelectedMeta.Id);
    }

    private void AdventureGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PrimarySelectedMeta is not null)
            PlayRequested?.Invoke(this, PrimarySelectedMeta.Id);
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedMetas();
        if (selected.Count == 0)
            return;

        if (selected.Count == 1)
        {
            var bundle = AdventureStore.Load(selected[0].Id);
            if (bundle is null)
                return;

            bundle.Metadata.Archived = !bundle.Metadata.Archived;
            AdventureStore.Save(bundle);
            RefreshList();
            return;
        }

        var ids = selected.Where(m => !m.Archived).Select(m => m.Id).ToList();
        if (ids.Count == 0)
            return;

        var updated = AdventureStore.SetArchivedMany(ids, archived: true);
        RefreshList();
        if (updated > 0)
            SelectionStatusBlock.Text = $"Archived {updated} adventure(s).";
    }

    private void Unarchive_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedMetas();
        if (selected.Count == 0)
            return;

        var ids = selected.Where(m => m.Archived).Select(m => m.Id).ToList();
        if (ids.Count == 0)
            return;

        var updated = AdventureStore.SetArchivedMany(ids, archived: false);
        RefreshList();
        if (updated > 0)
            SelectionStatusBlock.Text = $"Unarchived {updated} adventure(s).";
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedMetas();
        if (selected.Count == 0)
            return;

        var saved = new List<string>();
        var failures = new List<string>();

        foreach (var meta in selected)
        {
            try
            {
                saved.Add(BackupService.CreateBackup(meta.Id));
            }
            catch (Exception ex)
            {
                failures.Add($"{meta.Title}: {ex.Message}");
            }
        }

        var owner = Window.GetWindow(this);
        if (failures.Count == 0)
        {
            var message = saved.Count == 1
                ? $"Backup saved:\n{saved[0]}"
                : $"Saved {saved.Count} backup(s).";
            MessageBox.Show(owner, message, "Backup", MessageBoxButton.OK);
            return;
        }

        var detail = failures.Count == selected.Count
            ? string.Join(Environment.NewLine, failures)
            : $"Saved {saved.Count} backup(s).\n\nFailed:\n{string.Join(Environment.NewLine, failures)}";
        MessageBox.Show(owner, detail, "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedMetas();
        if (selected.Count == 0)
            return;

        var blocked = selected
            .Where(m => IsAdventureActiveInPlay?.Invoke(m.Id) == true)
            .ToList();
        var deletable = selected
            .Where(m => blocked.All(b => b.Id != m.Id))
            .ToList();

        if (deletable.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                blocked.Count == 1
                    ? "This adventure is the active play session. Exit Play mode before deleting."
                    : "The selected adventures include the active play session. Exit Play mode before deleting.",
                "Delete adventures",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (blocked.Count > 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"Skipping {blocked.Count} adventure(s) that are active in Play mode.",
                "Delete adventures",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        if (!ConfirmDelete(deletable))
            return;

        AdventureStore.DeleteMany(deletable.Select(m => m.Id));
        RefreshList();
    }

    private bool ConfirmDelete(IReadOnlyList<AdventureMetadata> adventures)
    {
        var owner = Window.GetWindow(this);
        var titleList = FormatTitleList(adventures.Select(a => a.Title));
        var message = adventures.Count == 1
            ? $"Delete \"{adventures[0].Title}\" permanently?\n\nYes = delete now. No = create a backup zip first, then delete."
            : $"Delete {adventures.Count} adventures permanently?\n\n{titleList}\n\nYes = delete now. No = backup each adventure first, then delete.";

        var backupFirst = MessageBox.Show(
            owner,
            message,
            adventures.Count == 1 ? "Confirm delete" : $"Confirm delete ({adventures.Count})",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (backupFirst == MessageBoxResult.Cancel)
            return false;

        if (backupFirst != MessageBoxResult.No)
            return true;

        var failures = new List<string>();
        foreach (var meta in adventures)
        {
            try
            {
                BackupService.CreateBackup(meta.Id);
            }
            catch (Exception ex)
            {
                failures.Add($"{meta.Title}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(
                owner,
                $"Backup failed for:\n{string.Join(Environment.NewLine, failures)}",
                "Backup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        MessageBox.Show(
            owner,
            adventures.Count == 1
                ? "Backup saved."
                : $"Saved {adventures.Count} backup(s).",
            "Backup",
            MessageBoxButton.OK);
        return true;
    }

    private static string FormatTitleList(IEnumerable<string> titles, int maxLines = 8)
    {
        var list = titles.ToList();
        if (list.Count == 0)
            return string.Empty;

        var lines = list.Take(maxLines).Select(t => $"• {t}");
        var text = string.Join(Environment.NewLine, lines);
        if (list.Count > maxLines)
            text += Environment.NewLine + $"• …and {list.Count - maxLines} more";

        return text;
    }

    private void WrapperSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WrapperSettingsDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || dlg.ResultSettings is null)
            return;

        WrapperSettingsStore.Save(dlg.ResultSettings);
        AppDirectories.EnsureCreated();
        UpdateStorageHint();
        RefreshList();
    }

    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PrimarySelectedMeta is not { } meta)
            return;

        if (!AdventureStore.MaterializeDirectory(meta.Id))
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Could not create the adventure folder. The adventure may be missing or inaccessible.",
                "Create folder on disk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var path = AppDirectories.AdventureDirectory(meta.Id);
        MessageBox.Show(
            Window.GetWindow(this),
            $"Adventure folder ready:\n{path}",
            "Create folder on disk",
            MessageBoxButton.OK);
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Import adventure folder",
        };

        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName))
            return;

        var sourceDir = dlg.FolderName;
        if (!AdventureDirectoryService.DirectoryHasAdventureMetadata(sourceDir))
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "The selected folder must contain adventure.json.",
                "Import folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var mode = MessageBox.Show(
            Window.GetWindow(this),
            "Copy into the adventures library (recommended), or use the folder in place without copying?\n\n"
            + "Yes = copy into library\n"
            + "No = use folder in place\n"
            + "Cancel = abort",
            "Import folder",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (mode == MessageBoxResult.Cancel)
            return;

        try
        {
            var options = new AdventureImportOptions
            {
                Mode = mode == MessageBoxResult.No
                    ? AdventureImportMode.RegisterInPlace
                    : AdventureImportMode.Copy,
            };

            var peek = AdventureStore.PeekMetadataFromDirectory(sourceDir);
            if (!string.IsNullOrWhiteSpace(peek?.LinkedProjectId))
            {
                var detachChoice = MessageBox.Show(
                    Window.GetWindow(this),
                    $"This folder references ChatGPT project {peek.LinkedProjectId}.\n\n"
                    + "Yes = keep linkage\nNo = detach\nCancel = abort",
                    "Import folder",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (detachChoice == MessageBoxResult.Cancel)
                    return;

                var bundle = AdventureStore.ImportFromDirectory(sourceDir, options);
                if (detachChoice == MessageBoxResult.No)
                {
                    bundle.Metadata.LinkedProjectId = null;
                    bundle.Metadata.LinkedConversationId = null;
                    bundle.Metadata.LinkedProjectHint = null;
                    bundle.Metadata.PinnedPlayTabKey = null;
                    bundle.Metadata.PinnedPlayTabTitle = null;
                    bundle.Metadata.PinnedPlayTabUrl = null;
                    bundle.Metadata.ProjectLink = null;
                    SourceManifestHelper.ClearRemoteBindings(bundle.SourceManifest);
                    AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
                }

                RefreshList();
                return;
            }

            AdventureStore.ImportFromDirectory(sourceDir, options);
            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Import folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Adventure backup (*.zip)|*.zip|All files|*.*",
        };

        if (dlg.ShowDialog() != true)
            return;

        var temp = Path.Combine(Path.GetTempPath(), "cgw-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(dlg.FileName, temp);

            var peek = AdventureStore.PeekMetadataFromDirectory(temp);
            var linkedId = peek?.LinkedProjectId;
            var detach = false;

            if (!string.IsNullOrWhiteSpace(linkedId))
            {
                var choice = MessageBox.Show(
                    Window.GetWindow(this),
                    $"This backup references ChatGPT project {linkedId}.\n\n"
                    + "Yes = keep linkage (may conflict if another adventure uses the same project).\n"
                    + "No = detach and link manually later.\n"
                    + "Cancel = abort import.",
                    "Import adventure",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel)
                    return;

                detach = choice == MessageBoxResult.No;
            }

            var bundle = AdventureStore.ImportFromDirectory(temp);
            if (detach)
            {
                bundle.Metadata.LinkedProjectId = null;
                bundle.Metadata.LinkedConversationId = null;
                bundle.Metadata.LinkedProjectHint = null;
                bundle.Metadata.PinnedPlayTabKey = null;
                bundle.Metadata.PinnedPlayTabTitle = null;
                bundle.Metadata.PinnedPlayTabUrl = null;
                bundle.Metadata.ProjectLink = null;
                SourceManifestHelper.ClearRemoteBindings(bundle.SourceManifest);
                AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
            }

            RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try { Directory.Delete(temp, recursive: true); }
                catch { /* ignore */ }
            }
        }
    }

    private void SaveScenarioLib_Click(object sender, RoutedEventArgs e)
    {
        if (PrimarySelectedMeta is null)
            return;

        var bundle = AdventureStore.Load(PrimarySelectedMeta.Id);
        if (bundle is null)
            return;

        var id = Guid.NewGuid();
        LibraryStore.SaveItem(
            LibraryStore.LibraryKind.Scenarios,
            id,
            bundle.Metadata.Title,
            bundle.Scenario,
            bundle.Metadata.Genre,
            bundle.Scenario.Tone);

        MessageBox.Show(Window.GetWindow(this), "Scenario saved to library.", "Library");
    }

    private void Libraries_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LibrariesDialog { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private sealed class AdventureGridRow
    {
        private readonly AdventureMetadata _meta;

        public AdventureGridRow(AdventureMetadata metadata) => _meta = metadata;

        public AdventureMetadata Meta => _meta;

        public string Title => _meta.Title;

        public string Genre => _meta.Genre;

        public string Status => _meta.Archived
            ? "Archived"
            : _meta.Status == AdventureStatus.Designing
                ? "In design"
                : _meta.Status.ToString();

        public string ProjectLinked => string.IsNullOrWhiteSpace(_meta.LinkedProjectId) ? "—" : "Yes";

        public string DesignThread
        {
            get
            {
                if (_meta.Status != AdventureStatus.Designing)
                    return "—";

                var bundle = AdventureStore.Load(_meta.Id);
                if (bundle is null)
                    return "—";

                return AdventureDesignContextService.GetDesignConversationId(bundle) is { Length: > 0 }
                    ? "Yes"
                    : "—";
            }
        }

        public string TurnCount
        {
            get
            {
                var bundle = AdventureStore.Load(_meta.Id);
                return bundle?.Log.Turns.Count(t => t.Status == TurnStatus.Accepted).ToString() ?? "0";
            }
        }

        public string LastPlayedDisplay =>
            _meta.LastPlayedAt == default ? "—" : _meta.LastPlayedAt.LocalDateTime.ToString("g");
    }
}
