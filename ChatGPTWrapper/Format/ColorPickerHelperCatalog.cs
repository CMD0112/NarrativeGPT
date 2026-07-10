namespace ChatGPTWrapper.Format;

public sealed class ColorPickerHelperDefinition
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Description { get; init; }
}

public static class ColorPickerHelperCatalog
{
    public static IReadOnlyList<ColorPickerHelperDefinition> GetHelpers(ColorPickerContext? context)
    {
        if (context is null)
            return CommonHelpers();

        return context.Kind switch
        {
            ColorPickerTargetKind.FormatColor => BuildFormatHelpers(context),
            ColorPickerTargetKind.ThemeToken => BuildThemeHelpers(context),
            ColorPickerTargetKind.HighlightText => BuildHighlightTextHelpers(context),
            ColorPickerTargetKind.HighlightBackground => BuildHighlightBackgroundHelpers(context),
            _ => CommonHelpers(),
        };
    }

    public static string? GetContextHint(ColorPickerContext? context)
    {
        if (context?.TargetKey is null)
            return null;

        if (context.Kind == ColorPickerTargetKind.FormatColor)
        {
            return context.TargetKey switch
            {
                nameof(ContinuousViewFormatSettings.RuledLineColor) =>
                    "Guides are drawn on prose blocks. Helpers tune the base ink; opacity sliders control how strongly it appears.",
                nameof(ContinuousViewFormatSettings.SegmentDividerColor) =>
                    "Dividers sit between message turns on the transcript canvas.",
                nameof(ContinuousViewFormatSettings.LinkColor) or nameof(ContinuousViewFormatSettings.LinkHoverColor) =>
                    "Links should stay distinguishable from body text while remaining readable on the prose canvas.",
                nameof(ContinuousViewFormatSettings.InlineCodeBackgroundColor) =>
                    "Inline code fills sit behind short spans inside running prose.",
                nameof(ContinuousViewFormatSettings.CodeBackgroundColor)
                    or nameof(ContinuousViewFormatSettings.CodeBorderColor)
                    or nameof(ContinuousViewFormatSettings.CodeLangLabelColor) =>
                    "Code block colors should separate fenced code from prose without overpowering it.",
                _ when context.TargetKey.EndsWith("TextColor", StringComparison.Ordinal) =>
                    "Text helpers optimize readability on the segment or overlay background shown in the preview.",
                _ when context.TargetKey.EndsWith("BackgroundColor", StringComparison.Ordinal) =>
                    "Background helpers keep paired body text readable after you adjust the fill.",
                _ when context.TargetKey.EndsWith("AccentColor", StringComparison.Ordinal) =>
                    "Accent helpers harmonize stripe colors with the active theme and role text.",
                _ => null,
            };
        }

        if (context.Kind == ColorPickerTargetKind.ThemeToken)
            return "Theme token helpers keep the wrapper shell readable on its surface backgrounds.";

        if (context.Kind == ColorPickerTargetKind.HighlightText)
            return "Highlight text should stay legible on its rule background and on transcript prose.";

        if (context.Kind == ColorPickerTargetKind.HighlightBackground)
            return "Rule backgrounds should separate highlighted phrases without hiding them.";

        return null;
    }

    private static IReadOnlyList<ColorPickerHelperDefinition> CommonHelpers() =>
    [
        Helper("fix-contrast", "Fix contrast", "Nudge the color until it meets readable contrast on the preview background."),
        Helper("match-theme-text", "Match theme text", "Use the active theme primary text color."),
        Helper("match-theme-accent", "Match theme accent", "Use the active theme accent color."),
    ];

    private static IReadOnlyList<ColorPickerHelperDefinition> BuildThemeHelpers(ColorPickerContext context)
    {
        var helpers = new List<ColorPickerHelperDefinition>(CommonHelpers());
        if (context.TargetKey?.StartsWith("Text", StringComparison.OrdinalIgnoreCase) == true)
        {
            helpers.Add(Helper("soften-muted", "Soften to muted", "Desaturate and lighten for secondary labels and hints."));
        }

        if (context.TargetKey?.StartsWith("Accent", StringComparison.OrdinalIgnoreCase) == true)
        {
            helpers.Add(Helper("vivid-accent", "Vivid accent", "Boost saturation for a stronger accent fill."));
        }

        return helpers;
    }

    private static IReadOnlyList<ColorPickerHelperDefinition> BuildHighlightTextHelpers(ColorPickerContext context)
    {
        var helpers = new List<ColorPickerHelperDefinition>
        {
            Helper("fix-contrast", "Fix contrast", "Make highlight text readable on the preview background."),
            Helper("distinct-from-prose", "Distinct from prose", "Rotate hue away from body text for quicker scanning."),
            Helper("match-theme-accent", "Match theme accent", "Borrow the wrapper accent for a cohesive cast palette."),
        };

        if (!string.IsNullOrWhiteSpace(context.PairedTextHex))
        {
            helpers.Add(Helper("complement-prose", "Complement prose", "Pick the complement of the current body text color."));
        }

        return helpers;
    }

