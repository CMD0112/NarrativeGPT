using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Format;

public static class ColorPickerContextFactory
{
    public static ColorPickerContext ForThemeToken(string tokenKey, string backgroundHex) =>
        new()
        {
            Kind = ColorPickerTargetKind.ThemeToken,
            TargetKey = tokenKey,
            ContextBackgroundHex = backgroundHex,
            ThemeTextPrimaryHex = ThemeRuntime.Current.GetHex("TextPrimary"),
            ThemeTextMutedHex = ThemeRuntime.Current.GetHex("TextMuted"),
            ThemeAccentHex = ThemeRuntime.Current.GetHex("AccentPrimary"),
        };

    public static ColorPickerContext ForFormatColor(
        string propertyName,
        ContinuousViewFormatSettings format,
        string backgroundHex)
    {
        var theme = ThemeRuntime.Current;
        var assistantText = ResolveColor(format.AssistantTextColor, theme.GetHex("TextPrimary"));
        var userText = ResolveColor(format.UserTextColor, theme.GetHex("TextPrimary"));
        var assistantAccent = ResolveColor(format.AssistantAccentColor, theme.GetHex("AccentPrimary"));
        var userAccent = ResolveColor(format.UserAccentColor, theme.GetHex("AccentPrimary"));

        return new ColorPickerContext
        {
            Kind = ColorPickerTargetKind.FormatColor,
            TargetKey = propertyName,
            FormatSettings = format,
            ContextBackgroundHex = backgroundHex,
            ProseCanvasHex = backgroundHex,
            AssistantTextHex = assistantText,
            UserTextHex = userText,
            AssistantAccentHex = assistantAccent,
            UserAccentHex = userAccent,
            ThemeTextPrimaryHex = theme.GetHex("TextPrimary"),
            ThemeTextMutedHex = theme.GetHex("TextMuted"),
            ThemeAccentHex = theme.GetHex("AccentPrimary"),
            PairedTextHex = ResolvePairedText(propertyName, assistantText, userText),
            RuledLineOpacity = format.RuledLineOpacity,
            RuledBandOpacity = format.RuledBandOpacity,
            ReadingGuideStyle = format.RuledLineStyle,
        };
    }

    public static ColorPickerContext ForHighlightText(
        string? ruleBackgroundHex,
        string canvasHex,
        string? currentTextHex)
    {
        var theme = ThemeRuntime.Current;
        return new ColorPickerContext
        {
            Kind = ColorPickerTargetKind.HighlightText,
            TargetKey = "HighlightText",
            ContextBackgroundHex = canvasHex,
            ProseCanvasHex = canvasHex,
            PairedTextHex = currentTextHex,
            ThemeTextPrimaryHex = theme.GetHex("TextPrimary"),
            ThemeAccentHex = theme.GetHex("AccentPrimary"),
        };
    }

    public static ColorPickerContext ForHighlightBackground(string canvasHex) =>
        new()
        {
            Kind = ColorPickerTargetKind.HighlightBackground,
            TargetKey = "HighlightBackground",
            ContextBackgroundHex = canvasHex,
            ProseCanvasHex = canvasHex,
            ThemeTextPrimaryHex = ThemeRuntime.Current.GetHex("TextPrimary"),
            ThemeAccentHex = ThemeRuntime.Current.GetHex("AccentPrimary"),
        };

    public static ColorPickerContext ForGeneric(string backgroundHex) =>
        new()
        {
            Kind = ColorPickerTargetKind.Generic,
            ContextBackgroundHex = backgroundHex,
            ThemeTextPrimaryHex = ThemeRuntime.Current.GetHex("TextPrimary"),
            ThemeAccentHex = ThemeRuntime.Current.GetHex("AccentPrimary"),
        };

    private static string? ResolvePairedText(string propertyName, string assistantText, string userText) =>
        propertyName switch
        {
            nameof(ContinuousViewFormatSettings.UserTextColor)
                or nameof(ContinuousViewFormatSettings.UserBackgroundColor)
                or nameof(ContinuousViewFormatSettings.UserAccentColor)
                or nameof(ContinuousViewFormatSettings.UserBorderColor) => userText,
            nameof(ContinuousViewFormatSettings.AssistantTextColor)
                or nameof(ContinuousViewFormatSettings.AssistantBackgroundColor)
                or nameof(ContinuousViewFormatSettings.AssistantAccentColor)
                or nameof(ContinuousViewFormatSettings.AssistantBorderColor) => assistantText,
            _ => assistantText,
        };

    private static string ResolveColor(string? custom, string fallback) =>
        string.IsNullOrWhiteSpace(custom) ? fallback : custom.Trim();
}
