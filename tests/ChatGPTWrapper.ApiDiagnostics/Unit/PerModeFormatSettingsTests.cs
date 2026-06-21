using ChatGPTWrapper;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PerModeFormatSettingsTests
{
    [Fact]
    public void Each_view_mode_stores_independent_format_preferences()
    {
        var settings = new UiChromeSettings
        {
            TranscriptViewMode = TranscriptViewMode.Continuous,
        };

        settings.ContinuousSettings.HideContextTagsInThread = false;
        settings.NativeSettings.HideContextTagsInThread = true;
        settings.WeaveSettings.HideContextTagsInThread = true;

        settings.TranscriptViewMode = TranscriptViewMode.Native;
        Assert.True(settings.HideContextTagsInThread);

        settings.TranscriptViewMode = TranscriptViewMode.Continuous;
        Assert.False(settings.HideContextTagsInThread);

        settings.TranscriptViewMode = TranscriptViewMode.Weave;
        Assert.True(settings.HideContextTagsInThread);
    }

    [Fact]
    public void Legacy_flat_ui_chrome_json_migrates_into_all_view_modes()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cgw-per-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var prior = AppDirectories.TestRootOverride;
        AppDirectories.TestRootOverride = temp;

        try
        {
            File.WriteAllText(
                Path.Combine(temp, "ui-chrome.json"),
                """
                {
                  "hideContextTagsInThread": false,
                  "continuousViewFormat": { "contentMaxWidthRem": 55 }
                }
                """);

            var loaded = UiChromeStore.Load();

            Assert.False(loaded.NativeSettings.HideContextTagsInThread);
            Assert.False(loaded.ContinuousSettings.HideContextTagsInThread);
            Assert.False(loaded.WeaveSettings.HideContextTagsInThread);
            Assert.Equal(55, loaded.NativeSettings.ContinuousViewFormat.ContentMaxWidthRem);
            Assert.Equal(55, loaded.ContinuousSettings.ContinuousViewFormat.ContentMaxWidthRem);
            Assert.Equal(55, loaded.WeaveSettings.ContinuousViewFormat.ContentMaxWidthRem);
        }
        finally
        {
            AppDirectories.TestRootOverride = prior;
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Chrome_payload_uses_active_view_mode_settings()
    {
        var settings = new UiChromeSettings
        {
            TranscriptViewMode = TranscriptViewMode.Weave,
        };
        settings.WeaveSettings.HideContextTagsInThread = false;
        settings.NativeSettings.HideContextTagsInThread = true;

        var payload = ChromePreferencesApplier.ToPayload(settings);

        Assert.Equal("weave", payload.TranscriptViewMode);
        Assert.False(payload.HideContextTagsInThread);
    }
}
