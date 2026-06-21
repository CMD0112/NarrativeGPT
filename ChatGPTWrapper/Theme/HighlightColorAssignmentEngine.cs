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
        string canvasBackgroundHex)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(theme);

        var canvas = canvasBackgroundHex;
        var minRatio = Math.Max(3.0, options.MinContrastRatio);
        var palette = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddReadable(string hex)
        {
            var readable = ThemeContrast.EnsureReadable(hex, canvas, minRatio);
            if (seen.Add(readable))
                palette.Add(readable);
        }

        switch (options.PaletteSource)
        {
            case HighlightPaletteSource.FixedClassic:
                foreach (var hex in HighlightColorCatalog.ClassicFixed)
                    AddReadable(hex);
                break;

            case HighlightPaletteSource.FixedEditorSwatches:
                foreach (var hex in HighlightColorCatalog.EditorSwatches)
                    AddReadable(hex);
                break;

            case HighlightPaletteSource.CustomSeeds:
                foreach (var hex in options.CustomSeedColors)
                {
                    if (!string.IsNullOrWhiteSpace(hex))
                        AddReadable(hex);
                }
                break;

            case HighlightPaletteSource.ThemeAccentOnly:
                AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen, includeSemanticSeeds: false);
                break;

            case HighlightPaletteSource.EvenHueWheel:
                AddEvenHueWheel(options, theme, canvas, minRatio, palette, seen);
                break;

            case HighlightPaletteSource.GoldenAngle:
            case HighlightPaletteSource.ThemeSemantic:
            default:
                AddThemeAccentHues(options, theme, canvas, minRatio, palette, seen,
                    includeSemanticSeeds: options.PaletteSource == HighlightPaletteSource.ThemeSemantic);
                break;
        }

        if (palette.Count == 0)
            palette.Add(ThemeContrast.EnsureReadable(theme.GetHex("AccentPrimary"), canvas, minRatio));

        return palette;
    }

    public static string AssignColor(HighlightColorAssignmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

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
            HighlightAssignmentStrategy.RoleBuckets => PickStable(context, ResolveRoleBucket(context.Role)),
            _ => PickStable(context, roleBucket: null),
        };
    }

    private static void AddThemeAccentHues(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvas,
        double minRatio,
        List<string> palette,
        HashSet<string> seen,
        bool includeSemanticSeeds)
    {
        void AddReadable(string hex)
        {
            var readable = ThemeContrast.EnsureReadable(hex, canvas, minRatio);
            if (seen.Add(readable))
                palette.Add(readable);
        }

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
        var count = Math.Clamp(options.GeneratedColorCount, 4, 48);
        var step = options.HueStepDegrees <= 0 ? 137.508 : options.HueStepDegrees;

        for (var i = 0; i < count; i++)
        {
            var hue = (accentHue + i * step) % 360.0;
            AddReadable(HighlightColorMath.HslToHex(hue, saturation, lightness));
        }
    }

    private static void AddEvenHueWheel(
        HighlightColorAssignmentOptions options,
        ResolvedTheme theme,
        string canvas,
        double minRatio,
        List<string> palette,
        HashSet<string> seen)
    {
        var anchorHex = theme.GetHex(HighlightColorCatalog.AnchorTokenKey(options.HueAnchor));
        if (!HighlightColorMath.TryRgbToHsl(anchorHex, out var accentHue, out _, out _))
            accentHue = 210;

        var isDarkCanvas = HighlightColorMath.RelativeLuminance(canvas) < 0.45;
        var saturation = options.Saturation ?? (isDarkCanvas ? 0.72 : 0.62);
        var lightness = options.Lightness ?? (isDarkCanvas ? 0.64 : 0.38);
        var count = Math.Clamp(options.GeneratedColorCount, 4, 48);
        var step = 360.0 / count;

        for (var i = 0; i < count; i++)
        {
            var hue = (accentHue + i * step) % 360.0;
            var readable = ThemeContrast.EnsureReadable(
                HighlightColorMath.HslToHex(hue, saturation, lightness),
                canvas,
                minRatio);
            if (seen.Add(readable))
                palette.Add(readable);
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
            return PickStable(context, roleBucket: 3);

        if (context.CharacterColors.TryGetValue(parentName, out var parentColor))
        {
            var baseColor = context.Options.AliasColorMode == HighlightAliasColorMode.MutedParent
                ? HighlightColorMath.Mute(parentColor)
                : parentColor;

            return Commit(context, ThemeContrast.EnsureReadable(baseColor, canvas, minRatio));
        }

        return PickStable(context, roleBucket: 3);
    }

    private static string PickSequential(HighlightColorAssignmentContext context)
    {
        var index = context.DiscoveryIndex % context.Palette.Count;
        return Commit(context, Ensure(context, context.Palette[index]));
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

        var start = (HighlightColorMath.StableHash(context.Phrase) + bucketOffset) % palette.Count;

        if (!context.Options.AvoidDuplicateColors)
            return Commit(context, Ensure(context, palette[start]));

        for (var offset = 0; offset < palette.Count; offset++)
        {
            var candidate = Ensure(context, palette[(start + offset) % palette.Count]);
            if (!context.UsedColors.Contains(candidate))
                return Commit(context, candidate);
        }

        return Commit(context, NudgeUnused(context, Ensure(context, palette[start])));
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

    private static string NudgeUnused(HighlightColorAssignmentContext context, string seed)
    {
        var canvas = context.CanvasBackgroundHex;
        var minRatio = Math.Max(3.0, context.Options.MinContrastRatio);
        var fallback = seed;

        for (var pass = 0; pass < 12; pass++)
        {
            fallback = HighlightColorMath.RelativeLuminance(canvas) < 0.45
                ? HighlightColorMath.Lighten(fallback, 0.06)
                : HighlightColorMath.Darken(fallback, 0.06);
            fallback = ThemeContrast.EnsureReadable(fallback, canvas, minRatio);
            if (!context.UsedColors.Contains(fallback))
                return fallback;
        }

        return fallback;
    }

    private static string Ensure(HighlightColorAssignmentContext context, string hex) =>
        ThemeContrast.EnsureReadable(hex, context.CanvasBackgroundHex, Math.Max(3.0, context.Options.MinContrastRatio));

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
