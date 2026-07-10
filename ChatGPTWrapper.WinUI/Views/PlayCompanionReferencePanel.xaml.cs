using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Controls;
using ChatGPTWrapper.WinUI.Helpers;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class PlayCompanionReferencePanel : UserControl
{
    private WinUiPlaySessionService? _session;
    private string _entityFilter = "Characters";
    private IReadOnlyList<EntityReferenceRow> _allRows = [];
    private bool _suppressFilterSegment;
    private PlayLayoutCapabilities _capabilities = PlayLayoutCapabilities.FromContentWidth(320);

    public PlayCompanionReferencePanel()
    {
        InitializeComponent();
        SizeChanged += OnPanelSizeChanged;
    }

    private double _lastLayoutWidth = -1;

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Only rebuild when width crosses a meaningful threshold — SizeChanged fires
        // continuously during companion resize and must not thrash the entity list.
        if (Math.Abs(e.NewSize.Width - _lastLayoutWidth) < 24)
            return;

        _lastLayoutWidth = e.NewSize.Width;
        RefreshEntities();
    }

    public void Bind(WinUiPlaySessionService session)
    {
        _session = session;
        InitializeFilters();
        RefreshEntities();
    }

    public void ApplyLayout(PlayLayoutContext context)
    {
        _capabilities = context.Capabilities;
        InitializeFilters();
        RefreshEntities();
    }

    public void RefreshEntities()
    {
        if (_session?.CurrentBundle is not { } bundle)
        {
            EntityList.ItemsSource = null;
            return;
        }

        var contentWidth = ActualWidth > 0
            ? PlayResponsiveTiers.ContentWidth(ActualWidth, PlayResponsiveTiers.CompactMargin)
            : 320;
        _capabilities = PlayLayoutCapabilities.FromContentWidth(contentWidth);
        _allRows = EntityReferenceRowBuilder.BuildRows(bundle, _entityFilter, _capabilities);
        ApplyEntityFilter();
    }

    private void InitializeFilters()
    {
        var filters = EntityReferenceRowBuilder.ResolveFilters(null);
        FilterSegment.ItemsSource = filters
            .Select(f => new SegmentedItemModel
            {
                Content = EntityReferenceRowBuilder.FilterDisplayLabel(f, _capabilities.UseEntityCompactTemplate),
                Tag = f,
            })
            .Cast<object>()
            .ToList();

        _suppressFilterSegment = true;
        try
        {
            var index = filters
                .Select((f, i) => (f, i))
                .FirstOrDefault(t => string.Equals(t.f, _entityFilter, StringComparison.OrdinalIgnoreCase))
                .i;
            FilterSegment.SelectedIndex = index >= 0 ? index : 0;
            if (FilterSegment.SelectedTag is string tag)
                _entityFilter = tag;
        }
        finally
        {
            _suppressFilterSegment = false;
        }
    }

    private void ApplyEntityFilter()
    {
        var needle = SearchBox.Text ?? string.Empty;
        var rows = string.IsNullOrWhiteSpace(needle)
            ? _allRows
            : EntityReferenceRowBuilder.FilterAndSortRows(_allRows, needle, EntityListSortMode.NameAscending, pinSortEnabled: false);
        EntityList.ItemsSource = rows;
    }

    private EntityReferenceRow? SelectedRow =>
        EntityList.SelectedItem as EntityReferenceRow;

    private void FilterSegment_SelectionChanged(object sender, EventArgs e)
    {
        if (_suppressFilterSegment || FilterSegment.SelectedTag is not string filter)
            return;

        _entityFilter = filter;
        RefreshEntities();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyEntityFilter();

    private void EntityList_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        WinUiListFlyoutHelper.SelectItemUnderPointer(sender, e);

    private void EntityContextFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
            return;

        var hasRow = SelectedRow is not null;
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
            item.IsEnabled = hasRow;
    }

    private async void EntityList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        await OpenEntityEditorAsync(row);
    }

    private async void EntityEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
            return;

        await OpenEntityEditorAsync(row);
    }

    private async Task ReloadEntitiesAfterMutationAsync()
    {
        if (_session?.CurrentBundle is { } bundle)
            await _session.LoadAdventureAsync(bundle.Metadata.Id);
        RefreshEntities();
    }

    private async void EntityDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle || SelectedRow is not { } row)
            return;

        if (await WinUiDialogHostService.ShowEntityDeleteAsync(App.CurrentMainWindow, bundle.Metadata.Id, row))
            await ReloadEntitiesAfterMutationAsync();
    }

    private void EntityTogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle || SelectedRow is not { } row)
            return;

        if (!EntityReferenceEditService.TryTogglePin(bundle, row))
            return;

        RefreshEntities();
    }

    private async void EntityMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle || SelectedRow is not { } row)
            return;

        if (await WinUiDialogHostService.ShowEntityMergeAsync(
                App.CurrentMainWindow,
                bundle.Metadata.Id,
                row,
                _entityFilter))
            await ReloadEntitiesAfterMutationAsync();
    }

    private async void EntityRetire_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle || SelectedRow is not { } row)
            return;

        if (await WinUiDialogHostService.ShowEntityRetireAsync(
                App.CurrentMainWindow,
                bundle.Metadata.Id,
                row,
                _entityFilter))
            await ReloadEntitiesAfterMutationAsync();
    }

    private async Task OpenEntityEditorAsync(EntityReferenceRow row)
    {
        await WinUiDialogHostService.ShowEntityEditAsync(
            App.CurrentMainWindow,
            _session!.CurrentBundle!.Metadata.Id,
            row);
        await ReloadEntitiesAfterMutationAsync();
    }
}
