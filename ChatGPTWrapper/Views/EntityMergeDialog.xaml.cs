using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class EntityMergeDialog : Window
{
    private readonly AdventureBundle _bundle;
    private readonly EntityReferenceRow _sourceRow;
    private readonly string _category;

    public EntityChangePlan? ResultPlan { get; private set; }

    public EntityMergeDialog(AdventureBundle bundle, EntityReferenceRow sourceRow, string category)
    {
        _bundle = bundle;
        _sourceRow = sourceRow;
        _category = category;
        InitializeComponent();
        SourceNameBlock.Text = sourceRow.Name;
        TargetCombo.ItemsSource = EntityReferenceRowBuilder.BuildRows(bundle, category, PlayLayoutCapabilities.FromContentWidth(800))
            .Where(r => r.Id != sourceRow.Id)
            .ToList();
        TargetCombo.SelectionChanged += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (TargetCombo.SelectedItem is not EntityReferenceRow target)
        {
            PreviewBlock.Text = "";
            return;
        }

        var mentions = CanonMentionIndexService.FindMentions(_bundle, [_sourceRow.Name]);
        PreviewBlock.Text = $"Will rewrite {mentions.Count} mention(s) from “{_sourceRow.Name}” to “{target.Name}” and remove the source entity.";
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (TargetCombo.SelectedItem is not EntityReferenceRow target)
        {
            MessageBox.Show(this, "Select a target entity.", "Merge entity", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultPlan = EntityChangePlanBuilder.BuildMergePlan(
            _bundle,
            _sourceRow.Id,
            target.Id,
            _category,
            _sourceRow.Name,
            target.Name);

        var result = EntityEditSourceSyncService.ApplyPlan(_bundle, ResultPlan);
        AdventureStore.Save(_bundle);
        DialogResult = true;
        Close();
    }
}
