namespace ChatGPTWrapper.Theme;

public static class HighlightColorGroupedAssignment
{
    public static string AssignColor(
      HighlightColorAssignmentOptions options,
      HighlightColorGroupingProfile? groupingProfile,
      PhraseHighlightRule? rule,
      string role,
      string phrase,
      IReadOnlyList<string> palette,
      string canvasBackgroundHex,
      IReadOnlyDictionary<string, string> characterColors,
      HighlightColorAssignmentState state,
      int discoveryIndex,
      ResolvedTheme? theme = null,
      string? fallbackColor = null,
      IReadOnlyList<string>? reservedForegroundColors = null)
    {
        theme ??= ThemeRuntime.Current;
        var reserved = reservedForegroundColors ?? [];
        var resolution = HighlightColorGroupingResolver.Resolve(groupingProfile, rule, role, phrase);

        if (resolution.IsExcluded)
            return fallbackColor ?? rule?.Color?.Trim() ?? "#FFD166";

        if (resolution.ShareColorWithinGroup
            && !string.IsNullOrWhiteSpace(resolution.GroupKey)
            && state.TryGetSharedGroupColor(resolution.GroupKey, out var sharedColor))
        {
            return sharedColor;
        }

        var scopedUsedColors = state.GetScopedUsedColors(resolution);
        var color = CastHighlightColorAssignment.AssignColor(
            options,
            role,
            phrase,
            palette,
            canvasBackgroundHex,
            characterColors,
            scopedUsedColors,
            discoveryIndex,
            theme,
            reserved);

        state.RegisterAssignedColor(color, resolution);
        return color;
    }
}
