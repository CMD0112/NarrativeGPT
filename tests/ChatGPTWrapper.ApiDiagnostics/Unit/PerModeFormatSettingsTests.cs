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

        settings.ContinuousSettings.HideAssistantEditArtifacts = false;
        settings.NativeSettings.HideAssistantEditArtifacts = true;
        settings.WeaveSettings.HideAssistantEditArtifacts = true;

        settings.TranscriptViewMode = TranscriptViewMode.Native;
        Assert.True(settings.HideAssistantEditArtifacts);

        settings.TranscriptViewMode = TranscriptViewMode.Continuous;
        Assert.False(settings.HideAssistantEditArtifacts);

        settings.TranscriptViewMode = TranscriptViewMode.Weave;
        Assert.True(settings.HideAssistantEditArtifacts);
    }

    [Fact]
    public void Thread_packet_display_policy_syncs_across_view_modes()
    {
        var settings = new UiChromeSettings
        {
            TranscriptViewMode = TranscriptViewMode.Weave,
        };

        settings.HideContextTagsInThread = false;
        settings.ExpandHiddenContextInThread = false;

        Assert.False(settings.NativeSettings.HideContextTagsInThread);
        Assert.False(settings.ContinuousSettings.HideContextTagsInThread);
        Assert.False(settings.WeaveSettings.HideContextTagsInThread);
        Assert.False(settings.NativeSettings.ExpandHiddenContextInThread);
        Assert.False(settings.ContinuousSettings.ExpandHiddenContextInThread);
        Assert.False(settings.WeaveSettings.ExpandHiddenContextInThread);
    }

    [Fact]
    public void Legacy_flat_ui_chrome_json_migrates_into_all_view_modes()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cgw-per-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var prior = AppDirectories.TestRootOverride;
        AppDirectories.ResetStoresForTests();
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
            AppDirectories.ResetStoresForTests();
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
