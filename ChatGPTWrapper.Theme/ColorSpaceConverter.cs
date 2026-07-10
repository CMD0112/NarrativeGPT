using System.Reflection;
using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

/// <summary>RGB, HSV, and HSL conversions shared by the color picker and highlight palette engine.</summary>
public static class ColorSpaceConverter
{
    public static bool TryParseColor(string hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            var normalized = hex.Trim();
            if (!normalized.StartsWith('#'))
            {
                if (TryParseKnownColorName(normalized, out color))
                    return true;

                normalized = "#" + normalized;
            }

            color = (Color)ColorConverter.ConvertFromString(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseKnownColorName(string name, out Color color)
    {
        color = default;
        var property = typeof(Colors).GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (property?.GetValue(null) is not Color known)
            return false;

        color = known;
        return true;
    }

    public static Color ParseColor(string hex) =>
        TryParseColor(hex, out var color) ? color : Colors.White;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            h = 0;
            return;
        }

        if (max == r)
            h = 60 * (((g - b) / delta) % 6);
        else if (max == g)
            h = 60 * (((b - r) / delta) + 2);
        else
            h = 60 * (((r - g) / delta) + 4);

        if (h < 0)
            h += 360;
    }

    public static Color HsvToRgb(double h, double s, double v)
    {
        if (s <= 0)
        {
            var gray = (byte)Math.Round(v * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        h = (h % 360 + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        double r, g, b;
        if (h < 60)
            (r, g, b) = (c, x, 0);
        else if (h < 120)
            (r, g, b) = (x, c, 0);
        else if (h < 180)
            (r, g, b) = (0, c, x);
        else if (h < 240)
            (r, g, b) = (0, x, c);
        else if (h < 300)
            (r, g, b) = (x, 0, c);
        else
            (r, g, b) = (c, 0, x);

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static void RgbToHsl(Color color, out double h, out double s, out double l)
    {
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
            return;
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
    }

    public static bool TryRgbToHsl(string hex, out double h, out double s, out double l)
    {
        h = s = l = 0;
        if (!TryParseColor(hex, out var color))
            return false;

        RgbToHsl(color, out h, out s, out l);
        return true;
    }

    public static Color HslToRgb(double h, double s, double l)
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

        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }

    public static string HslToHex(double h, double s, double l) => ToHex(HslToRgb(h, s, l));

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

    /// <summary>Linear sRGB blend (matches CSS color-mix percentages on opaque colors).</summary>
    public static string Mix(string foregroundHex, string backgroundHex, double foregroundPercent)
    {
        if (!TryParseColor(foregroundHex, out var foreground)
            || !TryParseColor(backgroundHex, out var background))
        {
            return foregroundHex;
        }

        var amount = Math.Clamp(foregroundPercent / 100.0, 0, 1);
        static byte Blend(byte channel, byte under, double t) =>
            (byte)Math.Clamp(Math.Round(channel * t + under * (1 - t)), 0, 255);

        return ToHex(Color.FromRgb(
            Blend(foreground.R, background.R, amount),
            Blend(foreground.G, background.G, amount),
            Blend(foreground.B, background.B, amount)));
    }

    /// <summary>Approximate visible ink when a base color is mixed toward a canvas at the given opacity.</summary>
    public static string SimulateOpacityOnCanvas(string baseHex, string canvasHex, double opacityPercent) =>
        Mix(baseHex, canvasHex, opacityPercent);

    /// <summary>Pick a base ink that renders to the target visible color at the given opacity on the canvas.</summary>
    public static string InverseOpacityOnCanvas(string visibleHex, string canvasHex, double opacityPercent)
    {
        if (!TryParseColor(visibleHex, out var visible) || !TryParseColor(canvasHex, out var canvas))
            return visibleHex;

        var amount = Math.Clamp(opacityPercent / 100.0, 0.01, 1);
        static byte Solve(byte target, byte under, double t) =>
            (byte)Math.Clamp(Math.Round((target - under * (1 - t)) / t), 0, 255);

        return ToHex(Color.FromRgb(
            Solve(visible.R, canvas.R, amount),
            Solve(visible.G, canvas.G, amount),
            Solve(visible.B, canvas.B, amount)));
    }

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

    public static bool IsLightCanvas(string canvasHex) => RelativeLuminance(canvasHex) >= 0.55;

    public static string RotateHue(string hex, double degrees)
    {
        if (!TryRgbToHsl(hex, out var h, out var s, out var l))
            return hex;

        return HslToHex(h + degrees, s, l);
    }

    /// <summary>Circular hue distance in degrees (0–180).</summary>
    public static double HueDistanceDegrees(double h1, double h2)
    {
        var diff = Math.Abs(h1 - h2);
        return Math.Min(diff, 360.0 - diff);
    }

    /// <summary>
    /// Weighted HSL distance. Hue is weighted by the lower saturation so greys do not dominate.
    /// </summary>
    public static double PerceptualDistance(string a, string b)
    {
        if (!TryRgbToHsl(a, out var h1, out var s1, out var l1)
            || !TryRgbToHsl(b, out var h2, out var s2, out var l2))
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        var hueDiff = HueDistanceDegrees(h1, h2) / 180.0;
        var hueWeight = Math.Min(s1, s2);
        var satDiff = Math.Abs(s1 - s2);
        var lumDiff = Math.Abs(l1 - l2);
        var weightedHue = hueDiff * hueWeight;
        return Math.Sqrt(weightedHue * weightedHue + satDiff * satDiff + lumDiff * lumDiff);
    }

    public static string FormatRgb(Color color) => $"rgb({color.R}, {color.G}, {color.B})";

    public static string FormatHsl(Color color)
    {
        RgbToHsl(color, out var h, out var s, out var l);
        return $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";
    }

    public static string FormatHsv(Color color)
    {
        RgbToHsv(color, out var h, out var s, out var v);
        return $"hsv({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(v * 100)}%)";
    }

    /// <summary>Nearest WPF named color within RGB distance threshold, or null.</summary>
    public static string? TryFindNearestNamedColor(Color color, int maxChannelDistance = 18)
    {
        string? bestName = null;
        var bestDistance = int.MaxValue;
        var maxDistanceSq = maxChannelDistance * maxChannelDistance * 3;

        foreach (var property in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.GetValue(null) is not Color candidate)
                continue;

            var dr = color.R - candidate.R;
            var dg = color.G - candidate.G;
            var db = color.B - candidate.B;
            var distanceSq = dr * dr + dg * dg + db * db;
            if (distanceSq >= bestDistance)
                continue;

            bestDistance = distanceSq;
            bestName = property.Name;
        }

        return bestDistance <= maxDistanceSq ? bestName : null;
    }
}
