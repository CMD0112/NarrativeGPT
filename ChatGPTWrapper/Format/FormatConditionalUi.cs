namespace ChatGPTWrapper.Format;

/// <summary>
/// Visibility rules for format dialog controls that depend on other settings.
/// </summary>
public static class FormatConditionalUi
{
    public static bool IsGuidesEnabled(ContinuousViewFormatSettings format) =>
        format.ShowRuledLines;

    public static bool IsDividerDetailVisible(ContinuousViewFormatSettings format) =>
        format.ShowSegmentDividers;

    public static bool IsProseGuideSettingVisible(string settingKey, ContinuousViewFormatSettings format)
    {
        if (!format.ShowRuledLines)
            return false;

        return format.RuledLineStyle switch
        {
            RuledLineStyle.Line => settingKey is FormatSettingKeys.RuledLineOpacity
                or FormatSettingKeys.RuledLineThicknessPx
                or FormatSettingKeys.RuledLineColor
                or FormatSettingKeys.ProseGuideClipToText,

            RuledLineStyle.Band => settingKey is FormatSettingKeys.RuledBandOpacity
                or FormatSettingKeys.RuledLineColor
                or FormatSettingKeys.ProseGuideClipToText
                or FormatSettingKeys.RuledBandInvertPhase,

            RuledLineStyle.ParagraphZebra => settingKey is FormatSettingKeys.RuledBandOpacity
                or FormatSettingKeys.RuledLineColor
                or FormatSettingKeys.RuledBandInvertPhase
                or FormatSettingKeys.RuledZebraContrastRatio,

            RuledLineStyle.Underline => settingKey is FormatSettingKeys.RuledLineOpacity
                or FormatSettingKeys.RuledLineThicknessPx
                or FormatSettingKeys.RuledUnderlineDashEm
                or FormatSettingKeys.RuledUnderlineGapEm
                or FormatSettingKeys.RuledLineColor
                or FormatSettingKeys.ProseGuideClipToText,

            RuledLineStyle.MarginRail => settingKey is FormatSettingKeys.RuledLineOpacity
                or FormatSettingKeys.RuledLineThicknessPx
                or FormatSettingKeys.RuledMarginTickRatio
                or FormatSettingKeys.RuledLineColor
                or FormatSettingKeys.ProseGuideClipToText,

            _ => false,
        };
    }

    public static string ProseGuideStyleDescription(RuledLineStyle style) =>
        style switch
        {
            RuledLineStyle.Line =>
                "Solid horizontal rules aligned to each text line. Thickness and strength control the bar; clip limits rules to the text width.",
            RuledLineStyle.Band =>
                "Alternating shaded rows per line of text. Use invert phase if the first row should be clear. Clip limits bands to wrapped line width.",
            RuledLineStyle.ParagraphZebra =>
                "Alternating background on whole paragraphs (odd/even blocks). Adjust contrast for even rows; invert phase swaps which paragraphs are stronger.",
            RuledLineStyle.Underline =>
                "Dashed baselines under each line. Dash length, gap, and thickness control the pattern; clip shortens dashes to the text width.",
            RuledLineStyle.MarginRail =>
                "Short ticks in the left margin per line. Tick height is a fraction of line height; thickness sets tick width.",
            _ => string.Empty,
        };

    public static bool IsColorSettingVisible(string settingsProperty, ContinuousViewFormatSettings format) =>
        settingsProperty switch
        {
            nameof(ContinuousViewFormatSettings.SegmentDividerColor) => format.ShowSegmentDividers,
            nameof(ContinuousViewFormatSettings.RuledLineColor) => format.ShowRuledLines,
            nameof(ContinuousViewFormatSettings.UserBackgroundColor) => format.UserBackgroundOpacity > 0,
            nameof(ContinuousViewFormatSettings.AssistantBackgroundColor) => format.AssistantBackgroundOpacity > 0,
            nameof(ContinuousViewFormatSettings.UserAccentColor)
                or nameof(ContinuousViewFormatSettings.UserBorderColor) => format.UserAccentBorderWidthPx > 0,
            nameof(ContinuousViewFormatSettings.AssistantAccentColor)
                or nameof(ContinuousViewFormatSettings.AssistantBorderColor) =>
                format.AssistantAccentBorderWidthPx > 0,
            _ => true,
        };

    public static bool IsRoleAppearanceSliderVisible(string settingKey, ContinuousViewFormatSettings format) =>
        settingKey switch
        {
            FormatSettingKeys.UserIndentRem => format.UserAccentBorderWidthPx > 0
                || format.UserBackgroundOpacity > 0,
            FormatSettingKeys.AssistantIndentRem => format.AssistantAccentBorderWidthPx > 0
                || format.AssistantBackgroundOpacity > 0,
            _ => true,
        };

    public static bool IsComposerClearanceVisible(UiChromeSettings chrome) =>
        chrome.IsTranscriptOverlayActive;

    public static bool IsWeaveSectionVisible(UiChromeSettings chrome) =>
        chrome.TranscriptViewMode == TranscriptViewMode.Weave;

    public static bool IsPhraseHighlightEditorVisible(UiChromeSettings chrome) =>
        chrome.PhraseHighlightsEnabled;

    public static bool IsExpandHiddenContextVisible(UiChromeSettings chrome) =>
        chrome.HideContextTagsInThread;
}
