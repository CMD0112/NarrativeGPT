using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class HighlightColorGroupingDialog : ShellDialogWindow
{
    private readonly ObservableCollection<GroupListItem> _groups = [];
    private readonly ObservableCollection<HighlightColorEntityRef> _includeEntities = [];
    private readonly ObservableCollection<HighlightColorEntityRef> _excludeEntities = [];
    private readonly IReadOnlyList<HighlightColorEntityRef> _entityCatalog;
    private readonly IReadOnlyList<PhraseHighlightEntityCategoryDescriptor> _entityCategories;
    private readonly Dictionary<string, CheckBox> _includeCategoryChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _excludeCategoryChecks = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressEvents;
    private bool _readOnly;
    private GroupListItem? _activeGroup;

    public HighlightColorGroupingProfile ResultProfile { get; private set; } = new();

    private HighlightColorGroupingDialog(
        HighlightColorGroupingProfile source,
        IReadOnlyList<HighlightColorEntityRef> entityCatalog,
        IReadOnlyList<PhraseHighlightEntityCategoryDescriptor> entityCategories,
        bool readOnly)
    {
        InitializeComponent();
        _readOnly = readOnly;
        _entityCatalog = entityCatalog;
        _entityCategories = entityCategories;
        ResultProfile = source.Clone();
        foreach (var group in ResultProfile.Groups)
            _groups.Add(new GroupListItem(group.Clone()));

        GroupsListView.ItemsSource = _groups;
        IncludeEntitiesListView.ItemsSource = _includeEntities;
        ExcludeEntitiesListView.ItemsSource = _excludeEntities;

        UnmatchedBehaviorCombo.ItemsSource = Enum.GetNames(typeof(HighlightColorUnmatchedBehavior));
        DescriptionBox.Text = ResultProfile.Description ?? "";
        UnmatchedBehaviorCombo.SelectedItem = ResultProfile.UnmatchedBehavior.ToString();

        AddGroupButton.IsEnabled = !readOnly;
        RemoveGroupButton.IsEnabled = !readOnly;
        GroupEditorPanel.IsEnabled = !readOnly;
        DescriptionBox.IsReadOnly = readOnly;

        EntityCatalogHintText.Text = _entityCatalog.Count > 0
            ? $"{_entityCatalog.Count} entities available from adventure / highlight rules for pickers."
            : "Open an adventure or add entity-linked highlight rules to pick individual entities. Category and phrase rules still work.";

        BuildCategoryCheckboxes(IncludeCategoriesPanel, _includeCategoryChecks);
        BuildCategoryCheckboxes(ExcludeCategoriesPanel, _excludeCategoryChecks);

        if (_groups.Count > 0)
            GroupsListView.SelectedIndex = 0;
    }

    public static bool? Show(
        Window? owner,
        HighlightColorGroupingProfile? source,
        out HighlightColorGroupingProfile profile,
        Guid? adventureId = null,
        IEnumerable<PhraseHighlightRule>? highlightRules = null,
        bool readOnly = false)
    {
        profile = source?.Clone() ?? new HighlightColorGroupingProfile
        {
            Id = HighlightColorGroupingProfileIds.Custom,
            Name = "Custom",
        };

        AdventureBundle? bundle = adventureId is not null ? AdventureStore.Load(adventureId.Value) : null;
        var catalog = HighlightColorGroupingEntityCatalog.MergeSources(bundle, highlightRules);
        var categories = PhraseHighlightEntitySourceCatalog.DescribeEntityCategories(
            bundle?.Entities,
            highlightRules);

        var dialog = new HighlightColorGroupingDialog(profile, catalog, categories, readOnly) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return false;

        profile = dialog.ResultProfile.Clone();
        return true;
    }

    private void GroupsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitActiveGroup();
        _activeGroup = GroupsListView.SelectedItem as GroupListItem;
        LoadGroupEditor(_activeGroup?.Rule);
        RemoveGroupButton.IsEnabled = !_readOnly && _activeGroup is not null;
    }

    private void AddGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var group = new HighlightColorGroupRule
        {
            Name = "New group",
            Priority = _groups.Count,
        };
        var item = new GroupListItem(group);
        _groups.Add(item);
        GroupsListView.SelectedItem = item;
    }

    private void RemoveGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsListView.SelectedItem is not GroupListItem group)
            return;

        var index = _groups.IndexOf(group);
        _groups.Remove(group);
        if (_groups.Count == 0)
        {
            _activeGroup = null;
            GroupEditorPanel.IsEnabled = false;
            return;
        }

        GroupsListView.SelectedIndex = Math.Min(index, _groups.Count - 1);
    }

    private void AddIncludeEntitiesButton_Click(object sender, RoutedEventArgs e) =>
        PickEntities(_includeEntities);

    private void RemoveIncludeEntitiesButton_Click(object sender, RoutedEventArgs e) =>
        RemoveSelectedEntities(IncludeEntitiesListView, _includeEntities);

    private void AddExcludeEntitiesButton_Click(object sender, RoutedEventArgs e) =>
        PickEntities(_excludeEntities);

    private void RemoveExcludeEntitiesButton_Click(object sender, RoutedEventArgs e) =>
        RemoveSelectedEntities(ExcludeEntitiesListView, _excludeEntities);

    private void PickEntities(ObservableCollection<HighlightColorEntityRef> target)
    {
        if (_entityCatalog.Count == 0)
        {
            MessageBox.Show(this,
                "No entity catalog available. Open an adventure in Play/Design or import entity-linked highlight rules first.",
                "Add entities");
            return;
        }

        var dialog = new Window
        {
            Title = "Select entities",
            Width = 420,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (System.Windows.Media.Brush)FindResource("BgBaseBrush"),
        };

        var list = new ListBox
        {
            SelectionMode = SelectionMode.Extended,
            DisplayMemberPath = nameof(HighlightColorEntityRef.Describe),
            Margin = new Thickness(16, 16, 16, 8),
        };
        list.ItemsSource = _entityCatalog;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
        };
        var ok = new Button { Content = "Add", IsDefault = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        dialog.Content = panel;

        if (dialog.ShowDialog() != true)
            return;

        foreach (HighlightColorEntityRef selected in list.SelectedItems)
        {
            if (target.Any(e => e.EntityId == selected.EntityId
                                && string.Equals(e.EntityCategory, selected.EntityCategory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(selected.Clone());
        }

        ApplyEditorToActiveGroup();
    }

    private void RemoveSelectedEntities(ListView listView, ObservableCollection<HighlightColorEntityRef> target)
    {
        foreach (HighlightColorEntityRef selected in listView.SelectedItems.Cast<HighlightColorEntityRef>().ToList())
            target.Remove(selected);

        ApplyEditorToActiveGroup();
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        ApplyEditorToActiveGroup();
        HideValidation();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveGroup();
        if (!TryValidate(out var error))
        {
            ShowValidation(error);
            return;
        }

        ResultProfile.Groups = _groups.Select(g => g.Rule.Clone()).ToList();
        ResultProfile.Description = DescriptionBox.Text.Trim();
        if (UnmatchedBehaviorCombo.SelectedItem is string behavior
            && Enum.TryParse<HighlightColorUnmatchedBehavior>(behavior, out var parsed))
        {
            ResultProfile.UnmatchedBehavior = parsed;
        }

        DialogResult = true;
        Close();
    }

    private bool TryValidate(out string error)
    {
        if (_groups.Count == 0)
        {
            error = "Add at least one grouping rule, or choose None in the Highlights editor.";
            return false;
        }

        foreach (var group in _groups.Select(g => g.Rule))
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                error = "Each group needs a name.";
                return false;
            }

            if (!HighlightColorGroupingMatcher.HasIncludeCriteria(group))
            {
                error = $"Group “{group.Name}” needs include criteria, match remainder, or exclude targeting.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private void LoadGroupEditor(HighlightColorGroupRule? group)
    {
        _suppressEvents = true;
        try
        {
            GroupEditorPanel.IsEnabled = group is not null && !_readOnly;
            _includeEntities.Clear();
            _excludeEntities.Clear();
            if (group is null)
                return;

            GroupNameBox.Text = group.Name;
            SetCategoryChecks(_includeCategoryChecks, group.EntityCategories);
            SetCategoryChecks(_excludeCategoryChecks, group.ExcludeEntityCategories);
            foreach (var entity in group.IncludeEntities)
                _includeEntities.Add(entity.Clone());
            foreach (var entity in group.ExcludeEntities)
                _excludeEntities.Add(entity.Clone());
            IncludePhrasesBox.Text = string.Join(", ", group.IncludePhrases);
            RolePrefixesBox.Text = string.Join(", ", group.RolePrefixes);
            ExcludePhrasesBox.Text = string.Join(", ", group.ExcludePhrases);
            PriorityBox.Text = group.Priority.ToString(CultureInfo.InvariantCulture);
            ShareColorCheckBox.IsChecked = group.ShareColorWithinGroup;
            MatchRemainderCheckBox.IsChecked = group.MatchRemainder;
            ExcludeAutoAssignCheckBox.IsChecked = group.ExcludeFromAutoAssign;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void CommitActiveGroup() => ApplyEditorToActiveGroup();

    private void ApplyEditorToActiveGroup()
    {
        if (_activeGroup is null)
            return;

        var group = _activeGroup.Rule;
        group.Name = GroupNameBox.Text.Trim();
        group.EntityCategories = ReadCategoryChecks(_includeCategoryChecks);
        group.ExcludeEntityCategories = ReadCategoryChecks(_excludeCategoryChecks);
        group.IncludeEntities = _includeEntities.Select(e => e.Clone()).ToList();
        group.ExcludeEntities = _excludeEntities.Select(e => e.Clone()).ToList();
        group.IncludePhrases = SplitList(IncludePhrasesBox.Text);
        group.RolePrefixes = SplitList(RolePrefixesBox.Text);
        group.ExcludePhrases = SplitList(ExcludePhrasesBox.Text);
        group.Priority = int.TryParse(PriorityBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority)
            ? priority
            : 0;
        group.ShareColorWithinGroup = ShareColorCheckBox.IsChecked == true;
        group.MatchRemainder = MatchRemainderCheckBox.IsChecked == true;
        group.ExcludeFromAutoAssign = ExcludeAutoAssignCheckBox.IsChecked == true;
        _activeGroup.RefreshSummary();
        GroupsListView.Items.Refresh();
    }

    private void BuildCategoryCheckboxes(WrapPanel panel, Dictionary<string, CheckBox> target)
    {
        panel.Children.Clear();
        target.Clear();

        foreach (var category in _entityCategories)
        {
            var label = category.EntityCount > 0
                ? $"{category.DisplayLabel} ({category.EntityCount})"
                : category.DisplayLabel;
            var check = new CheckBox
            {
                Content = label,
                Tag = category.UiCategory,
                Margin = new Thickness(0, 0, 14, 6),
                IsEnabled = !_readOnly,
            };
            check.Checked += Field_Changed;
            check.Unchecked += Field_Changed;
            target[category.UiCategory] = check;
            panel.Children.Add(check);
        }
    }

    private static void SetCategoryChecks(IReadOnlyDictionary<string, CheckBox> checks, IEnumerable<string> selected)
    {
        var set = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, check) in checks)
            check.IsChecked = set.Contains(category);
    }

    private static List<string> ReadCategoryChecks(IReadOnlyDictionary<string, CheckBox> checks)
    {
        var list = new List<string>();
        foreach (var (category, check) in checks)
        {
            if (check.IsChecked == true)
                list.Add(category);
        }

        return list;
    }

    private static List<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationText.Visibility = Visibility.Collapsed;
        ValidationText.Text = "";
    }

    private sealed class GroupListItem
    {
        public GroupListItem(HighlightColorGroupRule rule)
        {
            Rule = rule;
            RefreshSummary();
        }

        public HighlightColorGroupRule Rule { get; }

        public string Name => Rule.Name;

        public string Summary { get; private set; } = "";

        public void RefreshSummary() => Summary = HighlightColorGroupRuleDescriber.Describe(Rule);
    }
}
