using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public sealed class AdventureThreadManagerActions
{
    public required Func<Task> StartNarrativeFromSourcesAsync { get; init; }

    public required Func<Task> OpenPlayHandoffWizardAsync { get; init; }

    public required Func<Task> StartNewDesignThreadAsync { get; init; }

    public required Func<AdventureThreadKind, Guid, Task> ActivateEntryAsync { get; init; }

    public required Func<AdventureThreadKind, Guid, Task> OpenEntryAsync { get; init; }
}

public partial class AdventureThreadManagerDialog : Window
{
    private readonly Guid _adventureId;
    private readonly AdventureThreadManagerActions _actions;
    private readonly Dictionary<AdventureThreadKind, ThreadTabContent> _tabPanels = new();
    private bool _changed;

    public AdventureThreadManagerDialog(
        Guid adventureId,
        AdventureThreadManagerActions actions,
        AdventureThreadKind initialKind = AdventureThreadKind.Play)
    {
        _adventureId = adventureId;
        _actions = actions;
        InitializeComponent();

        BuildTab(AdventureThreadKind.Play, labelEditable: false);
        BuildTab(AdventureThreadKind.Design, labelEditable: true);

        KindTabs.SelectedIndex = initialKind == AdventureThreadKind.Design ? 1 : 0;
        RefreshCurrentTab();
    }

