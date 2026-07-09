using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog
{
    private PlaySettingsEditorBaseline _persistedBaseline = null!;

    private void CapturePersistedBaseline()
    {
        InjectionNarratorPanel.FlushToSession();
        FlushPlaySettingsUiToWorkingBundle();
        _persistedBaseline = PlaySettingsEditorBaseline.Capture(
            _bundle,
            _chromeSettings,
            ReadPreviewPlayerLineForBaseline(),
            _narratorSession.Bundle.Metadata.Settings);
    }

    private void RefreshTransportPersistedBaseline()
    {
        var meta = AdventureStore.ReadMetadataFromDisk(_bundle.Metadata.Id);
        if (meta?.Settings is null)
            return;

        _persistedBaseline = _persistedBaseline.WithPersistedSettings(meta.Settings);
    }

    /// <summary>
    /// Copies all play-settings UI domains into the working bundle (not chrome disk store).
    /// </summary>
    private void FlushPlaySettingsUiToWorkingBundle()
    {
        if (!IsLoaded)
            return;

        SaveQueueAndPreviewLine();
        SaveWorldPanel();
        SaveAdventureSettings();
        SaveAutomationSettingsTo(_bundle.Metadata.Settings);
        SaveAutomationContextFromGrid(_bundle);
        SaveThreadSnapshotSettingsTo(_bundle.Metadata.Settings);
        SaveUtilityDeliverySettings();
        SaveTurnOverrideSettings();
        SaveInjectionPolicyPanel();
        SavePlaySurfaceSettings();
        FlushPlayChromeSettingsToMemory();
        SaveNarratorBehaviorSettings();
        FlushCurrentAiActionEdits();
        SaveAiActionGuides();
        SaveStoryContextSettings();
    }

    private void FlushPlayChromeSettingsToMemory()
    {
        var chrome = _chromeSettings.PlaySurface;
        if (PlayCompanionOnEnterCombo.SelectedItem is PlayChromeComboItem onEnter)
            chrome.PlayCompanionOnEnter = onEnter.Id;
        if (PlayCompanionDefaultTabCombo.SelectedItem is string defaultTab)
            chrome.PlayCompanionDefaultTab = defaultTab;
        chrome.PlayCompanionRememberExpanders = PlayCompanionRememberExpandersCheck.IsChecked == true;
        if (PlayCompanionDefaultSectionCombo.SelectedItem is string defaultSection)
            chrome.PlayCompanionDefaultSection = defaultSection;
        if (NarratorPanelDensityCombo.SelectedItem is string narratorDensity)
        {
            chrome.NarratorPanelDensity = narratorDensity;
            if (_bundle is not null
                && (string.Equals(narratorDensity, "Minimal", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(narratorDensity, "Full", StringComparison.OrdinalIgnoreCase)))
            {
                _bundle.Metadata.Settings.PlayCompanionLastNarratorDensity = narratorDensity;
            }
        }

        if (AiToolsLayoutCombo.SelectedItem is PlayChromeComboItem layout)
            chrome.AiToolsLayout = layout.Id;
    }

    private void CommitTransportSettingsFromUi()
    {
        if (!IsLoaded || _playSettingsBinding)
            return;

        if (LocalUtilityInferenceDualRunCheck is not null)
        {
            LocalUtilityInferenceDualRunCheck.IsEnabled = LocalUtilityInferenceCheck.IsChecked == true;
            if (LocalUtilityInferenceCheck.IsChecked != true)
                LocalUtilityInferenceDualRunCheck.IsChecked = false;
        }

        if (ForceUtilityWorkerDomAttachCheck is not null)
        {
            ForceUtilityWorkerDomAttachCheck.IsEnabled = UseEphemeralUtilityWorkerChatCheck.IsChecked == true;
            if (UseEphemeralUtilityWorkerChatCheck.IsChecked != true)
                ForceUtilityWorkerDomAttachCheck.IsChecked = false;
        }

        ApplyMaxParallelUtilityWorkerJobsUi(UseEphemeralUtilityWorkerChatCheck.IsChecked == true);

        FlushPlaySettingsUiToWorkingBundle();
        TransportSettingsStore.Commit(_bundle, caller: nameof(PlayPromptInjectionDialog));
        RefreshTransportPersistedBaseline();
        UpdateUtilityWorkerStatusLine();
        UpdatePlaySettingsSaveUi();
        NotifyTransportSettingsCommitted();
    }

    private string ReadPreviewPlayerLineForBaseline() =>
        string.IsNullOrWhiteSpace(PreviewPlayerLinePanelBox.Text)
            ? PreviewPlayerLineBox.Text.Trim()
            : PreviewPlayerLinePanelBox.Text.Trim();

    private IReadOnlyList<string> BuildStagingEditsSummary()
    {
        FlushPlaySettingsUiToWorkingBundle();
        InjectionNarratorPanel.FlushToSession();
        return _persistedBaseline.Diff(
            _bundle,
            _chromeSettings,
            ReadPreviewPlayerLineForBaseline(),
            _narratorSession.Bundle.Metadata.Settings);
    }

    private bool HasUnsavedPlaySettings() =>
        BuildStagingEditsSummary().Count > 0;

    private void RefreshPersistedBaselineAfterSave()
    {
        CapturePersistedBaseline();
        _previewPlayerLineBaseline = ReadPreviewPlayerLineForBaseline();
    }
}
