using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

/// <summary>
/// WCAG-style contrast enforcement for theme tokens and user-picked colors.
/// </summary>
public static class ThemeContrast
{
    public const double MinBodyRatio = 4.5;
    public const double MinMutedRatio = 3.0;
    private const double RatioTolerance = 0.02;

    private static readonly string[] SurfaceTokenKeys =
    [
        "BgBase",
        "BgSurface",
        "BgElevated",
        "BgChrome",
        "BgWorkspace",
        "BgInset",
        "Popup",
        "Header",
        "RowHover",
        "RowSelected",
        "RowAlternate",
        "ButtonGhost",
        "ButtonGhostHover",
        "ButtonGhostPressed",
    ];

    private static readonly string[] SubtleOverlayTokenKeys =
    [
        "AccentSubtle",
        "SuccessSubtle",
        "WarningSubtle",
        "ErrorSubtle",
    ];

    private static readonly string[] AccentFillTokenKeys =
    [
        "AccentPrimary",
        "AccentPrimaryHover",
        "AccentPrimaryPressed",
    ];

    public static bool IsReadable(string foregroundHex, string backgroundHex, double minRatio = MinBodyRatio) =>
        ContrastRatio(foregroundHex, backgroundHex) + RatioTolerance >= minRatio;

    public static string EnsureReadable(string foregroundHex, string backgroundHex, double minRatio = MinBodyRatio)
    {
        if (IsReadable(foregroundHex, backgroundHex, minRatio))
            return NormalizeOpaqueHex(foregroundHex);

        var fg = NormalizeOpaqueHex(foregroundHex);
        for (var pass = 0; pass < 32; pass++)
        {
            if (IsReadable(fg, backgroundHex, minRatio))
                return fg;

            fg = NudgeTowardReadable(fg, backgroundHex);
        }

        return PickExtremeReadable(backgroundHex, minRatio);
    }

    public static void EnforceReadableTokens(IDictionary<string, string> tokens)
    {
        var textSurfaces = CollectTextSurfaces(tokens).ToList();
        if (textSurfaces.Count == 0)
            return;

        AdjustForeground(tokens, "TextPrimary", textSurfaces, MinBodyRatio);
        AdjustForeground(tokens, "TextMuted", textSurfaces, MinMutedRatio);
        AdjustForeground(tokens, "ContextMenuForeground", CollectMenuBackgrounds(tokens), MinBodyRatio);
        EnsureAccentLink(tokens);

        foreach (var (semantic, subtle) in new (string Fg, string Subtle)[]
                 {
                     ("Success", "SuccessSubtle"),
                     ("Warning", "WarningSubtle"),
                     ("Error", "ErrorSubtle"),
                 })
        {
            if (!tokens.TryGetValue(subtle, out var overlay)
                || !tokens.TryGetValue("BgSurface", out var underlay))
            {
                continue;
            }

            AdjustForeground(tokens, semantic, [Composite(overlay, underlay)], MinBodyRatio);
        }

        ThemeDerivation.RefreshDerivedTokens(tokens, onlyMissing: false);

        foreach (var (semantic, subtle) in new (string Fg, string Subtle)[]
                 {
                     ("Success", "SuccessSubtle"),
                     ("Warning", "WarningSubtle"),
                     ("Error", "ErrorSubtle"),
                 })
        {
            if (!tokens.TryGetValue(subtle, out var overlay)
                || !tokens.TryGetValue("BgSurface", out var underlay))
            {
                continue;
            }

            AdjustForeground(tokens, semantic, [Composite(overlay, underlay)], MinBodyRatio);
        }
    }

    /// <summary>
    /// Ensures primary button labels contrast with accent fills. Re-derives hover/pressed from accent each pass.
    /// </summary>
    public static void EnforceAccentButtonPairs(IDictionary<string, string> tokens)
    {
        if (!tokens.TryGetValue("AccentPrimary", out _))
            return;

        var textOnAccent = tokens.TryGetValue("TextOnAccent", out var existing)
            ? existing
            : "#FFFFFF";

        for (var pass = 0; pass < 32; pass++)
        {
            ThemeDerivation.RefreshDerivedTokens(tokens, onlyMissing: false);

            foreach (var bgKey in AccentFillTokenKeys)
            {
                if (tokens.TryGetValue(bgKey, out var bg))
                    textOnAccent = EnsureReadable(textOnAccent, bg, MinBodyRatio);
            }

            tokens["TextOnAccent"] = textOnAccent;

            var worstRatio = double.MaxValue;
            foreach (var bgKey in AccentFillTokenKeys)
            {
                if (!tokens.TryGetValue(bgKey, out var bg))
                    continue;

                worstRatio = Math.Min(worstRatio, ContrastRatio(textOnAccent, bg));
            }

            if (worstRatio >= MinBodyRatio)
                return;

            tokens["AccentPrimary"] = NudgeFillAwayFromText(tokens["AccentPrimary"], textOnAccent);
        }
    }

