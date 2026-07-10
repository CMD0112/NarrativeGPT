using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper;

public partial class FormatPreviewControl : UserControl
{
    private const string HighlightSampleText = "Mara opens the door. The hinges groan as you lean closer.";

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

        ApplySegment(
            AssistantSegment2,
            AssistantSampleText2,
            AssistantLabel2,
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

        var headingFont = FormatFontFamilies.ResolveWpfFontFamily(settings.HeadingFontFamily)
            ?? FormatFontFamilies.ResolveWpfFontFamily(settings.AssistantFontFamily);
        if (headingFont is not null)
            AssistantHeadingSample.FontFamily = headingFont;
        AssistantHeadingSample.FontSize = settings.AssistantFontSizeRem * settings.HeadingH2ScaleRem * 16;
        AssistantHeadingSample.Foreground = AssistantSampleText.Foreground;
        AssistantHeadingSample.Margin = new Thickness(0, 0, 0, settings.HeadingMarginRem * 16);

        var codeBlockFont = FormatFontFamilies.ResolveWpfFontFamily(settings.CodeFontFamily);
        if (codeBlockFont is not null)
            AssistantCodeBlockText.FontFamily = codeBlockFont;
        AssistantCodeBlockText.FontSize = settings.CodeFontSizeRem * 16;
        AssistantCodeBlockText.LineHeight = settings.CodeFontSizeRem * settings.CodeLineHeight * 16;
        AssistantCodeBlock.Padding = new Thickness(settings.CodeBlockPaddingRem * 16);
        AssistantCodeBlock.CornerRadius = new CornerRadius(settings.CodeBorderRadiusPx);
        var codeBlockBg = ParseColor(settings.CodeBackgroundColor)
            ?? Color.FromArgb(255, 30, 30, 34);
        AssistantCodeBlock.Background = new SolidColorBrush(codeBlockBg);
        var codeBorder = ParseColor(settings.CodeBorderColor) ?? Color.FromArgb(255, 58, 58, 66);
        AssistantCodeBlock.BorderBrush = new SolidColorBrush(codeBorder);

        var linkColor = ParseColor(settings.LinkColor) ?? Blend(DefaultText, DefaultUserAccent, 0.32);
        AssistantLinkRun.Foreground = new SolidColorBrush(linkColor);

        var inlineBg = ParseColor(settings.InlineCodeBackgroundColor)
            ?? Color.FromArgb(26, DefaultText.R, DefaultText.G, DefaultText.B);
        AssistantCodeRun.Background = new SolidColorBrush(inlineBg);
        var codeFont = FormatFontFamilies.ResolveWpfFontFamily(settings.CodeFontFamily);
        if (codeFont is not null)
            AssistantCodeRun.FontFamily = codeFont;
    }

    public void ApplyPhraseHighlights(
        IReadOnlyList<PhraseHighlightRule>? rules,
        bool enabled,
        int roleBaseFontWeight = 400)
    {
        AssistantHighlightSampleText.Inlines.Clear();
        if (!enabled || rules is null || rules.Count == 0)
        {
            AssistantHighlightSampleText.Inlines.Add(new Run(HighlightSampleText));
            return;
        }

        var activeRules = rules
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Phrase))
            .OrderByDescending(r => r.Phrase.Length)
            .ToList();
        var canvas = ThemeRuntime.Current.GetHex("BgBase");
        var segments = new List<(int Start, int Length, PhraseHighlightRule Rule)>();
        var text = HighlightSampleText;

        foreach (var rule in activeRules)
        {
            var phrase = rule.Phrase.Trim();
            if (phrase.Length == 0)
                continue;

            var compiled = PhraseHighlightMatching.CompileRule(phrase, rule.EntityId);
            foreach (var match in PhraseHighlightMatching.FindMatches(text, compiled))
                segments.Add((match.Start, match.End - match.Start, rule));
        }

        if (segments.Count == 0)
        {
            AssistantHighlightSampleText.Inlines.Add(new Run(text));
            return;
        }

        segments = segments
            .OrderBy(s => s.Start)
            .ThenByDescending(s => s.Length)
            .ToList();

        var cursor = 0;
        foreach (var segment in segments)
        {
            if (segment.Start < cursor)
                continue;

            if (segment.Start > cursor)
            {
                AssistantHighlightSampleText.Inlines.Add(new Run(text[cursor..segment.Start]));
            }

            var matched = text.Substring(segment.Start, segment.Length);
            var sanitized = PhraseHighlightRuleService.SanitizeForInjection(segment.Rule, canvas);
            var run = new Run(matched)
            {
                Foreground = TryBrush(sanitized.Color),
                FontWeight = FontWeight.FromOpenTypeWeight(
                    FormatHighlightComposition.ResolveRuleFontWeight(sanitized, roleBaseFontWeight)),
                FontStyle = sanitized.Italic ? FontStyles.Italic : FontStyles.Normal,
            };
            if (sanitized.Underline || sanitized.Strikethrough)
            {
                run.TextDecorations = new TextDecorationCollection();
                if (sanitized.Underline)
                    run.TextDecorations.Add(TextDecorations.Underline);
                if (sanitized.Strikethrough)
                    run.TextDecorations.Add(TextDecorations.Strikethrough);
            }

            if (sanitized.FontSizeScale is double scale && Math.Abs(scale - 1.0) > 0.001)
                run.FontSize = AssistantHighlightSampleText.FontSize * scale;
            if (!string.IsNullOrWhiteSpace(sanitized.FontFamily))
            {
                var family = FormatFontFamilies.ResolveWpfFontFamily(sanitized.FontFamily);
                if (family is not null)
                    run.FontFamily = family;
            }

            if (!string.IsNullOrWhiteSpace(sanitized.BackgroundColor))
            {
                var bg = ParseColor(sanitized.BackgroundColor);
                if (bg is not null)
                    run.Background = new SolidColorBrush(bg.Value);
            }
            AssistantHighlightSampleText.Inlines.Add(run);
            cursor = segment.Start + segment.Length;
        }

        if (cursor < text.Length)
            AssistantHighlightSampleText.Inlines.Add(new Run(text[cursor..]));
    }

    private static Brush TryBrush(string? hex)
    {
        var color = ParseColor(hex);
        return color is null ? new SolidColorBrush(DefaultText) : new SolidColorBrush(color.Value);
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
