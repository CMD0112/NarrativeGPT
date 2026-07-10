using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

/// <summary>
/// Play Settings layout constants derived from workbench open viewport metrics.
/// </summary>
internal readonly record struct PlaySettingsViewportMetrics(
    WorkbenchViewportClass ViewportClass,
    double FormColumnMaxWidth,
    double CardGridTwoUpBreakpoint,
    double NavRailWidthWide,
    double NavRailWidthNarrow,
    double ShellBreakpointMedium)
{
    public static PlaySettingsViewportMetrics FromWorkbench(WorkbenchViewportMetrics workbench) =>
        workbench.ViewportClass switch
        {
            WorkbenchViewportClass.Compact => new(
                WorkbenchViewportClass.Compact,
                FormColumnMaxWidth: 720,
                CardGridTwoUpBreakpoint: 880,
                NavRailWidthWide: 212,
                NavRailWidthNarrow: 196,
                ShellBreakpointMedium: 840),

            WorkbenchViewportClass.Spacious => new(
                WorkbenchViewportClass.Spacious,
                FormColumnMaxWidth: 960,
                CardGridTwoUpBreakpoint: 880,
                NavRailWidthWide: 248,
                NavRailWidthNarrow: 212,
                ShellBreakpointMedium: 960),

            _ => new(
                WorkbenchViewportClass.Standard,
                FormColumnMaxWidth: 880,
                CardGridTwoUpBreakpoint: PlaySettingsWorkbenchLayout.DefaultCardGridTwoUpBreakpoint,
                NavRailWidthWide: PlaySettingsWorkbenchLayout.DefaultNavRailWidthWide,
                NavRailWidthNarrow: PlaySettingsWorkbenchLayout.DefaultNavRailWidthNarrow,
                ShellBreakpointMedium: PlaySettingsWorkbenchLayout.DefaultShellBreakpointMedium),
        };
}
