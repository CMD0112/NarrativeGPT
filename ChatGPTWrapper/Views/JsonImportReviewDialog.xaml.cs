using System.Collections.ObjectModel;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class JsonImportReviewDialog : ShellDialogWindow
{
    private readonly Guid _adventureId;
    private readonly ObservableCollection<JsonImportReviewRow> _rows = [];

    private AdventureBundle? _bundle;
    private IReadOnlyDictionary<Guid, JsonImportProposalAnalysis> _analyses = new Dictionary<Guid, JsonImportProposalAnalysis>();

    public bool ChangesSaved { get; private set; }

    public JsonImportReviewDialog(Guid adventureId)
    {
        _adventureId = adventureId;
        InitializeComponent();
        ProposalList.ItemsSource = _rows;
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _bundle = AdventureStore.Load(_adventureId);
        if (_bundle is null)
        {
            StatusLine.Text = "Adventure could not be loaded.";
            return;
        }

        _analyses = JsonImportConflictService.AnalyzeQueue(_bundle)
            .ToDictionary(a => a.ProposalId);

        var selectedId = (ProposalList.SelectedItem as JsonImportReviewRow)?.Item.Id;
        _rows.Clear();
        foreach (var item in _bundle.Scenario.JsonImportReviewQueue)
        {
            _analyses.TryGetValue(item.Id, out var analysis);
            _rows.Add(new JsonImportReviewRow(item, analysis));
        }

        UpdateStatusLine();
        UpdatePreviewButtons();
        UpdatePreviewWarnings();
        UpdateActionButtons();

        if (_rows.Count == 0)
        {
            ClearDetail();
            return;
        }

        var restore = selectedId is not null
            ? _rows.FirstOrDefault(r => r.Item.Id == selectedId)
            : null;
        ProposalList.SelectedItem = restore ?? _rows[0];
    }

    private void UpdateStatusLine()
    {
        if (_bundle is null)
            return;

        var count = _bundle.Scenario.JsonImportReviewQueue.Count;
        if (count == 0)
        {
            StatusLine.Text = "No proposals remain in the review queue.";
            return;
        }

        var unsupported = _analyses.Values.Count(a => a.Severity == JsonImportConflictSeverity.Unsupported);
        var drift = _analyses.Values.Count(a => a.Severity == JsonImportConflictSeverity.Drift);
        var parts = new List<string> { $"{count} proposal{(count == 1 ? "" : "s")} awaiting review" };
        if (unsupported > 0)
            parts.Add($"{unsupported} unsupported");
        if (drift > 0)
            parts.Add($"{drift} drift");
        StatusLine.Text = string.Join(" · ", parts);
    }

    private void UpdatePreviewButtons()
    {
        var hasSnapshot = _bundle is not null && SourceJsonImportService.HasProposedJsonSnapshot(_bundle.Scenario);
        var snapshot = _bundle?.Scenario.JsonImportProposedSnapshot;
        PreviewScenarioButton.IsEnabled = hasSnapshot
                                          && !string.IsNullOrWhiteSpace(snapshot?.ScenarioJson);
        PreviewEntitiesButton.IsEnabled = hasSnapshot
                                          && !string.IsNullOrWhiteSpace(snapshot?.EntitiesJson);
    }

    private void UpdatePreviewWarnings()
    {
        PreviewWarningsPanel.Items.Clear();
        var warnings = _bundle?.Scenario.JsonImportProposedSnapshot?.PreviewWarnings;
        if (warnings is not { Count: > 0 })
            return;

        foreach (var warning in warnings)
        {
            PreviewWarningsPanel.Items.Add(new TextBlock
            {
                Text = warning,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
    }

    private void UpdateActionButtons()
    {
        var hasRows = _rows.Count > 0;
        var hasSelection = ProposalList.SelectedItem is not null;
        AcceptSelectedButton.IsEnabled = hasSelection;
        RejectSelectedButton.IsEnabled = hasSelection;
        AcceptAllButton.IsEnabled = hasRows;
        RejectAllButton.IsEnabled = hasRows;
    }

    private void ProposalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionButtons();
        if (ProposalList.SelectedItem is not JsonImportReviewRow row)
        {
            ClearDetail();
            return;
        }

        ShowDetail(row);
    }

    private void ClearDetail()
    {
        DetailPlaceholder.Visibility = Visibility.Visible;
        DetailSummary.Visibility = Visibility.Collapsed;
        DetailConflict.Visibility = Visibility.Collapsed;
        DetailProposedLabel.Visibility = Visibility.Collapsed;
        DetailProposed.Visibility = Visibility.Collapsed;
        DetailPriorLabel.Visibility = Visibility.Collapsed;
        DetailPrior.Visibility = Visibility.Collapsed;
        DetailDeterministicLabel.Visibility = Visibility.Collapsed;
        DetailDeterministic.Visibility = Visibility.Collapsed;
        DetailSourceLabel.Visibility = Visibility.Collapsed;
        DetailSourceExcerpt.Visibility = Visibility.Collapsed;
        DetailRationaleLabel.Visibility = Visibility.Collapsed;
        DetailRationale.Visibility = Visibility.Collapsed;
    }

    private void ShowDetail(JsonImportReviewRow row)
    {
        DetailPlaceholder.Visibility = Visibility.Collapsed;
        DetailSummary.Text = row.Summary;
        DetailSummary.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(row.Analysis?.DisplaySummary))
        {
            DetailConflict.Text = row.Analysis.DisplaySummary;
            DetailConflict.Foreground = ResolveConflictBrush(row.Analysis.Severity);
            DetailConflict.Visibility = Visibility.Visible;
        }
        else
        {
            DetailConflict.Visibility = Visibility.Collapsed;
        }

        SetDetailText(DetailProposedLabel, DetailProposed, "Proposed", row.Item.Value);
        SetDetailText(DetailPriorLabel, DetailPrior, "Current", row.Item.PriorValue);

        if (!string.IsNullOrWhiteSpace(row.Analysis?.DeterministicValue))
            SetDetailText(DetailDeterministicLabel, DetailDeterministic, "Deterministic re-import", row.Analysis.DeterministicValue);
        else
        {
            DetailDeterministicLabel.Visibility = Visibility.Collapsed;
            DetailDeterministic.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(row.Analysis?.SourceExcerpt))
        {
            var label = string.IsNullOrWhiteSpace(row.Analysis.SourceRef)
                ? "Source excerpt"
                : $"Source excerpt ({row.Analysis.SourceRef})";
            SetDetailText(DetailSourceLabel, DetailSourceExcerpt, label, row.Analysis.SourceExcerpt);
        }
        else
        {
            DetailSourceLabel.Visibility = Visibility.Collapsed;
            DetailSourceExcerpt.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(row.Item.Rationale))
            SetDetailText(DetailRationaleLabel, DetailRationale, "Rationale", row.Item.Rationale);
        else
        {
            DetailRationaleLabel.Visibility = Visibility.Collapsed;
            DetailRationale.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrWhiteSpace(row.Analysis?.EntityLinkageHint))
        {
            DetailConflict.Text = string.IsNullOrWhiteSpace(DetailConflict.Text)
                ? row.Analysis.EntityLinkageHint
                : $"{DetailConflict.Text}{Environment.NewLine}{row.Analysis.EntityLinkageHint}";
            DetailConflict.Visibility = Visibility.Visible;
        }
    }

    private static void SetDetailText(TextBlock label, TextBox box, string title, string value)
    {
        label.Text = title;
        label.Visibility = Visibility.Visible;
        box.Text = value.Trim();
        box.Visibility = Visibility.Visible;
    }

    private Brush ResolveConflictBrush(JsonImportConflictSeverity severity) => severity switch
    {
        JsonImportConflictSeverity.Unsupported => (Brush)FindResource("ErrorBrush"),
        JsonImportConflictSeverity.Drift => (Brush)FindResource("WarningBrush"),
        JsonImportConflictSeverity.Supported => (Brush)FindResource("SuccessBrush"),
        _ => (Brush)FindResource("TextMutedBrush"),
    };

    private void AcceptSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProposalList.SelectedItem is not JsonImportReviewRow row)
            return;

        AcceptItem(row.Item);
    }

    private void RejectSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ProposalList.SelectedItem is not JsonImportReviewRow row)
            return;

        RejectItem(row.Item);
    }

    private void AcceptAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _bundle.Scenario.JsonImportReviewQueue.Count == 0)
            return;

        var analyses = JsonImportConflictService.AnalyzeQueue(_bundle);
        if (!ConfirmAcceptAll(analyses))
            return;

        var applied = 0;
        foreach (var item in _bundle.Scenario.JsonImportReviewQueue.ToList())
        {
            if (SourceJsonImportService.ApplyAccepted(_bundle, item))
                applied++;
        }

        _bundle.Scenario.JsonImportReviewQueue.Clear();
        _bundle.Scenario.JsonImportProposedSnapshot = null;
        AdventureDesignService.HydrateFromScenario(_bundle);
        AdventureStore.Save(_bundle);
        ChangesSaved = true;
        Reload();

        if (applied == 0)
            MessageBox.Show(this, "No proposals could be applied.", "JSON import review", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RejectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _bundle.Scenario.JsonImportReviewQueue.Count == 0)
            return;

        _bundle.Scenario.JsonImportReviewQueue.Clear();
        _bundle.Scenario.JsonImportProposedSnapshot = null;
        AdventureStore.Save(_bundle);
        ChangesSaved = true;
        Reload();
    }

    private void AcceptItem(JsonImportReviewItem item)
    {
        if (_bundle is null)
            return;

        _analyses.TryGetValue(item.Id, out var analysis);
        if (analysis is not null && !ConfirmAccept(analysis))
            return;

        if (!SourceJsonImportService.ApplyAccepted(_bundle, item))
        {
            MessageBox.Show(this, "Could not apply JSON import proposal.", "JSON import review", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _bundle.Scenario.JsonImportReviewQueue.Remove(item);
        if (_bundle.Scenario.JsonImportReviewQueue.Count == 0)
            _bundle.Scenario.JsonImportProposedSnapshot = null;

        AdventureDesignService.HydrateFromScenario(_bundle);
        AdventureStore.Save(_bundle);
        ChangesSaved = true;
        Reload();
    }

    private void RejectItem(JsonImportReviewItem item)
    {
        if (_bundle is null)
            return;

        _bundle.Scenario.JsonImportReviewQueue.Remove(item);
        if (_bundle.Scenario.JsonImportReviewQueue.Count == 0)
            _bundle.Scenario.JsonImportProposedSnapshot = null;

        AdventureStore.Save(_bundle);
        ChangesSaved = true;
        Reload();
    }

    private static bool ConfirmAccept(JsonImportProposalAnalysis analysis)
    {
        var message = JsonImportConflictService.BuildAcceptWarningMessage(analysis);
        if (string.IsNullOrWhiteSpace(message))
            return true;

        return MessageBox.Show(
                   message + Environment.NewLine + Environment.NewLine + "Accept anyway?",
                   "JSON import review",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning)
               == MessageBoxResult.Yes;
    }

    private static bool ConfirmAcceptAll(IReadOnlyList<JsonImportProposalAnalysis> analyses)
    {
        var warnings = analyses
            .Select(JsonImportConflictService.BuildAcceptWarningMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (warnings.Count == 0)
            return true;

        var body = string.Join(
            Environment.NewLine + Environment.NewLine,
            warnings.Take(4));
        if (warnings.Count > 4)
            body += Environment.NewLine + Environment.NewLine + $"(+{warnings.Count - 4} more warning(s))";

        return MessageBox.Show(
                   body + Environment.NewLine + Environment.NewLine + "Accept all anyway?",
                   "JSON import review",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning)
               == MessageBoxResult.Yes;
    }

    private void PreviewScenario_Click(object sender, RoutedEventArgs e) =>
        ShowFilePreview("scenario");

    private void PreviewEntities_Click(object sender, RoutedEventArgs e) =>
        ShowFilePreview("entities");

    private void ShowFilePreview(string fileKind)
    {
        if (_bundle is null)
            return;

        var snapshot = _bundle.Scenario.JsonImportProposedSnapshot;
        if (snapshot is null)
            return;

        string currentText;
        string proposedText;
        string fileName;
        if (string.Equals(fileKind, "scenario", StringComparison.OrdinalIgnoreCase))
        {
            fileName = SourceJsonImportService.ScenarioJsonFileName;
            currentText = SourceJsonImportService.ReadCurrentScenarioJsonOnDisk(_bundle.Metadata.Id);
            proposedText = snapshot.ScenarioJson;
        }
        else if (string.Equals(fileKind, "entities", StringComparison.OrdinalIgnoreCase))
        {
            fileName = SourceJsonImportService.EntitiesJsonFileName;
            currentText = SourceJsonImportService.ReadCurrentEntitiesJsonOnDisk(_bundle.Metadata.Id);
            proposedText = snapshot.EntitiesJson;
        }
        else
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(proposedText))
            return;

        var dialog = new SourceCompareDialog(
            currentText,
            proposedText,
            $"Current {fileName}",
            $"Proposed {fileName}")
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class JsonImportReviewRow
    {
        public JsonImportReviewRow(JsonImportReviewItem item, JsonImportProposalAnalysis? analysis)
        {
            Item = item;
            Analysis = analysis;
            Summary = FormatSummary(item);
            ListLabel = BuildListLabel(item, analysis);
        }

        public JsonImportReviewItem Item { get; }

        public JsonImportProposalAnalysis? Analysis { get; }

        public string Summary { get; }

        public string ListLabel { get; }

        public override string ToString() => ListLabel;
    }

    private static string BuildListLabel(JsonImportReviewItem item, JsonImportProposalAnalysis? analysis)
    {
        var severity = analysis is null || analysis.Severity == JsonImportConflictSeverity.None
            ? ""
            : $" [{JsonImportConflictService.FormatSeverityLabel(analysis.Severity)}]";
        var preview = PreviewProposalText(item.Value);
        return $"{FormatSummary(item)}{severity} → {preview}";
    }

    private static string FormatSummary(JsonImportReviewItem item)
    {
        if (string.Equals(item.Kind, SourceJsonImportService.KindScenarioField, StringComparison.OrdinalIgnoreCase))
            return $"scenario.{item.Field}";

        return $"{item.Action} {item.EntityType} \"{item.Name}\"";
    }

    private static string PreviewProposalText(string value)
    {
        var trimmed = value.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 56 ? trimmed : trimmed[..53] + "…";
    }
}
