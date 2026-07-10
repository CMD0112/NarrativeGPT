using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public enum AdventureSessionDesignAvailability
{
    Ready,
    ReadyLocalSources,
    NeedsWizard,
    UnavailableHasPlayTurns,
}

/// <summary>
/// Pure rules for in-session Play/Design surface switching (CMD-21 Option 2).
/// </summary>
public static class AdventureSessionModePolicy
{
    public static bool CanSwitchToPlay(AdventureBundle? bundle) => bundle is not null;

    public static AdventureSessionDesignAvailability GetDesignAvailability(AdventureBundle bundle)
    {
        if (bundle.Metadata.Status == AdventureStatus.Designing)
            return AdventureSessionDesignAvailability.Ready;

        if (bundle.Metadata.Status != AdventureStatus.Designing
            && AdventureDesignContextService.CanOpenLocalSourcesEdit(bundle))
            return AdventureSessionDesignAvailability.ReadyLocalSources;

        var acceptedTurns = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
        if (acceptedTurns > 0)
            return AdventureSessionDesignAvailability.UnavailableHasPlayTurns;

        AdventureDesignService.EnsureWorkspace(bundle);
        if (bundle.DesignWorkspace.CurrentStep > AdventureDesignStep.Setup)
            return AdventureSessionDesignAvailability.Ready;

        return AdventureSessionDesignAvailability.NeedsWizard;
    }

    public static bool CanSwitchToDesign(AdventureBundle? bundle) =>
        bundle is not null
        && GetDesignAvailability(bundle) != AdventureSessionDesignAvailability.UnavailableHasPlayTurns;

    public static DesignModeEntryIntent ResolveDesignEntryIntent(AdventureBundle bundle)
    {
        if (bundle.Metadata.Status != AdventureStatus.Designing
            && AdventureDesignContextService.CanOpenLocalSourcesEdit(bundle))
        {
            return DesignModeEntryIntent.LocalSourcesEdit;
        }

        return DesignModeEntryIntent.Default;
    }

    public static bool ShouldPromoteToDesigning(AdventureBundle bundle) =>
        bundle.Metadata.Status != AdventureStatus.Designing
        && GetDesignAvailability(bundle) == AdventureSessionDesignAvailability.NeedsWizard;
}
