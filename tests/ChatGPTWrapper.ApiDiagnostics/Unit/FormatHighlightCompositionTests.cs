using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class FormatHighlightCompositionTests
{
    [Theory]
    [InlineData(400, true, 700)]
    [InlineData(500, true, 800)]
    [InlineData(600, true, 900)]
    [InlineData(700, true, 900)]
    [InlineData(400, false, 400)]
    public void ComposeFontWeight_adds_bold_delta_and_clamps(int roleBase, bool highlightBold, int expected)
    {
        Assert.Equal(expected, FormatHighlightComposition.ComposeFontWeight(roleBase, highlightBold));
    }

    [Fact]
    public void Format_css_emits_highlight_bold_weight_delta()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        var css = FormatCssPreview.BuildCssText(format);

        Assert.Contains(
            "--cgw-hl-bold-weight-delta: " + FormatHighlightComposition.BoldWeightDelta,
            css);
    }

    [Fact]
    public void Phrase_highlight_asset_composes_bold_weight_with_role_base()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-phrase-highlights.js");

        Assert.Contains("composeHighlightFontWeight", js);
        Assert.Contains("boldWeightCssDeclaration", js);
        Assert.Contains("--cgw-cv-user-font-weight", js);
        Assert.Contains("--cgw-cv-assistant-font-weight", js);
        Assert.Contains("--cgw-hl-bold-weight-delta", js);
        Assert.DoesNotContain("font-weight:700 !important", js);
    }

    [Fact]
    public void Format_settings_asset_emits_highlight_bold_weight_delta()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-format-settings.js");

        Assert.Contains("--cgw-hl-bold-weight-delta: 300", js);
    }
}
