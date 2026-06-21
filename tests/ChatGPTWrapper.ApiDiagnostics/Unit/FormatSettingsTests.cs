using System.Text.RegularExpressions;
using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatSettingsTests
{
    [Fact]
    public void Format_css_user_and_assistant_font_sizes_diverge()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserFontSizeRem = 0.85;
        format.AssistantFontSizeRem = 1.2;

        var css = FormatCssPreview.BuildCssText(format);

        Assert.Contains("--cgw-cv-user-font-size: 0.85rem", css);
        Assert.Contains("--cgw-cv-assistant-font-size: 1.2rem", css);
        Assert.DoesNotContain("--cgw-cv-block-font-size", css);
    }

    [Fact]
    public void Format_css_preview_emits_runtime_numeric_variables()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        var csharpVars = FormatCssBuilder.ListEmittedCssVariableNames(format)
            .Where(v => FormatTokenCatalog.NumericCssVariables.Contains(v))
            .OrderBy(v => v)
            .ToList();

        var js = WrapperAssetTestHelpers.ReadAsset("continuous-format-settings.js");
        var jsVars = ExtractCssVariablesFromJsBuildBlock(js)
            .Where(v => FormatTokenCatalog.NumericCssVariables.Contains(v))
            .OrderBy(v => v)
            .ToList();

        Assert.Equal(jsVars, csharpVars);
    }

    [Fact]
    public void Preset_round_trip_preserves_role_letter_spacing()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ApplyPreset(FormatPreset.Compact);

        Assert.Equal(0.008, format.UserLetterSpacingEm, 3);
        Assert.Equal(0.008, format.AssistantLetterSpacingEm, 3);

        var clone = format.Clone();
        Assert.Equal(format.UserLetterSpacingEm, clone.UserLetterSpacingEm);
        Assert.Equal(format.AssistantLetterSpacingEm, clone.AssistantLetterSpacingEm);
    }

    [Fact]
    public void Color_override_emitted_in_css_preview()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserAccentColor = "#FF6B6B";

        var css = FormatCssPreview.BuildCssText(format);
        Assert.Contains("--cgw-cv-user-accent: #FF6B6B", css);
    }

    [Fact]
    public void Reset_colors_clears_overrides()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserAccentColor = "#FF6B6B";
        format.LinkColor = "#00FF00";

        format.ResetColors();

        Assert.Null(format.UserAccentColor);
        Assert.Null(format.LinkColor);
    }

    [Fact]
    public void Accent_border_center_adjust_emitted_in_css_preview()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserAccentBorderWidthPx = 8;
        format.AssistantAccentBorderWidthPx = 0;

        var css = FormatCssPreview.BuildCssText(format);

        Assert.Contains("--cgw-cv-user-accent-center-adjust: 2.5px", css);
        Assert.Contains("--cgw-cv-assistant-accent-center-adjust: -1.5px", css);
    }

    [Fact]
    public void Font_family_preset_emitted_in_css_preview()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserFontFamily = FormatFontFamilies.Serif;
        format.AssistantFontFamily = "Palatino, serif";

        var css = FormatCssPreview.BuildCssText(format);

        Assert.Contains("--cgw-cv-user-font-family: Georgia, \"Times New Roman\", serif", css);
        Assert.Contains("--cgw-cv-assistant-font-family: Palatino, serif", css);
    }

    [Fact]
    public void Font_family_inherit_omitted_from_css_preview()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserFontFamily = FormatFontFamilies.Inherit;

        var css = FormatCssPreview.BuildCssText(format);

        Assert.DoesNotContain("--cgw-cv-user-font-family", css);
    }

    [Fact]
    public void Format_font_families_clone_round_trips()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserFontFamily = FormatFontFamilies.Mono;
        format.AssistantFontFamily = "Literata, serif";
        format.CodeFontFamily = FormatFontFamilies.Typewriter;
        format.HeadingFontFamily = FormatFontFamilies.Garamond;

        var clone = format.Clone();

        Assert.Equal(FormatFontFamilies.Mono, clone.UserFontFamily);
        Assert.Equal("Literata, serif", clone.AssistantFontFamily);
        Assert.Equal(FormatFontFamilies.Typewriter, clone.CodeFontFamily);
        Assert.Equal(FormatFontFamilies.Garamond, clone.HeadingFontFamily);
    }

    [Fact]
    public void Code_and_heading_font_family_emitted_in_css_preview()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.CodeFontFamily = FormatFontFamilies.Mono;
        format.HeadingFontFamily = FormatFontFamilies.Literary;

        var css = FormatCssPreview.BuildCssText(format);

        Assert.Contains("--cgw-cv-code-font-family: ui-monospace, \"Cascadia Code\", \"Segoe UI Mono\", Consolas, monospace", css);
        Assert.Contains("--cgw-cv-heading-font-family: \"Literata\", \"Palatino Linotype\", Palatino, Georgia, serif", css);
    }

    [Fact]
    public void Expanded_font_presets_resolve_css_stacks()
    {
        Assert.Contains("Literata", FormatFontFamilies.ResolveCssStack(FormatFontFamilies.Literary));
        Assert.Contains("Charter", FormatFontFamilies.ResolveCssStack(FormatFontFamilies.Charter));
        Assert.Equal("\"Courier New\", Courier, monospace", FormatFontFamilies.ResolveCssStack(FormatFontFamilies.Typewriter));
    }

    [Fact]
    public void ToCustom_stack_quotes_spaced_family_names()
    {
        Assert.Equal("\"Segoe UI\", sans-serif", FormatFontFamilies.ToCustomStack("Segoe UI"));
        Assert.Equal("Arial, sans-serif", FormatFontFamilies.ToCustomStack("Arial"));
    }

    private static IEnumerable<string> ExtractCssVariablesFromJsBuildBlock(string js)
    {
        var match = Regex.Match(js, @"function buildCssBlock[\s\S]*?var lines = \[([\s\S]*?)\];");
        if (!match.Success)
            yield break;

        foreach (Match varMatch in Regex.Matches(match.Groups[1].Value, @"--cgw-cv-[a-z0-9-]+"))
            yield return varMatch.Value;
    }
}
