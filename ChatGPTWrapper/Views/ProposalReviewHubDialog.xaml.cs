using System.Collections.ObjectModel;
using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.Views;

public partial class ProposalReviewHubDialog : ShellDialogWindow
{
    public event EventHandler? ItemsChanged;

    public event EventHandler<EntityReviewItem>? EntityAccepted;

    private readonly Guid _adventureId;

    private AdventureBundle? _bundle;

    private ProposalReviewCategory? _initialCategory;

    private bool _refreshing;

    public ProposalReviewHubDialog(AdventureBundle bundle, ProposalReviewCategory? initialCategory = null)
    {
        _adventureId = bundle.Metadata.Id;
        _initialCategory = initialCategory;
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    public bool ChangesSaved { get; private set; }

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
            _initialCategory = null;

            CategoryList.SelectedItem = categories.First(c => c.Category == selectedCategory);
        }
        finally
        {
            _refreshing = false;
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

    private void CategoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_refreshing || _bundle is null || CategoryList.SelectedItem is not ProposalReviewCategorySummary category)
            return;

        var items = ProposalReviewService.ListItems(_bundle, category.Category);
        ItemList.ItemsSource = items;
        if (items.Count > 0)
            ItemList.SelectedIndex = 0;
        else
        {
            ClearDetail();
            UpdateActionButtons(category.Category, null);
        }
    }

    private void ItemList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
                                        && ProposalReviewService.ListItems(_bundle!, category.Value).Any(i => i.CanAccept);
            DismissAllButton.IsEnabled = category is not null
                                         && ProposalReviewService.ListItems(_bundle!, category.Value).Any(i => i.CanDismiss);
        }
        else
        {
            AcceptButton.IsEnabled = item.CanAccept;
            DismissButton.IsEnabled = item.CanDismiss;
            AcceptAllButton.IsEnabled = category is not null
                                        && ProposalReviewService.ListItems(_bundle!, category.Value).Any(i => i.CanAccept);
            DismissAllButton.IsEnabled = category is not null
                                         && ProposalReviewService.ListItems(_bundle!, category.Value).Any(i => i.CanDismiss);
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

        EntityReviewItem? acceptedEntity = null;
        if (item.Key.Category == ProposalReviewCategory.Entity)
            acceptedEntity = _bundle.Entities.ReviewQueue.FirstOrDefault(e => e.Id == item.Key.Id);

        var result = ProposalReviewService.Accept(_bundle, item.Key);
        if (result.RequiresCanonReconcile && acceptedEntity is not null)
            EntityAccepted?.Invoke(this, acceptedEntity);

        ApplyResult(result, item.Key.Category);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || ItemList.SelectedItem is not ProposalReviewListItem item)
            return;

        ApplyResult(ProposalReviewService.Dismiss(_bundle, item.Key), item.Key.Category);
    }

    private void AcceptAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || GetSelectedCategory() is not { } category)
            return;

        if (category == ProposalReviewCategory.JsonImport
            && MessageBox.Show(
                this,
                "Accept all JSON import proposals in this category?\n\nUnsupported or drift items may still fail individually.",
                "Accept all",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var applied = ProposalReviewService.AcceptAll(_bundle, category);
        ChangesSaved = applied > 0;
        NotifyChanged();
        Reload();
    }

    private void DismissAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || GetSelectedCategory() is not { } category)
            return;

        if (MessageBox.Show(
                this,
                $"Dismiss all {category} proposals in this category?",
                "Dismiss all",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var dismissed = ProposalReviewService.DismissAll(_bundle, category);
        ChangesSaved = dismissed > 0;
        NotifyChanged();
        Reload();
    }

    private void DetailedReview_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new JsonImportReviewDialog(_adventureId) { Owner = this };
        dlg.ShowDialog();
        if (dlg.ChangesSaved)
        {
            ChangesSaved = true;
            NotifyChanged();
        }

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
            MessageBox.Show(
                this,
                "Could not apply this proposal. Check the detail panel and try again.",
                "Review proposals",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Reload();
            return;
        }

        ChangesSaved = true;
        NotifyChanged();
        Reload();
    }

    private void NotifyChanged() => ItemsChanged?.Invoke(this, EventArgs.Empty);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
