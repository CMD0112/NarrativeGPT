using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.PlayLayout;

/// <summary>
/// Builds side-aware layout snapshots and resolves which context applies to each play tab.
/// </summary>
public static class PlayLayoutCoordinator
{
    public static PlayLayoutSnapshot CreateSnapshot(
        double shellPanelWidth,
        double companionPanelWidth) =>
        new(
            PlayLayoutContext.FromPanel(PlayPanelSide.Left, shellPanelWidth),
            PlayLayoutContext.FromPanel(PlayPanelSide.Right, companionPanelWidth));

    public static PlayLayoutContext ResolveTabContext(
        PlayLayoutSnapshot snapshot,
        AdventureSettings settings,
        string tabName)
    {
        var side = PlayPanelLayoutService.ResolveTabPlacement(settings, tabName);
        return side == PlayPanelSide.Right ? snapshot.Companion : snapshot.Shell;
    }

    public static PlayPanelWidthFit EvaluateShell(AdventureSettings settings, PlayLayoutSnapshot snapshot) =>
        PlayPanelOptimalWidthCalculator.ValidateLeft(settings, snapshot.Shell.PanelWidth);

    public static PlayPanelWidthFit EvaluateCompanion(AdventureSettings settings, PlayLayoutSnapshot snapshot) =>
        PlayPanelOptimalWidthCalculator.ValidateRight(settings, snapshot.Companion.PanelWidth);
}
