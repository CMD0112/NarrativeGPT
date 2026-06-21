using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ChatGPTWrapper.Views;

public sealed class HexColorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        try
        {
            var normalized = hex.Trim().StartsWith('#') ? hex.Trim() : "#" + hex.Trim();
            var color = (Color)ColorConverter.ConvertFromString(normalized)!;
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
