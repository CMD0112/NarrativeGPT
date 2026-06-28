namespace ChatGPTWrapper.Theme;

using ChatGPTWrapper;

/// <summary>
/// Transcript body colors that automatic highlight assignment must not reuse.
/// </summary>
public static class HighlightColorReservedColors
{
    public static IReadOnlyList<string> Resolve(ResolvedTheme theme, ContinuousViewFormatSettings? format = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var textPrimary = theme.GetHex("TextPrimary");
        format ??= ContinuousViewFormatSettings.CreateDefaults();

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(reserved, format.UserTextColor ?? textPrimary);
        Add(reserved, format.AssistantTextColor ?? textPrimary);
        return reserved.ToList();
    }

    public static bool Conflicts(string? candidate, IEnumerable<string> reserved)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var normalized = Normalize(candidate);
        foreach (var reservedColor in reserved)
        {
            if (string.IsNullOrWhiteSpace(reservedColor))
                continue;

            if (string.Equals(normalized, Normalize(reservedColor), StringComparison.OrdinalIgnoreCase))
                return true;

            if (HighlightColorMath.ArePerceptuallySimilar(normalized, reservedColor, minDistance: 0.06))
                return true;
        }

        return false;
    }

    public static string Avoid(
        string color,
        IEnumerable<string> reserved,
        string canvasBackgroundHex,
        double minContrastRatio)
    {
        if (!Conflicts(color, reserved))
            return color;

        var step = 137.508;
        var candidate = color;
        for (var pass = 0; pass < 48; pass++)
        {
            candidate = HighlightColorMath.RotateHue(candidate, step);
            candidate = ThemeContrast.EnsureReadable(candidate, canvasBackgroundHex, minContrastRatio);
            if (!Conflicts(candidate, reserved))
                return candidate;
        }

        for (var pass = 0; pass < 24; pass++)
        {
            candidate = HighlightColorMath.RelativeLuminance(canvasBackgroundHex) < 0.45
                ? HighlightColorMath.Lighten(candidate, 0.07)
                : HighlightColorMath.Darken(candidate, 0.07);
            candidate = ThemeContrast.EnsureReadable(candidate, canvasBackgroundHex, minContrastRatio);
            if (!Conflicts(candidate, reserved))
                return candidate;
        }

        return candidate;
    }

    private static void Add(ISet<string> target, string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return;

        target.Add(Normalize(color));
    }

    private static string Normalize(string color)
    {
        var trimmed = color.Trim();
        return trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : $"#{trimmed.ToUpperInvariant()}";
    }
}
