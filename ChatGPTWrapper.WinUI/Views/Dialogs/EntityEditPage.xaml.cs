using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class EntityEditPage : UserControl
{
    private readonly EntityEditModel _model;
    private readonly Dictionary<string, TextBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);

    public EntityEditPage(EntityEditModel model)
    {
        _model = model;
        InitializeComponent();
        LoadModel();
    }

    public bool Deleted { get; private set; }

    public bool TryHarvest(out string? validationMessage)
    {
        validationMessage = null;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            validationMessage = "Name is required.";
            return false;
        }

        _model.Name = NameBox.Text.Trim();
        _model.SecondaryValue = SecondaryBox.Text.Trim();
        _model.Description = DescriptionBox.Text.Trim();
        _model.Pinned = PinnedCheck.IsChecked == true;
        _model.TagsText = TagsBox.Text.Trim();
        _model.AliasesText = AliasesBox.Text.Trim();

        if (_model.ShowQuestStatus && QuestStatusBox.SelectedItem is QuestStatus status)
            _model.QuestStatus = status;

        foreach (var field in _model.Fields)
        {
            if (_fieldBoxes.TryGetValue(field.Key, out var box))
                field.Value = box.Text.Trim();
        }

        _model.RefreshHeaderLabels();
        return true;
    }

    public void MarkDeleted() => Deleted = true;

    private void LoadModel()
    {
        TypeLine.Text = _model.IsNew
            ? $"New {_model.TypeLabel.ToLowerInvariant()}"
            : _model.TypeLabel;

        NameBox.Text = _model.Name;
        SecondaryLabel.Text = _model.SecondaryLabel;
        SecondaryBox.Text = _model.SecondaryValue;
        DescriptionBox.Text = _model.Description;

        if (_model.CanPin)
        {
            PinnedCheck.Visibility = Visibility.Visible;
            PinnedCheck.IsChecked = _model.Pinned;
        }

        if (_model.ShowQuestStatus)
        {
            QuestStatusPanel.Visibility = Visibility.Visible;
            QuestStatusBox.ItemsSource = Enum.GetValues<QuestStatus>().Cast<object>().ToList();
            QuestStatusBox.SelectedItem = _model.QuestStatus;
        }

        if (_model.ShowTags)
        {
            TagsPanel.Visibility = Visibility.Visible;
            TagsBox.Text = _model.TagsText;
        }

        if (_model.ShowAliases)
        {
            AliasesPanel.Visibility = Visibility.Visible;
            AliasesBox.Text = _model.AliasesText;
        }

        foreach (var field in _model.Fields.OrderBy(f => f.Order))
        {
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(new TextBlock
            {
                Text = field.Label,
                Style = (Style)Application.Current.Resources["ShellFormFieldLabelStyle"],
            });

            var box = new TextBox
            {
                Text = field.Value,
                AcceptsReturn = field.Multiline,
                TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = field.Multiline ? 72 : 32,
            };
            panel.Children.Add(box);
            _fieldBoxes[field.Key] = box;
            ExtraFieldsPanel.Children.Add(panel);
        }
    }
}