    public static IReadOnlyList<ContrastFailure> ValidateTokens(IDictionary<string, string> tokens)
    {
        var failures = new List<ContrastFailure>();
        var textSurfaces = CollectTextSurfaces(tokens).ToList();

        Check(tokens, failures, "TextPrimary", textSurfaces, MinBodyRatio);
        Check(tokens, failures, "TextMuted", textSurfaces, MinMutedRatio);
        Check(tokens, failures, "ContextMenuForeground", CollectMenuBackgrounds(tokens), MinBodyRatio);
        CheckAccentButtonPairs(tokens, failures);

        if (tokens.TryGetValue("AccentLink", out var link)
            && tokens.TryGetValue("BgSurface", out var surface))
        {
            if (!IsReadable(link, surface, MinBodyRatio))
            {
                failures.Add(new ContrastFailure("AccentLink", surface, ContrastRatio(link, surface), MinBodyRatio));
            }
        }

        if (tokens.TryGetValue("BgSurface", out var underlay))
        {
            foreach (var (fg, subtle) in new (string, string)[]
                     {
                         ("Success", "SuccessSubtle"),
                         ("Warning", "WarningSubtle"),
                         ("Error", "ErrorSubtle"),
                     })
            {
                if (tokens.TryGetValue(subtle, out var overlay))
                    Check(tokens, failures, fg, [Composite(overlay, underlay)], MinBodyRatio);
            }
        }

        return failures;
    }

    public static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        var fg = RelativeLuminance(foregroundHex);
        var bg = RelativeLuminance(backgroundHex);
        if (fg < 0 || bg < 0)
            return 1;

        var lighter = Math.Max(fg, bg);
        var darker = Math.Min(fg, bg);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static string Composite(string overlayHex, string underlayHex)
    {
        if (!TryParseColor(overlayHex, out var overlay) || !TryParseColor(underlayHex, out var underlay))
            return NormalizeOpaqueHex(underlayHex);

        var alpha = overlay.A / 255.0;
        if (alpha <= 0)
            return NormalizeOpaqueHex(underlayHex);
        if (alpha >= 1)
            return ToOpaqueHex(overlay);

        byte Blend(byte over, byte under) =>
            (byte)Math.Clamp(Math.Round(over * alpha + under * (1 - alpha)), 0, 255);

        return $"#{Blend(overlay.R, underlay.R):X2}{Blend(overlay.G, underlay.G):X2}{Blend(overlay.B, underlay.B):X2}";
    }

    private static void EnsureAccentLink(IDictionary<string, string> tokens)
    {
        if (!tokens.TryGetValue("AccentPrimary", out var accent))
            return;

        var surface = tokens.TryGetValue("BgSurface", out var bg) ? bg : "#161618";
        tokens["AccentLink"] = EnsureReadable(accent, surface, MinBodyRatio);
    }

    public static void RefreshAccentLink(IDictionary<string, string> tokens) => EnsureAccentLink(tokens);

    private static void CheckAccentButtonPairs(IDictionary<string, string> tokens, List<ContrastFailure> failures)
    {
        if (!tokens.TryGetValue("TextOnAccent", out var fg))
            return;

        foreach (var bgKey in AccentFillTokenKeys)
        {
            if (!tokens.TryGetValue(bgKey, out var bg))
                continue;

            var ratio = ContrastRatio(fg, bg);
            if (ratio + RatioTolerance < MinBodyRatio)
                failures.Add(new ContrastFailure("TextOnAccent", bg, ratio, MinBodyRatio));
        }
    }

    private static string NudgeFillAwayFromText(string fillHex, string textHex)
    {
        var current = ContrastRatio(textHex, fillHex);
        var best = fillHex;
        var bestRatio = current;

        foreach (var candidate in new[] { Darken(fillHex, 0.08), Lighten(fillHex, 0.08) })
        {
            var ratio = ContrastRatio(textHex, candidate);
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                best = candidate;
            }
        }

        if (bestRatio > current)
            return best;

        for (var pass = 0; pass < 32; pass++)
        {
            if (IsReadable(textHex, best, MinBodyRatio))
                return best;

            var darkened = Darken(best, 0.08);
            var lightened = Lighten(best, 0.08);
            var darkRatio = ContrastRatio(textHex, darkened);
            var lightRatio = ContrastRatio(textHex, lightened);

            if (darkRatio >= lightRatio && darkRatio > ContrastRatio(textHex, best))
                best = darkened;
            else if (lightRatio > ContrastRatio(textHex, best))
                best = lightened;
            else
                break;
        }