    private void BuildTab(AdventureThreadKind kind, bool labelEditable)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = !labelEditable,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            MinHeight = 220,
        };

        if (labelEditable)
        {
            grid.CellEditEnding += (_, e) =>
            {
                if (e.EditAction != DataGridEditAction.Commit
                    || e.Row.Item is not ThreadManagerRow row)
                {
                    return;
                }

                SaveLabel(row);
            };
        }

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Label",
            Binding = new Binding(nameof(ThreadManagerRow.Label)) { Mode = labelEditable ? BindingMode.TwoWay : BindingMode.OneWay },
            Width = new DataGridLength(120),
            IsReadOnly = !labelEditable,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(ThreadManagerRow.StatusDisplay)),
            Width = new DataGridLength(80),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Conversation",
            Binding = new Binding(nameof(ThreadManagerRow.ConversationDisplay)),
            Width = new DataGridLength(140),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Tab",
            Binding = new Binding(nameof(ThreadManagerRow.TabTitleDisplay)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
        });

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var setActive = new Button { Content = "Set active", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        setActive.Click += (_, _) => RunRowAction(grid, row => SetActiveAsync(kind, row));
        var archive = new Button { Content = "Archive", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        archive.Click += (_, _) => RunRowAction(grid, row => ArchiveAsync(kind, row));
        var open = new Button { Content = "Open in browser", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        open.Click += (_, _) => RunRowAction(grid, row => OpenAsync(kind, row));
        var newNarrative = new Button
        {
            Content = PlayThreadRotationCopy.NarrativeFromSourcesButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        newNarrative.Click += async (_, _) =>
        {
            if (kind == AdventureThreadKind.Play)
                await _actions.StartNarrativeFromSourcesAsync();
            else
                await StartNewDesignThreadAsync(kind);
        };
        var handOff = new Button
        {
            Content = PlayThreadRotationCopy.HandoffToNewChatButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
            Visibility = kind == AdventureThreadKind.Play ? Visibility.Visible : Visibility.Collapsed,
        };
        handOff.Click += async (_, _) => await _actions.OpenPlayHandoffWizardAsync();

        actions.Children.Add(setActive);
        actions.Children.Add(archive);
        actions.Children.Add(open);
        actions.Children.Add(newNarrative);
        if (kind == AdventureThreadKind.Play)
            actions.Children.Add(handOff);

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(actions);

        _tabPanels[kind] = new ThreadTabContent { Grid = grid, Root = panel };

        foreach (TabItem tab in KindTabs.Items)
        {
            if (tab.Tag as string != kind.ToString())
                continue;

            tab.Content = _tabPanels[kind].Root;
            break;
        }
    }

    private void KindTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshCurrentTab();

    private void RefreshCurrentTab()
    {
        if (KindTabs.SelectedItem is not TabItem tab
            || tab.Tag is not string kindName
            || !Enum.TryParse<AdventureThreadKind>(kindName, out var kind))
        {
            return;
        }

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var panel = _tabPanels[kind];
        var rows = AdventureThreadRegistryService.ListEntries(bundle, kind)
            .Select(entry => ThreadManagerRow.FromEntry(bundle, entry))
            .ToList();
        panel.Grid.ItemsSource = new ObservableCollection<ThreadManagerRow>(rows);

        var playActive = AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Play);
        var designActive = AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Design);
        ActiveSummaryBlock.Text = $"Active play: {playActive} · Active design: {designActive}";
    }

    private ThreadManagerRow? GetSelectedRow(DataGrid grid) =>
        grid.SelectedItem as ThreadManagerRow;

    private void RunRowAction(DataGrid grid, Func<ThreadManagerRow, Task> action)
    {
        if (GetSelectedRow(grid) is not { } row)
        {
            MessageBox.Show(this, "Select a thread row first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _ = RunRowActionAsync(action, row);
    }

    private async Task RunRowActionAsync(Func<ThreadManagerRow, Task> action, ThreadManagerRow row)
    {
        try
        {
            await action(row);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SetActiveAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        if (row.IsArchived)
        {
            MessageBox.Show(this, "Archived threads cannot be set active.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.IsActive)
            return;

        await _actions.ActivateEntryAsync(kind, row.EntryId);
        _changed = true;
        RefreshCurrentTab();
    }

    private Task ArchiveAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        if (row.IsActive)
        {
            MessageBox.Show(
                this,
                "Switch to a different active thread before archiving this one.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        if (row.IsArchived)
            return Task.CompletedTask;

        if (MessageBox.Show(
                this,
                $"Archive \"{row.Label}\"? The conversation id is kept for reference but will no longer be active.",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.ArchiveEntry(bundle, row.EntryId);
        AdventureThreadRegistryService.Persist(bundle);
        _changed = true;
        RefreshCurrentTab();
        return Task.CompletedTask;
    }

    private async Task OpenAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        await _actions.OpenEntryAsync(kind, row.EntryId);
        _changed = true;
        RefreshCurrentTab();
    }

    private async Task StartNewDesignThreadAsync(AdventureThreadKind kind)
    {
        if (kind != AdventureThreadKind.Design)
            return;

        await _actions.StartNewDesignThreadAsync();
        _changed = true;
        RefreshCurrentTab();
    }

    private void SaveLabel(ThreadManagerRow row)
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.UpdateEntryLabel(bundle, row.EntryId, row.Label);
        AdventureThreadRegistryService.Persist(bundle);
        _changed = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_changed)
            DialogResult = true;

        Close();
    }

    private sealed class ThreadTabContent
    {
        public required DataGrid Grid { get; init; }

        public required Panel Root { get; init; }
    }

    private sealed class ThreadManagerRow
    {
        public Guid EntryId { get; init; }

        public string Label { get; set; } = "";

        public string StatusDisplay { get; init; } = "";

        public string ConversationDisplay { get; init; } = "";

        public string TabTitleDisplay { get; init; } = "";

        public bool IsActive { get; init; }

        public bool IsArchived { get; init; }

        public static ThreadManagerRow FromEntry(AdventureBundle bundle, AdventureThreadEntry entry)
        {
            var isActive = AdventureThreadRegistryService.IsActiveEntry(bundle, entry.Id);
            var conversation = string.IsNullOrWhiteSpace(entry.ConversationId)
                ? "—"
                : entry.ConversationId.Length > 14
                    ? entry.ConversationId[..14] + "…"
                    : entry.ConversationId;

            return new ThreadManagerRow
            {
                EntryId = entry.Id,
                Label = entry.Label,
                StatusDisplay = isActive ? "Active" : entry.Status.ToString(),
                ConversationDisplay = conversation,
                TabTitleDisplay = string.IsNullOrWhiteSpace(entry.PinnedTabTitle) ? "—" : entry.PinnedTabTitle,
                IsActive = isActive,
                IsArchived = entry.Status == AdventureThreadStatus.Archived,
            };
        }
    }
}
