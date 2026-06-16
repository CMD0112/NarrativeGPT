using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

internal sealed class SourceSyncActionOption(SourceSyncAction action, string label)
{
    public SourceSyncAction Action { get; } = action;

    public string Label { get; } = label;
}

internal static class SourceSyncActionLabels
{
    public static string Format(SourceSyncAction action) =>
        action switch
        {
            SourceSyncAction.Skip => "Skip",
            SourceSyncAction.Pull => "Pull remote",
            SourceSyncAction.PushReplace => "Push local",
            SourceSyncAction.NeedsResolution => "Choose…",
            _ => action.ToString(),
        };

    public static IReadOnlyList<SourceSyncActionOption> OptionsFor(SourceSyncPlanItem item) =>
        ProjectFileSyncPlanner.GetAvailableActions(item)
            .Select(action => new SourceSyncActionOption(action, Format(action)))
            .ToList();
}
