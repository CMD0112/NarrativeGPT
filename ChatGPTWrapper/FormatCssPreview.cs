namespace ChatGPTWrapper;

internal static class FormatCssPreview
{
    public static string BuildCssText(ContinuousViewFormatSettings settings)
    {
        var borderWidth = settings.ShowSegmentDividers ? "1px" : "0";
        var active = "html[data-cgw-continuous-view=\"1\"] #cgw-continuous-view";
        var pending = "html[data-cgw-continuous-view=\"1\"][data-cgw-cv-pending=\"1\"] #cgw-continuous-view";
        return BuildBlock(active, settings, borderWidth) + BuildBlock(pending, settings, borderWidth);
    }

    private static string BuildBlock(
        string selector,
        ContinuousViewFormatSettings s,
        string borderWidth) =>
        selector + " {\n" +
        "  --cgw-cv-overlay-px: " + s.OverlayPaddingXRem + "rem;\n" +
        "  --cgw-cv-overlay-py: " + s.OverlayPaddingYRem + "rem;\n" +
        "  --cgw-cv-content-max-width: " + s.ContentMaxWidthRem + "rem;\n" +
        "  --cgw-cv-segment-spacing: " + s.SegmentSpacingRem + "rem;\n" +
        "  --cgw-cv-segment-border-width: " + borderWidth + ";\n" +
        "  --cgw-cv-block-margin: " + s.BlockMarginRem + "rem;\n" +
        "  --cgw-cv-prose-p-margin: " + s.ProseParagraphMarginRem + "rem;\n" +
        "  --cgw-cv-user-font-size: " + s.UserFontSizeRem + "rem;\n" +
        "  --cgw-cv-user-line-height: " + s.UserLineHeight + ";\n" +
        "  --cgw-cv-assistant-font-size: " + s.AssistantFontSizeRem + "rem;\n" +
        "  --cgw-cv-assistant-line-height: " + s.AssistantLineHeight + ";\n" +
        "  --cgw-cv-block-letter-spacing: " + s.BlockLetterSpacingEm + "em;\n" +
        "  --cgw-cv-enhanced-prose-line-height: " + s.EnhancedProseLineHeight + ";\n" +
        "  --cgw-cv-enhanced-prose-letter-spacing: " + s.EnhancedProseLetterSpacingEm + "em;\n" +
        "  --cgw-cv-code-font-size: " + s.CodeFontSizeRem + "rem;\n" +
        "  --cgw-cv-code-line-height: " + s.CodeLineHeight + ";\n" +
        "  --cgw-cv-code-block-padding: " + s.CodeBlockPaddingRem + "rem;\n" +
        "  --cgw-cv-heading-margin: " + s.HeadingMarginRem + "rem;\n" +
        "  --cgw-cv-heading-h1: " + s.HeadingH1ScaleRem + "rem;\n" +
        "  --cgw-cv-heading-h2: " + s.HeadingH2ScaleRem + "rem;\n" +
        "  --cgw-cv-heading-h3: " + s.HeadingH3ScaleRem + "rem;\n" +
        "  --cgw-cv-heading-h4: " + s.HeadingH4ScaleRem + "rem;\n" +
        "  --cgw-cv-heading-h5: " + s.HeadingH5ScaleRem + "rem;\n" +
        "  --cgw-cv-heading-h6: " + s.HeadingH6ScaleRem + "rem;\n" +
        "}\n";
}
