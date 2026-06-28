using System.IO;
using System.Windows;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class EntityChangePlanDiffPreviewDialog : ShellDialogWindow
{
    public EntityChangePlanDiffPreviewDialog(AdventureBundle bundle, IReadOnlyList<EntityChangePlan> plans)
    {
        InitializeComponent();
        SummaryLine.Text = plans.Count == 1
            ? plans[0].Summary
            : $"{plans.Count} staged changes";

        var parts = new List<string>();
        foreach (var plan in plans)
        {
            parts.Add($"=== {plan.Summary} ===");
            var context = new CanonEditContext
            {
                Category = plan.Category,
                EntityId = plan.EntityId,
                PriorName = plan.PriorName,
                NewName = plan.NewName,
                IsDelete = plan.IsDelete,
            };
            var report = CanonReconciliationService.DetectDrift(bundle, context);
            var preview = CanonReconciliationService.BuildPushPreview(bundle, report);
            foreach (var file in preview.Keys.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), file);
                var disk = File.Exists(path) ? File.ReadAllText(path) : "";
                parts.Add($"--- {file} ---");
                var diff = TextDiffService.ComputeLineDiff(disk, preview[file]);
                parts.Add(TextDiffService.FormatUnifiedDiff(diff, "current", "after apply"));
                parts.Add("");
            }
        }

        DiffPreviewBox.Text = string.Join(Environment.NewLine, parts).TrimEnd();
    }

    public EntityChangePlanDiffPreviewDialog(
        AdventureBundle bundle,
        EntityEditSourceSyncResult syncResult)
    {
        InitializeComponent();
        SummaryLine.Text = syncResult.Summary ?? "Last sync preview";
        var preview = EntityEditSourceSyncService.BuildDiffPreview(bundle, syncResult);
        var parts = new List<string>();
        foreach (var (file, projected) in preview.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), file);
            var disk = File.Exists(path) ? File.ReadAllText(path) : "";
            parts.Add($"--- {file} ---");
            var diff = TextDiffService.ComputeLineDiff(disk, projected);
            parts.Add(TextDiffService.FormatUnifiedDiff(diff, "current", "after sync"));
            parts.Add("");
        }

        DiffPreviewBox.Text = string.Join(Environment.NewLine, parts).TrimEnd();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
