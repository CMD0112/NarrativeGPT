using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Format;

/// <summary>Maps color-pick call sites to a context background for contrast preview.</summary>
public static class ColorPickerContextResolver
{
    public static string ResolveThemeTokenBackground(string tokenKey, ThemeSettings? themeSettings)
    {
        var theme = themeSettings is null
            ? ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings())
            : ThemeApplicationService.ResolveEffectiveTheme(themeSettings);

        if (tokenKey.StartsWith("Text", StringComparison.OrdinalIgnoreCase)
            || tokenKey.Equals("ContextMenuForeground", StringComparison.OrdinalIgnoreCase))
        {
            return FirstAvailable(theme, "BgSurface", "BgBase");
        }

        if (tokenKey.Equals("TextOnAccent", StringComparison.OrdinalIgnoreCase))
            return theme.GetHex("AccentPrimary");

        if (tokenKey.StartsWith("Accent", StringComparison.OrdinalIgnoreCase)
            || tokenKey is "Success" or "Warning" or "Error")
        {
            return FirstAvailable(theme, "BgSurface", "BgBase");
        }

        if (tokenKey.StartsWith("Border", StringComparison.OrdinalIgnoreCase))
            return FirstAvailable(theme, "BgSurface", "BgBase");

        if (tokenKey.StartsWith("Row", StringComparison.OrdinalIgnoreCase)
            || tokenKey.Equals("Header", StringComparison.OrdinalIgnoreCase))
        {
            return FirstAvailable(theme, "BgSurface", "BgBase");
        }

        if (tokenKey.StartsWith("ButtonGhost", StringComparison.OrdinalIgnoreCase))
            return FirstAvailable(theme, "BgSurface", "BgBase");

        if (tokenKey.StartsWith("ContextMenu", StringComparison.OrdinalIgnoreCase)
            || tokenKey.Equals("MenuPopup", StringComparison.OrdinalIgnoreCase)
            || tokenKey.Equals("Popup", StringComparison.OrdinalIgnoreCase))
        {
            return FirstAvailable(theme, "BgChrome", "BgBase");
        }

        if (tokenKey.StartsWith("Bg", StringComparison.OrdinalIgnoreCase))
            return theme.GetHex("BgBase");

        return ThemeRuntime.Current.GetHex("BgBase");
    }

    public static string ResolveFormatColorBackground(string propertyName, ContinuousViewFormatSettings format)
    {
        if (propertyName.EndsWith("TextColor", StringComparison.Ordinal))
        {
            var bgProperty = propertyName.Replace("TextColor", "BackgroundColor", StringComparison.Ordinal);
            var paired = GetFormatColor(format, bgProperty);
            if (!string.IsNullOrWhiteSpace(paired))
                return paired;

            if (propertyName.StartsWith("User", StringComparison.Ordinal))
                return ResolveEffective(format.UserBackgroundColor, format.OverlayBackgroundColor);

            if (propertyName.StartsWith("Assistant", StringComparison.Ordinal))
                return ResolveEffective(format.AssistantBackgroundColor, format.OverlayBackgroundColor);
        }

        if (propertyName.EndsWith("BackgroundColor", StringComparison.Ordinal)
            || propertyName is "OverlayBackgroundColor")
        {
            return ResolveEffective(format.OverlayBackgroundColor, ThemeRuntime.Current.GetHex("BgBase"));
        }

        if (propertyName is "LinkColor" or "LinkHoverColor")
            return ResolveEffective(format.OverlayBackgroundColor, ThemeRuntime.Current.GetHex("BgBase"));

        if (propertyName is "InlineCodeBackgroundColor" or "CodeBackgroundColor"
            or "CodeBorderColor" or "CodeLangLabelColor")
        {
            return ResolveEffective(format.CodeBackgroundColor, format.OverlayBackgroundColor);
        }

        if (propertyName is "TableBorderColor" or "TableHeaderBackgroundColor")
            return ResolveEffective(format.OverlayBackgroundColor, ThemeRuntime.Current.GetHex("BgBase"));

        if (propertyName is nameof(ContinuousViewFormatSettings.RuledLineColor)
            or nameof(ContinuousViewFormatSettings.SegmentDividerColor))
        {
            return ResolveEffective(
                format.AssistantBackgroundColor ?? format.OverlayBackgroundColor,
                ThemeRuntime.Current.GetHex("BgBase"));
        }

        return ThemeRuntime.Current.GetHex("BgBase");
    }

    public static string ResolveHighlightTextBackground(
        string? ruleBackgroundColor,
        string? userSegmentBackground,
        string? assistantSegmentBackground,
        string fallbackCanvas)
    {
        if (!string.IsNullOrWhiteSpace(ruleBackgroundColor))
            return ruleBackgroundColor;

        return fallbackCanvas;
    }

    private static string? GetFormatColor(ContinuousViewFormatSettings format, string propertyName) =>
        typeof(ContinuousViewFormatSettings).GetProperty(propertyName)?.GetValue(format) as string;

    private static string ResolveEffective(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : fallback ?? ThemeRuntime.Current.GetHex("BgBase");

    private static string FirstAvailable(ResolvedTheme theme, params string[] keys)
    {
        foreach (var key in keys)
        {
            var hex = theme.GetHex(key);
            if (!string.IsNullOrWhiteSpace(hex))
                return hex;
        }

        return ThemeRuntime.Current.GetHex("BgBase");
    }
}
