using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class CastPhraseImportDialog : Window
{
    private readonly AdventureBundle? _bundle;

    public HighlightColorAssignmentOptions? ColorAssignment { get; set; }

    public string? HighlightCanvasBackground { get; set; }

    public IReadOnlyList<PhraseHighlightRule>? ExistingRules { get; set; }

    public IReadOnlyList<PhraseHighlightRule> ImportedRules { get; private set; } = [];

    public CastPhraseImportDialog(AdventureBundle? bundle)
    {
        _bundle = bundle;
        InitializeComponent();

        IncludePlayerCheck.Checked += Options_Changed;
        IncludePlayerCheck.Unchecked += Options_Changed;
        IncludePartyCheck.Checked += Options_Changed;
        IncludePartyCheck.Unchecked += Options_Changed;
        IncludeAliasesCheck.Checked += Options_Changed;
        IncludeAliasesCheck.Unchecked += Options_Changed;

        var title = bundle?.Metadata?.Title;
        if (!string.IsNullOrWhiteSpace(title))
            Title = $"Import cast — {title.Trim()}";
    }

    public static bool? Show(
        Window? owner,
        AdventureBundle? bundle,
        out IReadOnlyList<PhraseHighlightRule> rules,
        HighlightColorAssignmentOptions? colorAssignment = null,
        string? highlightCanvasBackground = null,
        IReadOnlyList<PhraseHighlightRule>? existingRules = null)
    {
        rules = [];
        var dialog = new CastPhraseImportDialog(bundle)
        {
            Owner = owner,
            ColorAssignment = colorAssignment,
            HighlightCanvasBackground = highlightCanvasBackground,
            ExistingRules = existingRules,
        };
        dialog.RefreshCandidates();
        if (dialog.ShowDialog() != true)
            return false;

        rules = dialog.ImportedRules;
        return true;
    }

    private void Options_Changed(object sender, RoutedEventArgs e) => RefreshCandidates();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateList.ItemsSource is not IEnumerable<CastPhraseImportCandidate> candidates)
            return;

        foreach (var candidate in candidates)
            candidate.IsSelected = candidate.IsSelectable;

        UpdateSummary(candidates);
        HideValidation();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateList.ItemsSource is not IEnumerable<CastPhraseImportCandidate> candidates)
            return;

        foreach (var candidate in candidates)
            candidate.IsSelected = false;

        UpdateSummary(candidates);
        HideValidation();
    }

    private void SelectNewOnly_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateList.ItemsSource is not IEnumerable<CastPhraseImportCandidate> candidates)
            return;

        foreach (var candidate in candidates)
            candidate.IsSelected = !candidate.AlreadyExists;

        UpdateSummary(candidates);
        HideValidation();
    }

    private void RefreshCandidates()
    {
        if (CandidateList is null)
            return;

        HideValidation();

        var options = new CastPhraseImportOptions
        {
            IncludePlayer = IncludePlayerCheck.IsChecked == true,
            IncludeParty = IncludePartyCheck.IsChecked == true,
            IncludeAliases = IncludeAliasesCheck.IsChecked == true,
            ExistingRules = ExistingRules,
            Theme = ThemeRuntime.Current,
            HighlightCanvasBackground = HighlightCanvasBackground ?? ResolveHighlightCanvasBackground(),
            ColorAssignment = ColorAssignment,
        };
        var result = PhraseHighlightCastImportService.BuildCandidates(_bundle, options);
        var candidates = new ObservableCollection<CastPhraseImportCandidate>(result.Candidates);
        foreach (var candidate in candidates)
            candidate.PropertyChanged += Candidate_PropertyChanged;

        CandidateList.ItemsSource = candidates;

        var hasCandidates = candidates.Count > 0;
        CandidateList.Visibility = hasCandidates ? Visibility.Visible : Visibility.Collapsed;
        ColumnHeaderBorder.Visibility = hasCandidates ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = hasCandidates ? Visibility.Collapsed : Visibility.Visible;
        SelectAllButton.IsEnabled = hasCandidates;
        ClearSelectionButton.IsEnabled = hasCandidates;
        SelectNewOnlyButton.IsEnabled = hasCandidates;
        ImportButton.IsEnabled = hasCandidates;
        UpdateSummary(candidates);
        UpdateColorAnalysis(result.ColorAnalysis);
    }

    private void Candidate_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null and not nameof(CastPhraseImportCandidate.IsSelected))
            return;

        UpdateSummary();
    }

    private void UpdateSummary(IEnumerable<CastPhraseImportCandidate>? candidates = null)
    {
        candidates ??= CandidateList.ItemsSource as IEnumerable<CastPhraseImportCandidate> ?? [];
        var list = candidates as IList<CastPhraseImportCandidate> ?? candidates.ToList();

        if (list.Count == 0)
        {
            SummaryText.Visibility = Visibility.Collapsed;
            SummaryText.Text = string.Empty;
            return;
        }

        var selected = list.Count(c => c.IsSelected);
        var selectable = list.Count(c => !c.AlreadyExists);
        SummaryText.Text = selected == list.Count
            ? $"{list.Count} phrase{(list.Count == 1 ? "" : "s")} selected"
            : selectable > 0 && selected == selectable && list.Any(c => c.AlreadyExists)
                ? $"{selected} new selected · {list.Count - selectable} already added"
                : $"{selected} of {list.Count} selected";
        SummaryText.Visibility = Visibility.Visible;
    }

    private void UpdateColorAnalysis(HighlightColorCapacityAnalysis? analysis)
    {
        if (ColorAnalysisText is null)
            return;

        if (analysis is null || analysis.CandidateCount == 0 && analysis.ExistingRuleCount == 0)
        {
            ColorAnalysisText.Visibility = Visibility.Collapsed;
            ColorAnalysisText.Text = string.Empty;
            return;
        }

        ColorAnalysisText.Text = analysis.BuildSummaryLine();
        ColorAnalysisText.Foreground = analysis.PaletteMayBeInsufficient
            ? (Brush?)FindResource("WarningBrush") ?? ColorAnalysisText.Foreground
            : (Brush?)FindResource("TextMutedBrush") ?? ColorAnalysisText.Foreground;
        ColorAnalysisText.Visibility = Visibility.Visible;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateList.ItemsSource is not IEnumerable<CastPhraseImportCandidate> candidates)
        {
            DialogResult = false;
            return;
        }

        ImportedRules = new CastPhraseImportResult { Candidates = candidates.ToList() }.ToRules();
        if (ImportedRules.Count == 0)
        {
            ShowValidation("Select at least one phrase to import.");
            return;
        }

        DialogResult = true;
    }

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

    private static string ResolveHighlightCanvasBackground()
    {
        if (Application.Current?.Resources["BgBaseBrush"] is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";

        return ThemeRuntime.Current.GetHex("BgBase");
    }
}
