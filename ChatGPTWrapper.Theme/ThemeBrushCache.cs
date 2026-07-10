using System.Collections.Concurrent;
using System.Windows.Media;

namespace ChatGPTWrapper.Theme;

public static class ThemeBrushCache
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush GetBrush(string hex)
    {
        var normalized = NormalizeHex(hex);
        return Brushes.GetOrAdd(normalized, static key =>
        {
            var color = (Color)ColorConverter.ConvertFromString(key);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        });
    }

    public static Color GetColor(string hex)
    {
        var normalized = NormalizeHex(hex);
        return (Color)ColorConverter.ConvertFromString(normalized);
    }

    private static string NormalizeHex(string hex)
    {
        var trimmed = hex.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }
}
