using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class EntityRetireDialog : Window
{
    private readonly AdventureBundle _bundle;
    private readonly EntityReferenceRow _row;
    private readonly string _category;

    public EntityChangePlan? ResultPlan { get; private set; }

    public EntityRetireDialog(AdventureBundle bundle, EntityReferenceRow row, string category)
    {
        _bundle = bundle;
        _row = row;
        _category = category;
        InitializeComponent();
        RetireSummaryLine.Text = $"Retire “{row.Name}” from active cast/lore?";
    }

    private void Retire_Click(object sender, RoutedEventArgs e)
    {
        ResultPlan = EntityChangePlanBuilder.BuildRetirePlan(
            _bundle,
            _row.Id,
            _category,
            _row.Name,
            AliasOnlyCheck.IsChecked == true);

        var result = EntityEditSourceSyncService.ApplyPlan(_bundle, ResultPlan);
        AdventureStore.Save(_bundle);
        DialogResult = true;
        Close();
    }
}
