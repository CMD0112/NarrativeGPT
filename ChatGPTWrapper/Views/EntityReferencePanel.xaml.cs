using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;

namespace ChatGPTWrapper.Views;

public partial class EntityReferencePanel : UserControl
{
    public event EventHandler? SelectionChanged;

    public event EventHandler? EntitiesChanged;

    public event EventHandler? SuggestEntitiesRequested;

    public event EventHandler<EntityReferenceRow>? ExpandEntityRequested;

    private AdventureBundle? _bundle;
    private EntityReferencePanelOptions _options = new();
    private EntityReferenceEditCallbacks? _callbacks;
    private PlayLayoutCapabilities _layoutCapabilities = PlayLayoutCapabilities.FromContentWidth(320);
    private string _entityFilter = "Characters";
    private string _searchText = "";
    private EntityListSortMode _sortMode = EntityListSortMode.NameAscending;
    private IReadOnlyList<string> _filters = [];
    private EntityEditModel? _activeEditModel;
    private string? _activeEditCategory;
    private string? _activeEditPriorName;
    private EntityEditFormHost? _activeEditForm;
    private EntityWorkspaceHost? _activeWorkspace;

    public EntityReferencePanel()
    {
        InitializeComponent();
        WireEditForm(InlineEditForm);
        WireWorkspace(SideWorkspace);
        _filters = EntityReferenceRowBuilder.ResolveFilters(_options);
        BuildEntityFilterPills();
        Focusable = true;
    }

    public EntityReferenceRow? SelectedRow => EntityList.SelectedItem as EntityReferenceRow;

    public string CurrentFilter => _entityFilter;

    public MenuItem PinMenuItem => PinEntityMenuItem;

    public MenuItem SuggestEntitiesMenuItemControl => SuggestEntitiesMenuItem;

    public MenuItem ExpandEntityMenuItemControl => ExpandEntityMenuItem;

    public void Configure(EntityReferencePanelOptions options, EntityReferenceEditCallbacks? callbacks = null)
    {
        _options = options;
        _callbacks = WrapCallbacks(callbacks);
        _entityFilter = options.DefaultFilter;
        _filters = EntityReferenceRowBuilder.ResolveFilters(options);

        PinEntityMenuItem.Visibility = options.ShowPinToggle ? Visibility.Visible : Visibility.Collapsed;
        EntityRowPinMenuItem.Visibility = options.ShowPinToggle ? Visibility.Visible : Visibility.Collapsed;
        SuggestEntitiesMenuItem.Visibility = options.ShowAiActions ? Visibility.Visible : Visibility.Collapsed;
        ExpandEntityMenuItem.Visibility = options.ShowAiActions ? Visibility.Visible : Visibility.Collapsed;
        EntityMoreMenu.Visibility = options.ShowMoreMenu && (options.ShowPinToggle || options.ShowAiActions)
            ? Visibility.Visible
            : Visibility.Collapsed;

        InlineEditForm.ShowPinToggle = options.ShowPinToggle;
        SideWorkspace.ProfileFormHost.ShowPinToggle = options.ShowPinToggle;
        PinSortMenuItem.Visibility = options.ShowPinToggle ? Visibility.Visible : Visibility.Collapsed;

        CloseInlineEdit();
        BuildEntityFilterPills();
        RefreshList();
    }

    public void RefreshActiveHighlightState()
    {
        _activeEditForm?.RefreshHighlightFromChrome();
        _activeWorkspace?.ProfileFormHost.RefreshHighlightFromChrome();
    }

    private EntityReferenceEditCallbacks? WrapCallbacks(EntityReferenceEditCallbacks? inner)
    {
        if (inner is null)
            return null;

        return new EntityReferenceEditCallbacks
        {
            GetPhraseHighlightRules = inner.GetPhraseHighlightRules,
            CommitPhraseHighlightRules = rules =>
            {
                inner.CommitPhraseHighlightRules?.Invoke(rules);
                RefreshActiveHighlightState();
            },
            OpenSourceManagerAsync = inner.OpenSourceManagerAsync,
            OnBundleReloaded = inner.OnBundleReloaded,
            OnStatusRefreshRequested = inner.OnStatusRefreshRequested,
            OnSourceSyncCompleted = inner.OnSourceSyncCompleted,
            OnPhraseHighlightRulesChanged = () =>
            {
                RefreshActiveHighlightState();
                inner.OnPhraseHighlightRulesChanged?.Invoke();
            },
        };
    }

