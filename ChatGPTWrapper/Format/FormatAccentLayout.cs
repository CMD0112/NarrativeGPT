namespace ChatGPTWrapper.Format;

internal static class FormatAccentLayout
{
    /// <summary>Default accent border width (px) used as the visual centering reference.</summary>
    public const double ReferenceAccentBorderWidthPx = 3;

    /// <summary>Interactive segment gutter (rem) paired with negative margin in CSS.</summary>
    public const double InteractiveGutterRem = 0.35;

    public static double CenterAdjustPx(double accentBorderWidthPx) =>
        (accentBorderWidthPx - ReferenceAccentBorderWidthPx) / 2;
}
