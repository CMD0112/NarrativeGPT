using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog
{
    private void BindInjectionPolicyPanel()
    {
        PlayInjectionPolicyService.EnsureDefaults(_bundle.Metadata);
        var s = _bundle.Metadata.Settings;
        var policy = PlayInjectionPolicyService.Resolve(s);

        _suppressInjectionPolicyEvents = true;
        InjectionPresetCombo.ItemsSource = InjectionPresetLibrary.All
            .Select(p => new InjectionPresetComboItem(p.Id, p.DisplayName))
            .Concat([new InjectionPresetComboItem(InjectionPresetIds.Custom, "Custom")])
            .ToList();
        InjectionPresetCombo.DisplayMemberPath = nameof(InjectionPresetComboItem.DisplayName);

        var presetId = policy.InjectionPresetId ?? InjectionPresetIds.Standard;
        InjectionPresetCombo.SelectedItem = InjectionPresetCombo.Items
            .Cast<InjectionPresetComboItem>()
            .FirstOrDefault(i => string.Equals(i.Id, presetId, StringComparison.OrdinalIgnoreCase))
            ?? InjectionPresetCombo.Items.Cast<InjectionPresetComboItem>().First();

        UpdateInjectionPresetHint();

        _suppressMaxPacketSlider = true;
        MaxPacketSlider.Value = Math.Clamp(s.MaxPacketChars, 4000, 50000);
        MaxPacketSliderBox.Text = s.MaxPacketChars.ToString();
        MaxPacketBox.Text = s.MaxPacketChars.ToString();
        _suppressMaxPacketSlider = false;

        IncludeSummaryCheck.IsChecked = policy.IncludeSummary;
        IncludeStateCheck.IsChecked = policy.IncludeState;
        IncludeMemoryCheck.IsChecked = policy.IncludePinnedMemory;
        IncludeTranscriptCheck.IsChecked = policy.IncludeTranscript;
        IncludeCardsCheck.IsChecked = policy.IncludeTriggeredCards;
        IncludeSourcesCheck.IsChecked = policy.IncludeSourcesPointers;
        InjectAttachmentGuidanceInjectionCheck.IsChecked = s.InjectAttachmentGuidance;
        TranscriptMaxTurnsBox.Text = policy.TranscriptMaxTurns.ToString();
        UseContextTagsCheck.IsChecked = s.UseContextTags;
        UseSectionInjectionCheck.IsChecked = s.UseSectionInjection;
        _suppressInjectionPolicyEvents = false;
    }

    private void SaveInjectionPolicyPanel()
    {
        SaveInjectionPolicyTo(_bundle.Metadata.Settings);
    }

    private void SaveInjectionPolicyTo(
        AdventureSettings settings,
        AdventureBundle? contextBundle = null,
        bool syncUi = true)
    {
        settings.InjectionPolicy ??= new PlayInjectionPolicy();
        var policy = settings.InjectionPolicy;

        if (InjectionPresetCombo.SelectedItem is InjectionPresetComboItem presetItem)
            policy.InjectionPresetId = presetItem.Id;

        settings.MaxPacketChars = ReadEffectiveMaxPacketChars();

        policy.IncludeSummary = IncludeSummaryCheck.IsChecked == true;
        policy.IncludeState = IncludeStateCheck.IsChecked == true;
        policy.IncludePinnedMemory = IncludeMemoryCheck.IsChecked == true;
        policy.IncludeTranscript = IncludeTranscriptCheck.IsChecked == true;
        policy.IncludeTriggeredCards = IncludeCardsCheck.IsChecked == true;
        policy.IncludeSourcesPointers = IncludeSourcesCheck.IsChecked == true;
        settings.InjectAttachmentGuidance = InjectAttachmentGuidanceInjectionCheck.IsChecked == true;
        if (int.TryParse(TranscriptMaxTurnsBox.Text, out var turns))
            policy.TranscriptMaxTurns = Math.Max(0, turns);
        settings.UseContextTags = UseContextTagsCheck.IsChecked == true;
        settings.UseSectionInjection = UseSectionInjectionCheck.IsChecked == true;

        var bundle = contextBundle ?? _bundle;
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        InjectionPolicyGuard.EnforceMandatorySections(settings, readiness.CanDelegateStaticContent);

        if (!syncUi || contextBundle is not null && !ReferenceEquals(contextBundle, _bundle))
            return;

        if (!readiness.CanDelegateStaticContent)
            return;

        _suppressInjectionPolicyEvents = true;
        IncludeSourcesCheck.IsChecked = policy.IncludeSourcesPointers;
        IncludeStateCheck.IsChecked = policy.IncludeState;
        _suppressInjectionPolicyEvents = false;
    }

    private void UpdateInjectionPresetHint()
    {
        if (InjectionPresetCombo.SelectedItem is not InjectionPresetComboItem item)
            return;

        var spec = InjectionPresetLibrary.Find(item.Id);
        InjectionPresetHint.Text = spec?.Description
                                   ?? "Custom — manual section and budget settings.";
    }

    private void InjectionPolicy_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressInjectionPolicyEvents)
            return;

        if (sender == InjectionPresetCombo && InjectionPresetCombo.SelectedItem is InjectionPresetComboItem { Id: { } id }
            && !string.Equals(id, InjectionPresetIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            _suppressInjectionPolicyEvents = true;
            PlayInjectionPolicyService.ApplyPreset(_bundle.Metadata.Settings, id);
            BindInjectionPolicyPanel();
            MaxPacketBox.Text = _bundle.Metadata.Settings.MaxPacketChars.ToString();
            _suppressInjectionPolicyEvents = false;
        }
        else if (sender != InjectionPresetCombo)
        {
            PlayInjectionPolicyService.MarkCustom(_bundle.Metadata.Settings);
            SaveInjectionPolicyTo(_bundle.Metadata.Settings, syncUi: false);
            _suppressInjectionPolicyEvents = true;
            InjectionPresetCombo.SelectedItem = InjectionPresetCombo.Items
                .Cast<InjectionPresetComboItem>()
                .First(i => string.Equals(i.Id, InjectionPresetIds.Custom, StringComparison.OrdinalIgnoreCase));
            _suppressInjectionPolicyEvents = false;
        }

        SchedulePreviewRefresh();
    }

    private void MaxPacketSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _suppressMaxPacketSlider)
            return;

        var value = (int)Math.Round(MaxPacketSlider.Value);
        MaxPacketSliderBox.Text = value.ToString();
        MaxPacketBox.Text = value.ToString();
        PlayInjectionPolicyService.MarkCustom(_bundle.Metadata.Settings);
        SchedulePreviewRefresh();
    }

    private void MaxPacketSliderBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        if (!int.TryParse(MaxPacketSliderBox.Text, out var value))
            return;

        value = Math.Clamp(value, 4000, 50000);
        _suppressMaxPacketSlider = true;
        MaxPacketSlider.Value = value;
        MaxPacketSliderBox.Text = value.ToString();
        MaxPacketBox.Text = value.ToString();
        _suppressMaxPacketSlider = false;
        PlayInjectionPolicyService.MarkCustom(_bundle.Metadata.Settings);
        SchedulePreviewRefresh();
    }

    private void InjectAttachmentGuidance_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressInjectionPolicyEvents)
            return;

        var enabled = InjectAttachmentGuidanceInjectionCheck.IsChecked == true;
        _suppressInjectionPolicyEvents = true;
        InjectAttachmentGuidanceCheck.IsChecked = enabled;
        _suppressInjectionPolicyEvents = false;
        InjectionPolicy_Changed(sender, e);
    }

    internal int ReadEffectiveMaxPacketChars()
    {
        if (int.TryParse(MaxPacketSliderBox.Text, out var slider))
            return Math.Clamp(slider, 4000, 50000);
        if (int.TryParse(MaxPacketBox.Text, out var box))
            return Math.Clamp(box, 4000, 50000);
        return Math.Clamp(_bundle.Metadata.Settings.MaxPacketChars, 4000, 50000);
    }

    private void InjectionAffectingCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        SchedulePreviewRefresh();
    }

    private void InjectionAffectingText_Changed(object sender, TextChangedEventArgs e) =>
        SchedulePreviewRefresh();

    private void TurnOverrideSettings_Changed(object sender, RoutedEventArgs e)
    {
        MarkPlaySettingsDirty();
        SchedulePreviewRefresh();
    }

    private void ResetTurnOverrides_Click(object sender, RoutedEventArgs e)
    {
        ResetTurnOverridesUi();
        MarkPlaySettingsDirty();
        SchedulePreviewRefresh();
    }

    private void UtilityDeliverySettings_Changed(object sender, RoutedEventArgs e)
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

        SaveUtilityDeliverySettingsTo(_bundle.Metadata.Settings);
        TransportSettingsStore.Commit(_bundle, caller: nameof(PlayPromptInjectionDialog));
        UpdateUtilityWorkerStatusLine();
        UpdatePlaySettingsSaveUi();
        NotifyTransportSettingsCommitted();
    }

    private void UtilityDeliveryText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _playSettingsBinding)
            return;

        UpdatePlaySettingsSaveUi();
    }

    private sealed class InjectionPresetComboItem(string? id, string displayName)
    {
        public string? Id { get; } = id;

        public string DisplayName { get; } = displayName;
    }
}
