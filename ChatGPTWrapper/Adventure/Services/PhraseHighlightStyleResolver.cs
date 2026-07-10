using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

public static class PhraseHighlightStyleResolver
{
    private static readonly HashSet<string> AllowedTextTransforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "uppercase",
        "lowercase",
        "capitalize",
    };

    public static int ResolveFontWeight(PhraseHighlightRule rule, int roleBaseWeight)
    {
        if (rule.FontWeight is int absolute)
            return FormatHighlightComposition.ClampWeight(absolute);

        if (rule.Bold)
            return FormatHighlightComposition.ComposeFontWeight(roleBaseWeight, true);

        return roleBaseWeight;
    }

    public static bool OverridesFontWeight(PhraseHighlightRule rule) =>
        rule.FontWeight is not null || rule.Bold;

    public static string? BuildTextDecoration(PhraseHighlightRule rule)
    {
        var parts = new List<string>(2);
        if (rule.Underline)
            parts.Add("underline");
        if (rule.Strikethrough)
            parts.Add("line-through");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    public static string FormatStyleSummary(PhraseHighlightRule rule)
    {
        var parts = new List<string>();
        if (!rule.Enabled)
            parts.Add("Off");

        if (rule.FontWeight is int weight)
            parts.Add(PhraseHighlightFontWeightChoice.DescribeForSummary(rule));
        else if (rule.Bold)
            parts.Add("Bolder");

        if (rule.Italic)
            parts.Add("Italic");
        if (rule.Underline)
            parts.Add("U");
        if (rule.Strikethrough)
            parts.Add("S");

        if (rule.FontSizeScale is double scale && Math.Abs(scale - 1.0) > 0.001)
            parts.Add($"{scale:P0}");

        if (rule.LetterSpacingEm is double ls && Math.Abs(ls) > 0.0001)
            parts.Add($"LS{ls:0.###}");

        if (!string.IsNullOrWhiteSpace(rule.TextTransform))
            parts.Add(rule.TextTransform![..1].ToUpperInvariant() + rule.TextTransform[1..].ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(rule.BackgroundColor))
            parts.Add("Bg");

        if (rule.BorderWidthPx is > 0)
            parts.Add("Border");

        if (rule.Opacity is double opacity && opacity < 0.999)
            parts.Add($"{opacity:P0}");

        return parts.Count > 0 ? string.Join(" ", parts) : "—";
    }

    public static void CopyStyleFields(PhraseHighlightRule from, PhraseHighlightRule to)
    {
        to.Color = from.Color;
        to.BackgroundColor = from.BackgroundColor;
        to.FontWeight = from.FontWeight;
        to.Bold = from.Bold;
        to.Italic = from.Italic;
        to.Underline = from.Underline;
        to.Strikethrough = from.Strikethrough;
        to.FontSizeScale = from.FontSizeScale;
        to.LetterSpacingEm = from.LetterSpacingEm;
        to.FontFamily = from.FontFamily;
        to.TextTransform = from.TextTransform;
        to.Opacity = from.Opacity;
        to.BorderColor = from.BorderColor;
        to.BorderWidthPx = from.BorderWidthPx;
        to.BorderRadiusPx = from.BorderRadiusPx;
        to.PaddingXEm = from.PaddingXEm;
        to.PaddingYEm = from.PaddingYEm;
        to.TextShadow = from.TextShadow;
        to.BoxShadow = from.BoxShadow;
        to.Enabled = from.Enabled;
    }

    public static PhraseHighlightRule Sanitize(PhraseHighlightRule rule, string canvasBackground)
    {
        var effectiveBackground = string.IsNullOrWhiteSpace(rule.BackgroundColor)
            ? canvasBackground
            : rule.BackgroundColor!;

        return new PhraseHighlightRule
        {
            Phrase = rule.Phrase.Trim(),
            Color = ThemeContrast.EnsureReadable(rule.Color, effectiveBackground),
            BackgroundColor = SanitizeOptionalColor(rule.BackgroundColor),
            FontWeight = SanitizeFontWeight(rule.FontWeight),
            Bold = rule.Bold && rule.FontWeight is null,
            Italic = rule.Italic,
            Underline = rule.Underline,
            Strikethrough = rule.Strikethrough,
            FontSizeScale = SanitizeFontSizeScale(rule.FontSizeScale),
            LetterSpacingEm = SanitizeLetterSpacing(rule.LetterSpacingEm),
            FontFamily = SanitizeFontFamily(rule.FontFamily),
            TextTransform = SanitizeTextTransform(rule.TextTransform),
            Opacity = SanitizeOpacity(rule.Opacity),
            BorderColor = SanitizeOptionalColor(rule.BorderColor),
            BorderWidthPx = SanitizeBorderWidth(rule.BorderWidthPx),
            BorderRadiusPx = SanitizeBorderRadius(rule.BorderRadiusPx),
            PaddingXEm = SanitizePadding(rule.PaddingXEm),
            PaddingYEm = SanitizePadding(rule.PaddingYEm),
            TextShadow = SanitizeShadow(rule.TextShadow),
            BoxShadow = SanitizeShadow(rule.BoxShadow),
            Enabled = rule.Enabled,
            EntityId = rule.EntityId,
            EntityCategory = rule.EntityCategory,
        };
    }

    private static int? SanitizeFontWeight(int? weight) =>
        weight is null ? null : FormatHighlightComposition.ClampWeight(weight.Value);

    private static double? SanitizeFontSizeScale(double? scale) =>
        scale is null ? null : Math.Clamp(scale.Value, 0.5, 2.5);

    private static double? SanitizeLetterSpacing(double? value) =>
        value is null ? null : Math.Clamp(value.Value, -0.2, 0.5);

    private static double? SanitizeOpacity(double? value) =>
        value is null ? null : Math.Clamp(value.Value, 0.05, 1.0);

    private static int? SanitizeBorderWidth(int? value) =>
        value is null ? null : Math.Clamp(value.Value, 0, 8);

    private static int? SanitizeBorderRadius(int? value) =>
        value is null ? null : Math.Clamp(value.Value, 0, 24);

    private static double? SanitizePadding(double? value) =>
        value is null ? null : Math.Clamp(value.Value, 0, 1.0);

    private static string? SanitizeFontFamily(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? SanitizeTextTransform(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return AllowedTextTransforms.Contains(trimmed)
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static string? SanitizeOptionalColor(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed.TrimStart('#');
    }

    private static string? SanitizeShadow(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
