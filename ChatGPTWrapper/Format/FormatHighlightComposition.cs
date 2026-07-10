namespace ChatGPTWrapper.Format;

/// <summary>
/// How continuous-view format settings combine with phrase-highlight rules.
/// Highlights own color and background; weight and style compose with role defaults.
/// </summary>
public static class FormatHighlightComposition
{
    /// <summary>Weight added when a highlight rule sets Bold (400 → 700).</summary>
    public const int BoldWeightDelta = 300;

    public const int MinWeight = 100;

    public const int MaxWeight = 900;

    public static int ClampWeight(int weight) =>
        Math.Clamp(weight, MinWeight, MaxWeight);

    /// <summary>Role base weight plus highlight bold delta, clamped.</summary>
    public static int ComposeFontWeight(int roleBaseWeight, bool highlightBold) =>
        highlightBold
            ? ClampWeight(roleBaseWeight + BoldWeightDelta)
            : roleBaseWeight;

    public static bool ComposeItalic(bool roleItalic, bool highlightItalic) =>
        roleItalic || highlightItalic;

    public static int ResolveRuleFontWeight(PhraseHighlightRule rule, int roleBaseWeight)
    {
        if (rule.FontWeight is int absolute)
            return ClampWeight(absolute);

        return ComposeFontWeight(roleBaseWeight, rule.Bold);
    }
}
