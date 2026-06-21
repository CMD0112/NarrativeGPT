using System.Reflection;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper;

internal static class FormatCssBuilder
{
    public static string BuildCssText(ContinuousViewFormatSettings settings)
    {
        var active = "html[data-cgw-continuous-view=\"1\"] #cgw-continuous-view";
        var pending = "html[data-cgw-continuous-view=\"1\"][data-cgw-cv-pending=\"1\"] #cgw-continuous-view";
        return BuildBlock(active, settings) + BuildBlock(pending, settings) + BuildWeaveCssText(settings);
    }

    public static string BuildWeaveCssText(ContinuousViewFormatSettings settings)
    {
        var s = settings ?? ContinuousViewFormatSettings.CreateDefaults();
        var selector = "html[data-cgw-transcript-mode=\"weave\"] #cgw-continuous-view.cgw-weave-view";
        var embedKind = s.WeaveEmbedKind switch
        {
            WeaveEmbedKind.Aside => "aside",
            WeaveEmbedKind.Auto => "auto",
            _ => "blockquote",
        };
        var lines = new List<string>
        {
            "  --cgw-weave-content-max-width: " + s.ContentMaxWidthRem + "rem",
            "  --cgw-weave-paragraph-gap: " + s.ProseParagraphMarginRem + "rem",
            "  --cgw-weave-embed-margin-block: " + s.WeaveEmbedMarginBlockRem + "rem",
            "  --cgw-weave-body-font-size: " + s.AssistantFontSizeRem + "rem",
            "  --cgw-weave-body-line-height: " + s.AssistantLineHeight,
            "  --cgw-weave-body-letter-spacing: " + s.AssistantLetterSpacingEm + "em",
            "  --cgw-weave-body-font-weight: " + s.AssistantFontWeight,
            "  --cgw-weave-embed-font-size: " + s.UserFontSizeRem + "rem",
            "  --cgw-weave-embed-line-height: " + s.UserLineHeight,
            "  --cgw-weave-embed-letter-spacing: " + s.UserLetterSpacingEm + "em",
            "  --cgw-weave-embed-font-weight: " + s.UserFontWeight,
            "  --cgw-weave-embed-accent-width: " + s.UserAccentBorderWidthPx + "px",
            "  --cgw-weave-embed-accent-center-adjust: " + FormatAccentLayout.CenterAdjustPx(s.UserAccentBorderWidthPx) + "px",
            "  --cgw-weave-embed-kind-preset: " + embedKind,
        };

        AppendFontFamily(lines, "--cgw-weave-body-font-family", s.AssistantFontFamily);
        AppendFontFamily(lines, "--cgw-weave-embed-font-family", s.UserFontFamily);

        if (!string.IsNullOrWhiteSpace(s.AssistantTextColor))
            lines.Add("  --cgw-weave-body-text: " + s.AssistantTextColor.Trim());
        if (!string.IsNullOrWhiteSpace(s.UserTextColor))
            lines.Add("  --cgw-weave-embed-text: " + s.UserTextColor.Trim());
        if (!string.IsNullOrWhiteSpace(s.UserBackgroundColor))
        {
            lines.Add("  --cgw-weave-embed-bg: " + s.UserBackgroundColor.Trim());
            lines.Add("  --cgw-weave-embed-aside-bg: " + s.UserBackgroundColor.Trim());
        }
        if (!string.IsNullOrWhiteSpace(s.UserAccentColor))
            lines.Add("  --cgw-weave-embed-accent: " + s.UserAccentColor.Trim());

        return selector + " {\n" + string.Join(";\n", lines) + ";\n}\n";
    }

    public static IReadOnlyList<string> ListEmittedCssVariableNames(ContinuousViewFormatSettings settings)
    {
        var css = BuildBlock("sel", settings);
        var names = new List<string>();
        foreach (var line in css.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("--cgw-cv-", StringComparison.Ordinal))
                continue;
            var end = trimmed.IndexOf(':');
            if (end > 0)
                names.Add(trimmed[..end].Trim());
        }

