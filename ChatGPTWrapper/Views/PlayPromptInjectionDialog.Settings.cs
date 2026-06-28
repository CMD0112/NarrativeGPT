using System.Text.Json;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog
{
    private static readonly NarratorParameter[] TurnOverrideParameters =
    [
        NarratorParameter.ResponseLength,
        NarratorParameter.DetailLevel,
        NarratorParameter.Tone,
        NarratorParameter.Difficulty,
    ];

    private IReadOnlyDictionary<NarratorParameter, ComboBox> TurnOverrideCombos =>
        new Dictionary<NarratorParameter, ComboBox>
        {
            [NarratorParameter.ResponseLength] = TurnOverrideResponseLengthCombo,
            [NarratorParameter.DetailLevel] = TurnOverrideDetailLevelCombo,
            [NarratorParameter.Tone] = TurnOverrideToneCombo,
            [NarratorParameter.Difficulty] = TurnOverrideDifficultyCombo,
        };

    private void BindNextSendPanel()
    {
        foreach (var (parameter, combo) in TurnOverrideCombos)
        {
            var isEditable = parameter is NarratorParameter.Tone or NarratorParameter.Difficulty;
            NarratorControlsService.PopulateCombo(combo, _bundle, parameter, NarratorOverrideScope.Turn, isEditable);
        }
    }

    private void SaveTurnOverrideSettingsTo(AdventureSettings settings)
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = settings },
        };

        foreach (var (parameter, combo) in TurnOverrideCombos)
            NarratorControlsService.SaveComboValue(bundle, combo, parameter, NarratorOverrideScope.Turn);
    }

    private void SaveTurnOverrideSettings() =>
        SaveTurnOverrideSettingsTo(_bundle.Metadata.Settings);

    private bool HasTurnOverrideChanges()
    {
        if (!IsLoaded)
            return false;

        var staging = new AdventureSettings();
        SaveTurnOverrideSettingsTo(staging);
        return !TurnOverridesEqual(_bundle.Metadata.Settings.PlayTurnOverrides, staging.PlayTurnOverrides);
    }

    private static bool TurnOverridesEqual(PlayTurnOverrideSettings left, PlayTurnOverrideSettings right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);

    private void ResetTurnOverridesUi()
    {
        NarratorOverrideResolver.ClearTurnOverrides(_bundle.Metadata.Settings);
        BindNextSendPanel();
    }

    private void BindUtilityDeliveryPanel()
    {
        AdventureStore.SyncUtilityWorkerCapabilitiesFromDisk(_bundle);
        var s = _bundle.Metadata.Settings;
        HideInlineUtilityCheck.IsChecked = s.HideInlineUtilityDuringPlay;
        ShowInlineUtilityTrafficCheck.IsChecked = s.ShowInlineUtilityTraffic;

        if (UtilityInjectionModeCombo.Items.Count == 0)
        {
            UtilityInjectionModeCombo.ItemsSource = new[]
            {
                new UtilityInjectionModeComboItem(PlayUtilityInjectionMode.LegacyInlineSend, "Inline send (separate composer submit)"),
                new UtilityInjectionModeComboItem(PlayUtilityInjectionMode.InjectionFirst, "Injection-first (bundle with next play packet)"),
            };
            UtilityInjectionModeCombo.DisplayMemberPath = nameof(UtilityInjectionModeComboItem.DisplayName);
        }

        UtilityInjectionModeCombo.SelectedItem = UtilityInjectionModeCombo.Items
            .Cast<UtilityInjectionModeComboItem>()
            .FirstOrDefault(i => i.Mode == s.PlayUtilityInjectionMode)
            ?? UtilityInjectionModeCombo.Items.Cast<UtilityInjectionModeComboItem>().First();

        if (UtilityExecutionPolicyCombo.Items.Count == 0)
        {
            UtilityExecutionPolicyCombo.ItemsSource = new[]
            {
                new UtilityExecutionPolicyComboItem(UtilityExecutionPolicy.PlayInjectionPreferred, "Play injection preferred"),
                new UtilityExecutionPolicyComboItem(UtilityExecutionPolicy.WorkerPreferred, "Utility worker preferred"),
                new UtilityExecutionPolicyComboItem(UtilityExecutionPolicy.WorkerOnly, "Utility worker only"),
            };
            UtilityExecutionPolicyCombo.DisplayMemberPath = nameof(UtilityExecutionPolicyComboItem.DisplayName);
        }

        UtilityExecutionPolicyCombo.SelectedItem = UtilityExecutionPolicyCombo.Items
            .Cast<UtilityExecutionPolicyComboItem>()
            .FirstOrDefault(i => i.Policy == s.UtilityExecutionPolicy)
            ?? UtilityExecutionPolicyCombo.Items.Cast<UtilityExecutionPolicyComboItem>().First();

        MaxUtilitySectionsBox.Text = s.MaxUtilitySectionsPerSend.ToString();
        AutoSpillToWorkerCheck.IsChecked = s.AutoSpillToWorker;
        UpdateUtilityWorkerStatusLine();
    }

    private void UpdateUtilityWorkerStatusLine()
    {
        if (UtilityWorkerStatusLine is null)
            return;

        if (UtilityWorkerCapabilityGate.IsGreen(_bundle))
        {
            UtilityWorkerStatusLine.Text =
                "Utility worker: ready — Worker preferred / Worker only lane policies can route jobs to the worker thread.";
            UtilityWorkerStatusLine.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            return;
        }

        var caps = _bundle.Metadata.UtilityWorkerCapabilities;
        var detail = caps?.LastProbeError;
        if (string.IsNullOrWhiteSpace(detail) && caps?.HostReady != true)
            detail = "not verified";
        else if (string.IsNullOrWhiteSpace(detail))
            detail = "capabilities incomplete";

        UtilityWorkerStatusLine.Text =
            $"Utility worker: not ready ({detail}). Worker preferred falls back to the play thread until ready; Worker only blocks manual jobs. Open Threads hub → Utility worker to verify.";
        UtilityWorkerStatusLine.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
    }

    private void SaveUtilityDeliverySettingsTo(AdventureSettings settings)
    {
        settings.HideInlineUtilityDuringPlay = HideInlineUtilityCheck.IsChecked == true;
        settings.ShowInlineUtilityTraffic = ShowInlineUtilityTrafficCheck.IsChecked == true;

        if (UtilityInjectionModeCombo.SelectedItem is UtilityInjectionModeComboItem injectionItem)
            settings.PlayUtilityInjectionMode = injectionItem.Mode;

        if (UtilityExecutionPolicyCombo.SelectedItem is UtilityExecutionPolicyComboItem policyItem)
            settings.UtilityExecutionPolicy = policyItem.Policy;

        if (int.TryParse(MaxUtilitySectionsBox.Text, out var maxSections))
            settings.MaxUtilitySectionsPerSend = Math.Clamp(maxSections, 0, 8);

        settings.AutoSpillToWorker = AutoSpillToWorkerCheck.IsChecked == true;
    }

    private void SaveUtilityDeliverySettings() =>
        SaveUtilityDeliverySettingsTo(_bundle.Metadata.Settings);

    private bool HasUtilityDeliveryChanges() => false;

    private void BindAutomationPanel()
    {
        var s = _bundle.Metadata.Settings;
        AutomationCheck.IsChecked = s.AdventureAutomationEnabled;
        AutoExtractEntitiesCheck.IsChecked = s.AutoExtractEntities;
        AutoProposeMemoriesCheck.IsChecked = s.AutoProposeMemories;
        AutoUpdateSummaryCheck.IsChecked = s.AutoUpdateSummary;
        SummaryIntervalBox.Text = s.SummaryUpdateIntervalTurns.ToString();
        AutoContinuityCheckCheck.IsChecked = s.AutoContinuityCheck;
        AutoSyncInstructionsCheck.IsChecked = s.AutoSyncProjectInstructions;

        var hasProject = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        AutoExtractEntitiesCheck.IsEnabled = hasProject;
        AutoProposeMemoriesCheck.IsEnabled = hasProject;
        AutoUpdateSummaryCheck.IsEnabled = hasProject;
        AutoContinuityCheckCheck.IsEnabled = hasProject;
        AutoSyncInstructionsCheck.IsEnabled = hasProject;
        AutoExtractEntitiesHint.Text = hasProject
            ? "Proposals appear in Reference → review queue after each accepted turn."
            : "Link a Project to enable auto entity extraction.";

        AttachPlaySettingsAutosaveHandlers();
    }

    private void SaveAutomationSettingsTo(AdventureSettings settings)
    {
        settings.AdventureAutomationEnabled = AutomationCheck.IsChecked == true;
        settings.AutoExtractEntities = AutoExtractEntitiesCheck.IsChecked == true;
        settings.AutoProposeMemories = AutoProposeMemoriesCheck.IsChecked == true;
        settings.AutoUpdateSummary = AutoUpdateSummaryCheck.IsChecked == true;
        if (int.TryParse(SummaryIntervalBox.Text, out var interval))
            settings.SummaryUpdateIntervalTurns = Math.Max(1, interval);
        settings.AutoContinuityCheck = AutoContinuityCheckCheck.IsChecked == true;
        settings.AutoSyncProjectInstructions = AutoSyncInstructionsCheck.IsChecked == true;
    }

    private bool HasAutomationChanges()
    {
        var s = _bundle.Metadata.Settings;
        return AutomationCheck.IsChecked != s.AdventureAutomationEnabled
            || AutoExtractEntitiesCheck.IsChecked != s.AutoExtractEntities
            || AutoProposeMemoriesCheck.IsChecked != s.AutoProposeMemories
            || AutoUpdateSummaryCheck.IsChecked != s.AutoUpdateSummary
            || AutoContinuityCheckCheck.IsChecked != s.AutoContinuityCheck
            || AutoSyncInstructionsCheck.IsChecked != s.AutoSyncProjectInstructions
            || ReadSummaryIntervalTurns() != s.SummaryUpdateIntervalTurns;
    }

    /// <summary>Writes the selected AI tool's in-progress edits into the working bundle.</summary>
    private void FlushCurrentAiActionEdits()
    {
        if (string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return;

        GenerationJobGuideService.SetInstructionOverride(_bundle, _selectedAiActionJobId, AiActionInstructionBox.Text);
        SaveJobOverrideSettingsTo(_bundle.Metadata.Settings);
        SaveStoryContextSettingsTo(_bundle);
    }

    private sealed class UtilityInjectionModeComboItem(PlayUtilityInjectionMode mode, string displayName)
    {
        public PlayUtilityInjectionMode Mode { get; } = mode;

        public string DisplayName { get; } = displayName;
    }

    private sealed class UtilityExecutionPolicyComboItem(UtilityExecutionPolicy policy, string displayName)
    {
        public UtilityExecutionPolicy Policy { get; } = policy;

        public string DisplayName { get; } = displayName;
    }
}
