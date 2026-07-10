using ChatGPTWrapper.Theme;
using Xunit;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ThemeDensityPresetTests
{
    [Fact]
    public void Comfortable_density_increases_body_font_when_not_overridden()
    {
        var settings = new ThemeSettings { DensityPreset = ThemeDensityPreset.Comfortable };
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);

        Assert.Equal(14, resolved.FontSizeBody);
        Assert.Equal(36, resolved.ControlMinHeight);
        Assert.Equal(320, resolved.CompanionDefaultWidth);
    }

    [Fact]
    public void Compact_density_reduces_structural_metrics()
    {
        var settings = new ThemeSettings { DensityPreset = ThemeDensityPreset.Compact };
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);

        Assert.Equal(12, resolved.FontSizeBody);
        Assert.Equal(30, resolved.ControlMinHeight);
        Assert.Equal(280, resolved.CompanionDefaultWidth);
    }

    [Fact]
    public void Explicit_typography_override_wins_over_density_tier()
    {
        var settings = new ThemeSettings
        {
            DensityPreset = ThemeDensityPreset.Compact,
            FontSizeBody = 15,
        };
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);

        Assert.Equal(15, resolved.FontSizeBody);
    }

    [Fact]
    public void BuildCssVariableBlock_emits_compose_density_vars()
    {
        var settings = new ThemeSettings { DensityPreset = ThemeDensityPreset.Compact };
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
        var css = ThemeApplicationService.BuildCssVariableBlock(resolved);

        Assert.Contains("--cgw-compose-font-size: 14px;", css);
        Assert.Contains("--cgw-compose-send-size: 28px;", css);
    }

    [Fact]
    public void CreateDefaultSettings_uses_Comfortable_density()
    {
        var settings = ThemeApplicationService.CreateDefaultSettings();
        Assert.Equal(ThemeDensityPreset.Comfortable, settings.DensityPreset);
    }

    [Fact]
    public void Comfortable_and_Compact_differ_on_control_min_height_and_compose_vars()
    {
        var comfortable = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            DensityPreset = ThemeDensityPreset.Comfortable,
        });
        var compact = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            DensityPreset = ThemeDensityPreset.Compact,
        });

        Assert.True(compact.ControlMinHeight < comfortable.ControlMinHeight);
        Assert.True(compact.ComposeSendSize < comfortable.ComposeSendSize);

        var comfortableCss = ThemeApplicationService.BuildCssVariableBlock(comfortable);
        var compactCss = ThemeApplicationService.BuildCssVariableBlock(compact);
        Assert.Contains("--cgw-compose-send-size: 34px;", comfortableCss);
        Assert.Contains("--cgw-compose-send-size: 28px;", compactCss);
    }
}
