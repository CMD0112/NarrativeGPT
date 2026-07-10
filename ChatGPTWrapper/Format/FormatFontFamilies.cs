using System.Windows.Media;

namespace ChatGPTWrapper.Format;

public static class FormatFontFamilies
{
    public const string Inherit = "inherit";

    public const string Sans = "sans";

    public const string Serif = "serif";

    public const string Mono = "mono";

    public const string Humanist = "humanist";

    public const string Literary = "literary";

    public const string Typewriter = "typewriter";

    public const string Charter = "charter";

    public const string Garamond = "garamond";

    public const string Custom = "custom";

    public static readonly IReadOnlyDictionary<string, string> PresetStacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Sans] = "system-ui, \"Segoe UI\", sans-serif",
            [Serif] = "Georgia, \"Times New Roman\", serif",
            [Mono] = "ui-monospace, \"Cascadia Code\", \"Segoe UI Mono\", Consolas, monospace",
            [Humanist] = "\"Segoe UI\", system-ui, -apple-system, BlinkMacSystemFont, sans-serif",
            [Literary] = "\"Literata\", \"Palatino Linotype\", Palatino, Georgia, serif",
            [Typewriter] = "\"Courier New\", Courier, monospace",
            [Charter] = "\"Charter\", \"Bitstream Charter\", Georgia, serif",
            [Garamond] = "Garamond, \"EB Garamond\", \"Times New Roman\", serif",
        };

    public static readonly IReadOnlyList<(string Id, string Label)> PresetOptions =
    [
        (Inherit, "Inherit"),
        (Sans, "Sans-serif"),
        (Serif, "Serif"),
        (Mono, "Monospace"),
        (Humanist, "Humanist sans"),
        (Literary, "Literary serif"),
        (Typewriter, "Typewriter"),
        (Charter, "Charter"),
        (Garamond, "Garamond"),
        (Custom, "Custom…"),
    ];

    public static string? ResolveCssStack(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || stored.Equals(Inherit, StringComparison.OrdinalIgnoreCase))
            return null;

        var trimmed = stored.Trim();
        if (PresetStacks.TryGetValue(trimmed, out var stack))
            return stack;

        return trimmed;
    }

    public static string GetPresetId(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || stored.Equals(Inherit, StringComparison.OrdinalIgnoreCase))
            return Inherit;

        var trimmed = stored.Trim();
        return PresetStacks.ContainsKey(trimmed) ? trimmed : Custom;
    }

    public static string? NormalizeStored(string? presetId, string? customValue)
    {
        if (string.IsNullOrWhiteSpace(presetId)
            || presetId.Equals(Inherit, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (presetId.Equals(Custom, StringComparison.OrdinalIgnoreCase))
        {
            var custom = customValue?.Trim();
            return string.IsNullOrWhiteSpace(custom) ? null : custom;
        }

        return PresetStacks.ContainsKey(presetId) ? presetId : null;
    }

    public static string ToCustomStack(string fontFamilyName)
    {
        var trimmed = fontFamilyName.Trim();
        if (trimmed.Contains(',', StringComparison.Ordinal))
            return trimmed;

        return trimmed.Contains(' ') ? $"\"{trimmed}\", sans-serif" : $"{trimmed}, sans-serif";
    }

    public static FontFamily? ResolveWpfFontFamily(string? stored)
    {
        var css = ResolveCssStack(stored);
        if (css is null)
            return null;

        var first = css.Split(',')[0].Trim().Trim('"');
        try
        {
            return new FontFamily(first);
        }
        catch
        {
            return null;
        }
    }
}