    private static IReadOnlyList<ColorPickerHelperDefinition> BuildHighlightBackgroundHelpers(ColorPickerContext context)
    {
        return
        [
            Helper("subtle-fill", "Subtle fill", "Soft tinted background that keeps text readable."),
            Helper("match-theme-accent", "Match theme accent", "Accent-tinted fill at low strength."),
            Helper("fix-contrast", "Fix contrast", "Tune fill so primary text would remain readable on top."),
        ];
    }

    private static IReadOnlyList<ColorPickerHelperDefinition> BuildFormatHelpers(ColorPickerContext context)
    {
        if (context.TargetKey == nameof(ContinuousViewFormatSettings.RuledLineColor))
            return BuildReadingGuideHelpers(context);

        if (context.TargetKey == nameof(ContinuousViewFormatSettings.SegmentDividerColor))
        {
            return
            [
                Helper("subtle-divider", "Subtle divider", "Muted ink derived from prose text for quiet separators."),
                Helper("match-assistant-accent", "Match assistant accent", "Use the assistant accent stripe color."),
                Helper("match-theme-accent", "Match theme accent", "Use the wrapper accent color."),
                Helper("optimize-guide-contrast", "Optimize guide contrast", "Tune guide ink for visible but calm contrast on the canvas."),
            ];
        }

        if (context.TargetKey is nameof(ContinuousViewFormatSettings.LinkColor)
            or nameof(ContinuousViewFormatSettings.LinkHoverColor))
        {
            return
            [
                Helper("fix-contrast", "Fix contrast", "Ensure links meet readable contrast on the prose canvas."),
                Helper("distinct-link", "Distinct link", "Shift hue away from body text while preserving readability."),
                Helper("match-theme-accent", "Match theme accent", "Align links with the wrapper accent."),
            ];
        }

        if (context.TargetKey?.EndsWith("TextColor", StringComparison.Ordinal) == true)
        {
            return
            [
                Helper("fix-contrast", "Fix contrast", "Readable body text on the preview background."),
                Helper("soften-muted", "Soften to muted", "Lower contrast for secondary tone (still above minimum)."),
                Helper("match-theme-text", "Match theme text", "Use theme primary text on this role."),
            ];
        }

        if (context.TargetKey?.EndsWith("BackgroundColor", StringComparison.Ordinal) == true
            || context.TargetKey == nameof(ContinuousViewFormatSettings.OverlayBackgroundColor))
        {
            return
            [
                Helper("subtle-fill", "Subtle fill", "Gentle surface tint from the theme base."),
                Helper("preserve-text-contrast", "Preserve text contrast", "Adjust fill so paired body text stays readable."),
                Helper("match-theme-surface", "Match theme surface", "Use the wrapper surface background."),
            ];
        }

        if (context.TargetKey?.EndsWith("AccentColor", StringComparison.Ordinal) == true)
        {
            return
            [
                Helper("match-theme-accent", "Match theme accent", "Use the wrapper accent."),
                Helper("vivid-accent", "Vivid accent", "Saturated accent for stronger role distinction."),
                Helper("muted-accent", "Muted accent", "Desaturated accent that stays calm beside prose."),
            ];
        }

        if (context.TargetKey is nameof(ContinuousViewFormatSettings.InlineCodeBackgroundColor)
            or nameof(ContinuousViewFormatSettings.CodeBackgroundColor))
        {
            return
            [
                Helper("code-inset", "Code inset", "Muted inset fill derived from the prose canvas."),
                Helper("subtle-fill", "Subtle fill", "Soft tint slightly darker or lighter than the canvas."),
                Helper("match-theme-surface", "Match theme surface", "Use theme elevated surface color."),
            ];
        }

        return CommonHelpers();
    }

    private static IReadOnlyList<ColorPickerHelperDefinition> BuildReadingGuideHelpers(ColorPickerContext context)
    {
        var styleHint = context.ReadingGuideStyle switch
        {
            RuledLineStyle.Band => "Row bands use the band opacity slider.",
            RuledLineStyle.ParagraphZebra => "Paragraph zebra uses the band opacity slider on alternating paragraphs.",
            RuledLineStyle.Underline => "Underline guides use the ruled line opacity slider.",
            RuledLineStyle.MarginRail => "Margin rails use the ruled line thickness slider for stripe width.",
            _ => "Ruled lines use the ruled line opacity slider.",
        };

        return
        [
            Helper("match-prose-ink", "Match prose ink", $"Use assistant prose text as the guide base. {styleHint}"),
            Helper("match-assistant-accent", "Match assistant accent", $"Use the assistant accent stripe. {styleHint}"),
            Helper("optimize-guide-contrast", "Optimize guide contrast", "Tune base ink so guides stay visible but calm on the canvas."),
            Helper("low-glare-guides", "Low-glare guides", "Bias guide ink for long reading on the current canvas."),
            Helper("high-contrast-guides", "High-contrast guides", "Stronger guide ink for low-vision scanning."),
            Helper("match-theme-accent", "Match theme accent", "Borrow the wrapper accent for guide ink."),
        ];
    }

    private static ColorPickerHelperDefinition Helper(string id, string label, string description) =>
        new() { Id = id, Label = label, Description = description };
}
