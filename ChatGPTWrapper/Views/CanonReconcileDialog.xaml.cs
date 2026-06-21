using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class CanonReconcileDialog : Window
{
    private readonly AdventureBundle _bundle;
    private readonly CanonEditContext _context;
    private readonly CanonDriftReport _report;
    private readonly IReadOnlyList<PhraseHighlightRule>? _phraseRules;
    private readonly Func<Task>? _openSourceManagerAsync;
    private bool _closeHandled;

    public CanonReconcileResult Result { get; private set; } = CanonReconcileResult.Deferred;

    public CanonReconcileDialog(
        AdventureBundle bundle,
        CanonEditContext context,
        CanonDriftReport report,
        IReadOnlyList<PhraseHighlightRule>? phraseRules = null,
        Func<Task>? openSourceManagerAsync = null)
    {
        _bundle = bundle;
        _context = context;
        _report = report;
        _phraseRules = phraseRules;
        _openSourceManagerAsync = openSourceManagerAsync;
        InitializeComponent();
        Closing += OnClosing;
        BindUi();
    }

    private void BindUi()
    {
        var drifted = _report.DriftedFileNames;
        SummaryLine.Text = drifted.Count == 0
            ? "No file drift detected."
            : $"Drift in: {string.Join(", ", drifted)}";

        var hints = drifted
            .SelectMany(file =>
            {
                var entry = _bundle.SourceManifest.Entries.FirstOrDefault(e =>
                    string.Equals(e.RelativePath, file, StringComparison.OrdinalIgnoreCase));
                return entry is null ? [] : SectionDiffService.GetChangedSectionsSincePublish(entry);
            })
            .ToList();

        var hintText = SectionDiffService.FormatRepublishHint(hints);
        if (!string.IsNullOrWhiteSpace(hintText))
            SummaryLine.Text += Environment.NewLine + hintText;

        if (!string.IsNullOrWhiteSpace(_context.PriorName)
            && !string.IsNullOrWhiteSpace(_context.NewName)
            && !string.Equals(_context.PriorName, _context.NewName, StringComparison.OrdinalIgnoreCase))
        {
            RenameLine.Text = $"Rename detected: {_context.PriorName} → {_context.NewName}";
            RenameLine.Visibility = Visibility.Visible;
            RenameOptionsPanel.Visibility = Visibility.Visible;

            var renamePlan = RenameReconciliationService.BuildPlan(_bundle, _context, _report, _phraseRules);
            if (renamePlan.ContextIndexUpdates.Count > 0)
            {
                RenameLine.Text += Environment.NewLine
                    + "Context index: "
                    + string.Join("; ", renamePlan.ContextIndexUpdates.Take(3));
            }
        }

        var previewParts = new List<string>();
        foreach (var file in _report.Files.Where(f => f.HasDrift))
        {
            previewParts.Add($"=== {file.FileName} (projected push preview) ===");
            var disk = file.DiskContent ?? "(no file on disk)";
            var diff = TextDiffService.ComputeLineDiff(disk, file.ProjectedContent);
            previewParts.Add(TextDiffService.FormatUnifiedDiff(diff, "current", "after push"));
            previewParts.Add("");
        }

        DiffPreviewBox.Text = string.Join(Environment.NewLine, previewParts).TrimEnd();
    }

    private void Push_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmPush())
            return;

        if (IsRenameContext())
            RenameReconciliationService.ApplyCrossCanonText(_bundle, _context);

        CanonReconciliationService.ApplyPushToSources(_bundle, _report);

        if (IsRenameContext())
        {
            var refreshed = CanonReconciliationService.DetectDrift(_bundle, _context);
            RenameReconciliationService.Apply(_bundle, _context, refreshed, new RenameReconciliationOptions
            {
                AddPriorNameAsAlias = AddAliasCheck.IsChecked == true,
                UpdateContextIndex = UpdateContextIndexCheck.IsChecked == true,
                UpdatePhraseHighlights = UpdatePhraseHighlightsCheck.IsChecked == true,
            }, _phraseRules is null ? null : _phraseRules.ToList());
        }

        var postReport = CanonReconciliationService.DetectDrift(_bundle, _context);
        CanonReconciliationService.SetNotifyFromEntityEdit(_bundle, _context, _report);
        AdventureStore.Save(_bundle);
        Finish(CanonReconcileResult.Pushed, dialogResult: true);
    }

    private void Pull_Click(object sender, RoutedEventArgs e)
    {
        var rollback = ProjectSourceImportService.CaptureImportState(_bundle);
        var dryRun = ProjectSourceImportService.Import(_bundle, new SourceImportOptions
        {
            Files = _report.DriftedFileNames,
            DryRun = true,
        });
        var changeReport = dryRun.ChangeReport
            ?? ProjectSourceImportService.BuildChangeReport(rollback, _bundle);

        var confirm = MessageBox.Show(
            this,
            (dryRun.Summary ?? "Import preview") + Environment.NewLine + Environment.NewLine
            + changeReport.Format()
            + Environment.NewLine + Environment.NewLine
            + "Apply these changes to scenario.json and entities.json?",
            "Pull from sources",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            ProjectSourceImportService.RestoreImportState(_bundle, rollback);
            return;
        }

        var result = CanonReconciliationService.ApplyPullFromSources(_bundle, _report);
        if (!result.Success)
        {
            ProjectSourceImportService.RestoreImportState(_bundle, rollback);
            MessageBox.Show(this, result.Summary, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AdventureDesignService.HydrateFromScenario(_bundle);
        var postReport = CanonReconciliationService.DetectDrift(_bundle, _context);
        CanonReconciliationService.SetNotifyFromDrift(_bundle, postReport, _context);
        AdventureStore.Save(_bundle);
        Finish(CanonReconcileResult.Pulled, dialogResult: true);
    }

    private void Defer_Click(object sender, RoutedEventArgs e)
    {
        CanonReconciliationService.MarkUnresolvedDrift(_bundle);
        AdventureStore.Save(_bundle);
        Finish(CanonReconcileResult.Deferred, dialogResult: false);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeHandled)
            return;

        if (_report.HasDrift)
        {
            CanonReconciliationService.MarkUnresolvedDrift(_bundle);
            AdventureStore.Save(_bundle);
        }

        Result = CanonReconcileResult.Deferred;
    }

    private void Finish(CanonReconcileResult result, bool dialogResult)
    {
        _closeHandled = true;
        Result = result;
        DialogResult = dialogResult;
        Close();
    }

    private async void OpenSourceManager_Click(object sender, RoutedEventArgs e)
    {
        if (_openSourceManagerAsync is not null)
            await _openSourceManagerAsync();
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        var file = _report.Files.FirstOrDefault(f => f.HasDrift);
        if (file is null)
            return;

        var dlg = new SourceCompareDialog(
            file.DiskContent ?? "",
            file.ProjectedContent,
            "current on disk",
            "projected push")
        {
            Owner = this,
        };
        dlg.ShowDialog();
    }

    private bool ConfirmPush()
    {
        if (_report.DriftedFileNames.Count == 0)
            return false;

        return MessageBox.Show(
            this,
            "Update local sources/*.md from current JSON?\n\n"
            + string.Join(", ", _report.DriftedFileNames)
            + "\n\nMark Published separately in Source Manager after uploading to your Project.",
            "Push to sources",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private bool IsRenameContext() =>
        !string.IsNullOrWhiteSpace(_context.PriorName)
        && !string.IsNullOrWhiteSpace(_context.NewName)
        && !string.Equals(_context.PriorName, _context.NewName, StringComparison.OrdinalIgnoreCase);
}

public enum CanonReconcileResult
{
    Deferred,
    Pushed,
    Pulled,
}
