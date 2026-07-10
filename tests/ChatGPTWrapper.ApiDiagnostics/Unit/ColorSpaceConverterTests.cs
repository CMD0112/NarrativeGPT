using System.Windows.Media;
using ChatGPTWrapper.Theme;
using Color = System.Windows.Media.Color;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ColorSpaceConverterTests
{
    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#808080")]
    [InlineData("#00FF00")]
    public void RgbToHsvToRgb_round_trips_within_one_channel(string hex)
    {
        Assert.True(ColorSpaceConverter.TryParseColor(hex, out var original));
        ColorSpaceConverter.RgbToHsv(original, out var h, out var s, out var v);
        var roundTrip = ColorSpaceConverter.HsvToRgb(h, s, v);

        Assert.InRange(Math.Abs(original.R - roundTrip.R), 0, 1);
        Assert.InRange(Math.Abs(original.G - roundTrip.G), 0, 1);
        Assert.InRange(Math.Abs(original.B - roundTrip.B), 0, 1);
    }

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#808080")]
    [InlineData("#336699")]
    public void RgbToHslToRgb_round_trips_within_one_channel(string hex)
    {
        Assert.True(ColorSpaceConverter.TryParseColor(hex, out var original));
        ColorSpaceConverter.RgbToHsl(original, out var h, out var s, out var l);
        var roundTrip = ColorSpaceConverter.HslToRgb(h, s, l);

        Assert.InRange(Math.Abs(original.R - roundTrip.R), 0, 1);
        Assert.InRange(Math.Abs(original.G - roundTrip.G), 0, 1);
        Assert.InRange(Math.Abs(original.B - roundTrip.B), 0, 1);
    }

    [Fact]
    public void Grey_has_zero_saturation_in_hsl()
    {
        var grey = Color.FromRgb(128, 128, 128);
        ColorSpaceConverter.RgbToHsl(grey, out _, out var s, out var l);
        Assert.True(s < 0.001);
        Assert.InRange(l, 0.49, 0.51);
    }

    [Fact]
    public void RotateHue_wraps_at_360()
    {
        var rotated = ColorSpaceConverter.RotateHue("#FF0000", 360);
        Assert.Equal("#FF0000", rotated, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseColor_accepts_named_color()
    {
        Assert.True(ColorSpaceConverter.TryParseColor("cornflowerblue", out var color));
        Assert.NotEqual(default(Color), color);
    }

    [Fact]
    public void TryParseColor_accepts_hex_without_hash()
    {
        Assert.True(ColorSpaceConverter.TryParseColor("FF5733", out var color));
        Assert.Equal(0xFF, color.R);
        Assert.Equal(0x57, color.G);
        Assert.Equal(0x33, color.B);
    }

    [Fact]
    public void ToHex_formats_uppercase_channels()
    {
        var hex = ColorSpaceConverter.ToHex(Color.FromRgb(91, 159, 212));
        Assert.Equal("#5B9FD4", hex, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatHsl_emits_percent_lightness()
    {
        var color = Color.FromRgb(255, 0, 0);
        var formatted = ColorSpaceConverter.FormatHsl(color);
        Assert.StartsWith("hsl(", formatted, StringComparison.Ordinal);
        Assert.Contains("%", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatHsv_emits_percent_saturation_and_value()
    {
        var color = Color.FromRgb(30, 0, 49);
        var formatted = ColorSpaceConverter.FormatHsv(color);
        Assert.StartsWith("hsv(", formatted, StringComparison.Ordinal);
        Assert.Contains("%", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void TryFindNearestNamedColor_matches_exact_named_color()
    {
        Assert.True(ColorSpaceConverter.TryParseColor("cornflowerblue", out var color));
        var name = ColorSpaceConverter.TryFindNearestNamedColor(color);
        Assert.Equal("CornflowerBlue", name, StringComparer.OrdinalIgnoreCase);
    }
}