    public void LoadBundle(AdventureBundle bundle)
    {
        _bundle = bundle;
        CloseInlineEdit();
        RefreshList();
    }

    public void ApplyLayout(PlayLayoutCapabilities capabilities)
    {
        _layoutCapabilities = capabilities;

        EntityListHost.Padding = capabilities.UseEntityCompactTemplate
            ? new Thickness(2)
            : capabilities.UseEntityWideTemplate
                ? new Thickness(6)
                : new Thickness(4);
        EntityActionsRow.Margin = capabilities.UseCompactSessionPadding
            ? new Thickness(0, 0, 0, 6)
            : new Thickness(0, 0, 0, 8);

        EntityList.ItemTemplate = capabilities.UseEntityCompactTemplate
            ? (DataTemplate)FindResource("EntityRowTemplateCompact")
            : (DataTemplate)FindResource("EntityRowTemplateAdaptive");

        EntityMoreMenuItem.Header = capabilities.UseCompactEntityMore
            ? "More…"
            : "More entity actions…";

        BuildEntityFilterPills();
        RefreshList();
    }

    public void SetFilter(string filter)
    {
        CloseInlineEdit();
        _entityFilter = filter;
        BuildEntityFilterPills();
        RefreshList();
    }

    public void SelectEntity(string filter, Guid entityId)
    {
        SetFilter(filter);
        var row = _bundle is null
            ? null
            : EntityReferenceRowBuilder.FindRow(_bundle, filter, entityId, _layoutCapabilities);
        if (row is not null)
            EntityList.SelectedItem = row;
    }

    public void RefreshList()
    {
        if (_bundle is null)
        {
            EntityList.ItemsSource = null;
            UpdateEmptyState(0);
            return;
        }

        var selectedId = (EntityList.SelectedItem as EntityReferenceRow)?.Id;
        var rows = EntityReferenceRowBuilder.BuildRows(_bundle, _entityFilter, _layoutCapabilities);
        rows = EntityReferenceRowBuilder.FilterAndSortRows(
            rows,
            _searchText,
            _sortMode,
            _options.ShowPinToggle);

        EntityList.ItemsSource = rows;

        if (selectedId is { } id)
        {
            var restored = rows.FirstOrDefault(r => r.Id == id);
            if (restored is not null)
                EntityList.SelectedItem = restored;
        }

        UpdateEmptyState(rows.Count);
    }

    private void UpdateEmptyState(int count)
    {
        var compact = _layoutCapabilities.UseCompactEntityMore
                      || _layoutCapabilities.UseShellHeaderFlyouts;
        var label = EntityReferenceRowBuilder.FilterDisplayLabel(_entityFilter, compact);
        EmptyStateText.Text = string.IsNullOrWhiteSpace(_searchText)
            ? $"No entities in {label}"
            : $"No entities match “{_searchText.Trim()}”";
        EmptyStatePanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EntityList.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EntitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = EntitySearchBox.Text;
        RefreshList();
    }

