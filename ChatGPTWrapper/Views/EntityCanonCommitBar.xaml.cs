using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class EntityCanonCommitBar : UserControl
{
    public event EventHandler? PlansChanged;

    private AdventureBundle? _bundle;
    private CanonHealthSnapshot? _snapshot;

    public EntityCanonCommitBar()
    {
        InitializeComponent();
    }

    public void Bind(AdventureBundle? bundle)
    {
        _bundle = bundle;
        if (bundle is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        _snapshot = CanonHealthService.Analyze(bundle);
        if (!_snapshot.NeedsAttention)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        CommitBarSummary.Text = _snapshot.BuildSummary();
        ApplyButton.Content = _snapshot.StagedPlanCount > 0 ? "Sync canon" : "Sync sources from JSON";
        PreviewButton.Visibility = _snapshot.StagedPlanCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscardButton.Visibility = _snapshot.StagedPlanCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    private void PreviewAll_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _snapshot is null || _snapshot.StagedPlanCount == 0)
            return;

        var dlg = new EntityChangePlanDiffPreviewDialog(_bundle, EntityChangePlanQueueService.GetPending(_bundle))
        {
            Owner = Window.GetWindow(this),
        };
        dlg.ShowDialog();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var owner = Window.GetWindow(this);
        var result = CanonHealthService.TrySyncAll(_bundle);
        AdventureStore.Save(_bundle);

        if (result.RepairResult?.RequiresManualReconcile == true)
        {
            EntityReferenceEditService.PromptReconcile(
                _bundle,
                owner,
                new CanonEditContext { Category = "" },
                null);
        }

        Bind(_bundle);
        PlansChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _snapshot is null || _snapshot.StagedPlanCount == 0)
            return;

        if (MessageBox.Show(Window.GetWindow(this),
                "Discard all staged canon changes?",
                "Discard staged changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        EntityChangePlanQueueService.DiscardAll(_bundle);
        Bind(_bundle);
        PlansChanged?.Invoke(this, EventArgs.Empty);
    }
}
