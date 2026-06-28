using ChatGPTWrapper;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ColorPickerHelperTests
{
    [Fact]
    public void Reading_guide_helpers_include_style_aware_entries()
    {
        var context = ColorPickerContextFactory.ForFormatColor(
            nameof(ContinuousViewFormatSettings.RuledLineColor),
            new ContinuousViewFormatSettings { RuledLineStyle = RuledLineStyle.ParagraphZebra },
            "#101010");

        var helpers = ColorPickerHelperCatalog.GetHelpers(context);

        Assert.Contains(helpers, h => h.Id == "optimize-guide-contrast");
        Assert.Contains(helpers, h => h.Id == "low-glare-guides");
        Assert.All(
            helpers.Where(h => h.Id == "match-prose-ink"),
            h => Assert.Contains("Paragraph zebra", h.Description));
    }

    [Fact]
    public void Optimize_guide_contrast_improves_visible_ink_on_dark_canvas()
    {
        var context = ColorPickerContextFactory.ForFormatColor(
            nameof(ContinuousViewFormatSettings.RuledLineColor),
            new ContinuousViewFormatSettings
            {
                RuledLineOpacity = 12,
                AssistantTextColor = "#303030",
            },
            "#101010");

        var beforeVisible = ColorSpaceConverter.SimulateOpacityOnCanvas("#303030", "#101010", 12);
        var tuned = ColorPickerHelperExecutor.Apply(
            "optimize-guide-contrast",
            context,
            "#303030");
        var afterVisible = ColorSpaceConverter.SimulateOpacityOnCanvas(tuned, "#101010", 12);

        Assert.True(
            ThemeContrast.ContrastRatio(afterVisible, "#101010")
            > ThemeContrast.ContrastRatio(beforeVisible, "#101010"));
    }

    [Fact]
    public void Mix_approximates_css_color_mix_on_opaque_colors()
    {
        var mixed = ColorSpaceConverter.Mix("#FFFFFF", "#000000", 25);
        Assert.Equal("#404040", mixed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveFormatColorBackground_maps_reading_guide_canvas_to_assistant_surface()
    {
        var format = new ContinuousViewFormatSettings
        {
            AssistantBackgroundColor = "#1A1A22",
            OverlayBackgroundColor = "#0F0F12",
        };

        var background = ColorPickerContextResolver.ResolveFormatColorBackground(
            nameof(ContinuousViewFormatSettings.RuledLineColor),
            format);

        Assert.Equal("#1A1A22", background, StringComparer.OrdinalIgnoreCase);
    }
}
