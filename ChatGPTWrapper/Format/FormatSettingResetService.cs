using System.Reflection;

namespace ChatGPTWrapper.Format;

/// <summary>
/// Resets individual <see cref="ContinuousViewFormatSettings"/> properties to factory defaults.
/// </summary>
public static class FormatSettingResetService
{
    private static readonly ContinuousViewFormatSettings Defaults = ContinuousViewFormatSettings.CreateDefaults();

    public static bool TryReset(ContinuousViewFormatSettings target, string propertyName)
    {
        var property = ResolveProperty(propertyName);
        if (property is null)
            return false;

        property.SetValue(target, property.GetValue(Defaults));
        return true;
    }

    public static object? GetDefaultValue(string propertyName) =>
        ResolveProperty(propertyName)?.GetValue(Defaults);

    public static bool IsAtDefault(ContinuousViewFormatSettings current, string propertyName)
    {
        var property = ResolveProperty(propertyName);
        if (property is null)
            return true;

        var currentValue = property.GetValue(current);
        var defaultValue = property.GetValue(Defaults);
        return Equals(currentValue, defaultValue);
    }

    private static PropertyInfo? ResolveProperty(string propertyName) =>
        typeof(ContinuousViewFormatSettings).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
}
