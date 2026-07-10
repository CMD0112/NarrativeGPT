namespace ChatGPTWrapper.Theme;

public sealed class HighlightColorAssignmentContext
{
    public required HighlightColorAssignmentOptions Options { get; init; }

    public required ResolvedTheme Theme { get; init; }

    public required string CanvasBackgroundHex { get; init; }

    public required IReadOnlyList<string> Palette { get; init; }

    public required string Role { get; init; }

    public required string Phrase { get; init; }

    public required IReadOnlyDictionary<string, string> CharacterColors { get; init; }

    public required ISet<string> UsedColors { get; init; }

    public int DiscoveryIndex { get; init; }

    public int AssignmentSalt { get; init; }

    public IReadOnlyList<string> ReservedForegroundColors { get; init; } = [];
}

/// <summary>Builds palettes and assigns phrase highlight colors from theme + profile options.</summary>
public static class HighlightColorAssignmentEngine
{
    public static string ResolveCanvas(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string? canvasOverride = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(theme);

        if (!string.IsNullOrWhiteSpace(canvasOverride))
            return canvasOverride.Trim();

        return theme.GetHex(HighlightColorCatalog.CanvasTokenKey(options.CanvasSource));
    }

    public static IReadOnlyList<string> BuildPalette(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvasBackgroundHex) =>
        BuildPalette(options, theme, canvasBackgroundHex, minimumDistinctColors: null);

    public static IReadOnlyList<string> BuildPalette(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvasBackgroundHex,
        int? minimumDistinctColors,
        IReadOnlyList<string>? reservedForegroundColors = null) =>
        BuildPaletteInternal(options, theme, canvasBackgroundHex, minimumDistinctColors, reservedForegroundColors ?? []);

    private static IReadOnlyList<string> BuildPaletteInternal(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvasBackgroundHex,
        int? minimumDistinctColors,
        IReadOnlyList<string> reservedForegroundColors)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(theme);

        var canvas = canvasBackgroundHex;
        var minRatio = Math.Max(3.0, options.MinContrastRatio);
        var palette = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedCount = ResolveGeneratedColorCount(options, minimumDistinctColors);

        void AddReadable(string hex, bool requireDistinct = true) =>
            AddDistinctReadable(hex, palette, seen, canvas, minRatio, requireDistinct, reservedForegroundColors);

        switch (options.PaletteSource)
        {
            case HighlightPaletteSource.FixedClassic:
                foreach (var hex in HighlightColorCatalog.ClassicFixed)
                    AddReadable(hex, requireDistinct: false);
                AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                    includeSemanticSeeds: false, resolvedCount, reservedForegroundColors);
                break;

