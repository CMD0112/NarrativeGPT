using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class EntityRenameWizardDialog : Window
{
    private readonly AdventureBundle _bundle;
    private readonly CanonEditContext _context;
    private readonly List<MentionRow> _rows;

    public EntityChangePlan? ResultPlan { get; private set; }

    public EntityRenameWizardDialog(
        AdventureBundle bundle,
        CanonEditContext context,
        IReadOnlyList<CanonMentionHit> mentions)
    {
        _bundle = bundle;
        _context = context;
        _rows = mentions.Select(m => new MentionRow(m)).ToList();
        InitializeComponent();

        RenameSummaryLine.Text = $"Rename {context.PriorName} → {context.NewName}. Choose how to handle each mention.";
        MentionsGrid.ItemsSource = _rows;

        var actionColumn = MentionsGrid.Columns.OfType<DataGridComboBoxColumn>().First();
        actionColumn.ItemsSource = Enum.GetValues<EntityTextReplacementAction>();

        UpdateDiffPreview();
        MentionsGrid.CellEditEnding += (_, _) => UpdateDiffPreview();
    }

    private void UpdateDiffPreview()
    {
        var plan = EntityChangePlanBuilder.BuildRenamePlan(
            _bundle,
            _context,
            _rows.Select(r => r.Hit).ToList());

        var report = CanonReconciliationService.DetectDrift(_bundle, _context);
        var preview = CanonReconciliationService.BuildPushPreview(_bundle, report);
        var parts = new List<string> { plan.Summary, $"{_rows.Count(r => r.Action != EntityTextReplacementAction.Skip)} mention(s) will be updated." };
        foreach (var file in preview.Keys.Take(3))
        {
            var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(_bundle), file);
            var disk = File.Exists(path) ? File.ReadAllText(path) : "";
            var diff = TextDiffService.ComputeLineDiff(disk, preview[file]);
            parts.Add(TextDiffService.FormatUnifiedDiff(diff, file, "after rename"));
        }

        DiffPreviewBox.Text = string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ResultPlan = EntityChangePlanBuilder.BuildRenamePlan(
            _bundle,
            _context,
            _rows.Select(r => r.Hit).ToList());
        DialogResult = true;
        Close();
    }

    private sealed class MentionRow
    {
        public MentionRow(CanonMentionHit hit) => Hit = hit;

        public CanonMentionHit Hit { get; }

        public string File => Hit.File;

        public string MatchedTerm => Hit.MatchedTerm;

        public string Snippet => Hit.Snippet;

        public EntityTextReplacementAction Action
        {
            get => Hit.Action;
            set => Hit.Action = value;
        }
    }
}
