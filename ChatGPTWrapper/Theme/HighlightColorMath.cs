using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

internal static class HighlightColorMath
{
    public static double RelativeLuminance(string hex)
    {
        if (!TryParseColor(hex, out var color))
            return 0;

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

    public static bool TryRgbToHsl(string hex, out double h, out double s, out double l)
    {
        h = s = l = 0;
        if (!TryParseColor(hex, out var color))
            return false;

        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;

        if (Math.Abs(max - min) < 0.00001)
        {
            h = 0;
            s = 0;
            return true;
        }

        var delta = max - min;
        s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        if (Math.Abs(max - r) < 0.00001)
            h = ((g - b) / delta + (g < b ? 6 : 0)) * 60.0;
        else if (Math.Abs(max - g) < 0.00001)
            h = ((b - r) / delta + 2) * 60.0;
        else
            h = ((r - g) / delta + 4) * 60.0;

        if (h < 0)
            h += 360;

        return true;
    }

    public static string HslToHex(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0)
                t += 1;
            if (t > 1)
                t -= 1;
            if (t < 1.0 / 6.0)
                return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0)
                return q;
            if (t < 2.0 / 3.0)
                return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        double r;
        double g;
        double b;

        if (s <= 0.00001)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
            g = HueToRgb(p, q, h / 360.0);
            b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
        }

        return $"#{(byte)Math.Round(r * 255):X2}{(byte)Math.Round(g * 255):X2}{(byte)Math.Round(b * 255):X2}";
    }

    public static string Lighten(string hex, double amount)
    {
        if (!TryParseColor(hex, out var color))
            return hex;

        static byte Clamp(double channel) => (byte)Math.Clamp(Math.Round(channel), 0, 255);

        return $"#{Clamp(color.R + amount * 255):X2}{Clamp(color.G + amount * 255):X2}{Clamp(color.B + amount * 255):X2}";
    }

    public static string Darken(string hex, double amount) => Lighten(hex, -amount);

    public static string Mute(string hex, double desaturate = 0.35, double lighten = 0.08)
    {
        if (!TryRgbToHsl(hex, out var h, out var s, out var l))
            return hex;

        return HslToHex(h, s * (1 - desaturate), Math.Min(1, l + lighten));
    }

    public static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in value.Trim())
                hash = (hash * 31) + char.ToUpperInvariant(ch);
            return hash & int.MaxValue;
        }
    }

    public static bool TryParseColor(string hex, out Color color)
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
}
