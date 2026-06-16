using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

internal sealed class SourceSyncRowViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<SourceSyncActionOption> _availableActionOptions = [];

    public SourceSyncRowViewModel(SourceSyncPlanItem item)
    {
        PlanItem = item;
        RebuildActionOptions();
        RefreshLabels();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? ActionChanged;

    public SourceSyncPlanItem PlanItem { get; }

    public string FileName => PlanItem.Entry.RelativePath;

    public string StateLabel { get; private set; } = "";

    public string LocalHashShort { get; private set; } = "";

    public string RemoteHashShort { get; private set; } = "";

    public string ActionLabel { get; private set; } = "";

    public IReadOnlyList<SourceSyncActionOption> AvailableActionOptions => _availableActionOptions;

    public SourceSyncActionOption? SelectedActionOption
    {
        get => _availableActionOptions.FirstOrDefault(o => o.Action == SelectedAction);
        set
        {
            if (value is null)
                return;

            SelectedAction = value.Action;
        }
    }

    public SourceSyncAction SelectedAction
    {
        get => ProjectFileSyncPlanner.ResolveAction(PlanItem);
        set
        {
            if (!ProjectFileSyncPlanner.ApplyUserAction(PlanItem, value))
                return;

            RefreshLabels();
            Notify(nameof(SelectedAction));
            Notify(nameof(SelectedActionOption));
            Notify(nameof(ActionLabel));
            ActionChanged?.Invoke();
        }
    }

    public void RefreshLabels()
    {
        RebuildActionOptions();
        StateLabel = PlanItem.Entry.SyncState.ToString();
        LocalHashShort = SourceManifestHelper.ShortHash(PlanItem.Entry.LocalSha256);
        RemoteHashShort = SourceManifestHelper.ShortHash(PlanItem.Entry.RemoteSha256);
        ActionLabel = SourceSyncActionLabels.Format(ProjectFileSyncPlanner.ResolveAction(PlanItem));
        Notify(nameof(AvailableActionOptions));
        Notify(nameof(SelectedAction));
        Notify(nameof(SelectedActionOption));
    }

    public void NotifyActionChanged()
    {
        RefreshLabels();
        Notify(nameof(SelectedAction));
        Notify(nameof(SelectedActionOption));
        Notify(nameof(ActionLabel));
        ActionChanged?.Invoke();
    }

    private void RebuildActionOptions() =>
        _availableActionOptions = SourceSyncActionLabels.OptionsFor(PlanItem);

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
