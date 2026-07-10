using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatConditionalUiTests
{
    [Fact]
    public void Band_style_shows_band_controls_only()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ShowRuledLines = true;
        format.RuledLineStyle = RuledLineStyle.Band;

        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledBandOpacity, format));
        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledBandInvertPhase, format));
        Assert.False(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledLineThicknessPx, format));
    }

    [Fact]
    public void Underline_style_exposes_dash_and_thickness_controls()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ShowRuledLines = true;
        format.RuledLineStyle = RuledLineStyle.Underline;

        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledUnderlineDashEm, format));
        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledUnderlineGapEm, format));
        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledLineThicknessPx, format));
        Assert.False(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledBandOpacity, format));
    }

    [Fact]
    public void Paragraph_zebra_exposes_contrast_and_invert()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ShowRuledLines = true;
        format.RuledLineStyle = RuledLineStyle.ParagraphZebra;

        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledZebraContrastRatio, format));
        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledBandInvertPhase, format));
        Assert.False(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.ProseGuideClipToText, format));
    }

    [Fact]
    public void Margin_rail_exposes_tick_ratio()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ShowRuledLines = true;
        format.RuledLineStyle = RuledLineStyle.MarginRail;

        Assert.True(FormatConditionalUi.IsProseGuideSettingVisible(FormatSettingKeys.RuledMarginTickRatio, format));
    }

    [Fact]
    public void Background_color_hidden_when_opacity_zero()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserBackgroundOpacity = 0;

        Assert.False(FormatConditionalUi.IsColorSettingVisible(nameof(ContinuousViewFormatSettings.UserBackgroundColor), format));

        format.UserBackgroundOpacity = 12;
        Assert.True(FormatConditionalUi.IsColorSettingVisible(nameof(ContinuousViewFormatSettings.UserBackgroundColor), format));
    }

    [Fact]
    public void Accent_colors_hidden_when_stripe_width_zero()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserAccentBorderWidthPx = 0;

        Assert.False(FormatConditionalUi.IsColorSettingVisible(nameof(ContinuousViewFormatSettings.UserAccentColor), format));
    }
}
