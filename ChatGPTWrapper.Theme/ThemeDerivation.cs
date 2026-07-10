using System.Globalization;
using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

internal static class ThemeDerivation
{
    public static void ApplyDerivedTokens(IDictionary<string, string> tokens)
    {
        RefreshDerivedTokens(tokens, onlyMissing: true);
    }

    public static void RefreshDerivedTokens(IDictionary<string, string> tokens, bool onlyMissing = false)
    {
        if (TryGet(tokens, "AccentPrimary", out var accent))
        {
            SetToken(tokens, "AccentPrimaryHover", Lighten(accent, 0.08), onlyMissing);
            SetToken(tokens, "AccentPrimaryPressed", Darken(accent, 0.08), onlyMissing);
            SetToken(tokens, "AccentSubtle", ToSubtleHex(accent), onlyMissing);
        }

        if (TryGet(tokens, "Success", out var success))
            SetToken(tokens, "SuccessSubtle", ToSubtleHex(success), onlyMissing);

        if (TryGet(tokens, "Warning", out var warning))
            SetToken(tokens, "WarningSubtle", ToSubtleHex(warning), onlyMissing);

        if (TryGet(tokens, "Error", out var error))
            SetToken(tokens, "ErrorSubtle", ToSubtleHex(error), onlyMissing);
    }

    public static string ToCssAccentSubtle(string hex) =>
        TryParseColor(hex, out var color)
            ? $"rgba({color.R}, {color.G}, {color.B}, 0.13)"
            : "rgba(91, 159, 212, 0.13)";

    private static void SetIfMissing(IDictionary<string, string> tokens, string key, string value) =>
        SetToken(tokens, key, value, onlyMissing: true);

    private static void SetToken(IDictionary<string, string> tokens, string key, string value, bool onlyMissing)
    {
        if (!onlyMissing || !tokens.ContainsKey(key))
            tokens[key] = value;
    }

    private static bool TryGet(IDictionary<string, string> tokens, string key, out string value)
    {
        if (tokens.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value))
            return true;

        value = string.Empty;
        return false;
    }

    private static string Lighten(string hex, double amount)
    {
        if (!TryParseColor(hex, out var color))
            return hex;

        return ToHex(AdjustRgb(color, amount, amount, amount));
    }

    private static string Darken(string hex, double amount)
    {
        if (!TryParseColor(hex, out var color))
            return hex;

        return ToHex(AdjustRgb(color, -amount, -amount, -amount));
    }

    private static string ToSubtleHex(string hex)
    {
        if (!TryParseColor(hex, out var color))
            return "#33000000";

        return $"#33{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static Color AdjustRgb(Color color, double r, double g, double b)
    {
        static byte Clamp(double channel) => (byte)Math.Clamp(Math.Round(channel), 0, 255);

        return Color.FromRgb(
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

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
