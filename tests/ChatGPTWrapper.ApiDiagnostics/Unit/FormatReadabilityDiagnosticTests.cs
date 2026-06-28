using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatReadabilityDiagnosticTests
{
    [Fact]
    public void Wide_content_max_width_emits_line_length_suggestion()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 60;

        var warnings = FormatReadabilityAnalyzer.Analyze(format);

        Assert.Contains(warnings, w => w.SettingKey == FormatSettingKeys.ContentMaxWidthRem);
    }

    [Fact]
    public void Clashing_assistant_colors_emit_contrast_warning()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.AssistantTextColor = "#1A1A1C";
        format.AssistantBackgroundColor = "#161618";
        format.AssistantBackgroundOpacity = 100;

        var warnings = FormatReadabilityAnalyzer.Analyze(format);

        Assert.Contains(warnings, w =>
            w.SettingKey == FormatSettingKeys.AssistantTextColor
            && w.Severity == FormatReadabilitySeverity.Warning);
    }

    [Fact]
    public void Long_form_readability_preset_has_no_high_severity_warnings()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ApplyReadabilityPreset(ReadabilityPreset.LongFormReading);

        var warnings = FormatReadabilityAnalyzer.Analyze(format);

        Assert.DoesNotContain(warnings, w => w.Severity == FormatReadabilitySeverity.Error);
    }

    [Fact]
    public void Unreadable_highlight_rule_emits_warning()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        var rules = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "door",
                Color = "#888888",
                BackgroundColor = "#8A8A8A",
                Enabled = true,
            },
        };

        var warnings = FormatReadabilityAnalyzer.Analyze(format, rules, phraseHighlightsEnabled: true);

        Assert.Contains(warnings, w => w.Message.Contains("Highlight rule", StringComparison.Ordinal));
    }
}
