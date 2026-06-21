namespace ChatGPTWrapper.Format;

public static class FormatSettingsSanity
{
    public static IReadOnlyList<string> GetWarnings(ContinuousViewFormatSettings format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var warnings = new List<string>();

        if (format.ContentMaxWidthRem is < 10 or > 90)
            warnings.Add("Content max width is extreme; readability may suffer.");

        if (format.UserFontSizeRem is < 0.6 or > 2.5
            || format.AssistantFontSizeRem is < 0.6 or > 2.5)
        {
            warnings.Add("Font size is outside typical reading range.");
        }

        if (format.UserLineHeight is < 0.9 or > 3.5
            || format.AssistantLineHeight is < 0.9 or > 3.5)
        {
            warnings.Add("Line height may make text hard to read.");
        }

        if (format.UserLineHeight <= 0 || format.AssistantLineHeight <= 0)
            warnings.Add("Line height must be greater than zero.");

        if (format.ComposerClearanceMinPx > 0
            && format.ComposerClearanceMaxPx > 0
            && format.ComposerClearanceMinPx > format.ComposerClearanceMaxPx)
        {
            warnings.Add("Composer min clearance exceeds max clearance.");
        }

        return warnings;
    }
}
