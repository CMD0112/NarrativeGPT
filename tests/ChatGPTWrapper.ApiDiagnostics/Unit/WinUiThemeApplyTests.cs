using ChatGPTWrapper.Theme;
using Xunit;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class WinUiThemeApplyTests
{
    [Fact]
    public void ResolveEffectiveTheme_compact_density_changes_control_metrics()
    {
        var comfortable = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            DensityPreset = ThemeDensityPreset.Comfortable,
        });

        var compact = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            DensityPreset = ThemeDensityPreset.Compact,
        });

        Assert.True(compact.ControlMinHeight <= comfortable.ControlMinHeight);
        Assert.NotEqual(comfortable.ComputeFingerprint(), compact.ComputeFingerprint());
    }

    [Fact]
    public void BuildCssVariableBlock_contains_cgw_root_tokens()
    {
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var css = ThemeApplicationService.BuildCssVariableBlock(resolved);

        Assert.Contains("--cgw-bg-base:", css, StringComparison.Ordinal);
        Assert.Contains("--cgw-accent:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterWinUiApplyHandler_invokes_on_apply()
    {
        ResolvedTheme? applied = null;
        ThemeApplicationService.RegisterWinUiApplyHandler(theme => applied = theme);

        var settings = ThemeApplicationService.CreateDefaultSettings();
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
        ThemeApplicationService.InvalidateApplyCache();
        Assert.True(ThemeApplicationService.ApplyToWinUi(resolved));
        Assert.NotNull(applied);
        Assert.Equal(resolved.ComputeFingerprint(), applied!.ComputeFingerprint());
    }
}
