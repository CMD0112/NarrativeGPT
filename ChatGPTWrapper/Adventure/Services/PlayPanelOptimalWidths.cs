using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record PlayPanelOptimalWidths(double LeftWidth, double RightWidth);

public static class PlayPanelOptimalWidthCalculator
{
    public const double MinLeftWidth = 200;
    public const double MaxLeftWidth = 640;
    public const double MinRightWidth = 180;
    public const double MaxRightWidth = 480;

    public static PlayPanelOptimalWidths Resolve(
        AdventureSettings settings,
        double maxLeftWidth,
        double maxRightWidth)
    {
        var left = ResolveSideWidth(settings, PlayPanelSide.Left, maxLeftWidth, MinLeftWidth);
        var right = ResolveSideWidth(settings, PlayPanelSide.Right, maxRightWidth, MinRightWidth);
        return new PlayPanelOptimalWidths(left, right);
    }

    public static PlayPanelWidthFit ValidateLeft(AdventureSettings settings, double panelWidth) =>
        PlayPanelWidthRequirements.Evaluate(settings, PlayPanelSide.Left, panelWidth);

    public static PlayPanelWidthFit ValidateRight(AdventureSettings settings, double panelWidth) =>
        PlayPanelWidthRequirements.Evaluate(settings, PlayPanelSide.Right, panelWidth);

    private static double ResolveSideWidth(
        AdventureSettings settings,
        string side,
        double maxWidth,
        double minWidth)
    {
        var requirementWidth = PlayPanelWidthRequirements.OptimalPanelWidth(
            settings,
            side,
            PlayPanelWidthTier.Enhanced);

        var preset = PlayLayoutPresetLibrary.Find(settings.PlayLayoutPresetId);
        var presetWidth = side == PlayPanelSide.Left
            ? preset?.RecommendedLeftWidth ?? 0
            : preset?.RecommendedRightWidth ?? 0;

        var target = Math.Max(requirementWidth, presetWidth);
        return Clamp(target, minWidth, maxWidth);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}
