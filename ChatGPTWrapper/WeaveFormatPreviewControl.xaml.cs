using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper;

public partial class WeaveFormatPreviewControl : UserControl
{
    public WeaveFormatPreviewControl()
    {
        InitializeComponent();
    }

    public void ApplySettings(ContinuousViewFormatSettings format)
    {
        var settings = format ?? ContinuousViewFormatSettings.CreateDefaults();
        var weaveCss = FormatCssBuilder.BuildWeaveCssText(settings);
        WeavePreviewRoot.SetValue(FrameworkElement.TagProperty, weaveCss);

        BodySampleText.FontSize = settings.AssistantFontSizeRem * 12;
        BodySampleText.LineHeight = settings.AssistantLineHeight * BodySampleText.FontSize;
        var bodyFont = FormatFontFamilies.ResolveWpfFontFamily(settings.AssistantFontFamily);
        if (bodyFont is not null)
            BodySampleText.FontFamily = bodyFont;

        EmbedSampleText.FontSize = settings.UserFontSizeRem * 12;
        EmbedSampleText.LineHeight = settings.UserLineHeight * EmbedSampleText.FontSize;
        var embedFont = FormatFontFamilies.ResolveWpfFontFamily(settings.UserFontFamily);
        if (embedFont is not null)
            EmbedSampleText.FontFamily = embedFont;

        var accentAdjust = FormatAccentLayout.CenterAdjustPx(settings.UserAccentBorderWidthPx);
        var embedLeftPad = 12 + accentAdjust;

        var accent = ParseBrush(settings.UserAccentColor) ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x8A, 0xFD));
        EmbedBlockquote.BorderBrush = accent;
        EmbedBlockquote.BorderThickness = new Thickness(settings.UserAccentBorderWidthPx, 0, 0, 0);
        EmbedBlockquote.Padding = new Thickness(embedLeftPad, 8, 12, 8);
        EmbedAside.BorderBrush = accent;
        EmbedAside.BorderThickness = new Thickness(settings.UserAccentBorderWidthPx, 0, 0, 0);
        EmbedAside.Padding = new Thickness(embedLeftPad, 10, 12, 10);
        EmbedPullQuote.BorderBrush = accent;
        EmbedRunIn.BorderBrush = accent;

        var embedBg = ParseBrush(settings.UserBackgroundColor);
        if (embedBg is not null)
        {
            EmbedAside.Background = embedBg;
            EmbedRunIn.Background = embedBg;
        }

        var bodyText = ParseBrush(settings.AssistantTextColor);
        if (bodyText is not null)
            BodySampleText.Foreground = bodyText;

        var embedText = ParseBrush(settings.UserTextColor);
        if (embedText is not null)
            EmbedSampleText.Foreground = embedText;

        var margin = settings.WeaveEmbedMarginBlockRem * 4;
        EmbedBlockquote.Margin = new Thickness(accentAdjust, margin, -accentAdjust, margin);
        EmbedAside.Margin = new Thickness(accentAdjust, margin, -accentAdjust, margin);
        EmbedPullQuote.Margin = new Thickness(0, margin, 0, margin);
        EmbedRunIn.Margin = new Thickness(0, margin, 0, margin);

        var kind = settings.WeaveEmbedKind;
        EmbedBlockquote.Visibility = kind is WeaveEmbedKind.Blockquote or WeaveEmbedKind.Auto
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmbedAside.Visibility = kind is WeaveEmbedKind.Aside
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmbedPullQuote.Visibility = kind is WeaveEmbedKind.Auto
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmbedRunIn.Visibility = kind is WeaveEmbedKind.Auto
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static Brush? ParseBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        try
        {
            var normalized = hex.Trim().StartsWith('#') ? hex.Trim() : "#" + hex.Trim();
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalized)!);
        }
        catch
        {
            return null;
        }
    }
}
