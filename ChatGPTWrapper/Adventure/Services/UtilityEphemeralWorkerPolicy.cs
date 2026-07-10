using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>CMD-412: opt-in ephemeral project chat for utility worker setup and per-job sends.</summary>
internal static class UtilityEphemeralWorkerPolicy
{
    public static bool IsEnabled(AdventureBundle bundle) =>
        bundle.Metadata.Settings.UseEphemeralUtilityWorkerChat;

    public static bool ShouldUseEphemeralLane(AdventureBundle bundle, string jobId) =>
        IsEnabled(bundle) || UtilityWorkerTransitionCatalog.ForcesEphemeralLane(jobId);

    public static bool IsWorkerLaneAvailable(AdventureBundle bundle, string? jobId = null)
    {
        if (jobId is not null && UtilityWorkerTransitionCatalog.ForcesEphemeralLane(jobId))
        {
            return !string.IsNullOrWhiteSpace(
                AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata));
        }

        if (IsEnabled(bundle))
        {
            return !string.IsNullOrWhiteSpace(
                AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata));
        }

        return UtilityWorkerCapabilityGate.IsGreen(bundle);
    }

    public static bool RequiresWorkerPin(AdventureBundle bundle, string? jobId = null) =>
        !ShouldUseEphemeralLane(bundle, jobId ?? "")
        && !UtilityWorkerPinService.HasWorkerPin(bundle);

    /// <summary>Ephemeral + force DOM attach for all staged reference files (QA / testing).</summary>
    public static bool ForceDomAttach(AdventureBundle bundle) =>
        IsEnabled(bundle) && bundle.Metadata.Settings.ForceUtilityWorkerDomAttach;
}