        return IsReadable(textHex, best, MinBodyRatio)
            ? best
            : RelativeLuminance(textHex) > 0.5 ? "#000000" : "#FFFFFF";
    }

    private static void Check(
        IDictionary<string, string> tokens,
        List<ContrastFailure> failures,
        string foregroundKey,
        IReadOnlyList<string> backgrounds,
        double minRatio)
    {
        if (!tokens.TryGetValue(foregroundKey, out var fg))
            return;

        foreach (var bg in backgrounds)
        {
            var ratio = ContrastRatio(fg, bg);
            if (ratio + RatioTolerance < minRatio)
                failures.Add(new ContrastFailure(foregroundKey, bg, ratio, minRatio));
        }
    }

    private static void AdjustForeground(
        IDictionary<string, string> tokens,
        string foregroundKey,
        IReadOnlyList<string> backgrounds,
        double minRatio)
    {
        if (backgrounds.Count == 0 || !tokens.TryGetValue(foregroundKey, out var fg))
            return;

        for (var pass = 0; pass < 32; pass++)
        {
            var needsWork = false;
            foreach (var bg in backgrounds)
            {
                if (IsReadable(fg, bg, minRatio))
                    continue;

                fg = EnsureReadable(fg, bg, minRatio);
                needsWork = true;
            }

            if (!needsWork)
                break;
        }

        tokens[foregroundKey] = fg;
    }

    private static IEnumerable<string> CollectSolidSurfaces(IDictionary<string, string> tokens)
    {
        foreach (var key in SurfaceTokenKeys)
        {
            if (tokens.TryGetValue(key, out var hex))
                yield return hex;
        }
    }

    private static IEnumerable<string> CollectTextSurfaces(IDictionary<string, string> tokens)
    {
        foreach (var hex in CollectSolidSurfaces(tokens))
            yield return hex;

        if (!tokens.TryGetValue("BgSurface", out var underlay))
            yield break;

        foreach (var key in SubtleOverlayTokenKeys)
        {
            if (tokens.TryGetValue(key, out var overlay))
                yield return Composite(overlay, underlay);
        }
    }

    private static IReadOnlyList<string> CollectMenuBackgrounds(IDictionary<string, string> tokens)
    {
        var list = new List<string>();
        foreach (var key in new[] { "ContextMenuBackground", "MenuPopup", "Popup" })
        {
            if (tokens.TryGetValue(key, out var hex))
                list.Add(hex);
        }

        return list.Count > 0 ? list : CollectSolidSurfaces(tokens).ToList();
    }

    private static string NudgeTowardReadable(string foregroundHex, string backgroundHex)
    {
        var bgLum = RelativeLuminance(backgroundHex);
        if (bgLum < 0)
            return foregroundHex;

        return bgLum < 0.5
            ? Lighten(foregroundHex, 0.08)
            : Darken(foregroundHex, 0.08);
    }

    private static string PickExtremeReadable(string backgroundHex, double minRatio)
    {
        const string white = "#FFFFFF";
        const string black = "#000000";
        return ContrastRatio(white, backgroundHex) >= ContrastRatio(black, backgroundHex)
            ? (ContrastRatio(white, backgroundHex) >= minRatio ? white : black)
            : (ContrastRatio(black, backgroundHex) >= minRatio ? black : white);
    }

    private static double RelativeLuminance(string hex)
    {
        if (!TryParseColor(hex, out var color))
            return -1;

        static double Channel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = Channel(color.R);
        var g = Channel(color.G);
        var b = Channel(color.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static string Lighten(string hex, double amount)
    {
        if (!TryParseColor(hex, out var color))
            return hex;

        return ToOpaqueHex(AdjustRgb(color, amount, amount, amount));
    }

    private static string Darken(string hex, double amount)
    {
        if (!TryParseColor(hex, out var color))
            return hex;

        return ToOpaqueHex(AdjustRgb(color, -amount, -amount, -amount));
    }

    private static Color AdjustRgb(Color color, double r, double g, double b)
    {
        static byte Clamp(double channel) => (byte)Math.Clamp(Math.Round(channel), 0, 255);

        return Color.FromArgb(
            color.A,
            Clamp(color.R + r * 255),
            Clamp(color.G + g * 255),
            Clamp(color.B + b * 255));
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            var normalized = hex.Trim();
            if (!normalized.StartsWith('#'))
                normalized = "#" + normalized;

            color = (Color)ColorConverter.ConvertFromString(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeOpaqueHex(string hex)
    {
        if (!TryParseColor(hex, out var color))
            return hex;

        return ToOpaqueHex(color);
    }

    private static string ToOpaqueHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

public sealed record ContrastFailure(
    string ForegroundToken,
    string BackgroundHex,
    double ActualRatio,
    double RequiredRatio);
