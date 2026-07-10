using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatReadingGuidesTests
{
    [Theory]
    [InlineData(SegmentDividerStyle.Solid, "solid")]
    [InlineData(SegmentDividerStyle.Dashed, "dashed")]
    [InlineData(SegmentDividerStyle.Dotted, "dotted")]
    public void ToCssDividerStyle_maps_expected_values(SegmentDividerStyle style, string expected) =>
        Assert.Equal(expected, FormatReadingGuides.ToCssDividerStyle(style));

    [Theory]
    [InlineData(RuledLineStyle.Line, "line")]
    [InlineData(RuledLineStyle.Band, "band")]
    [InlineData(RuledLineStyle.ParagraphZebra, "paragraph-zebra")]
    [InlineData(RuledLineStyle.Underline, "underline")]
    [InlineData(RuledLineStyle.MarginRail, "margin-rail")]
    public void ToRuledStyleAttribute_maps_expected_values(RuledLineStyle style, string expected) =>
        Assert.Equal(expected, FormatReadingGuides.ToRuledStyleAttribute(style));

    [Theory]
    [InlineData("paragraph-zebra", RuledLineStyle.ParagraphZebra)]
    [InlineData("underline", RuledLineStyle.Underline)]
    [InlineData("margin-rail", RuledLineStyle.MarginRail)]
    public void ParseRuledLineStyle_maps_new_styles(string raw, RuledLineStyle expected) =>
        Assert.Equal(expected, FormatReadingGuides.ParseRuledLineStyle(raw));
}
