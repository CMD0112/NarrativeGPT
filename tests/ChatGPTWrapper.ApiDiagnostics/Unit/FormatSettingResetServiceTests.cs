using System.Reflection;
using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatSettingResetServiceTests
{
    [Fact]
    public void TryReset_restores_writable_property_from_defaults()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 20;
        format.ShowRuledLines = true;
        format.RuledLineStyle = RuledLineStyle.Band;
        format.SegmentDividerStyle = SegmentDividerStyle.Dashed;
        format.UserTextColor = "#aabbcc";

        Assert.True(FormatSettingResetService.TryReset(format, FormatSettingKeys.ContentMaxWidthRem));
        Assert.Equal(42, format.ContentMaxWidthRem);

        Assert.True(FormatSettingResetService.TryReset(format, FormatSettingKeys.ShowRuledLines));
        Assert.False(format.ShowRuledLines);

        Assert.True(FormatSettingResetService.TryReset(format, FormatSettingKeys.RuledLineStyle));
        Assert.Equal(RuledLineStyle.Line, format.RuledLineStyle);

        Assert.True(FormatSettingResetService.TryReset(format, FormatSettingKeys.SegmentDividerStyle));
        Assert.Equal(SegmentDividerStyle.Solid, format.SegmentDividerStyle);

        Assert.True(FormatSettingResetService.TryReset(format, FormatSettingKeys.UserTextColor));
        Assert.Null(format.UserTextColor);
    }

    [Fact]
    public void TryReset_returns_false_for_unknown_property()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        Assert.False(FormatSettingResetService.TryReset(format, "NotARealProperty"));
    }

    [Fact]
    public void TryReset_restores_every_public_writable_property()
    {
        var format = CreateNonDefaultSettings();
        var defaults = ContinuousViewFormatSettings.CreateDefaults();

        foreach (var property in typeof(ContinuousViewFormatSettings)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite))
        {
            Assert.True(FormatSettingResetService.TryReset(format, property.Name));
            Assert.Equal(property.GetValue(defaults), property.GetValue(format));
        }
    }

    [Fact]
    public void IsAtDefault_detects_drift()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        Assert.True(FormatSettingResetService.IsAtDefault(format, FormatSettingKeys.SegmentSpacingRem));

        format.SegmentSpacingRem = 2;
        Assert.False(FormatSettingResetService.IsAtDefault(format, FormatSettingKeys.SegmentSpacingRem));
    }

    private static ContinuousViewFormatSettings CreateNonDefaultSettings()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        foreach (var property in typeof(ContinuousViewFormatSettings)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite))
        {
            var value = CreateAlternateValue(property.PropertyType, property.GetValue(format));
            property.SetValue(format, value);
        }

        return format;
    }

    private static object? CreateAlternateValue(Type type, object? current)
    {
        if (type == typeof(string))
            return current is null ? "#112233" : null;

        if (type == typeof(bool))
            return current is not true;

        if (type == typeof(int))
            return current is int i ? i + 1 : 1;

        if (type == typeof(double))
            return current is double d ? d + 1.5 : 1.5;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            foreach (var value in values)
            {
                if (!Equals(value, current))
                    return value;
            }

            return values.GetValue(0);
        }

        return current;
    }
}
