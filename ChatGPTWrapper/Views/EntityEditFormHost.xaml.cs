using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class EntityEditFormHost : UserControl
{
    public static readonly DependencyProperty ShowInlineActionsProperty =
        DependencyProperty.Register(
            nameof(ShowInlineActions),
            typeof(bool),
            typeof(EntityEditFormHost),
            new PropertyMetadata(false, OnShowInlineActionsChanged));

    private EntityEditModel? _model;

    public event EventHandler? SaveRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? DeleteRequested;

    public EntityEditFormHost()
    {
        InitializeComponent();
    }

    public bool ShowInlineActions
    {
        get => (bool)GetValue(ShowInlineActionsProperty);
        set => SetValue(ShowInlineActionsProperty, value);
    }

    private static void OnShowInlineActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EntityEditFormHost host)
            host.InlineActionsPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public void LoadModel(EntityEditModel model)
    {
        _model = model;
        NameBox.Text = model.Name;
        SecondaryLabelBlock.Text = model.SecondaryLabel;
        RoleBox.Text = model.SecondaryValue;
        DescriptionBox.Text = model.Description;
        PinnedCheck.IsChecked = model.Pinned;
        PinnedCheck.Visibility = model.CanPin ? Visibility.Visible : Visibility.Collapsed;
        InlineDeleteButton.Visibility = model.IsNew ? Visibility.Collapsed : Visibility.Visible;

        if (model.ShowQuestStatus)
        {
            QuestStatusPanel.Visibility = Visibility.Visible;
            QuestStatusBox.ItemsSource = Enum.GetValues<QuestStatus>();
            QuestStatusBox.SelectedItem = model.QuestStatus;
        }
        else
        {
            QuestStatusPanel.Visibility = Visibility.Collapsed;
        }

        TagsPanel.Visibility = model.ShowTags ? Visibility.Visible : Visibility.Collapsed;
        TagsBox.Text = model.TagsText;
        AliasesPanel.Visibility = model.ShowAliases ? Visibility.Visible : Visibility.Collapsed;
        AliasesBox.Text = model.AliasesText;

        BuildExtraFields();
        RefreshPortrait();
    }

    public bool TryHarvestModel(out string? validationMessage)
    {
        validationMessage = null;
        if (_model is null)
            return false;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            validationMessage = "Name is required.";
            NameBox.Focus();
            return false;
        }

        _model.Name = NameBox.Text.Trim();
        _model.SecondaryValue = RoleBox.Text.Trim();
        _model.Description = DescriptionBox.Text.Trim();
        _model.Pinned = PinnedCheck.IsChecked == true;
        _model.TagsText = TagsBox.Text.Trim();
        _model.AliasesText = AliasesBox.Text.Trim();
        if (_model.ShowQuestStatus && QuestStatusBox.SelectedItem is QuestStatus status)
            _model.QuestStatus = status;

        return true;
    }

    public EntityEditModel? Model => _model;

    private void BuildExtraFields()
    {
        ExtraFieldsPanel.Children.Clear();
        if (_model is null)
            return;

        foreach (var field in _model.Fields.OrderBy(f => f.Order))
        {
            ExtraFieldsPanel.Children.Add(new TextBlock
            {
                Text = field.Label,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var multiline = field.Multiline;
            var box = new TextBox
            {
                Text = field.Value,
                Tag = field,
                Margin = new Thickness(0, 0, 0, 10),
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multiline ? 72 : 0,
                VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            };
            box.TextChanged += (_, _) => field.Value = box.Text;
            ExtraFieldsPanel.Children.Add(box);
        }
    }

    private void RefreshPortrait()
    {
        if (_model is null)
            return;

        ImageSource? source = null;
        if (!string.IsNullOrWhiteSpace(_model.PendingImageSourcePath))
            source = EntityMediaService.TryLoadImageFromAbsolute(_model.PendingImageSourcePath, 280);
        else if (!_model.ClearImage)
            source = EntityMediaService.TryLoadImage(_model.AdventureId, _model.ImagePath, 280);

        if (source is null)
        {
            PortraitImage.Source = null;
            PortraitImage.Visibility = Visibility.Collapsed;
            PortraitPlaceholder.Visibility = Visibility.Visible;
            ClearPortraitButton.Visibility = Visibility.Collapsed;
            return;
        }

        PortraitImage.Source = source;
        PortraitImage.Visibility = Visibility.Visible;
        PortraitPlaceholder.Visibility = Visibility.Collapsed;
        ClearPortraitButton.Visibility = Visibility.Visible;
    }

    private void ChoosePortrait_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|All files|*.*",
            Title = "Choose portrait or reference image",
        };
        if (dlg.ShowDialog() != true)
            return;

        _model.PendingImageSourcePath = dlg.FileName;
        _model.ClearImage = false;
        RefreshPortrait();
    }

    private void ClearPortrait_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
            return;

        _model.PendingImageSourcePath = null;
        _model.ClearImage = true;
        RefreshPortrait();
    }

    private void InlineSave_Click(object sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(this, EventArgs.Empty);

    private void InlineCancel_Click(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void InlineDelete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);
}
