using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ColorPickerContextResolverTests
{
    [Fact]
    public void ResolveFormatColorBackground_maps_user_text_to_user_background()
    {
        var format = new ContinuousViewFormatSettings
        {
            UserTextColor = "#111111",
            UserBackgroundColor = "#222222",
        };

        var background = ColorPickerContextResolver.ResolveFormatColorBackground(
            nameof(ContinuousViewFormatSettings.UserTextColor),
            format);

        Assert.Equal("#222222", background, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveFormatColorBackground_falls_back_to_overlay_for_background_tokens()
    {
        var format = new ContinuousViewFormatSettings
        {
            OverlayBackgroundColor = "#333333",
        };

        var background = ColorPickerContextResolver.ResolveFormatColorBackground(
            nameof(ContinuousViewFormatSettings.AssistantBackgroundColor),
            format);

        Assert.Equal("#333333", background, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveThemeTokenBackground_maps_text_primary_to_surface()
    {
        var theme = ThemeApplicationService.CreateDefaultSettings();
        var background = ColorPickerContextResolver.ResolveThemeTokenBackground("TextPrimary", theme);
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(theme);

        Assert.Equal(resolved.GetHex("BgSurface"), background, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveHighlightTextBackground_prefers_rule_background()
    {
        var background = ColorPickerContextResolver.ResolveHighlightTextBackground(
            "#ABCDEF",
            userSegmentBackground: "#111111",
            assistantSegmentBackground: "#222222",
            fallbackCanvas: "#333333");

        Assert.Equal("#ABCDEF", background, StringComparer.OrdinalIgnoreCase);
    }
}
