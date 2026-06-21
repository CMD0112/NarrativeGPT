using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper;

public partial class FormatPreviewControl : UserControl
{
    private static readonly Color DefaultUserAccent = (Color)ColorConverter.ConvertFromString("#5B9FD4")!;
    private static readonly Color DefaultText = (Color)ColorConverter.ConvertFromString("#EDEDF0")!;

    public FormatPreviewControl()
    {
        InitializeComponent();
    }

    public void ApplySettings(ContinuousViewFormatSettings settings)
    {
        ApplySegment(
            UserSegment,
            UserSampleText,
            UserLabel,
            settings.UserFontSizeRem * 16,
            settings.UserLineHeight,
            settings.UserLetterSpacingEm,
            settings.UserFontWeight,
            settings.UserTextColor,
            settings.UserBackgroundColor,
            settings.UserBackgroundOpacity,
            settings.UserBorderColor ?? settings.UserAccentColor,
            settings.UserAccentBorderWidthPx,
            settings.UserIndentRem,
            settings.UserFontFamily,
            settings.ShowRoleLabels,
            true);

        ApplySegment(
            AssistantSegment,
            AssistantSampleText,
            AssistantLabel,
            settings.AssistantFontSizeRem * 16,
            settings.AssistantLineHeight,
            settings.AssistantLetterSpacingEm,
            settings.AssistantFontWeight,
            settings.AssistantTextColor,
            settings.AssistantBackgroundColor,
            settings.AssistantBackgroundOpacity,
            settings.AssistantBorderColor ?? settings.AssistantAccentColor,
            settings.AssistantAccentBorderWidthPx,
            settings.AssistantIndentRem,
            settings.AssistantFontFamily,
            settings.ShowRoleLabels,
            false);

        var linkColor = ParseColor(settings.LinkColor) ?? Blend(DefaultText, DefaultUserAccent, 0.32);
        AssistantLinkRun.Foreground = new SolidColorBrush(linkColor);

        var inlineBg = ParseColor(settings.InlineCodeBackgroundColor)
            ?? Color.FromArgb(26, DefaultText.R, DefaultText.G, DefaultText.B);
        AssistantCodeRun.Background = new SolidColorBrush(inlineBg);
        var codeFont = FormatFontFamilies.ResolveWpfFontFamily(settings.CodeFontFamily);
        if (codeFont is not null)
            AssistantCodeRun.FontFamily = codeFont;
    }

    private static void ApplySegment(
        Border border,
        TextBlock sampleText,
        TextBlock label,
        double fontSizePx,
        double lineHeight,
        double letterSpacingEm,
        int fontWeight,
        string? textColor,
        string? backgroundColor,
        double backgroundOpacity,
        string? borderColor,
        double borderWidthPx,
        double indentRem,
        string? fontFamily,
        bool showRoleLabels,
        bool isUser)
    {
        var text = ParseColor(textColor) ?? DefaultText;
        sampleText.Foreground = new SolidColorBrush(text);
        sampleText.FontSize = fontSizePx;
        sampleText.LineHeight = fontSizePx * lineHeight;
        sampleText.FontWeight = FontWeight.FromOpenTypeWeight(fontWeight);
        var wpfFont = FormatFontFamilies.ResolveWpfFontFamily(fontFamily);
        if (wpfFont is not null)
            sampleText.FontFamily = wpfFont;

        var bgBase = ParseColor(backgroundColor)
            ?? (isUser ? DefaultUserAccent : DefaultText);
        var alpha = (byte)Math.Clamp(Math.Round(backgroundOpacity / 100.0 * 255), 0, 255);
        border.Background = new SolidColorBrush(Color.FromArgb(alpha, bgBase.R, bgBase.G, bgBase.B));

        var accent = ParseColor(borderColor) ?? (isUser ? DefaultUserAccent : Color.FromArgb(48, DefaultText.R, DefaultText.G, DefaultText.B));
        border.BorderBrush = new SolidColorBrush(accent);
        border.BorderThickness = new Thickness(borderWidthPx, 0, 0, 0);
        var accentAdjust = FormatAccentLayout.CenterAdjustPx(borderWidthPx);
        border.Margin = new Thickness(accentAdjust, 0, -accentAdjust, 0);
        border.Padding = new Thickness(10 + indentRem * 16, 8, 8, 8);
        border.CornerRadius = new CornerRadius(6);

        label.Visibility = showRoleLabels ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var trimmed = hex.Trim();
        try
        {
            if (trimmed.StartsWith('#'))
                return (Color)ColorConverter.ConvertFromString(trimmed)!;

            if (trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                return null;

            return (Color)ColorConverter.ConvertFromString("#" + trimmed.TrimStart('#'))!;
        }
        catch
        {
            return null;
        }
    }

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(a.R * (1 - amount) + b.R * amount),
            (byte)Math.Round(a.G * (1 - amount) + b.G * amount),
            (byte)Math.Round(a.B * (1 - amount) + b.B * amount));
    }
}
