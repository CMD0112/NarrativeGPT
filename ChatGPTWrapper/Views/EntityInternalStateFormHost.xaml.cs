using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class EntityInternalStateFormHost : UserControl
{
    private EntityInternalStateEditModel? _model;
    private AdventureBundle? _bundle;
    private Guid _entityId;
    private string _kindId = "";
    private readonly Dictionary<EntityInternalStateFieldValue, FrameworkElement> _editors = new();

    public EntityInternalStateFormHost()
    {
        InitializeComponent();
    }

    public bool IsSupportedKind { get; private set; }

    public void Load(AdventureBundle bundle, Guid entityId, string category, AdventurePlayEntityKind kind, string entityName)
    {
        var kindId = EntityInternalStateService.ResolveKindIdForCategory(category, kind);
        IsSupportedKind = !string.IsNullOrWhiteSpace(kindId) && EntityInternalStateKind.All.Contains(kindId);

        if (!IsSupportedKind)
        {
            SectionsPanel.Children.Clear();
            SummaryText.Visibility = Visibility.Collapsed;
            ShowEmptySectionsCheck.Visibility = Visibility.Collapsed;
            SectionsPanel.Children.Add(new TextBlock
            {
                Text = "Internal state is not available for this entity type yet.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextMutedBrush"),
            });
            return;
        }

        ShowEmptySectionsCheck.Visibility = Visibility.Visible;
        _bundle = bundle;
        _entityId = entityId;
        _kindId = kindId;
        _model = EntityInternalStateEditMapper.Load(bundle, entityId, kindId, entityName);
        if (!string.IsNullOrWhiteSpace(_model.SummaryLine))
        {
            SummaryText.Text = _model.SummaryLine;
            SummaryText.Visibility = Visibility.Visible;
        }
        else
        {
            SummaryText.Visibility = Visibility.Collapsed;
        }

        RebuildLifecycleActions();
        RebuildSections();
    }

    private void RebuildLifecycleActions()
    {
        LifecycleActionsPanel.Children.Clear();
        if (_bundle is null || string.IsNullOrWhiteSpace(_kindId))
        {
            LifecycleActionsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        LifecycleActionsPanel.Visibility = Visibility.Visible;

        var resetBtn = new Button
        {
            Content = "Reset from canon",
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Re-seed mapped state fields from entities.json (soft overlaps only).",
        };
        resetBtn.Click += ResetFromCanon_Click;
        LifecycleActionsPanel.Children.Add(resetBtn);

        var promoteBtn = new Button
        {
            Content = "Promote to canon",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Queue canon evolution proposals from diverged state fields.",
        };
        promoteBtn.Click += PromoteToCanon_Click;
        LifecycleActionsPanel.Children.Add(promoteBtn);
    }

    private void ResetFromCanon_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || string.IsNullOrWhiteSpace(_kindId))
            return;

        if (MessageBox.Show(
                Window.GetWindow(this),
                "Reset mapped play-state fields from canon profile?",
                "Reset from canon",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        EntityCanonStateLifecycleService.ResetFromCanon(_bundle, _kindId, _entityId);
        AdventureStore.Save(_bundle, AdventureSaveScope.EntityInternalState);
        _model = EntityInternalStateEditMapper.Load(_bundle, _entityId, _kindId, _model?.EntityName ?? "");
        RebuildSections();
    }

    private void PromoteToCanon_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || string.IsNullOrWhiteSpace(_kindId))
            return;

        var count = EntityCanonStateLifecycleService.QueuePromoteDrafts(_bundle, _kindId, _entityId);
        if (count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "No diverged mapped fields to promote.",
                "Promote to canon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        AdventureStore.Save(_bundle, AdventureSaveScope.Entities);
        MessageBox.Show(
            Window.GetWindow(this),
            $"Queued {count} canon evolution proposal(s) — open Review proposals to accept.",
            "Promote to canon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public bool HasChanges() =>
        _model is not null && EntityInternalStateEditMapper.HasChanges(_model);

    public void Apply(AdventureBundle bundle)
    {
        if (_model is null || !IsSupportedKind)
            return;

        EntityInternalStateEditMapper.Apply(bundle, _model);
        _model.Snapshot = SerializeSnapshot(_model.FieldValues);
    }

    private void ShowEmptySectionsCheck_Changed(object sender, RoutedEventArgs e) => RebuildSections();

    private void RebuildSections()
    {
        SectionsPanel.Children.Clear();
        _editors.Clear();
        if (_model is null || !IsSupportedKind)
            return;

        var showEmpty = ShowEmptySectionsCheck.IsChecked == true;
        foreach (var section in EntityInternalStateSchema.GetSections(_model.KindId))
        {
            var fields = _model.FieldValues
                .Where(v => string.Equals(v.Binding.GroupId, section.GroupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v.Binding.Order)
                .ToList();

            if (fields.Count == 0)
                continue;

            if (!showEmpty && fields.All(f => string.IsNullOrWhiteSpace(f.Value) && f.Binding.Kind != EntityInternalStateFieldKind.Bool))
                continue;

            var expander = new Expander
            {
                Header = section.Title,
                IsExpanded = fields.Any(f => !string.IsNullOrWhiteSpace(f.Value) || f.Binding.Kind == EntityInternalStateFieldKind.Bool && f.Value == "true"),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var card = CreateSectionCard();
            var panel = (StackPanel)card.Child;
            foreach (var field in fields)
            {
                var editor = CreateFieldEditor(field);
                _editors[field] = editor;
                panel.Children.Add(editor);
            }

            expander.Content = card;
            SectionsPanel.Children.Add(expander);
        }
    }

    private static Border CreateSectionCard() =>
        new()
        {
            Style = Application.Current.TryFindResource("ShellCardStyle") as Style,
            Padding = new Thickness(10),
            Child = new StackPanel(),
        };

    private FrameworkElement CreateFieldEditor(EntityInternalStateFieldValue field)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = field.Binding.Label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (!string.IsNullOrWhiteSpace(field.Binding.Hint))
        {
            stack.Children.Add(new TextBlock
            {
                Text = field.Binding.Hint,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        var divergenceBlock = new TextBlock
        {
            Foreground = (Brush)FindResource("WarningBrush"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(divergenceBlock);
        UpdateFieldDivergenceHint(field, divergenceBlock);

        if (field.Binding.Kind == EntityInternalStateFieldKind.Bool)
        {
            var check = new CheckBox
            {
                IsChecked = string.Equals(field.Value, "true", StringComparison.OrdinalIgnoreCase),
                Tag = field,
            };
            check.Checked += (_, _) =>
            {
                field.Value = "true";
                UpdateFieldDivergenceHint(field, divergenceBlock);
            };
            check.Unchecked += (_, _) =>
            {
                field.Value = "false";
                UpdateFieldDivergenceHint(field, divergenceBlock);
            };
            stack.Children.Add(check);
            return stack;
        }

        var multiline = field.Binding.Kind is EntityInternalStateFieldKind.StringList
                        or EntityInternalStateFieldKind.StringDictionary
                        || IsMultilineString(field.Binding);

        var box = new TextBox
        {
            Text = field.Value,
            Tag = field,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 0,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
        };
        box.TextChanged += (_, _) =>
        {
            field.Value = box.Text;
            UpdateFieldDivergenceHint(field, divergenceBlock);
        };
        stack.Children.Add(box);
        return stack;
    }

    private void UpdateFieldDivergenceHint(EntityInternalStateFieldValue field, TextBlock divergenceBlock)
    {
        if (_bundle is null || string.IsNullOrWhiteSpace(_kindId))
        {
            divergenceBlock.Visibility = Visibility.Collapsed;
            return;
        }

        var message = EntityCanonStateOverlapService.DescribeLiveDivergence(
            _bundle, _kindId, _entityId, field.Binding.Path, field.Value);
        if (string.IsNullOrWhiteSpace(message))
        {
            divergenceBlock.Visibility = Visibility.Collapsed;
            divergenceBlock.Text = "";
            return;
        }

        divergenceBlock.Text = message;
        divergenceBlock.Visibility = Visibility.Visible;
    }

    private static bool IsMultilineString(EntityInternalStateFieldBinding binding) =>
        binding.Path.EndsWith(".Notes", StringComparison.Ordinal)
        || binding.Path is "Motivation" or "Progress" or "PartialAnswer" or "VoiceNotes"
            or "LastPlayerInteraction" or "LastBondingMoment" or "LastMajorBeat";

    private static string SerializeSnapshot(IReadOnlyList<EntityInternalStateFieldValue> values) =>
        string.Join('\u001e', values.Select(v => $"{v.Binding.Path}\u001f{v.Value}"));
}
