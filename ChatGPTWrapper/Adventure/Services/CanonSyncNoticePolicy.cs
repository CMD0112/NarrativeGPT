using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public static class CanonSyncNoticePolicy
{
    /// <summary>
    /// True when the author still needs to verify or complete source sync (publish, reconcile, staged apply).
    /// </summary>
    public static bool RequiresVerification(AdventureBundle? bundle, EntityEditSourceSyncResult? syncResult = null)
    {
        if (syncResult is { Staged: true } or { RequiresManualReconcile: true })
            return true;

        if (bundle is null)
            return false;

        if (CanonReconciliationService.HasUnresolvedDrift(bundle))
            return true;

        if (CanonHealthService.Analyze(bundle).NeedsAttention)
            return true;

        return ProjectSourceInjectionService.Evaluate(bundle).NeedsRepublishCount > 0;
    }
}
