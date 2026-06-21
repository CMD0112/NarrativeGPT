using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ThemeContrastTests
{
    [Fact]
    public void EnsureReadable_lightens_text_on_dark_background()
    {
        var adjusted = ThemeContrast.EnsureReadable("#333333", "#161618");
        Assert.True(ThemeContrast.IsReadable(adjusted, "#161618"));
        Assert.NotEqual("#333333", adjusted, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureReadable_darkens_text_on_light_background()
    {
        var adjusted = ThemeContrast.EnsureReadable("#CCCCCC", "#F5F5F5");
        Assert.True(ThemeContrast.IsReadable(adjusted, "#F5F5F5"));
    }

    [Fact]
    public void EnforceReadableTokens_fixes_low_contrast_custom_override()
    {
        var settings = new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.DefaultDark,
            CustomOverrides = new Dictionary<string, string>
            {
                ["TextPrimary"] = "#2A2A2A",
                ["BgBase"] = "#1A1A1A",
            },
        };

        var resolved = ThemeApplicationService.ResolveEffectiveTheme(settings);
        Assert.Empty(ThemeContrast.ValidateTokens(resolved.Tokens));
        Assert.True(ThemeContrast.IsReadable(resolved.GetHex("TextPrimary"), resolved.GetHex("BgBase")));
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void All_presets_pass_contrast_validation(string presetId)
    {
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            ActivePresetId = presetId,
        });

        Assert.Empty(ThemeContrast.ValidateTokens(resolved.Tokens));
    }

    [Fact]
    public void EnforceAccentButtonPairs_fixes_white_text_on_white_accent_fill()
    {
        var tokens = ThemeTokenCatalog.CreateDefaultDarkTokens();
        tokens["BgSurface"] = "#FFFFFF";
        tokens["BgBase"] = "#FFFFFF";
        tokens["AccentPrimary"] = "#FFFFFF";
        tokens["AccentPrimaryHover"] = "#FFFFFF";
        tokens["AccentPrimaryPressed"] = "#FFFFFF";
        tokens["TextOnAccent"] = "#FFFFFF";

        ThemeContrast.EnforceAccentButtonPairs(tokens);

        Assert.True(ThemeContrast.IsReadable(tokens["TextOnAccent"], tokens["AccentPrimary"]));
        Assert.NotEqual(
            tokens["TextOnAccent"].ToUpperInvariant(),
            tokens["AccentPrimary"].ToUpperInvariant());
    }

    [Fact]
    public void ResolveEffectiveTheme_keeps_readable_primary_buttons_on_light_presets()
    {
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.HighContrastLight,
        });

        Assert.True(ThemeContrast.IsReadable(
            resolved.GetHex("TextOnAccent"),
            resolved.GetHex("AccentPrimary")));
        Assert.Empty(ThemeContrast.ValidateTokens(resolved.Tokens));
    }

    [Fact]
    public void NeonPurple_resolved_theme_has_readable_accent_button_text()
    {
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(new ThemeSettings
        {
            ActivePresetId = ThemePresetIds.HighContrastNeonPurple,
        });

        Assert.True(ThemeContrast.IsReadable(
            resolved.GetHex("TextOnAccent"),
            resolved.GetHex("AccentPrimaryPressed")));
        Assert.Empty(ThemeContrast.ValidateTokens(resolved.Tokens));
    }

    public static IEnumerable<object[]> PresetIds =>
        ThemePresetIds.AllBuiltIn.Select(id => new object[] { id });
}
