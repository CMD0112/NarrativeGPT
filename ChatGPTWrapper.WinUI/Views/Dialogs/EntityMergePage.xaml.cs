using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class EntityMergePage : UserControl
{
    private readonly AdventureBundle _bundle;
    private readonly EntityReferenceRow _sourceRow;

    public EntityMergePage(AdventureBundle bundle, EntityReferenceRow sourceRow, string category)
    {
        _bundle = bundle;
        _sourceRow = sourceRow;
        InitializeComponent();
        SourceNameBlock.Text = sourceRow.Name;
        TargetCombo.ItemsSource = EntityReferenceRowBuilder
            .BuildRows(bundle, category, PlayLayoutCapabilities.FromContentWidth(800))
            .Where(r => r.Id != sourceRow.Id)
            .ToList();
    }

    public EntityReferenceRow? SelectedTarget =>
        TargetCombo.SelectedItem as EntityReferenceRow;

    private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetCombo.SelectedItem is not EntityReferenceRow target)
        {
            PreviewBlock.Text = "";
            return;
        }

        var mentions = CanonMentionIndexService.FindMentions(_bundle, [_sourceRow.Name]);
        PreviewBlock.Text =
            $"Will rewrite {mentions.Count} mention(s) from “{_sourceRow.Name}” to “{target.Name}” and remove the source entity.";
    }
}
