using ChatGPTWrapper;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class ChromePreferencesTests
{
    [Fact]
    public void Chrome_preferences_asset_defines_unified_applier()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("chrome-preferences.js");
        Assert.Contains("__cgwApplyChromePreferences", js);
        Assert.Contains("classifyImpact", js);
        Assert.Contains("__cgwSetTranscriptViewMode", js);
        Assert.Contains("transcriptViewMode", js);
        Assert.Contains("force: true", js);
    }

    [Fact]
    public void Format_settings_clears_composer_clearance_globals_at_zero()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-format-settings.js");
        Assert.Contains("delete globalThis.__cgwComposerClearanceMinPx", js);
        Assert.Contains("delete globalThis.__cgwComposerClearanceMaxPx", js);
        Assert.Contains("data-cgw-cv-pending=\"1\"", js);
    }

    [Fact]
    public void Continuous_view_reads_composer_clearance_dynamically()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-transcript-view.js");
        Assert.Contains("readComposerClearanceMinPx", js);
        Assert.Contains("readComposerClearanceMaxPx", js);
        Assert.Contains("__cgwUpdateComposerClearance", js);
        Assert.Contains("__cgwFormatSettingsRevision", js);
        Assert.Contains("__cgwScheduleContinuousViewRebuild", js);
    }

    [Fact]
    public void BuildApplyScript_uses_unified_applier()
    {
        var settings = new UiChromeSettings
        {
            TranscriptViewMode = TranscriptViewMode.Weave,
            ChromePreferencesRevision = 3,
        };
        var script = ChromePreferencesApplier.BuildApplyScript(settings);
        Assert.Contains("__cgwApplyChromePreferences", script);
        Assert.Contains("\"revision\":3", script);
        Assert.Contains("\"transcriptViewMode\":\"weave\"", script);
    }

    [Fact]
    public void Ui_chrome_migrates_continuous_view_enabled_to_transcript_mode()
    {
        var path = Path.Combine(Path.GetTempPath(), "ui-chrome-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{"continuousViewEnabled":true}""");
            var json = File.ReadAllText(path);
            var settings = new UiChromeSettings();
            TranscriptViewModeMigration.ApplyFromJson(settings, json);
            TranscriptViewModeMigration.Normalize(settings);
            Assert.Equal(TranscriptViewMode.Continuous, settings.TranscriptViewMode);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Format_css_preview_matches_runtime_variable_names()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 40;
        var css = FormatCssPreview.BuildCssText(format);
        Assert.Contains("--cgw-cv-content-max-width: 40rem", css);
        Assert.Contains("data-cgw-cv-pending=\"1\"", css);
        Assert.Contains("--cgw-cv-user-letter-spacing:", css);
        Assert.Contains("--cgw-cv-assistant-letter-spacing:", css);
        Assert.DoesNotContain("--cgw-cv-block-font-size", css);
    }
}
