using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightFontWeightChoiceTests
{
    [Theory]
    [InlineData(null, false, PhraseHighlightFontWeightMode.MatchRole)]
    [InlineData(null, true, PhraseHighlightFontWeightMode.Bolder)]
    [InlineData(600, false, PhraseHighlightFontWeightMode.Absolute)]
    [InlineData(600, true, PhraseHighlightFontWeightMode.Absolute)]
    public void DecodeMode_maps_storage_to_editor_choice(
        int? fontWeight,
        bool bold,
        PhraseHighlightFontWeightMode expected)
    {
        var rule = new PhraseHighlightRule { FontWeight = fontWeight, Bold = bold };
        Assert.Equal(expected, PhraseHighlightFontWeightChoice.DecodeMode(rule));
    }

    [Fact]
    public void Apply_bolder_clears_absolute_weight()
    {
        var rule = new PhraseHighlightRule { FontWeight = 700, Bold = false };

        PhraseHighlightFontWeightChoice.Apply(rule, PhraseHighlightFontWeightMode.Bolder);

        Assert.Null(rule.FontWeight);
        Assert.True(rule.Bold);
    }

    [Fact]
    public void Apply_absolute_clears_bolder_flag()
    {
        var rule = new PhraseHighlightRule { Bold = true };

        PhraseHighlightFontWeightChoice.Apply(rule, PhraseHighlightFontWeightMode.Absolute, 500);

        Assert.Equal(500, rule.FontWeight);
        Assert.False(rule.Bold);
    }

    [Fact]
    public void TryResolveComboTag_uses_custom_for_non_named_absolute_weight()
    {
        var rule = new PhraseHighlightRule { FontWeight = 550 };

        Assert.Equal(PhraseHighlightFontWeightChoice.CustomTag, PhraseHighlightFontWeightChoice.TryResolveComboTag(rule));
    }

    [Fact]
    public void DescribeResolvedHint_explains_bolder_delta()
    {
        var rule = new PhraseHighlightRule { Bold = true };
        var hint = PhraseHighlightFontWeightChoice.DescribeResolvedHint(rule, 400);

        Assert.Contains("400 + 300", hint);
        Assert.Contains("700", hint);
    }
}
