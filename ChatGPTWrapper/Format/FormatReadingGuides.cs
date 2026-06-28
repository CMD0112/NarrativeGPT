namespace ChatGPTWrapper.Format;

public enum RuledLineStyle
{
    Line,
    Band,
    ParagraphZebra,
    Underline,
    MarginRail,
}

public enum SegmentDividerStyle
{
    Solid,
    Dashed,
    Dotted,
}

internal static class FormatReadingGuides
{
    public static string ToCssDividerStyle(SegmentDividerStyle style) =>
        style switch
        {
            SegmentDividerStyle.Dashed => "dashed",
            SegmentDividerStyle.Dotted => "dotted",
            _ => "solid",
        };

    public static string ToRuledStyleAttribute(RuledLineStyle style) =>
        style switch
        {
            RuledLineStyle.Band => "band",
            RuledLineStyle.ParagraphZebra => "paragraph-zebra",
            RuledLineStyle.Underline => "underline",
            RuledLineStyle.MarginRail => "margin-rail",
            _ => "line",
        };

    public static RuledLineStyle ParseRuledLineStyle(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "band" or "1" => RuledLineStyle.Band,
            "paragraph-zebra" or "paragraphzebra" or "2" => RuledLineStyle.ParagraphZebra,
            "underline" or "3" => RuledLineStyle.Underline,
            "margin-rail" or "marginrail" or "4" => RuledLineStyle.MarginRail,
            _ => RuledLineStyle.Line,
        };

    public static SegmentDividerStyle ParseSegmentDividerStyle(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "dashed" => SegmentDividerStyle.Dashed,
            "dotted" => SegmentDividerStyle.Dotted,
            _ => SegmentDividerStyle.Solid,
        };
}
