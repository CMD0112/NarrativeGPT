using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Polls adventure session state for shell status chips (CMD-421).</summary>
public sealed class ShellStatusService
{
    private int _activeJobCount;

    public void SetActiveJobCount(int count) => _activeJobCount = Math.Max(0, count);

    public ShellStatusSnapshot BuildSnapshot(Guid? adventureId)
    {
        if (adventureId is not { } id)
            return new ShellStatusSnapshot { JobActive = _activeJobCount > 0 };

        var bundle = AdventureStore.Load(id);
        if (bundle is null)
            return new ShellStatusSnapshot { JobActive = _activeJobCount > 0 };

        var pending = PendingReviewService.GetCounts(bundle).Total;
        var needsLink = !AdventureProjectBindingService.HasLinkedProject(bundle);
        var bridgeHealthy = !string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId);

        return new ShellStatusSnapshot
        {
            ReviewCount = pending,
            NeedsLink = needsLink,
            JobActive = _activeJobCount > 0,
            BridgeHealthy = bridgeHealthy,
            BridgeSummary = bridgeHealthy ? "Bridge linked" : "Bridge not linked",
        };
    }
}
