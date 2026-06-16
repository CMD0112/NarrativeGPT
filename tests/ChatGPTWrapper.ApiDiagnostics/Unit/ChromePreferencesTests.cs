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
            ContinuousViewEnabled = true,
            ChromePreferencesRevision = 3,
        };
        var script = ChromePreferencesApplier.BuildApplyScript(settings);
        Assert.Contains("__cgwApplyChromePreferences", script);
        Assert.Contains("\"revision\":3", script);
    }

    [Fact]
    public void Format_css_preview_matches_runtime_variable_names()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 40;
        var css = FormatCssPreview.BuildCssText(format);
        Assert.Contains("--cgw-cv-content-max-width: 40rem", css);
        Assert.Contains("data-cgw-cv-pending=\"1\"", css);
    }
}
