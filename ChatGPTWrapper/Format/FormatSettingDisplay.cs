namespace ChatGPTWrapper.Format;

public enum FormatSettingTier
{
    Essential,
    Common,
    Advanced,
}

public sealed class FormatSettingDefinition
{
    public required string Key { get; init; }

    public required string DisplayLabel { get; init; }

    public required string HelpText { get; init; }

    public required FormatSettingTier Tier { get; init; }

    public string? CssVariable { get; init; }

    public string SearchText =>
        $"{DisplayLabel} {HelpText} {Key} {CssVariable}".Trim();
}

public static class FormatSettingKeys
{
    public const string ContentMaxWidthRem = nameof(ContinuousViewFormatSettings.ContentMaxWidthRem);
    public const string OverlayPaddingXRem = nameof(ContinuousViewFormatSettings.OverlayPaddingXRem);
    public const string OverlayPaddingYRem = nameof(ContinuousViewFormatSettings.OverlayPaddingYRem);
    public const string SegmentSpacingRem = nameof(ContinuousViewFormatSettings.SegmentSpacingRem);
    public const string BlockMarginRem = nameof(ContinuousViewFormatSettings.BlockMarginRem);
    public const string ProseParagraphMarginRem = nameof(ContinuousViewFormatSettings.ProseParagraphMarginRem);
    public const string SegmentDividerOpacity = nameof(ContinuousViewFormatSettings.SegmentDividerOpacity);
    public const string SegmentBorderRadiusPx = nameof(ContinuousViewFormatSettings.SegmentBorderRadiusPx);
    public const string UserFontSizeRem = nameof(ContinuousViewFormatSettings.UserFontSizeRem);
    public const string UserLineHeight = nameof(ContinuousViewFormatSettings.UserLineHeight);
    public const string UserLetterSpacingEm = nameof(ContinuousViewFormatSettings.UserLetterSpacingEm);
    public const string UserFontWeight = nameof(ContinuousViewFormatSettings.UserFontWeight);
    public const string UserFontFamily = nameof(ContinuousViewFormatSettings.UserFontFamily);
    public const string UserAccentBorderWidthPx = nameof(ContinuousViewFormatSettings.UserAccentBorderWidthPx);
    public const string UserBackgroundOpacity = nameof(ContinuousViewFormatSettings.UserBackgroundOpacity);
    public const string UserIndentRem = nameof(ContinuousViewFormatSettings.UserIndentRem);
    public const string AssistantFontSizeRem = nameof(ContinuousViewFormatSettings.AssistantFontSizeRem);
    public const string AssistantLineHeight = nameof(ContinuousViewFormatSettings.AssistantLineHeight);
    public const string AssistantLetterSpacingEm = nameof(ContinuousViewFormatSettings.AssistantLetterSpacingEm);
    public const string AssistantFontWeight = nameof(ContinuousViewFormatSettings.AssistantFontWeight);
    public const string AssistantFontFamily = nameof(ContinuousViewFormatSettings.AssistantFontFamily);
    public const string AssistantAccentBorderWidthPx = nameof(ContinuousViewFormatSettings.AssistantAccentBorderWidthPx);
    public const string AssistantBackgroundOpacity = nameof(ContinuousViewFormatSettings.AssistantBackgroundOpacity);
    public const string AssistantIndentRem = nameof(ContinuousViewFormatSettings.AssistantIndentRem);
    public const string CodeFontSizeRem = nameof(ContinuousViewFormatSettings.CodeFontSizeRem);
    public const string CodeFontFamily = nameof(ContinuousViewFormatSettings.CodeFontFamily);
    public const string CodeLineHeight = nameof(ContinuousViewFormatSettings.CodeLineHeight);
    public const string CodeBlockPaddingRem = nameof(ContinuousViewFormatSettings.CodeBlockPaddingRem);
    public const string CodeBorderRadiusPx = nameof(ContinuousViewFormatSettings.CodeBorderRadiusPx);
    public const string HeadingMarginRem = nameof(ContinuousViewFormatSettings.HeadingMarginRem);
    public const string HeadingFontFamily = nameof(ContinuousViewFormatSettings.HeadingFontFamily);
    public const string HeadingH1ScaleRem = nameof(ContinuousViewFormatSettings.HeadingH1ScaleRem);
    public const string HeadingH2ScaleRem = nameof(ContinuousViewFormatSettings.HeadingH2ScaleRem);
    public const string HeadingH3ScaleRem = nameof(ContinuousViewFormatSettings.HeadingH3ScaleRem);
    public const string HeadingH4ScaleRem = nameof(ContinuousViewFormatSettings.HeadingH4ScaleRem);
    public const string HeadingH5ScaleRem = nameof(ContinuousViewFormatSettings.HeadingH5ScaleRem);
    public const string HeadingH6ScaleRem = nameof(ContinuousViewFormatSettings.HeadingH6ScaleRem);
    public const string ComposerClearanceMinPx = nameof(ContinuousViewFormatSettings.ComposerClearanceMinPx);
    public const string ComposerClearanceMaxPx = nameof(ContinuousViewFormatSettings.ComposerClearanceMaxPx);
    public const string WeaveEmbedMarginBlockRem = nameof(ContinuousViewFormatSettings.WeaveEmbedMarginBlockRem);
    public const string UserTextColor = nameof(ContinuousViewFormatSettings.UserTextColor);
    public const string UserAccentColor = nameof(ContinuousViewFormatSettings.UserAccentColor);
    public const string UserBackgroundColor = nameof(ContinuousViewFormatSettings.UserBackgroundColor);
    public const string AssistantTextColor = nameof(ContinuousViewFormatSettings.AssistantTextColor);
    public const string AssistantAccentColor = nameof(ContinuousViewFormatSettings.AssistantAccentColor);
    public const string AssistantBackgroundColor = nameof(ContinuousViewFormatSettings.AssistantBackgroundColor);
    public const string LinkColor = nameof(ContinuousViewFormatSettings.LinkColor);
    public const string InlineCodeBackgroundColor = nameof(ContinuousViewFormatSettings.InlineCodeBackgroundColor);
    public const string CodeBackgroundColor = nameof(ContinuousViewFormatSettings.CodeBackgroundColor);
    public const string OverlayBackgroundColor = nameof(ContinuousViewFormatSettings.OverlayBackgroundColor);
    public const string ShowSegmentDividers = nameof(ContinuousViewFormatSettings.ShowSegmentDividers);
    public const string ShowRuledLines = nameof(ContinuousViewFormatSettings.ShowRuledLines);
    public const string ProseGuideClipToText = nameof(ContinuousViewFormatSettings.ProseGuideClipToText);
    public const string RuledLineOpacity = nameof(ContinuousViewFormatSettings.RuledLineOpacity);
    public const string RuledLineStyle = nameof(ContinuousViewFormatSettings.RuledLineStyle);
    public const string RuledBandOpacity = nameof(ContinuousViewFormatSettings.RuledBandOpacity);
    public const string RuledLineThicknessPx = nameof(ContinuousViewFormatSettings.RuledLineThicknessPx);
    public const string RuledMarginTickRatio = nameof(ContinuousViewFormatSettings.RuledMarginTickRatio);
    public const string RuledBandInvertPhase = nameof(ContinuousViewFormatSettings.RuledBandInvertPhase);
    public const string RuledUnderlineDashEm = nameof(ContinuousViewFormatSettings.RuledUnderlineDashEm);
    public const string RuledUnderlineGapEm = nameof(ContinuousViewFormatSettings.RuledUnderlineGapEm);
    public const string RuledZebraContrastRatio = nameof(ContinuousViewFormatSettings.RuledZebraContrastRatio);
    public const string RuledLineColor = nameof(ContinuousViewFormatSettings.RuledLineColor);
    public const string SegmentDividerWidthPx = nameof(ContinuousViewFormatSettings.SegmentDividerWidthPx);
    public const string SegmentDividerStyle = nameof(ContinuousViewFormatSettings.SegmentDividerStyle);
    public const string ShowRoleLabels = nameof(ContinuousViewFormatSettings.ShowRoleLabels);
    public const string WeaveEmbedKind = nameof(ContinuousViewFormatSettings.WeaveEmbedKind);
}