            case HighlightPaletteSource.FixedEditorSwatches:
                foreach (var hex in HighlightColorCatalog.EditorSwatches)
                    AddReadable(hex, requireDistinct: false);
                AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                    includeSemanticSeeds: false, resolvedCount, reservedForegroundColors);
                break;

            case HighlightPaletteSource.CustomSeeds:
                foreach (var hex in options.CustomSeedColors)
                {
                    if (!string.IsNullOrWhiteSpace(hex))
                        AddReadable(hex);
                }

                AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                    includeSemanticSeeds: false, resolvedCount, reservedForegroundColors);
                break;

            case HighlightPaletteSource.ThemeAccentOnly:
                AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                    includeSemanticSeeds: false, resolvedCount, reservedForegroundColors);
                break;

            case HighlightPaletteSource.EvenHueWheel:
                foreach (var hex in options.CustomSeedColors)
                {
                    if (!string.IsNullOrWhiteSpace(hex))
                        AddReadable(hex);
                }

                AddEvenHueWheel(options, theme, canvas, minRatio, palette, seen, resolvedCount, reservedForegroundColors);
                break;

            case HighlightPaletteSource.GoldenAngle:
            case HighlightPaletteSource.ThemeSemantic:
            default:
                if (minimumDistinctColors is > 0)
                {
                    if (options.PaletteSource == HighlightPaletteSource.ThemeSemantic)
                    {
                        AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                            includeSemanticSeeds: true,
                            resolvedCount: Math.Min(6, resolvedCount),
                            reservedForegroundColors);
                    }

                    AddEvenHueWheel(options, theme, canvas, minRatio, palette, seen, resolvedCount, reservedForegroundColors);
                }
                else
                {
                    AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                        includeSemanticSeeds: options.PaletteSource == HighlightPaletteSource.ThemeSemantic,
                        resolvedCount,
                        reservedForegroundColors);
                }

                break;
        }

        if (palette.Count == 0)
        {
            var fallback = AvoidReserved(
                ThemeContrast.EnsureReadable(theme.GetHex("AccentPrimary"), canvas, minRatio),
                reservedForegroundColors,
                canvas,
                minRatio);
            palette.Add(fallback);
        }

        return palette;
    }

    /// <summary>Resolves how many distinct generated hues to target (dynamic when <see cref="HighlightColorAssignmentOptions.GeneratedColorCount"/> is 0).</summary>
    public static int ResolveGeneratedColorCount(
        HighlightColorAssignmentOptions options,
        int? minimumDistinctColors = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baseCount = options.GeneratedColorCount <= 0
            ? HighlightColorCatalog.DefaultDynamicGeneratedColors
            : options.GeneratedColorCount;

        baseCount = Math.Clamp(baseCount, 4, HighlightColorCatalog.MaxGeneratedColors);

        if (minimumDistinctColors is null or <= 0)
            return baseCount;

        var scaled = minimumDistinctColors.Value + HighlightColorCatalog.DynamicPaletteHeadroom;
        return Math.Clamp(
            Math.Max(baseCount, scaled),
            HighlightColorCatalog.MinGeneratedColors,
            HighlightColorCatalog.MaxGeneratedColors);
    }

    public static string AssignColor(HighlightColorAssignmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var reserved in context.ReservedForegroundColors)
            context.UsedColors.Add(reserved);

        if (context.Palette.Count == 0)
            return Ensure(context, "#FFD166");

        if (context.Role.Equals("Player", StringComparison.OrdinalIgnoreCase))
            return AssignPlayer(context);

        if (TryParseAliasParent(context.Role, out var parentName))
            return AssignAlias(context, parentName);

        return context.Options.AssignmentStrategy switch
        {
            HighlightAssignmentStrategy.Sequential => PickSequential(context),
            HighlightAssignmentStrategy.StableHash => PickStable(context, roleBucket: null),
            HighlightAssignmentStrategy.RoleBuckets => PickOptimalDistinct(context, ResolveRoleBucket(context.Role)),
            HighlightAssignmentStrategy.OptimalDistinct => PickOptimalDistinct(context, roleBucket: null),
            HighlightAssignmentStrategy.RoleBased => PickOptimalDistinct(context, roleBucket: null),
            _ => PickOptimalDistinct(context, roleBucket: null),
        };
    }

    private static void AddDistinctReadable(
        string hex,
        List<string> palette,
        HashSet<string> seen,
        string canvas,
        double minRatio,
        bool requireDistinct,
        IReadOnlyList<string> reservedForegroundColors)
    {
        var readable = ThemeContrast.EnsureReadable(hex, canvas, minRatio);
        readable = AvoidReserved(readable, reservedForegroundColors, canvas, minRatio);
        if (HighlightColorReservedColors.Conflicts(readable, reservedForegroundColors))
            return;

        if (requireDistinct && palette.Any(existing => HighlightColorMath.ArePerceptuallySimilar(readable, existing)))
            return;

        if (seen.Add(readable))
            palette.Add(readable);
    }

    private static bool TryAddGeneratedHue(
        double hue,
        double saturation,
        double lightness,
        List<string> palette,
        HashSet<string> seen,
        string canvas,
        double minRatio,
        int globalIndex,
        IReadOnlyList<string> reservedForegroundColors)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var h = (hue + globalIndex * 11.7 + attempt * 23.3) % 360.0;
            var band = (globalIndex + attempt) % 5;
            var sat = Math.Clamp(saturation + (band - 2) * 0.045, 0.35, 0.95);
            var light = Math.Clamp(lightness + (band - 2) * 0.035, 0.32, 0.78);
            var readable = ThemeContrast.EnsureReadable(
                HighlightColorMath.HslToHex(h, sat, light),
                canvas,
                minRatio);
            readable = AvoidReserved(readable, reservedForegroundColors, canvas, minRatio);
            if (HighlightColorReservedColors.Conflicts(readable, reservedForegroundColors))
                continue;

            var minDistance = attempt < 28
                ? HighlightColorMath.MinPaletteDistinctDistance
                : HighlightColorMath.MinPaletteDistinctDistance * 0.65;

            if (palette.Any(existing => HighlightColorMath.PerceptualDistance(readable, existing) < minDistance))
                continue;

            if (!seen.Add(readable))
                continue;

            palette.Add(readable);
            return true;
        }

        return false;
    }

    private static void AddThemeAccentHues(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvas,
        double minRatio,
        List<string> palette,
        HashSet<string> seen,
        bool includeSemanticSeeds,
        int resolvedCount,
        IReadOnlyList<string> reservedForegroundColors)
    {
        void AddReadable(string hex) =>
            AddDistinctReadable(hex, palette, seen, canvas, minRatio, requireDistinct: true, reservedForegroundColors);

        if (includeSemanticSeeds)
        {
            foreach (var key in HighlightColorCatalog.ThemeSeedTokenKeys)
            {
                if (theme.Tokens.TryGetValue(key, out var hex) && !string.IsNullOrWhiteSpace(hex))
                    AddReadable(hex);
            }
        }

        var anchorHex = theme.GetHex(HighlightColorCatalog.AnchorTokenKey(options.HueAnchor));
        if (!HighlightColorMath.TryRgbToHsl(anchorHex, out var accentHue, out _, out _))
            return;

        var isDarkCanvas = HighlightColorMath.RelativeLuminance(canvas) < 0.45;
        var saturation = options.Saturation ?? (isDarkCanvas ? 0.68 : 0.58);
        var lightness = options.Lightness ?? (isDarkCanvas ? 0.62 : 0.40);
        var count = resolvedCount;
        var step = options.HueStepDegrees <= 0 ? 137.508 : options.HueStepDegrees;
        var added = 0;

        for (var i = 0; added < count && i < count * 48; i++)
        {
            var hue = (accentHue + i * step) % 360.0;
            var band = i % 3;
            var sat = Math.Clamp(saturation + (band == 1 ? 0.08 : band == 2 ? -0.04 : 0), 0.35, 0.95);
            var light = Math.Clamp(lightness + (band - 1) * 0.07, 0.32, 0.78);
            if (TryAddGeneratedHue(hue, sat, light, palette, seen, canvas, minRatio, i, reservedForegroundColors))
                added++;
        }
    }

    private static void AddEvenHueWheel(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvas,
        double minRatio,
        List<string> palette,
        HashSet<string> seen,
        int resolvedCount,
        IReadOnlyList<string> reservedForegroundColors)
    {
        var anchorHex = theme.GetHex(HighlightColorCatalog.AnchorTokenKey(options.HueAnchor));
        if (!HighlightColorMath.TryRgbToHsl(anchorHex, out var accentHue, out _, out _))
            accentHue = 210;

        var isDarkCanvas = HighlightColorMath.RelativeLuminance(canvas) < 0.45;
        var saturation = options.Saturation ?? (isDarkCanvas ? 0.72 : 0.62);
        var lightness = options.Lightness ?? (isDarkCanvas ? 0.64 : 0.38);
        var count = resolvedCount;
        var step = 360.0 / Math.Max(count, 1);
        var added = 0;

        for (var i = 0; added < count && i < count * 48; i++)
        {
            var hue = (accentHue + i * step) % 360.0;
            var band = i % 3;
            var sat = Math.Clamp(saturation + (band == 1 ? 0.06 : 0), 0.35, 0.95);
            var light = Math.Clamp(lightness + (band - 1) * 0.06, 0.32, 0.78);
            if (TryAddGeneratedHue(hue, sat, light, palette, seen, canvas, minRatio, i, reservedForegroundColors))
                added++;
        }
    }

    private static string AssignPlayer(HighlightColorAssignmentContext context)
    {
        var canvas = context.CanvasBackgroundHex;
        var minRatio = Math.Max(3.0, context.Options.MinContrastRatio);
        var palette = context.Palette;

        string raw = context.Options.PlayerColorMode switch
        {
            HighlightPlayerColorMode.PaletteFirst => palette[0],
            HighlightPlayerColorMode.Success => context.Theme.GetHex("Success"),
            HighlightPlayerColorMode.Warning => context.Theme.GetHex("Warning"),
            HighlightPlayerColorMode.Custom when !string.IsNullOrWhiteSpace(context.Options.PlayerCustomColor)
                => context.Options.PlayerCustomColor!,
            _ => context.Theme.GetHex("AccentPrimary"),
        };

        return Commit(context, ThemeContrast.EnsureReadable(raw, canvas, minRatio));
    }

    private static string AssignAlias(HighlightColorAssignmentContext context, string parentName)
    {
        var canvas = context.CanvasBackgroundHex;
        var minRatio = Math.Max(3.0, context.Options.MinContrastRatio);

        if (context.Options.AliasColorMode == HighlightAliasColorMode.Distinct)
            return PickOptimalDistinct(context, roleBucket: 3);

        if (context.CharacterColors.TryGetValue(parentName, out var parentColor))
        {
            var baseColor = context.Options.AliasColorMode == HighlightAliasColorMode.MutedParent
                ? HighlightColorMath.Mute(parentColor)
                : parentColor;

            return Commit(context, ThemeContrast.EnsureReadable(baseColor, canvas, minRatio));
        }

        return PickOptimalDistinct(context, roleBucket: 3);
    }

    private static string PickOptimalDistinct(HighlightColorAssignmentContext context, int? roleBucket)
    {
        var palette = context.Palette;
        if (palette.Count == 0)
            return Ensure(context, "#FFD166");

        var indices = EnumerateBucketIndices(palette.Count, roleBucket).ToList();
        if (indices.Count == 0)
            indices = Enumerable.Range(0, palette.Count).ToList();

        var bestScore = double.NegativeInfinity;
        var bestIndices = new List<int>();

        foreach (var index in indices)
        {
            var candidate = Ensure(context, palette[index]);
            var score = ScoreDistinctness(context, candidate);
            if (score > bestScore + 0.0001)
            {
                bestScore = score;
                bestIndices = [index];
            }
            else if (Math.Abs(score - bestScore) <= 0.0001)
            {
                bestIndices.Add(index);
            }
        }

        if (bestIndices.Count == 0)
            return Commit(context, NudgeDistinctUnused(context, Ensure(context, palette[0])));

        var pool = BuildDistinctCandidatePool(context, indices, palette, bestIndices, bestScore);
        var tiebreak = (HighlightColorMath.StableHash(context.Phrase)
                        + context.AssignmentSalt * 31
                        + context.DiscoveryIndex * 17) % pool.Count;
        var chosen = Ensure(context, palette[pool[tiebreak]]);
        if (IsDistinctFromUsed(context, chosen) || !context.Options.AvoidDuplicateColors)
            return Commit(context, chosen);

        return Commit(context, NudgeDistinctUnused(context, chosen));
    }

    /// <summary>
    /// When assignment salt changes (reroll), widen the viable palette indices so salt can shift colors
    /// even when one index clearly wins on distinctness.
    /// </summary>
    private static List<int> BuildDistinctCandidatePool(
        HighlightColorAssignmentContext context,
        IReadOnlyList<int> indices,
        IReadOnlyList<string> palette,
        IReadOnlyList<int> bestIndices,
        double bestScore)
    {
        if (context.AssignmentSalt == 0)
            return bestIndices.ToList();

        const double nearBestRatio = 0.72;
        var nearBest = new List<int>();
        foreach (var index in indices)
        {
            var candidate = Ensure(context, palette[index]);
            var score = ScoreDistinctness(context, candidate);
            if (score >= bestScore * nearBestRatio - 0.001)
                nearBest.Add(index);
        }

        return nearBest.Count > 0 ? nearBest.Distinct().ToList() : bestIndices.ToList();
    }

    private static double ScoreDistinctness(HighlightColorAssignmentContext context, string color)
    {
        if (context.UsedColors.Count == 0)
            return 1.0;

        var minDistance = double.MaxValue;
        foreach (var used in context.UsedColors)
            minDistance = Math.Min(minDistance, HighlightColorMath.PerceptualDistance(color, used));

        if (!context.Options.AvoidDuplicateColors
            && context.UsedColors.Contains(color))
        {
            return minDistance * 0.25;
        }

        return minDistance;
    }

    private static IEnumerable<int> EnumerateBucketIndices(int paletteCount, int? roleBucket)
    {
        if (paletteCount <= 0)
            yield break;

        if (roleBucket is null)
        {
            for (var i = 0; i < paletteCount; i++)
                yield return i;
            yield break;
        }

        var bucketOffset = roleBucket switch
        {
            0 => 0,
            1 => Math.Max(1, paletteCount / 4),
            2 => Math.Max(2, paletteCount / 2),
            3 => Math.Max(3, (paletteCount * 3) / 4),
            _ => 0,
        };

        var bucketSize = Math.Max(1, paletteCount / 4);
        for (var offset = 0; offset < bucketSize; offset++)
            yield return (bucketOffset + offset) % paletteCount;
    }

    private static string PickSequential(HighlightColorAssignmentContext context)
    {
        var palette = context.Palette;
        if (palette.Count == 0)
            return Ensure(context, "#FFD166");

        var start = (context.DiscoveryIndex + context.AssignmentSalt) % palette.Count;

        if (!context.Options.AvoidDuplicateColors)
            return Commit(context, Ensure(context, palette[start]));

        for (var offset = 0; offset < palette.Count; offset++)
        {
            var candidate = Ensure(context, palette[(start + offset) % palette.Count]);
            if (IsDistinctFromUsed(context, candidate))
                return Commit(context, candidate);
        }

        return Commit(context, NudgeDistinctUnused(context, Ensure(context, palette[start])));
    }

    private static string PickStable(HighlightColorAssignmentContext context, int? roleBucket)
    {
        var palette = context.Palette;
        if (palette.Count == 0)
            return Ensure(context, "#FFD166");

        var bucketOffset = roleBucket switch
        {
            0 => 0,
            1 => Math.Max(1, palette.Count / 4),
            2 => Math.Max(2, palette.Count / 2),
            3 => Math.Max(3, (palette.Count * 3) / 4),
            _ => 0,
        };

        var start = (HighlightColorMath.StableHash(context.Phrase) + context.AssignmentSalt + bucketOffset) % palette.Count;

        if (!context.Options.AvoidDuplicateColors)
            return Commit(context, Ensure(context, palette[start]));

        for (var offset = 0; offset < palette.Count; offset++)
        {
            var candidate = Ensure(context, palette[(start + offset) % palette.Count]);
            if (IsDistinctFromUsed(context, candidate))
                return Commit(context, candidate);
        }

        return Commit(context, NudgeDistinctUnused(context, Ensure(context, palette[start])));
    }

    private static bool IsDistinctFromUsed(HighlightColorAssignmentContext context, string color)
    {
        if (!context.Options.AvoidDuplicateColors)
            return !context.UsedColors.Contains(color);

        return HighlightColorMath.IsDistinctFromAll(color, context.UsedColors);
    }

    private static int? ResolveRoleBucket(string role)
    {
        if (role.Equals("Player", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (role.StartsWith("Alias · ", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (role.Equals("Party", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Character", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Ally", StringComparison.OrdinalIgnoreCase))
        {
            return role.Equals("Character", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        }

        return role.Contains("Party", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static string NudgeDistinctUnused(HighlightColorAssignmentContext context, string seed)
    {
        var canvas = context.CanvasBackgroundHex;
        var minRatio = Math.Max(3.0, context.Options.MinContrastRatio);
        var step = context.Options.HueStepDegrees <= 0 ? 137.508 : context.Options.HueStepDegrees;

        if (!HighlightColorMath.TryRgbToHsl(seed, out var h, out var s, out var l))
            return NudgeLuminanceUnused(context, seed);

        for (var pass = 0; pass < 36; pass++)
        {
            h = (h + step) % 360.0;
            var band = pass % 3;
            var sat = Math.Clamp(s + (band == 1 ? 0.06 : band == 2 ? -0.04 : 0), 0.35, 0.95);
            var light = Math.Clamp(l + (band - 1) * 0.05, 0.32, 0.78);
            var candidate = ThemeContrast.EnsureReadable(HighlightColorMath.HslToHex(h, sat, light), canvas, minRatio);
            candidate = Ensure(context, candidate);
            if (IsDistinctFromUsed(context, candidate) && !HighlightColorReservedColors.Conflicts(candidate, context.ReservedForegroundColors))
                return Commit(context, candidate);
        }

        return Commit(context, Ensure(context, NudgeLuminanceUnused(context, seed)));
    }

    private static string NudgeLuminanceUnused(HighlightColorAssignmentContext context, string seed)
    {
        var canvas = context.CanvasBackgroundHex;
        var minRatio = Math.Max(3.0, context.Options.MinContrastRatio);
        var fallback = seed;

        for (var pass = 0; pass < 12; pass++)
        {
            fallback = HighlightColorMath.RelativeLuminance(canvas) < 0.45
                ? HighlightColorMath.Lighten(fallback, 0.06)
                : HighlightColorMath.Darken(fallback, 0.06);
            fallback = Ensure(context, fallback);
            if (IsDistinctFromUsed(context, fallback) && !HighlightColorReservedColors.Conflicts(fallback, context.ReservedForegroundColors))
                return fallback;
        }

        return fallback;
    }

    private static string Ensure(HighlightColorAssignmentContext context, string hex)
    {
        var minRatio = Math.Max(3.0, context.Options.MinContrastRatio);
        var readable = ThemeContrast.EnsureReadable(hex, context.CanvasBackgroundHex, minRatio);
        return AvoidReserved(readable, context.ReservedForegroundColors, context.CanvasBackgroundHex, minRatio);
    }

    private static string AvoidReserved(
        string color,
        IReadOnlyList<string> reservedForegroundColors,
        string canvasBackgroundHex,
        double minContrastRatio) =>
        HighlightColorReservedColors.Avoid(color, reservedForegroundColors, canvasBackgroundHex, minContrastRatio);

    private static string Commit(HighlightColorAssignmentContext context, string color)
    {
        context.UsedColors.Add(color);
        return color;
    }

    private static bool TryParseAliasParent(string role, out string parentName)
    {
        parentName = "";
        const string prefix = "Alias · ";
        if (!role.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        parentName = role[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(parentName);
    }
}
