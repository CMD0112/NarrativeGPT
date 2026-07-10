using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class CastPhraseImportDialog : ShellDialogWindow
{
    private readonly AdventureBundle? _bundle;
    private readonly Dictionary<string, CheckBox> _entitySourceChecks = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> _includedSourceKeys = PhraseHighlightEntitySourceCatalog.ResolveDefaultImportSourceKeys();
    private int _assignmentSalt;
    private readonly Dictionary<string, int> _phraseSaltOffsets = new(StringComparer.OrdinalIgnoreCase);

    public HighlightColorAssignmentOptions? ColorAssignment { get; set; }

    public HighlightColorGroupingProfile? GroupingProfile { get; set; }

    public ContinuousViewFormatSettings? ContinuousViewFormat { get; set; }

    public string? HighlightCanvasBackground { get; set; }

    public IReadOnlyList<PhraseHighlightRule>? ExistingRules { get; set; }

    public IReadOnlyList<PhraseHighlightRule> ImportedRules { get; private set; } = [];

    public CastPhraseImportDialog(AdventureBundle? bundle)
    {
        _bundle = bundle;
        InitializeComponent();

        BuildEntitySourceChecks();
        ApplyIncludedSourceKeys(_includedSourceKeys);

        IncludeEntityAliasesCheck.Checked += Options_Changed;
        IncludeEntityAliasesCheck.Unchecked += Options_Changed;

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
        IReadOnlyList<PhraseHighlightRule>? existingRules = null,
        HighlightColorGroupingProfile? groupingProfile = null,
        ContinuousViewFormatSettings? continuousViewFormat = null)
    {
        rules = [];
        var dialog = new CastPhraseImportDialog(bundle)
        {
            Owner = owner,
            ColorAssignment = colorAssignment,
            HighlightCanvasBackground = highlightCanvasBackground,
            ExistingRules = existingRules,
            GroupingProfile = groupingProfile,
            ContinuousViewFormat = continuousViewFormat,
        };
        dialog.RefreshCandidates();
        if (dialog.ShowDialog() != true)
            return false;

        rules = dialog.ImportedRules;
        return true;
    }

    private void Options_Changed(object sender, RoutedEventArgs e) => RefreshCandidates();

    private void ImportPresetCast_Click(object sender, RoutedEventArgs e) =>
        ApplyImportPreset(PhraseHighlightEntitySourceCatalog.PresetCast);

    private void ImportPresetWorld_Click(object sender, RoutedEventArgs e) =>
        ApplyImportPreset(PhraseHighlightEntitySourceCatalog.PresetWorld);

    private void ImportPresetPlot_Click(object sender, RoutedEventArgs e) =>
        ApplyImportPreset(PhraseHighlightEntitySourceCatalog.PresetPlot);

    private void ImportPresetAll_Click(object sender, RoutedEventArgs e) =>
        ApplyImportPreset(PhraseHighlightEntitySourceCatalog.PresetAll);

    private void ImportPresetNone_Click(object sender, RoutedEventArgs e) =>
        ApplyImportPreset(PhraseHighlightEntitySourceCatalog.PresetNone);

    private void ApplyImportPreset(string presetId)
    {
        ApplyIncludedSourceKeys(PhraseHighlightEntitySourceCatalog.ResolvePresetImportSourceKeys(presetId));
        RefreshCandidates();
    }

    private void BuildEntitySourceChecks()
    {
        if (EntitySourceChecksPanel is null)
            return;

        EntitySourceChecksPanel.Children.Clear();
        _entitySourceChecks.Clear();

        foreach (var source in PhraseHighlightEntitySourceCatalog.DescribeImportSources(_bundle?.Entities))
        {
            var check = new CheckBox
            {
                Content = source.DisplayLabel,
                Tag = source.SourceKey,
                Margin = new Thickness(0, 0, 14, 6),
                ToolTip = $"{source.TypeLabel} · {source.UiCategory}",
            };
            check.Checked += EntitySourceCheck_Changed;
            check.Unchecked += EntitySourceCheck_Changed;
            _entitySourceChecks[source.SourceKey] = check;
            EntitySourceChecksPanel.Children.Add(check);
        }
    }

    private void EntitySourceCheck_Changed(object sender, RoutedEventArgs e)
    {
        _includedSourceKeys = ReadIncludedSourceKeys();
        RefreshCandidates();
    }

    private void ApplyIncludedSourceKeys(IReadOnlySet<string> keys)
    {
        _includedSourceKeys = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceKey, check) in _entitySourceChecks)
            check.IsChecked = _includedSourceKeys.Contains(sourceKey);
    }

    private IReadOnlySet<string> ReadIncludedSourceKeys() =>
        _entitySourceChecks
            .Where(pair => pair.Value.IsChecked == true)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

    private void RerollColors_Click(object sender, RoutedEventArgs e)
    {
        var priorSelection = CaptureSelectionState();
        _phraseSaltOffsets.Clear();
        _assignmentSalt++;
        RefreshCandidates(priorSelection);
    }

    private void CandidateColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CastPhraseImportCandidate candidate }
            || candidate.AlreadyExists)
        {
            return;
        }

        var priorSelection = CaptureSelectionState();
        _phraseSaltOffsets[candidate.Phrase] = _phraseSaltOffsets.TryGetValue(candidate.Phrase, out var offset)
            ? offset + 1
            : 1;
        RefreshCandidates(priorSelection);
        e.Handled = true;
    }

    private Dictionary<string, bool> CaptureSelectionState()
    {
        if (CandidateList.ItemsSource is not IEnumerable<CastPhraseImportCandidate> candidates)
            return [];

        return candidates.ToDictionary(c => c.Phrase, c => c.IsSelected, StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshCandidates(Dictionary<string, bool>? priorSelection = null)
    {
        if (CandidateList is null)
            return;

        HideValidation();

        var options = new CastPhraseImportOptions
        {
            IncludedSourceKeys = ReadIncludedSourceKeys(),
            IncludeEntityAliases = IncludeEntityAliasesCheck.IsChecked == true,
            ExistingRules = ExistingRules,
            Theme = ThemeRuntime.Current,
            HighlightCanvasBackground = HighlightCanvasBackground ?? ResolveHighlightCanvasBackground(),
            ColorAssignment = ColorAssignment,
            AssignmentSalt = _assignmentSalt,
            GroupingProfile = GroupingProfile,
            ContinuousViewFormat = ContinuousViewFormat,
        };
        var result = PhraseHighlightCastImportService.BuildCandidates(_bundle, options);
        var candidates = new ObservableCollection<CastPhraseImportCandidate>(result.Candidates);

        if (_phraseSaltOffsets.Count > 0 && ColorAssignment is not null)
        {
            RerollPerPhraseOffsets(candidates);
        }

        foreach (var candidate in candidates)
        {
            if (priorSelection is not null
                && priorSelection.TryGetValue(candidate.Phrase, out var wasSelected))
            {
                candidate.IsSelected = wasSelected;
            }

            candidate.PropertyChanged += Candidate_PropertyChanged;
        }

        CandidateList.ItemsSource = candidates;

        var hasCandidates = candidates.Count > 0;
        CandidateList.Visibility = hasCandidates ? Visibility.Visible : Visibility.Collapsed;
        ColumnHeaderBorder.Visibility = hasCandidates ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = hasCandidates ? Visibility.Collapsed : Visibility.Visible;
        SelectAllButton.IsEnabled = hasCandidates;
        ClearSelectionButton.IsEnabled = hasCandidates;
        SelectNewOnlyButton.IsEnabled = hasCandidates;
        RerollColorsButton.IsEnabled = hasCandidates;
        ImportButton.IsEnabled = hasCandidates;
        UpdateSummary(candidates);
        UpdateColorAnalysis(result.ColorAnalysis);
    }

    private void RerollPerPhraseOffsets(IList<CastPhraseImportCandidate> candidates)
    {
        if (ColorAssignment is null)
            return;

        var theme = ThemeRuntime.Current;
        var canvas = HighlightCanvasBackground ?? ResolveHighlightCanvasBackground();
        var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assignmentState = new HighlightColorAssignmentState();
        var reserved = HighlightColorReservedColors.Resolve(theme, ContinuousViewFormat);
        HighlightColorCapacityAnalyzer.SeedFromExistingRules(ExistingRules, usedColors, characterColors);
        foreach (var used in usedColors)
            assignmentState.GlobalUsedColors.Add(used);
        foreach (var color in reserved)
            assignmentState.GlobalUsedColors.Add(color);

        var discoveryIndex = 0;
        foreach (var candidate in candidates.Where(c => !c.AlreadyExists))
        {
            if (!_phraseSaltOffsets.TryGetValue(candidate.Phrase, out var offset) || offset <= 0)
            {
                discoveryIndex++;
                if (!candidate.Role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
                    characterColors[candidate.Phrase] = candidate.Color;
                continue;
            }

            candidate.Color = PhraseHighlightColorAssignmentService.ReassignCandidateColor(
                candidate.Role,
                candidate.Phrase,
                ColorAssignment,
                theme,
                canvas,
                characterColors,
                usedColors,
                discoveryIndex++,
                phraseSaltOffset: offset,
                groupingProfile: GroupingProfile,
                entityCategory: candidate.EntityCategory,
                entityId: candidate.EntityId,
                assignmentState: assignmentState,
                reservedForegroundColors: reserved);

            if (!candidate.Role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
                characterColors[candidate.Phrase] = candidate.Color;
        }
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
