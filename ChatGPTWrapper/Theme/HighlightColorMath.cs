using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

internal static class HighlightColorMath
{
    public const double MinDistinctDistance = 0.14;
    public const double MinPaletteDistinctDistance = 0.085;

    public static double RelativeLuminance(string hex)
    {
        if (!ColorSpaceConverter.TryParseColor(hex, out var color))
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

    public static bool TryRgbToHsl(string hex, out double h, out double s, out double l) =>
        ColorSpaceConverter.TryRgbToHsl(hex, out h, out s, out l);

    public static string HslToHex(double h, double s, double l) =>
        ColorSpaceConverter.HslToHex(h, s, l);

    public static string Lighten(string hex, double amount) =>
        ColorSpaceConverter.Lighten(hex, amount);

    public static string Darken(string hex, double amount) =>
        ColorSpaceConverter.Darken(hex, amount);

    public static string Mute(string hex, double desaturate = 0.35, double lighten = 0.08) =>
        ColorSpaceConverter.Mute(hex, desaturate, lighten);

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

    public static double HueDistanceDegrees(double h1, double h2) =>
        ColorSpaceConverter.HueDistanceDegrees(h1, h2);

    public static double PerceptualDistance(string a, string b) =>
        ColorSpaceConverter.PerceptualDistance(a, b);

    public static bool ArePerceptuallySimilar(string a, string b, double minDistance = MinDistinctDistance) =>
        PerceptualDistance(a, b) < minDistance;

    public static bool IsDistinctFromAll(string color, IEnumerable<string> others, double minDistance = MinDistinctDistance)
    {
        foreach (var other in others)
        {
            if (PerceptualDistance(color, other) < minDistance)
                return false;
        }

        return true;
    }

    public static string RotateHue(string hex, double degrees) =>
        ColorSpaceConverter.RotateHue(hex, degrees);

    public static bool TryParseColor(string hex, out Color color) =>
        ColorSpaceConverter.TryParseColor(hex, out color);
}
