using ChatGPTWrapper.WinUiBridge;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Merges format/theme dialog settings into ui-chrome and applies live preview to WinUI WebViews.</summary>
internal static class WinUiChromeSettingsApplier
{
    public static void Apply(UiChromeSettings incoming, bool persist, int? previewRevision = null)
    {
        var chrome = UiChromeStore.Load();
        Merge(incoming, chrome);

        if (persist)
            UiChromeStore.Save(chrome);

        var revision = previewRevision ?? chrome.ChromePreferencesRevision;
        _ = WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            await WinUiTranscriptViewCoordinator.ApplyPreviewAsync(chrome, revision);
            App.CurrentMainWindow?.RefreshShellChromeFromThemeChange();
        });
    }

    private static void Merge(UiChromeSettings incoming, UiChromeSettings target)
    {
        TranscriptViewModeSettingsExtensions.CopyAllModeSettings(incoming, target);
        target.TranscriptViewMode = incoming.TranscriptViewMode;
        target.ActiveHighlightColorProfileId = incoming.ActiveHighlightColorProfileId;
        target.HighlightColorProfiles = incoming.HighlightColorProfiles.Select(p => p.Clone()).ToList();
        target.HighlightColorCustomOptions = incoming.HighlightColorCustomOptions.Clone();
        target.ActiveHighlightColorGroupingProfileId = incoming.ActiveHighlightColorGroupingProfileId;
        target.HighlightColorGroupingProfiles = incoming.HighlightColorGroupingProfiles.Select(p => p.Clone()).ToList();
        target.HighlightColorGroupingCustomProfile = incoming.HighlightColorGroupingCustomProfile.Clone();
        target.ChromePreferencesRevision = incoming.ChromePreferencesRevision;
    }
}
