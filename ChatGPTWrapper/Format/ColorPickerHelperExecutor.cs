using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Format;

public static class ColorPickerHelperExecutor
{
    private const double GuideContrastTarget = 2.2;
    private const double GuideHighContrastTarget = 3.2;

    public static string Apply(string helperId, ColorPickerContext? context, string currentHex)
    {
        var canvas = ResolveCanvas(context);
        var current = Normalize(currentHex, context?.ThemeTextPrimaryHex ?? "#ECECEC");

        return helperId switch
        {
            "fix-contrast" => ThemeContrast.EnsureReadable(current, canvas),
            "match-theme-text" => Normalize(context?.ThemeTextPrimaryHex, current),
            "match-theme-accent" => Normalize(context?.ThemeAccentHex, current),
            "soften-muted" => ColorSpaceConverter.Mute(
                Normalize(context?.ThemeTextMutedHex, ThemeContrast.EnsureReadable(current, canvas)),
                desaturate: 0.25,
                lighten: 0.04),
            "vivid-accent" => Saturate(Normalize(context?.ThemeAccentHex, current), 1.18),
            "muted-accent" => ColorSpaceConverter.Mute(Normalize(context?.ThemeAccentHex, current), 0.45, 0.06),
            "match-prose-ink" => Normalize(context?.AssistantTextHex, current),
            "match-assistant-accent" => Normalize(context?.AssistantAccentHex, current),
            "match-user-accent" => Normalize(context?.UserAccentHex, current),
            "optimize-guide-contrast" => OptimizeGuideInk(current, context, GuideContrastTarget),
            "low-glare-guides" => OptimizeGuideInk(
                ColorSpaceConverter.Mute(Normalize(context?.AssistantTextHex, current), 0.5, ColorSpaceConverter.IsLightCanvas(canvas) ? 0.08 : -0.04),
                context,
                GuideContrastTarget),
            "high-contrast-guides" => OptimizeGuideInk(
                Normalize(context?.AssistantTextHex, current),
                context,
                GuideHighContrastTarget),
            "subtle-divider" => ColorSpaceConverter.Mute(Normalize(context?.AssistantTextHex, current), 0.55, 0.05),
            "distinct-link" => DistinctReadable(
                Normalize(context?.AssistantTextHex, current),
                canvas,
                degrees: 28),
            "distinct-from-prose" => DistinctReadable(
                Normalize(context?.PairedTextHex ?? context?.AssistantTextHex, current),
                canvas,
                degrees: 42),
            "complement-prose" => DistinctReadable(
                Normalize(context?.PairedTextHex ?? context?.AssistantTextHex, current),
                canvas,
                degrees: 180),
            "subtle-fill" => SubtleFill(canvas),
            "preserve-text-contrast" => PreserveTextContrast(current, canvas, context?.PairedTextHex),
            "match-theme-surface" => Normalize(ThemeRuntime.Current.GetHex("BgSurface"), current),
            "code-inset" => CodeInset(canvas),
            _ => current,
        };
    }

    private static string OptimizeGuideInk(string seed, ColorPickerContext? context, double targetRatio)
    {
        var canvas = ResolveCanvas(context);
        var opacity = ResolveGuideOpacity(context);
        var baseInk = Normalize(seed, context?.AssistantTextHex ?? context?.ThemeTextPrimaryHex ?? "#ECECEC");

        var bestBase = baseInk;
        var bestRatio = ThemeContrast.ContrastRatio(
            ColorSpaceConverter.SimulateOpacityOnCanvas(bestBase, canvas, opacity),
            canvas);

        foreach (var candidate in new[]
                 {
                     baseInk,
                     Normalize(context?.AssistantAccentHex, baseInk),
                     Normalize(context?.ThemeAccentHex, baseInk),
                     ColorSpaceConverter.IsLightCanvas(canvas) ? "#111111" : "#F4F4F4",
                 })
        {
            var visible = ColorSpaceConverter.SimulateOpacityOnCanvas(candidate, canvas, opacity);
            var ratio = ThemeContrast.ContrastRatio(visible, canvas);
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                bestBase = candidate;
            }
        }

        if (bestRatio + 0.02 >= targetRatio)
            return bestBase;

        var anchor = ColorSpaceConverter.IsLightCanvas(canvas)
            ? ColorSpaceConverter.Darken(canvas, 0.22)
            : ColorSpaceConverter.Lighten(canvas, 0.42);
        var targetVisible = ThemeContrast.EnsureReadable(anchor, canvas, targetRatio);
        var inverted = ColorSpaceConverter.InverseOpacityOnCanvas(targetVisible, canvas, opacity);
        var invertedVisible = ColorSpaceConverter.SimulateOpacityOnCanvas(inverted, canvas, opacity);
        var invertedRatio = ThemeContrast.ContrastRatio(invertedVisible, canvas);
        return invertedRatio > bestRatio ? inverted : bestBase;
    }

    private static string DistinctReadable(string referenceHex, string canvasHex, double degrees)
    {
        var shifted = ColorSpaceConverter.RotateHue(referenceHex, degrees);
        return ThemeContrast.EnsureReadable(shifted, canvasHex, ThemeContrast.MinMutedRatio);
    }

    private static string SubtleFill(string canvasHex)
    {
        var themeBase = ThemeRuntime.Current.GetHex("BgSurface");
        return ColorSpaceConverter.IsLightCanvas(canvasHex)
            ? ColorSpaceConverter.Mix(themeBase, canvasHex, 18)
            : ColorSpaceConverter.Mix(themeBase, canvasHex, 24);
    }

    private static string CodeInset(string canvasHex) =>
        ColorSpaceConverter.IsLightCanvas(canvasHex)
            ? ColorSpaceConverter.Darken(canvasHex, 0.05)
            : ColorSpaceConverter.Lighten(canvasHex, 0.06);

    private static string PreserveTextContrast(string fillHex, string canvasHex, string? textHex)
    {
        var text = Normalize(textHex, ThemeRuntime.Current.GetHex("TextPrimary"));
        if (ThemeContrast.IsReadable(text, fillHex))
            return fillHex;

        for (var i = 0; i < 20; i++)
        {
            fillHex = ColorSpaceConverter.IsLightCanvas(canvasHex)
                ? ColorSpaceConverter.Darken(fillHex, 0.03)
                : ColorSpaceConverter.Lighten(fillHex, 0.03);

            if (ThemeContrast.IsReadable(text, fillHex))
                return fillHex;
        }

        return fillHex;
    }

    private static string Saturate(string hex, double factor)
    {
        if (!ColorSpaceConverter.TryRgbToHsl(hex, out var h, out var s, out var l))
            return hex;

        return ColorSpaceConverter.HslToHex(h, Math.Min(1, s * factor), l);
    }

    private static double ResolveGuideOpacity(ColorPickerContext? context)
    {
        if (context?.ReadingGuideStyle is RuledLineStyle.Band or RuledLineStyle.ParagraphZebra)
            return context.RuledBandOpacity ?? 6;

        return context?.RuledLineOpacity ?? 12;
    }

    private static string ResolveCanvas(ColorPickerContext? context) =>
        Normalize(
            context?.ProseCanvasHex ?? context?.ContextBackgroundHex,
            ThemeRuntime.Current.GetHex("BgBase"));

    private static string Normalize(string? hex, string fallback) =>
        string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim();
}