public static class FormatSettingDisplay
{
    private static readonly IReadOnlyDictionary<string, FormatSettingDefinition> ByKey =
        BuildRegistry();

    public static IReadOnlyDictionary<string, FormatSettingDefinition> Registry => ByKey;

    public static FormatSettingDefinition Get(string key) =>
        ByKey.TryGetValue(key, out var def)
            ? def
            : new FormatSettingDefinition
            {
                Key = key,
                DisplayLabel = key,
                HelpText = string.Empty,
                Tier = FormatSettingTier.Advanced,
            };

    public static string GetLabel(string key) => Get(key).DisplayLabel;

    public static string GetHelpText(string key) => Get(key).HelpText;

    public static FormatSettingTier GetTier(string key) => Get(key).Tier;

    public static string GetSearchText(string key) => Get(key).SearchText;

    public static IReadOnlyList<FormatSettingDefinition> EssentialSettings =>
        ByKey.Values.Where(d => d.Tier == FormatSettingTier.Essential).ToList();

    private static Dictionary<string, FormatSettingDefinition> BuildRegistry()
    {
        var entries = new[]
        {
            Def(FormatSettingKeys.ContentMaxWidthRem, "Message width", "How wide each message column may grow. Narrower columns are easier to read for long prose.", FormatSettingTier.Essential, "--cgw-cv-content-max-width"),
            Def(FormatSettingKeys.SegmentSpacingRem, "Space between messages", "Vertical gap between user and assistant turns.", FormatSettingTier.Essential, "--cgw-cv-segment-spacing"),
            Def(FormatSettingKeys.UserFontSizeRem, "Your text size", "Base size for your messages in the transcript.", FormatSettingTier.Essential, "--cgw-cv-user-font-size"),
            Def(FormatSettingKeys.UserLineHeight, "Your line spacing", "Space between lines in your messages.", FormatSettingTier.Essential, "--cgw-cv-user-line-height"),
            Def(FormatSettingKeys.AssistantFontSizeRem, "Assistant text size", "Base size for narrator and assistant prose.", FormatSettingTier.Essential, "--cgw-cv-assistant-font-size"),
            Def(FormatSettingKeys.AssistantLineHeight, "Assistant line spacing", "Space between lines in assistant messages.", FormatSettingTier.Essential, "--cgw-cv-assistant-line-height"),
            Def(FormatSettingKeys.ShowSegmentDividers, "Show message dividers", "Draw a subtle line between turns.", FormatSettingTier.Essential),
            Def(FormatSettingKeys.ShowRuledLines, "Prose reading guides", "Faint line-based or banded backgrounds that make long prose easier to scan.", FormatSettingTier.Essential),
            Def(FormatSettingKeys.ShowRoleLabels, "Show role labels", "Display You / Assistant labels above each segment.", FormatSettingTier.Essential),
            Def(FormatSettingKeys.UserTextColor, "Your text color", "Color of your message text.", FormatSettingTier.Essential, "--cgw-cv-user-text"),
            Def(FormatSettingKeys.AssistantTextColor, "Assistant text color", "Color of assistant and narrator prose.", FormatSettingTier.Essential, "--cgw-cv-assistant-text"),
            Def(FormatSettingKeys.UserAccentColor, "Your accent color", "Left accent stripe color on your messages.", FormatSettingTier.Common),
            Def(FormatSettingKeys.LinkColor, "Link color", "Color of hyperlinks in transcript prose.", FormatSettingTier.Common),
            Def(FormatSettingKeys.InlineCodeBackgroundColor, "Inline code background", "Fill behind inline `code` spans.", FormatSettingTier.Common),
            Def(FormatSettingKeys.CodeBackgroundColor, "Code block background", "Fill behind fenced code blocks.", FormatSettingTier.Advanced),
            Def(FormatSettingKeys.OverlayBackgroundColor, "Overlay background", "Tint behind the entire transcript overlay.", FormatSettingTier.Common),

            Def(FormatSettingKeys.OverlayPaddingXRem, "Side padding", "Horizontal inset for the transcript overlay.", FormatSettingTier.Common, "--cgw-cv-overlay-px"),
            Def(FormatSettingKeys.OverlayPaddingYRem, "Top/bottom padding", "Vertical inset for the transcript overlay.", FormatSettingTier.Common, "--cgw-cv-overlay-py"),
            Def(FormatSettingKeys.BlockMarginRem, "Block margin", "Space around markdown blocks inside a message.", FormatSettingTier.Common, "--cgw-cv-block-margin"),
            Def(FormatSettingKeys.ProseParagraphMarginRem, "Paragraph margin", "Space between paragraphs.", FormatSettingTier.Common, "--cgw-cv-prose-p-margin"),
            Def(FormatSettingKeys.SegmentDividerOpacity, "Divider strength", "How visible segment divider lines appear.", FormatSettingTier.Common, "--cgw-cv-segment-divider-opacity"),
            Def(FormatSettingKeys.SegmentDividerWidthPx, "Divider thickness", "Stroke width of message divider lines.", FormatSettingTier.Common, "--cgw-cv-segment-border-width"),
            Def(FormatSettingKeys.SegmentDividerStyle, "Divider style", "Solid, dashed, or dotted message dividers.", FormatSettingTier.Common, "--cgw-cv-segment-divider-style"),
            Def(FormatSettingKeys.RuledLineStyle, "Guide style", "How reading guides are drawn in prose blocks.", FormatSettingTier.Common),
            Def(FormatSettingKeys.ProseGuideClipToText, "Clip to text width", "Guides stop at the end of each wrapped line instead of spanning the full column.", FormatSettingTier.Common),
            Def(FormatSettingKeys.RuledLineOpacity, "Line strength", "Visibility of ruled lines, underlines, and margin ticks.", FormatSettingTier.Common, "--cgw-cv-ruled-line-opacity"),
            Def(FormatSettingKeys.RuledBandOpacity, "Band strength", "How visible shaded rows or zebra paragraphs appear.", FormatSettingTier.Common, "--cgw-cv-ruled-band-opacity"),
            Def(FormatSettingKeys.RuledLineThicknessPx, "Line thickness", "Stroke width of ruled lines and margin tick marks.", FormatSettingTier.Common, "--cgw-cv-ruled-line-thickness"),
            Def(FormatSettingKeys.RuledMarginTickRatio, "Tick height", "Margin tick length as a fraction of line height.", FormatSettingTier.Common, "--cgw-cv-margin-rail-tick"),
            Def(FormatSettingKeys.RuledBandInvertPhase, "Invert phase", "Start row bands or zebra paragraphs on the alternate phase (clear first).", FormatSettingTier.Common),
            Def(FormatSettingKeys.RuledUnderlineDashEm, "Dash length", "Length of each underline dash relative to font size.", FormatSettingTier.Common, "--cgw-cv-ruled-underline-dash"),
            Def(FormatSettingKeys.RuledUnderlineGapEm, "Dash gap", "Space between underline dashes relative to font size.", FormatSettingTier.Common, "--cgw-cv-ruled-underline-gap"),
            Def(FormatSettingKeys.RuledZebraContrastRatio, "Zebra contrast", "How strong even paragraphs appear relative to odd ones (0.1–1).", FormatSettingTier.Common, "--cgw-cv-zebra-contrast"),
            Def(FormatSettingKeys.RuledLineColor, "Guide color", "Override color for guides (defaults to role accent).", FormatSettingTier.Common, "--cgw-cv-ruled-line-color"),
            Def(FormatSettingKeys.SegmentBorderRadiusPx, "Message corner radius", "Rounded corners on message cards.", FormatSettingTier.Common, "--cgw-cv-segment-border-radius"),
            Def(FormatSettingKeys.UserLetterSpacingEm, "Your letter spacing", "Tracking for your messages.", FormatSettingTier.Common, "--cgw-cv-user-letter-spacing"),
            Def(FormatSettingKeys.UserFontWeight, "Your font weight", "Boldness of your message text. Phrase highlight bold adds on top of this weight.", FormatSettingTier.Essential, "--cgw-cv-user-font-weight"),
            Def(FormatSettingKeys.UserFontFamily, "Your font family", "Typeface for your messages.", FormatSettingTier.Essential),
            Def(FormatSettingKeys.UserAccentBorderWidthPx, "Your accent border", "Left accent stripe width on your messages.", FormatSettingTier.Common, "--cgw-cv-user-accent-border-width"),
            Def(FormatSettingKeys.UserBackgroundOpacity, "Your background tint", "Subtle fill behind your messages.", FormatSettingTier.Common, "--cgw-cv-user-bg-opacity"),
            Def(FormatSettingKeys.UserIndentRem, "Your indent", "Extra left offset for your messages.", FormatSettingTier.Common, "--cgw-cv-user-indent"),
            Def(FormatSettingKeys.AssistantLetterSpacingEm, "Assistant letter spacing", "Tracking for assistant prose.", FormatSettingTier.Common, "--cgw-cv-assistant-letter-spacing"),
            Def(FormatSettingKeys.AssistantFontWeight, "Assistant font weight", "Boldness of assistant text. Phrase highlight bold adds on top of this weight.", FormatSettingTier.Essential, "--cgw-cv-assistant-font-weight"),
            Def(FormatSettingKeys.AssistantFontFamily, "Assistant font family", "Typeface for assistant messages.", FormatSettingTier.Essential),
            Def(FormatSettingKeys.AssistantAccentBorderWidthPx, "Assistant accent border", "Left accent stripe on assistant messages.", FormatSettingTier.Common, "--cgw-cv-assistant-accent-border-width"),
            Def(FormatSettingKeys.AssistantBackgroundOpacity, "Assistant background tint", "Subtle fill behind assistant messages.", FormatSettingTier.Common, "--cgw-cv-assistant-bg-opacity"),
            Def(FormatSettingKeys.AssistantIndentRem, "Assistant indent", "Extra left offset for assistant messages.", FormatSettingTier.Common, "--cgw-cv-assistant-indent"),

            Def(FormatSettingKeys.CodeFontSizeRem, "Code font size", "Size of monospace code text.", FormatSettingTier.Advanced, "--cgw-cv-code-font-size"),
            Def(FormatSettingKeys.CodeFontFamily, "Code font family", "Monospace stack for code blocks.", FormatSettingTier.Advanced),
            Def(FormatSettingKeys.CodeLineHeight, "Code line height", "Line spacing inside code blocks.", FormatSettingTier.Advanced, "--cgw-cv-code-line-height"),
            Def(FormatSettingKeys.CodeBlockPaddingRem, "Code block padding", "Inner padding around code fences.", FormatSettingTier.Advanced, "--cgw-cv-code-block-padding"),
            Def(FormatSettingKeys.CodeBorderRadiusPx, "Code corner radius", "Rounded corners on code blocks.", FormatSettingTier.Advanced, "--cgw-cv-code-border-radius"),
            Def(FormatSettingKeys.HeadingMarginRem, "Heading margin", "Space around headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-margin"),
            Def(FormatSettingKeys.HeadingFontFamily, "Heading font family", "Typeface for markdown headings.", FormatSettingTier.Advanced),
            Def(FormatSettingKeys.HeadingH1ScaleRem, "Heading 1 size", "Scale for H1 headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-h1"),
            Def(FormatSettingKeys.HeadingH2ScaleRem, "Heading 2 size", "Scale for H2 headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-h2"),
            Def(FormatSettingKeys.HeadingH3ScaleRem, "Heading 3 size", "Scale for H3 headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-h3"),
            Def(FormatSettingKeys.HeadingH4ScaleRem, "Heading 4 size", "Scale for H4 headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-h4"),
            Def(FormatSettingKeys.HeadingH5ScaleRem, "Heading 5 size", "Scale for H5 headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-h5"),
            Def(FormatSettingKeys.HeadingH6ScaleRem, "Heading 6 size", "Scale for H6 headings.", FormatSettingTier.Advanced, "--cgw-cv-heading-h6"),
            Def(FormatSettingKeys.ComposerClearanceMinPx, "Composer min clearance", "Minimum space reserved above the composer.", FormatSettingTier.Advanced),
            Def(FormatSettingKeys.ComposerClearanceMaxPx, "Composer max clearance", "Maximum space reserved above the composer.", FormatSettingTier.Advanced),
            Def(FormatSettingKeys.WeaveEmbedMarginBlockRem, "Weave embed margin", "Vertical margin around embedded player lines in Weave mode.", FormatSettingTier.Advanced),
            Def(FormatSettingKeys.WeaveEmbedKind, "Weave embed style", "How player lines are embedded in Weave mode.", FormatSettingTier.Advanced),
        };

        return entries.ToDictionary(d => d.Key, StringComparer.Ordinal);
    }

    private static FormatSettingDefinition Def(
        string key,
        string label,
        string help,
        FormatSettingTier tier,
        string? cssVariable = null) =>
        new()
        {
            Key = key,
            DisplayLabel = label,
            HelpText = help,
            Tier = tier,
            CssVariable = cssVariable,
        };
}