        return names;
    }

    private static string BuildBlock(string selector, ContinuousViewFormatSettings s)
    {
        var borderWidth = s.ShowSegmentDividers ? "1px" : "0";
        var lines = new List<string>
        {
            "  --cgw-cv-overlay-px: " + s.OverlayPaddingXRem + "rem",
            "  --cgw-cv-overlay-py: " + s.OverlayPaddingYRem + "rem",
            "  --cgw-cv-content-max-width: " + s.ContentMaxWidthRem + "rem",
            "  --cgw-cv-segment-spacing: " + s.SegmentSpacingRem + "rem",
            "  --cgw-cv-segment-border-width: " + borderWidth,
            "  --cgw-cv-segment-divider-opacity: " + s.SegmentDividerOpacity,
            "  --cgw-cv-segment-border-radius: " + s.SegmentBorderRadiusPx + "px",
            "  --cgw-cv-block-margin: " + s.BlockMarginRem + "rem",
            "  --cgw-cv-prose-p-margin: " + s.ProseParagraphMarginRem + "rem",
            "  --cgw-cv-user-font-size: " + s.UserFontSizeRem + "rem",
            "  --cgw-cv-user-line-height: " + s.UserLineHeight,
            "  --cgw-cv-user-letter-spacing: " + s.UserLetterSpacingEm + "em",
            "  --cgw-cv-user-font-weight: " + s.UserFontWeight,
            "  --cgw-cv-user-accent-border-width: " + s.UserAccentBorderWidthPx + "px",
            "  --cgw-cv-user-accent-center-adjust: " + FormatAccentLayout.CenterAdjustPx(s.UserAccentBorderWidthPx) + "px",
            "  --cgw-cv-user-indent: " + s.UserIndentRem + "rem",
            "  --cgw-cv-user-bg-opacity: " + s.UserBackgroundOpacity,
            "  --cgw-cv-assistant-font-size: " + s.AssistantFontSizeRem + "rem",
            "  --cgw-cv-assistant-line-height: " + s.AssistantLineHeight,
            "  --cgw-cv-assistant-letter-spacing: " + s.AssistantLetterSpacingEm + "em",
            "  --cgw-cv-assistant-font-weight: " + s.AssistantFontWeight,
            "  --cgw-cv-assistant-accent-border-width: " + s.AssistantAccentBorderWidthPx + "px",
            "  --cgw-cv-assistant-accent-center-adjust: " + FormatAccentLayout.CenterAdjustPx(s.AssistantAccentBorderWidthPx) + "px",
            "  --cgw-cv-assistant-indent: " + s.AssistantIndentRem + "rem",
            "  --cgw-cv-assistant-bg-opacity: " + s.AssistantBackgroundOpacity,
            "  --cgw-cv-enhanced-prose-line-height: " + s.EnhancedProseLineHeight,
            "  --cgw-cv-enhanced-prose-letter-spacing: " + s.EnhancedProseLetterSpacingEm + "em",
            "  --cgw-cv-code-font-size: " + s.CodeFontSizeRem + "rem",
            "  --cgw-cv-code-line-height: " + s.CodeLineHeight,
            "  --cgw-cv-code-block-padding: " + s.CodeBlockPaddingRem + "rem",
            "  --cgw-cv-code-border-radius: " + s.CodeBorderRadiusPx + "px",
            "  --cgw-cv-heading-margin: " + s.HeadingMarginRem + "rem",
            "  --cgw-cv-heading-h1: " + s.HeadingH1ScaleRem + "rem",
            "  --cgw-cv-heading-h2: " + s.HeadingH2ScaleRem + "rem",
            "  --cgw-cv-heading-h3: " + s.HeadingH3ScaleRem + "rem",
            "  --cgw-cv-heading-h4: " + s.HeadingH4ScaleRem + "rem",
            "  --cgw-cv-heading-h5: " + s.HeadingH5ScaleRem + "rem",
            "  --cgw-cv-heading-h6: " + s.HeadingH6ScaleRem + "rem",
        };

        foreach (var token in FormatTokenCatalog.ColorTokens)
        {
            var value = GetColorProperty(s, token.SettingsProperty);
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add("  " + token.CssVariable + ": " + value.Trim());
        }

        AppendFontFamily(lines, "--cgw-cv-user-font-family", s.UserFontFamily);
        AppendFontFamily(lines, "--cgw-cv-assistant-font-family", s.AssistantFontFamily);
        AppendFontFamily(lines, "--cgw-cv-code-font-family", s.CodeFontFamily);
        AppendFontFamily(lines, "--cgw-cv-heading-font-family", s.HeadingFontFamily);

        return selector + " {\n" + string.Join(";\n", lines) + ";\n}\n";
    }

    private static void AppendFontFamily(List<string> lines, string cssVariable, string? stored)
    {
        var stack = FormatFontFamilies.ResolveCssStack(stored);
        if (!string.IsNullOrWhiteSpace(stack))
            lines.Add("  " + cssVariable + ": " + stack);
    }

    private static string? GetColorProperty(ContinuousViewFormatSettings settings, string propertyName)
    {
        var prop = typeof(ContinuousViewFormatSettings).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return prop?.GetValue(settings) as string;
    }
}
