using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightStyleResolverTests
{
    [Theory]
    [InlineData(null, true, 400, 700)]
    [InlineData(650, true, 400, 650)]
    [InlineData(null, false, 500, 500)]
    public void ResolveFontWeight_prefers_absolute_weight_over_bold_bump(
        int? absolute,
        bool bold,
        int roleBase,
        int expected)
    {
        var rule = new PhraseHighlightRule
        {
            FontWeight = absolute,
            Bold = bold,
        };

        Assert.Equal(expected, PhraseHighlightStyleResolver.ResolveFontWeight(rule, roleBase));
    }

    [Fact]
    public void Sanitize_clamps_extended_style_fields()
    {
        var rule = new PhraseHighlightRule
        {
            Phrase = " Mara ",
            Color = "#FFD166",
            FontWeight = 1200,
            FontSizeScale = 4,
            LetterSpacingEm = 2,
            Opacity = 2,
            BorderWidthPx = 20,
            TextTransform = "UPPERCASE",
        };

        var sanitized = PhraseHighlightStyleResolver.Sanitize(rule, "#161618");

        Assert.Equal("Mara", sanitized.Phrase);
        Assert.Equal(900, sanitized.FontWeight);
        Assert.Equal(2.5, sanitized.FontSizeScale);
        Assert.Equal(0.5, sanitized.LetterSpacingEm);
        Assert.Equal(1.0, sanitized.Opacity);
        Assert.Equal(8, sanitized.BorderWidthPx);
        Assert.Equal("uppercase", sanitized.TextTransform);
    }

    [Fact]
    public void CopyStyleFields_copies_typography_and_decoration()
    {
        var source = new PhraseHighlightRule
        {
            Color = "#FF0000",
            FontWeight = 550,
            Underline = true,
            FontSizeScale = 1.1,
            BorderWidthPx = 2,
            TextShadow = "0 1px 1px #000",
        };
        var target = new PhraseHighlightRule();

        PhraseHighlightStyleResolver.CopyStyleFields(source, target);

        Assert.Equal(550, target.FontWeight);
        Assert.True(target.Underline);
        Assert.Equal(1.1, target.FontSizeScale);
        Assert.Equal(2, target.BorderWidthPx);
        Assert.Equal("0 1px 1px #000", target.TextShadow);
    }

    [Fact]
    public void Format_css_still_exposes_bold_weight_delta()
    {
        var css = FormatCssPreview.BuildCssText(ContinuousViewFormatSettings.CreateDefaults());
        Assert.Contains(
            "--cgw-hl-bold-weight-delta: " + FormatHighlightComposition.BoldWeightDelta,
            css);
    }
}
