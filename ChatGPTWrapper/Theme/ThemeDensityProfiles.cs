namespace ChatGPTWrapper.Theme;

public enum ThemeDensityPreset
{
    /// <summary>Legacy spacing — no density tier overrides beyond explicit user fields.</summary>
    Default = 0,

    /// <summary>Roomier controls and typography (fresh-install default).</summary>
    Comfortable = 1,

    /// <summary>Denser companion and chrome for power users.</summary>
    Compact = 2,
}

public sealed class ThemeDensityMetrics
{
    public double FontSizeBody { get; init; }

    public double FontSizeTitle { get; init; }

    public double FontSizeHint { get; init; }

    public double SpaceXs { get; init; }

    public double SpaceSm { get; init; }

    public double SpaceMd { get; init; }

    public double SpaceLg { get; init; }

    public double SpaceXl { get; init; }

    public double ControlMinHeight { get; init; }

    public double CompanionDefaultWidth { get; init; }

    public double ComposeFontSize { get; init; }

    public double ComposeSendSize { get; init; }
}

public static class ThemeDensityProfiles
{
    public static ThemeDensityMetrics GetMetrics(ThemeDensityPreset preset) =>
        preset switch
        {
            ThemeDensityPreset.Comfortable => new ThemeDensityMetrics
            {
                FontSizeBody = 14,
                FontSizeTitle = 16,
                FontSizeHint = 12,
                SpaceXs = 4,
                SpaceSm = 8,
                SpaceMd = 12,
                SpaceLg = 16,
                SpaceXl = 24,
                ControlMinHeight = 36,
                CompanionDefaultWidth = 320,
                ComposeFontSize = 16,
                ComposeSendSize = 34,
            },
            ThemeDensityPreset.Compact => new ThemeDensityMetrics
            {
                FontSizeBody = 12,
                FontSizeTitle = 14,
                FontSizeHint = 11,
                SpaceXs = 3,
                SpaceSm = 6,
                SpaceMd = 10,
                SpaceLg = 14,
                SpaceXl = 20,
                ControlMinHeight = 30,
                CompanionDefaultWidth = 280,
                ComposeFontSize = 14,
                ComposeSendSize = 28,
            },
            _ => new ThemeDensityMetrics
            {
                FontSizeBody = 13,
                FontSizeTitle = 15,
                FontSizeHint = 11,
                SpaceXs = 4,
                SpaceSm = 8,
                SpaceMd = 12,
                SpaceLg = 16,
                SpaceXl = 24,
                ControlMinHeight = 32,
                CompanionDefaultWidth = 300,
                ComposeFontSize = 16,
                ComposeSendSize = 32,
            },
        };

    public static (
        double FontSizeBody,
        double FontSizeTitle,
        double FontSizeHint,
        double SpaceXs,
        double SpaceSm,
        double SpaceMd,
        double SpaceLg,
        double SpaceXl,
        double ControlMinHeight,
        double CompanionDefaultWidth,
        double ComposeFontSize,
        double ComposeSendSize) MergeTypography(
        ThemeSettings settings,
        double fontSizeBody,
        double fontSizeTitle,
        double fontSizeHint,
        double spaceXs,
        double spaceSm,
        double spaceMd,
        double spaceLg,
        double spaceXl)
    {
        var metrics = GetMetrics(settings.DensityPreset);
        var controlMinHeight = metrics.ControlMinHeight;
        var companionWidth = metrics.CompanionDefaultWidth;
        var composeFontSize = metrics.ComposeFontSize;
        var composeSendSize = metrics.ComposeSendSize;

        if (settings.DensityPreset != ThemeDensityPreset.Default)
        {
            if (settings.FontSizeBody is null)
                fontSizeBody = metrics.FontSizeBody;
            if (settings.FontSizeTitle is null)
                fontSizeTitle = metrics.FontSizeTitle;
            if (settings.FontSizeHint is null)
                fontSizeHint = metrics.FontSizeHint;
            if (settings.SpaceXs is null)
                spaceXs = metrics.SpaceXs;
            if (settings.SpaceSm is null)
                spaceSm = metrics.SpaceSm;
            if (settings.SpaceMd is null)
                spaceMd = metrics.SpaceMd;
            if (settings.SpaceLg is null)
                spaceLg = metrics.SpaceLg;
            if (settings.SpaceXl is null)
                spaceXl = metrics.SpaceXl;
        }

        return (
            fontSizeBody,
            fontSizeTitle,
            fontSizeHint,
            spaceXs,
            spaceSm,
            spaceMd,
            spaceLg,
            spaceXl,
            controlMinHeight,
            companionWidth,
            composeFontSize,
            composeSendSize);
    }
}
