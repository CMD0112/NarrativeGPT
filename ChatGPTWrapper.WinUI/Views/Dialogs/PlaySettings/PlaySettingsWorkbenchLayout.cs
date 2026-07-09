using ChatGPTWrapper.Shell;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

/// <summary>
/// Content-width contract for the Play Settings workbench shell.
/// See docs/reference/wrapper-ui-paradigm.md § Workbench content width.
/// </summary>
internal enum PlaySettingsContentLayoutMode
{
    /// <summary>Single readable column (~720px max), left-aligned when wider.</summary>
    FormColumn,

    /// <summary>Section cards flow 2-up when content area is wide enough.</summary>
    CardGrid,

    /// <summary>Master-detail grids stretch to the content column.</summary>
    MasterDetail,

    /// <summary>Preview and similar surfaces fill the viewport.</summary>
    FullBleed,
}

internal readonly record struct PlaySettingsLayoutSnapshot(
    double ContentMaxWidth,
    HorizontalAlignment ContentHorizontalAlignment,
    ScrollBarVisibility OuterVerticalScroll);

internal static class PlaySettingsWorkbenchLayout
{
    public const double DefaultFormColumnMaxWidth = 720;
    public const double DefaultNavRailWidthWide = 232;
    public const double DefaultNavRailWidthNarrow = 200;
    public const double DefaultShellBreakpointMedium = 880;
    public const double DefaultCardGridTwoUpBreakpoint = 880;

    private static PlaySettingsViewportMetrics _viewport = PlaySettingsViewportMetrics.FromWorkbench(
        new WorkbenchViewportMetrics(
            1000,
            820,
            1000,
            720,
            WorkbenchViewportClass.Standard,
            new WorkAreaBounds(1920, 1080)));

    public static void ApplyViewport(PlaySettingsViewportMetrics viewport) =>
        _viewport = viewport;

    public static PlaySettingsViewportMetrics CurrentViewport => _viewport;

    public static PlaySettingsContentLayoutMode GetLayoutMode(PlaySettingsTab tab) =>
        tab switch
        {
            PlaySettingsTab.Preview => PlaySettingsContentLayoutMode.FullBleed,
            PlaySettingsTab.UtilityJobs or PlaySettingsTab.MemoryCards or PlaySettingsTab.History
                => PlaySettingsContentLayoutMode.MasterDetail,
            PlaySettingsTab.Session or PlaySettingsTab.Sources
                => PlaySettingsContentLayoutMode.CardGrid,
            _ => PlaySettingsContentLayoutMode.FormColumn,
        };

    public static double ResolveNavRailWidth(double shellWidth)
    {
        var v = _viewport;
        return shellWidth < v.ShellBreakpointMedium ? v.NavRailWidthNarrow : v.NavRailWidthWide;
    }

    public static PlaySettingsLayoutSnapshot Resolve(
        PlaySettingsContentLayoutMode mode,
        double contentAreaWidth)
    {
        if (contentAreaWidth <= 0)
            contentAreaWidth = _viewport.FormColumnMaxWidth;

        var formMax = _viewport.FormColumnMaxWidth;

        return mode switch
        {
            PlaySettingsContentLayoutMode.FullBleed => new(
                double.PositiveInfinity,
                HorizontalAlignment.Stretch,
                ScrollBarVisibility.Disabled),

            PlaySettingsContentLayoutMode.MasterDetail or PlaySettingsContentLayoutMode.CardGrid => new(
                double.PositiveInfinity,
                HorizontalAlignment.Stretch,
                ScrollBarVisibility.Auto),

            PlaySettingsContentLayoutMode.FormColumn when contentAreaWidth <= formMax => new(
                contentAreaWidth,
                HorizontalAlignment.Stretch,
                ScrollBarVisibility.Auto),

            _ => new(
                formMax,
                HorizontalAlignment.Left,
                ScrollBarVisibility.Auto),
        };
    }
}
