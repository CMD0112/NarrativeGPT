namespace ChatGPTWrapper.Theme;

/// <summary>
/// Convenience entry points for cast import color assignment.
/// Prefer <see cref="HighlightColorAssignmentEngine"/> with explicit profile options.
/// </summary>
public static class CastHighlightColorAssignment
{
    public static IReadOnlyList<string> BuildPalette(ResolvedTheme theme, string canvasBackgroundHex) =>
        HighlightColorAssignmentEngine.BuildPalette(
            HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony),
            theme,
            canvasBackgroundHex);

    public static IReadOnlyList<string> BuildPalette(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvasBackgroundHex) =>
        HighlightColorAssignmentEngine.BuildPalette(options, theme, canvasBackgroundHex);

    public static string AssignColor(
        string role,
        string phrase,
        IReadOnlyList<string> palette,
        string canvasBackgroundHex,
        IReadOnlyDictionary<string, string> characterColors,
        ISet<string> usedColors) =>
        AssignColor(
            HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony),
            role,
            phrase,
            palette,
            canvasBackgroundHex,
            characterColors,
            usedColors,
            discoveryIndex: 0,
            theme: null);

    public static string AssignColor(
        HighlightColorAssignmentOptions options,
        string role,
        string phrase,
        IReadOnlyList<string> palette,
        string canvasBackgroundHex,
        IReadOnlyDictionary<string, string> characterColors,
        ISet<string> usedColors,
        int discoveryIndex,
        ResolvedTheme? theme)
    {
        theme ??= ThemeRuntime.Current;
        var context = new HighlightColorAssignmentContext
        {
            Options = options,
            Theme = theme,
            CanvasBackgroundHex = canvasBackgroundHex,
            Palette = palette,
            Role = role,
            Phrase = phrase,
            CharacterColors = characterColors,
            UsedColors = usedColors,
            DiscoveryIndex = discoveryIndex,
        };

        return HighlightColorAssignmentEngine.AssignColor(context);
    }
}
