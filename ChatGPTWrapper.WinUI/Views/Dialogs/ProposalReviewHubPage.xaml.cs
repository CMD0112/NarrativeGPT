using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class ProposalReviewHubPage : UserControl
{
    private readonly Guid _adventureId;
    private readonly ProposalReviewCategory? _initialCategory;
    private AdventureBundle? _bundle;
    private bool _refreshing;
    private string _inferenceSourceFilter = "all";

    public ProposalReviewHubPage(Guid adventureId, ProposalReviewCategory? initialCategory = null)
    {
        _adventureId = adventureId;
        _initialCategory = initialCategory;
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public bool ChangesSaved { get; private set; }

    public event EventHandler? ItemsChanged;

    private void Reload()
    {
        _refreshing = true;
        try
        {
            _bundle = AdventureStore.Load(_adventureId);
            if (_bundle is null)
            {
                StatusLine.Text = "Adventure could not be loaded.";
                return;
            }

            var categories = ProposalReviewService.ListCategories(_bundle);
            CategoryList.ItemsSource = categories;
            BindSourceFilterCombo();
            UpdateStatusLine(categories);

            if (categories.Count == 0)
            {
                ItemList.ItemsSource = null;
                ClearDetail();
                UpdateActionButtons(null, null);
                return;
            }

            var selectedCategory = _initialCategory is { } initial
                                   && categories.Any(c => c.Category == initial)
                ? initial
                : categories[0].Category;

            CategoryList.SelectedItem = categories.First(c => c.Category == selectedCategory);
        }
        finally
        {
            _refreshing = false;
        }

        if (CategoryList.SelectedItem is ProposalReviewCategorySummary selected)
            ShowCategoryItems(selected);
    }

    private void BindSourceFilterCombo()
    {
        if (SourceFilterCombo.Items.Count == 0)
        {
            SourceFilterCombo.ItemsSource = ProposalReviewService.ListInferenceSourceFilters()
                .Select(f => new SourceFilterItem(f, ProposalReviewService.FormatInferenceSourceFilterLabel(f)))
                .ToList();
            SourceFilterCombo.DisplayMemberPath = nameof(SourceFilterItem.Label);
            SourceFilterCombo.SelectedValuePath = nameof(SourceFilterItem.Id);
        }

        SourceFilterCombo.SelectedValue = _inferenceSourceFilter;
        SourceFilterPanel.Visibility = GetSelectedCategory() == ProposalReviewCategory.DualRunCompare
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SourceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing)
            return;

        var filter = ReadSourceFilterFromCombo();
        if (string.IsNullOrWhiteSpace(filter))
            return;

        _inferenceSourceFilter = filter;
        if (CategoryList.SelectedItem is ProposalReviewCategorySummary category)
            ShowCategoryItems(category);
    }

    private string? ReadSourceFilterFromCombo()
    {
        if (SourceFilterCombo.SelectedValue is string value)
            return value;

        if (SourceFilterCombo.SelectedItem is SourceFilterItem item)
            return item.Id;

        return _inferenceSourceFilter;
    }

    private void ShowCategoryItems(ProposalReviewCategorySummary category)
    {
        if (_bundle is null)
            return;

        SourceFilterPanel.Visibility = category.Category == ProposalReviewCategory.DualRunCompare
            ? Visibility.Collapsed
            : Visibility.Visible;

        var items = category.Category == ProposalReviewCategory.DualRunCompare
            ? ProposalReviewService.ListItems(_bundle, category.Category)
            : ProposalReviewService.ListItems(_bundle, category.Category, ReadSourceFilterFromCombo() ?? _inferenceSourceFilter);
        ItemList.ItemsSource = items;
        if (items.Count > 0)
            ItemList.SelectedIndex = 0;
        else
        {
            ClearDetail();
            UpdateActionButtons(category.Category, null);
        }
    }

    private void UpdateStatusLine(IReadOnlyList<ProposalReviewCategorySummary> categories)
    {
        if (categories.Count == 0)
        {
            StatusLine.Text = "No proposals awaiting review.";
            return;
        }

        var total = categories.Sum(c => c.Count);
        StatusLine.Text = total == 1
            ? "1 proposal awaiting review"
            : $"{total} proposals awaiting review across {categories.Count} categories";
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _bundle is null || CategoryList.SelectedItem is not ProposalReviewCategorySummary category)
            return;

        ShowCategoryItems(category);
    }

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _bundle is null)
            return;

        if (ItemList.SelectedItem is not ProposalReviewListItem item)
        {
            ClearDetail();
            UpdateActionButtons(GetSelectedCategory(), null);
            return;
        }

        DetailPlaceholder.Visibility = Visibility.Collapsed;
        DetailBox.Text = ProposalReviewService.BuildDetail(_bundle, item.Key);
        UpdateActionButtons(item.Key.Category, item);
    }

    private ProposalReviewCategory? GetSelectedCategory() =>
        (CategoryList.SelectedItem as ProposalReviewCategorySummary)?.Category;

    private void UpdateActionButtons(ProposalReviewCategory? category, ProposalReviewListItem? item)
    {
        if (item is null)
        {
            AcceptButton.IsEnabled = false;
            DismissButton.IsEnabled = false;
            AcceptAllButton.IsEnabled = category is not null
                                        && ListItemsForCategory(category.Value).Any(i => i.CanAccept);
            DismissAllButton.IsEnabled = category is not null
                                         && ListItemsForCategory(category.Value).Any(i => i.CanDismiss);
        }
        else
        {
            AcceptButton.IsEnabled = item.CanAccept;
            DismissButton.IsEnabled = item.CanDismiss;
            AcceptAllButton.IsEnabled = category is not null
                                        && ListItemsForCategory(category.Value).Any(i => i.CanAccept);
            DismissAllButton.IsEnabled = category is not null
                                         && ListItemsForCategory(category.Value).Any(i => i.CanDismiss);
        }

        DetailedReviewButton.Visibility = category == ProposalReviewCategory.JsonImport
                                          && (_bundle?.Scenario.JsonImportReviewQueue.Count ?? 0) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ClearDetail()
    {
        DetailPlaceholder.Visibility = Visibility.Visible;
        DetailBox.Text = "";
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || ItemList.SelectedItem is not ProposalReviewListItem item)
            return;

        ApplyResult(ProposalReviewService.Accept(_bundle, item.Key), item.Key.Category);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || ItemList.SelectedItem is not ProposalReviewListItem item)
            return;

        ApplyResult(ProposalReviewService.Dismiss(_bundle, item.Key), item.Key.Category);
    }

    private async void AcceptAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || GetSelectedCategory() is not { } category)
            return;

        if (category == ProposalReviewCategory.JsonImport)
        {
            var proceed = await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                "Accept all",
                "Accept all JSON import proposals in this category?\n\nUnsupported or drift items may still fail individually.");
            if (!proceed)
                return;
        }

        var applied = ProposalReviewService.AcceptAll(_bundle, category);
        ChangesSaved = applied > 0;
        NotifyChanged();
        Reload();
    }

    private async void DismissAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || GetSelectedCategory() is not { } category)
            return;

        var proceed = await WinUiDialogHelper.ConfirmAsync(
            App.CurrentMainWindow,
            "Dismiss all",
            $"Dismiss all {category} proposals in this category?");
        if (!proceed)
            return;

        var dismissed = ProposalReviewService.DismissAll(_bundle, category);
        ChangesSaved = dismissed > 0;
        NotifyChanged();
        Reload();
    }

    private async void DetailedReview_Click(object sender, RoutedEventArgs e)
    {
        await WinUiDialogHostService.ShowJsonImportReviewAsync(App.CurrentMainWindow, _adventureId);
        ChangesSaved = true;
        NotifyChanged();
        Reload();
    }

    private void ApplyResult(ProposalReviewResult result, ProposalReviewCategory category)
    {
        if (result.Status == ProposalReviewActionStatus.NotFound)
        {
            Reload();
            return;
        }

        if (result.Status == ProposalReviewActionStatus.Failed)
        {
            _ = WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Review proposals",
                "Could not apply this proposal. Check the detail panel and try again.");
            Reload();
            return;
        }

        ChangesSaved = true;
        NotifyChanged();
        Reload();
    }

    private void NotifyChanged()
    {
        ItemsChanged?.Invoke(this, EventArgs.Empty);
        WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
    }

    private IReadOnlyList<ProposalReviewListItem> ListItemsForCategory(ProposalReviewCategory category)
    {
        var filter = ReadSourceFilterFromCombo() ?? _inferenceSourceFilter;
        return _bundle is null
            ? []
            : category == ProposalReviewCategory.DualRunCompare
                ? ProposalReviewService.ListItems(_bundle, category)
                : ProposalReviewService.ListItems(_bundle, category, filter);
    }

    private sealed class SourceFilterItem(string id, string label)
    {
        public string Id { get; } = id;
        public string Label { get; } = label;
    }
}
