using ChatGPTWrapper;
using ChatGPTWrapper.Format;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class FormatRefinementPage : UserControl
{
    private readonly UiChromeSettings _working;
    private readonly Action<UiChromeSettings, bool, int?> _applySettings;
    private readonly int _previewRevisionBase;
    private int _previewNonce;
    private bool _suppressEvents;
    private FormatRefinementCategory _selectedCategory = FormatRefinementCategory.Layout;

    public FormatRefinementPage(
        UiChromeSettings working,
        Action<UiChromeSettings, bool, int?> applySettings)
    {
        _working = working;
        _applySettings = applySettings;
        _previewRevisionBase = _working.ChromePreferencesRevision;
        InitializeComponent();
        Loaded += (_, _) => InitializeCategories();
    }

    private void InitializeCategories()
    {
        _suppressEvents = true;
        try
        {
            CategoryCombo.ItemsSource = FormatRefinementCatalog.Categories
                .Select(c => new RefinementCategoryItem(c, RefinementCategoryLabel(c)))
                .ToList();
            CategoryCombo.DisplayMemberPath = nameof(RefinementCategoryItem.Label);
            CategoryCombo.SelectedIndex = 0;
            _selectedCategory = FormatRefinementCategory.Layout;
        }
        finally
        {
            _suppressEvents = false;
        }

        RefreshRefinementPanel();
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || CategoryCombo.SelectedItem is not RefinementCategoryItem item)
            return;

        _selectedCategory = item.Category;
        RefreshRefinementPanel();
    }

    private void RefreshRefinementPanel()
    {
        var context = BuildRefinementContext();
        var format = _working.ActiveModeSettings().ContinuousViewFormat;
        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(format, context, _selectedCategory);
        var common = FormatRefinementCatalog.GetCommonActions(_selectedCategory);

        SuggestedPanel.Children.Clear();
        if (suggested.Count == 0)
        {
            SuggestedHeader.Visibility = Visibility.Collapsed;
        }
        else
        {
            SuggestedHeader.Visibility = Visibility.Visible;
            SuggestedHeader.Text = suggested.Count == 1
                ? "Suggested for you"
                : $"Suggested for you ({suggested.Count})";

            foreach (var suggestion in suggested)
                SuggestedPanel.Children.Add(CreateRefinementButton(suggestion.Action, suggested: true));
        }

        CommonPanel.Children.Clear();
        foreach (var action in common)
            CommonPanel.Children.Add(CreateRefinementButton(action, suggested: false));
    }

    private Button CreateRefinementButton(FormatRefinementAction action, bool suggested)
    {
        var button = new Button
        {
            Content = action.Label,
            Tag = action.Id,
            Style = suggested
                ? (Style)Application.Current.Resources["ShellPrimaryButtonStyle"]
                : (Style)Application.Current.Resources["ShellGhostButtonStyle"],
            Padding = new Thickness(10, 5, 10, 5),
        };
        button.Click += (_, _) => ApplyRefinementAction(action.Id);
        return button;
    }

    private void ApplyRefinementAction(string actionId)
    {
        if (!FormatRefinementCatalog.TryApply(actionId, _working.ActiveModeSettings().ContinuousViewFormat))
            return;

        _working.ActiveModeSettings().ActiveFormatProfileId = FormatProfileIds.Custom;
        PushLivePreview();
        RefreshRefinementPanel();
    }

    private FormatRefinementContext BuildRefinementContext() =>
        new()
        {
            TranscriptViewMode = _working.TranscriptViewMode,
            PhraseHighlightsEnabled = _working.ActiveModeSettings().PhraseHighlightsEnabled,
            PhraseHighlightRules = _working.PhraseHighlightRules,
        };

    private void PushLivePreview()
    {
        _previewNonce++;
        var revision = _previewRevisionBase + _previewNonce;
        _applySettings(_working, false, revision);
    }

    private static string RefinementCategoryLabel(FormatRefinementCategory category) =>
        category switch
        {
            FormatRefinementCategory.Layout => "Layout",
            FormatRefinementCategory.Typography => "Typography",
            FormatRefinementCategory.Colors => "Colors",
            FormatRefinementCategory.RoleDistinction => "Role distinction",
            FormatRefinementCategory.CodeHeadings => "Code & headings",
            FormatRefinementCategory.Weave => "Weave",
            _ => category.ToString(),
        };

    private sealed record RefinementCategoryItem(FormatRefinementCategory Category, string Label);
}
