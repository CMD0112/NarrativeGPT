using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class EntityEditDialog : ShellDialogWindow
{
    private readonly AdventureBundle _bundle;
    private readonly string _category;
    private readonly EntityReferenceEditCallbacks? _callbacks;
    private bool _contextTabsMounted;

    public bool Deleted { get; private set; }

    public EntityEditDialog(
        AdventureBundle bundle,
        EntityEditModel model,
        string category,
        EntityReferenceEditCallbacks? callbacks = null)
    {
        _bundle = bundle;
        _category = category;
        _callbacks = callbacks;
        InitializeComponent();
        Title = model.IsNew ? $"New {model.TypeLabel.ToLowerInvariant()}" : model.Name;
        HeaderTitleBlock.Text = model.IsNew
            ? $"Add {model.TypeLabel.ToLowerInvariant()}"
            : model.Name;
        DeleteButton.Visibility = model.IsNew ? Visibility.Collapsed : Visibility.Visible;

        BuildHeaderLabels(model);
        UpdateHeaderPortrait(model);
        UpdateHeaderSyncBadge(model);
        UpdateHeaderStateSkim(model);
        FormHost.ShowGroupedSections = true;
        FormHost.SetComposerInsert(callbacks?.InsertIntoComposer);
        FormHost.LoadModel(model, callbacks);
        Workspace.LoadModel(bundle, model, category, callbacks);
        MountContextTabs();

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void BuildHeaderLabels(EntityEditModel model)
    {
        HeaderLabelsPanel.Children.Clear();
        foreach (var label in model.HeaderLabels)
        {
            HeaderLabelsPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("ShellBadgeStyle"),
                Margin = new Thickness(0, 0, 6, 4),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                },
            });
        }
    }

    private void UpdateHeaderPortrait(EntityEditModel model)
    {
        ImageSource? source = null;
        if (!string.IsNullOrWhiteSpace(model.PendingImageSourcePath))
            source = EntityMediaService.TryLoadImageFromAbsolute(model.PendingImageSourcePath, 144);
        else if (!string.IsNullOrWhiteSpace(model.ImagePath))
            source = EntityMediaService.TryLoadImage(model.AdventureId, model.ImagePath, 144);

        if (source is null)
        {
            PortraitThumbImage.Visibility = Visibility.Collapsed;
            PortraitPlaceholderText.Visibility = Visibility.Visible;
            PortraitPlaceholderText.Text = string.IsNullOrWhiteSpace(model.Name)
                ? "?"
                : model.Name.Trim()[..1].ToUpperInvariant();
            return;
        }

        PortraitThumbImage.Source = source;
        PortraitThumbImage.Visibility = Visibility.Visible;
        PortraitPlaceholderText.Visibility = Visibility.Collapsed;
    }

    private void UpdateHeaderSyncBadge(EntityEditModel model)
    {
        if (model.IsNew)
        {
            HeaderSyncBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var status = EntitySyncStatusService.GetStatus(_bundle, model.Id, _category);
        if (status == EntitySyncStatus.InSync)
        {
            HeaderSyncBadge.Visibility = Visibility.Collapsed;
            return;
        }

        HeaderSyncBadge.Text = EntitySyncStatusService.BadgeText(status);
        HeaderSyncBadge.Foreground = status switch
        {
            EntitySyncStatus.UnresolvedDrift => (Brush)FindResource("WarningBrush"),
            EntitySyncStatus.NeedsPublish => (Brush)FindResource("AccentPrimaryBrush"),
            _ => (Brush)FindResource("TextMutedBrush"),
        };
        HeaderSyncBadge.Visibility = Visibility.Visible;
    }

    private void UpdateHeaderStateSkim(EntityEditModel model)
    {
        if (!string.Equals(_category, "Player", StringComparison.OrdinalIgnoreCase) || model.IsNew)
        {
            HeaderStatePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var location = _bundle.State.CurrentLocation?.Trim();
        var condition = _bundle.State.PlayerCondition?.Trim();
        if (string.IsNullOrWhiteSpace(location) && string.IsNullOrWhiteSpace(condition))
        {
            HeaderStatePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location))
            parts.Add($"Location: {location}");
        if (!string.IsNullOrWhiteSpace(condition))
            parts.Add($"Condition: {condition}");
        HeaderStateSkim.Text = string.Join(" · ", parts);
        HeaderStatePanel.Visibility = Visibility.Visible;
        HeaderStateLink.Visibility = _callbacks?.OpenStateTab is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void HeaderStateLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _callbacks?.OpenStateTab?.Invoke();

    private void MountContextTabs()
    {
        if (_contextTabsMounted)
            return;

        SourcesContentHost.Content = Workspace.DetachTabContent(EntityWorkspaceTab.Sources);
        MentionsContentHost.Content = Workspace.DetachTabContent(EntityWorkspaceTab.Mentions);
        HistoryContentHost.Content = Workspace.DetachTabContent(EntityWorkspaceTab.History);
        _contextTabsMounted = true;
    }

    private void DialogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_contextTabsMounted || DialogTabs.SelectedItem is not TabItem tab)
            return;

        if (tab.Header is not string header || header == "Profile")
            return;

        Workspace.RefreshTabs();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!FormHost.TryHarvestModel(out var validationMessage))
        {
            MessageBox.Show(this, validationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var model = FormHost.Model;
        if (model is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete “{model.Name}”? This cannot be undone.",
                $"Delete {model.TypeLabel.ToLowerInvariant()}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Deleted = true;
        DialogResult = true;
        Close();
    }
}