    private void SortName_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = EntityListSortMode.NameAscending;
        RefreshList();
    }

    private void SortRecentlyEdited_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = EntityListSortMode.RecentlyEdited;
        RefreshList();
    }

    private void SortPinnedFirst_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = EntityListSortMode.PinnedFirst;
        RefreshList();
    }

    private void EntityPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (EntityList.Items.Count == 0)
            return;

        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (SelectedRow is { } row)
                {
                    TryOpenEditor(row);
                    e.Handled = true;
                }
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var index = EntityList.SelectedIndex;
        if (index < 0 && delta > 0)
            index = -1;

        var next = Math.Clamp(index + delta, 0, EntityList.Items.Count - 1);
        EntityList.SelectedIndex = next;
        EntityList.ScrollIntoView(EntityList.SelectedItem);
    }

    private async void SyncBadge_Click(object sender, MouseButtonEventArgs e)
    {
        if (_bundle is null || sender is not FrameworkElement { DataContext: EntityReferenceRow row })
            return;

        e.Handled = true;
        switch (row.SyncStatus)
        {
            case EntitySyncStatus.UnresolvedDrift:
            case EntitySyncStatus.SourcesStale:
                EntityReferenceEditService.PromptReconcile(
                    _bundle,
                    Window.GetWindow(this),
                    new CanonEditContext { Category = _entityFilter, EntityId = row.Id },
                    _callbacks);
                break;
            case EntitySyncStatus.NeedsPublish:
                if (_callbacks?.OpenSourceManagerAsync is not null)
                    await _callbacks.OpenSourceManagerAsync();
                break;
        }
    }

    public void UpdateSecondaryActionStates(bool hasLinkedProject, bool hasRecentExchange)
    {
        if (_options.ShowAiActions)
        {
            SuggestEntitiesMenuItem.IsEnabled = hasLinkedProject && hasRecentExchange;
            ExpandEntityMenuItem.IsEnabled = hasLinkedProject && SelectedRow is not null;
        }

        if (_options.ShowPinToggle)
        {
            PinEntityMenuItem.IsEnabled = SelectedRow is not null;
            EntityRowPinMenuItem.IsEnabled = SelectedRow is not null;
        }
    }

    public bool TryOpenPlayerEditor(Window? owner = null)
    {
        if (_bundle is null)
            return false;

        if (_filters.Any(f => string.Equals(f, "Player", StringComparison.OrdinalIgnoreCase)))
            SetFilter("Player");

        var row = EntityReferenceRowBuilder.FindRow(
            _bundle,
            "Player",
            EntityEditMapper.PlayerEntityId,
            _layoutCapabilities);
        if (row is not null)
            EntityList.SelectedItem = row;

        if (!EntityReferenceEditService.TryOpenEditor(
                _bundle,
                owner ?? Window.GetWindow(this),
                "Player",
                row,
                isNew: false,
                _callbacks,
                _options.PromptCanonReconcile))
            return false;

        FinishEntityMutation();
        return true;
    }

    public bool TryOpenEditor(EntityReferenceRow? row, bool isNew = false)
    {
        if (_bundle is null)
            return false;

        return TryOpenModalEditor(row, isNew);
    }

    private bool TryOpenModalEditor(EntityReferenceRow? row, bool isNew)
    {
        if (!EntityReferenceEditService.TryOpenEditor(
                _bundle!,
                Window.GetWindow(this),
                _entityFilter,
                row,
                isNew,
                _callbacks,
                _options.PromptCanonReconcile))
            return false;

        FinishEntityMutation();
        return true;
    }

    private void WireEditForm(EntityEditFormHost form)
    {
        form.SaveRequested += (_, _) => CommitActiveEdit(deleted: false);
        form.CancelRequested += (_, _) => CloseInlineEdit();
        form.DeleteRequested += (_, _) => ConfirmDeleteActive();
    }

    private void WireWorkspace(EntityWorkspaceHost workspace)
    {
        workspace.SaveRequested += (_, _) => CommitActiveEdit(deleted: false);
        workspace.CancelRequested += (_, _) => CloseInlineEdit();
        workspace.DeleteRequested += (_, _) => ConfirmDeleteActive();
    }

    private void ConfirmDeleteActive()
    {
        if (_activeEditModel is null)
            return;

        if (MessageBox.Show(
                Window.GetWindow(this),
                $"Delete “{_activeEditModel.Name}”? This cannot be undone.",
                "Delete entity",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        CommitActiveEdit(deleted: true);
    }

    private void CommitActiveEdit(bool deleted)
    {
        if (_bundle is null || _activeEditModel is null || _activeEditCategory is null)
            return;

        var form = _activeEditForm ?? _activeWorkspace?.ProfileFormHost;
        if (form is null && !deleted)
            return;

        if (!EntityReferenceEditService.TryFinishEntityEditorSave(
                _bundle,
                form!,
                _activeEditModel,
                deleted,
                _activeEditCategory,
                _activeEditPriorName,
                Window.GetWindow(this),
                _callbacks,
                _options.PromptCanonReconcile,
                _options.PromptRenameWizard))
        {
            return;
        }

        CloseInlineEdit();
        FinishEntityMutation();
    }

    private void CloseInlineEdit()
    {
        _activeEditModel = null;
        _activeEditCategory = null;
        _activeEditPriorName = null;
        _activeEditForm = null;
        _activeWorkspace = null;
        InlineEditHost.Visibility = Visibility.Collapsed;
        SideEditHost.Visibility = Visibility.Collapsed;
    }

    private void ApplySidePanelLayout(bool sidePanelVisible)
    {
        SideEditColumn.Width = sidePanelVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (!sidePanelVisible)
            SideEditHost.Visibility = Visibility.Collapsed;
    }

    private void FinishEntityMutation()
    {
        RefreshList();
        EntitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildEntityFilterPills()
    {
        EntityFilterPanel.Children.Clear();
        var compact = _layoutCapabilities.UseCompactEntityMore
                      || _layoutCapabilities.UseShellHeaderFlyouts;

        foreach (var filter in _filters)
        {
            var button = new ToggleButton
            {
                Tag = filter,
                Content = EntityReferenceRowBuilder.FilterDisplayLabel(filter, compact),
                Style = (Style)FindResource("FilterPillToggleStyle"),
                Margin = new Thickness(0, 0, compact ? 4 : 6, 4),
                Padding = compact ? new Thickness(6, 3, 6, 3) : new Thickness(8, 3, 8, 3),
                IsChecked = filter.Equals(_entityFilter, StringComparison.OrdinalIgnoreCase),
            };
            button.Checked += EntityFilterPill_Changed;
            button.Unchecked += EntityFilterPill_Changed;
            EntityFilterPanel.Children.Add(button);
        }
    }

    private void EntityFilterPill_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton pill || pill.IsChecked != true)
            return;

        foreach (ToggleButton child in EntityFilterPanel.Children.OfType<ToggleButton>())
        {
            if (!ReferenceEquals(child, pill))
                child.IsChecked = false;
        }

        CloseInlineEdit();
        _entityFilter = pill.Tag?.ToString() ?? "Characters";
        RefreshList();
    }

    private void EntityList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, EventArgs.Empty);

    private void EntityList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var target = e.OriginalSource as DependencyObject;
        while (target is not null and not ListBoxItem)
            target = VisualTreeHelper.GetParent(target);

        if (target is ListBoxItem item)
        {
            item.IsSelected = true;
            e.Handled = true;
        }
    }

    private void EntityList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (SelectedRow is { } row)
            TryOpenEditor(row);
    }

    private void AddEntity_Click(object sender, RoutedEventArgs e) =>
        TryOpenEditor(row: null, isNew: true);

    private void EditEntity_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
            TryOpenEditor(row);
    }

    private void DeleteEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is not { } row)
            return;

        if (!EntityReferenceEditService.TryDelete(
                _bundle,
                Window.GetWindow(this),
                row,
                _callbacks,
                _options.PromptCanonReconcile))
            return;

        FinishEntityMutation();
    }

    private void TogglePinEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is not { } row)
            return;

        if (!EntityReferenceEditService.TryTogglePin(_bundle, row))
            return;

        FinishEntityMutation();
    }

    private void SuggestEntities_Click(object sender, RoutedEventArgs e) =>
        SuggestEntitiesRequested?.Invoke(this, EventArgs.Empty);

    private void ExpandEntity_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is { } row)
            ExpandEntityRequested?.Invoke(this, row);
    }

    private void RenameEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is not { } row)
            return;

        TryOpenEditor(row);
        if (_activeEditModel is null)
            return;

        MessageBox.Show(Window.GetWindow(this),
            "Edit the name in Profile, then Save to open the rename wizard.",
            "Rename entity",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void MergeEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is not { } row)
            return;

        var dlg = new EntityMergeDialog(_bundle, row, _entityFilter)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true)
            return;

        FinishEntityMutation();
    }

    private void RetireEntity_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is not { } row)
            return;

        var dlg = new EntityRetireDialog(_bundle, row, _entityFilter)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() != true)
            return;

        FinishEntityMutation();
    }
}
