using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Format;

public enum FormatReadabilitySeverity
{
    Info,
    Warning,
    Error,
}

public sealed record FormatReadabilityWarning(
    string Message,
    string SettingKey,
    FormatReadabilitySeverity Severity);

public static class FormatReadabilityAnalyzer
{
    private const double MaxComfortableWidthRem = 48;
    private const double ApproxCharsPerRem = 2.2;

    public static IReadOnlyList<FormatReadabilityWarning> Analyze(
        ContinuousViewFormatSettings format,
        IReadOnlyList<PhraseHighlightRule>? highlights = null,
        bool phraseHighlightsEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(format);

        var warnings = new List<FormatReadabilityWarning>();

        AddBasicSanityWarnings(warnings, format);

        if (format.ContentMaxWidthRem > MaxComfortableWidthRem)
        {
            var approxChars = (int)Math.Round(format.ContentMaxWidthRem * ApproxCharsPerRem);
            warnings.Add(new FormatReadabilityWarning(
                $"Message width is wide (~{approxChars} characters). Narrower columns improve long-form reading.",
                FormatSettingKeys.ContentMaxWidthRem,
                FormatReadabilitySeverity.Info));
        }

        AddTextContrastWarning(
            warnings,
            format.UserTextColor,
            ResolveSegmentBackground(format.UserBackgroundColor, format.UserBackgroundOpacity),
            "Your text may be hard to read against the message background.",
            FormatSettingKeys.UserTextColor);

        AddTextContrastWarning(
            warnings,
            format.AssistantTextColor,
            ResolveSegmentBackground(format.AssistantBackgroundColor, format.AssistantBackgroundOpacity),
            "Assistant text may be hard to read against the message background.",
            FormatSettingKeys.AssistantTextColor);

        if (phraseHighlightsEnabled && highlights is { Count: > 0 })
            AddHighlightContrastWarnings(warnings, format, highlights);

        return warnings
            .OrderByDescending(w => w.Severity)
            .ThenBy(w => w.SettingKey, StringComparer.Ordinal)
            .Take(5)
            .ToList();
    }

    public static IReadOnlyList<string> GetWarningMessages(
        ContinuousViewFormatSettings format,
        IReadOnlyList<PhraseHighlightRule>? highlights = null,
        bool phraseHighlightsEnabled = false) =>
        Analyze(format, highlights, phraseHighlightsEnabled)
            .Select(w => w.Message)
            .ToList();

    private static void AddTextContrastWarning(
        List<FormatReadabilityWarning> warnings,
        string? textColor,
        string backgroundHex,
        string message,
        string settingKey)
    {
        var fg = ResolveColorHex(textColor, "#EDEDF0");
        if (!ThemeContrast.IsReadable(fg, backgroundHex))
        {
            warnings.Add(new FormatReadabilityWarning(message, settingKey, FormatReadabilitySeverity.Warning));
        }
    }

    private static void AddHighlightContrastWarnings(
        List<FormatReadabilityWarning> warnings,
        ContinuousViewFormatSettings format,
        IReadOnlyList<PhraseHighlightRule> highlights)
    {
        var proseBg = ResolveSegmentBackground(format.AssistantBackgroundColor, format.AssistantBackgroundOpacity);
        var proseFg = ResolveColorHex(format.AssistantTextColor, "#EDEDF0");

        foreach (var rule in highlights.Take(8))
        {
            if (string.IsNullOrWhiteSpace(rule.Color))
                continue;

            var highlightBg = ResolveColorHex(rule.BackgroundColor, "#333333");
            if (!ThemeContrast.IsReadable(rule.Color, highlightBg, ThemeContrast.MinMutedRatio)
                || !ThemeContrast.IsReadable(rule.Color, proseBg, ThemeContrast.MinMutedRatio))
            {
                warnings.Add(new FormatReadabilityWarning(
                    $"Highlight rule \"{rule.Phrase}\" may be unreadable on assistant prose.",
                    FormatSettingKeys.AssistantTextColor,
                    FormatReadabilitySeverity.Warning));
                break;
            }

            if (!ThemeContrast.IsReadable(proseFg, highlightBg, ThemeContrast.MinMutedRatio))
            {
                warnings.Add(new FormatReadabilityWarning(
                    "A highlight background may clash with surrounding assistant text.",
                    FormatSettingKeys.AssistantTextColor,
                    FormatReadabilitySeverity.Info));
                break;
            }
        }
    }

    private static void AddBasicSanityWarnings(
        List<FormatReadabilityWarning> warnings,
        ContinuousViewFormatSettings format)
    {
        if (format.ContentMaxWidthRem is < 10 or > 90)
        {
            warnings.Add(new FormatReadabilityWarning(
                "Content max width is extreme; readability may suffer.",
                FormatSettingKeys.ContentMaxWidthRem,
                FormatReadabilitySeverity.Warning));
        }

        if (format.UserFontSizeRem is < 0.6 or > 2.5
            || format.AssistantFontSizeRem is < 0.6 or > 2.5)
        {
            warnings.Add(new FormatReadabilityWarning(
                "Font size is outside typical reading range.",
                FormatSettingKeys.AssistantFontSizeRem,
                FormatReadabilitySeverity.Warning));
        }

        if (format.UserLineHeight is < 0.9 or > 3.5
            || format.AssistantLineHeight is < 0.9 or > 3.5)
        {
            warnings.Add(new FormatReadabilityWarning(
                "Line height may make text hard to read.",
                FormatSettingKeys.AssistantLineHeight,
                FormatReadabilitySeverity.Warning));
        }

        if (format.UserLineHeight <= 0 || format.AssistantLineHeight <= 0)
        {
            warnings.Add(new FormatReadabilityWarning(
                "Line height must be greater than zero.",
                FormatSettingKeys.AssistantLineHeight,
                FormatReadabilitySeverity.Error));
        }

        if (format.ComposerClearanceMinPx > 0
            && format.ComposerClearanceMaxPx > 0
            && format.ComposerClearanceMinPx > format.ComposerClearanceMaxPx)
        {
            warnings.Add(new FormatReadabilityWarning(
                "Composer min clearance exceeds max clearance.",
                FormatSettingKeys.ComposerClearanceMinPx,
                FormatReadabilitySeverity.Warning));
        }
    }

    private static string ResolveSegmentBackground(string? explicitColor, double opacityPercent)
    {
        if (!string.IsNullOrWhiteSpace(explicitColor))
            return NormalizeHex(explicitColor);

        if (opacityPercent > 0)
            return BlendHex("#161618", "#EDEDF0", opacityPercent / 100d);

        return "#161618";
    }

    private static string ResolveColorHex(string? color, string fallback) =>
        string.IsNullOrWhiteSpace(color) ? fallback : NormalizeHex(color);

    private static string NormalizeHex(string hex)
    {
        if (!hex.StartsWith('#'))
            hex = "#" + hex;
        return hex.Length is 7 or 9 ? hex : "#EDEDF0";
    }

    private static string BlendHex(string bgHex, string fgHex, double fgAmount)
    {
        var bg = ParseRgb(bgHex);
        var fg = ParseRgb(fgHex);
        var t = Math.Clamp(fgAmount, 0, 1);
        var r = (byte)Math.Round(bg.R + (fg.R - bg.R) * t);
        var g = (byte)Math.Round(bg.G + (fg.G - bg.G) * t);
        var b = (byte)Math.Round(bg.B + (fg.B - bg.B) * t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8)
            hex = hex[..6];
        if (hex.Length != 6)
            return (22, 22, 24);

        return (
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16));
    }
}
