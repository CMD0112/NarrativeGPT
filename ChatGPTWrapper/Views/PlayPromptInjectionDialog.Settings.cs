using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
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
        LocalUtilityInferenceCheck.IsChecked = s.LocalUtilityInference.Enabled;
        LocalUtilityInferenceDualRunCheck.IsChecked = s.LocalUtilityInference.DualRun;
        LocalUtilityInferenceDualRunCheck.IsEnabled = s.LocalUtilityInference.Enabled;
        var localDefaults = ChatGPTWrapper.Core.LocalInference.LocalInferenceOptions.FromEnvironment();
        LocalInferenceBaseUrlBox.Text = string.IsNullOrWhiteSpace(s.LocalUtilityInference.BaseUrl)
            ? localDefaults.BaseUrl
            : s.LocalUtilityInference.BaseUrl;
        LocalInferenceModelBox.Text = string.IsNullOrWhiteSpace(s.LocalUtilityInference.Model)
            ? localDefaults.Model
            : s.LocalUtilityInference.Model;

        if (UtilityInjectionModeCombo.Items.Count == 0)
        {
            UtilityInjectionModeCombo.ItemsSource = new[]
            {
                new UtilityInjectionModeComboItem(
                    PlayUtilityInjectionMode.LegacyInlineSend,
                    "Separate send — utility jobs submit on their own after the play turn"),
                new UtilityInjectionModeComboItem(
                    PlayUtilityInjectionMode.InjectionFirst,
                    "Bundled send — embed utility sections in the next play packet"),
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
                new UtilityExecutionPolicyComboItem(
                    UtilityExecutionPolicy.PlayInjectionPreferred,
                    "Play thread first — inject when possible, else utility worker"),
                new UtilityExecutionPolicyComboItem(
                    UtilityExecutionPolicy.WorkerPreferred,
                    "Utility worker first — ephemeral chat when enabled, else pinned thread"),
                new UtilityExecutionPolicyComboItem(
                    UtilityExecutionPolicy.WorkerOnly,
                    "Utility worker only — never inject on the play thread"),
            };
            UtilityExecutionPolicyCombo.DisplayMemberPath = nameof(UtilityExecutionPolicyComboItem.DisplayName);
        }

        UtilityExecutionPolicyCombo.SelectedItem = UtilityExecutionPolicyCombo.Items
            .Cast<UtilityExecutionPolicyComboItem>()
            .FirstOrDefault(i => i.Policy == s.UtilityExecutionPolicy)
            ?? UtilityExecutionPolicyCombo.Items.Cast<UtilityExecutionPolicyComboItem>().First();

        MaxUtilitySectionsBox.Text = s.MaxUtilitySectionsPerSend.ToString();
        AutoSpillToWorkerCheck.IsChecked = s.AutoSpillToWorker;
        UseEphemeralUtilityWorkerChatCheck.IsChecked = s.UseEphemeralUtilityWorkerChat;
        BindMaxParallelUtilityWorkerJobsCombo();
        ApplyMaxParallelUtilityWorkerJobsUi(s.UseEphemeralUtilityWorkerChat);
        var parallelSlots = UtilityWorkerParallelPolicy.NormalizeForUi(
            s.MaxParallelUtilityWorkerJobs,
            s.UseEphemeralUtilityWorkerChat);
        MaxParallelUtilityWorkerJobsCombo.SelectedItem = MaxParallelUtilityWorkerJobsCombo.Items
            .Cast<MaxParallelUtilityWorkerJobsComboItem>()
            .FirstOrDefault(i => i.SlotCount == parallelSlots)
            ?? MaxParallelUtilityWorkerJobsCombo.Items.Cast<MaxParallelUtilityWorkerJobsComboItem>().First();
        ForceUtilityWorkerDomAttachCheck.IsChecked = s.ForceUtilityWorkerDomAttach;
        ForceUtilityWorkerDomAttachCheck.IsEnabled = s.UseEphemeralUtilityWorkerChat;
        UpdateUtilityWorkerStatusLine();
    }

    private void UpdateUtilityWorkerStatusLine()
    {
        if (UtilityWorkerStatusLine is null)
            return;

        if (UtilityEphemeralWorkerPolicy.IsEnabled(_bundle))
        {
            var slots = UtilityWorkerParallelPolicy.ResolveMaxSlots(_bundle);
            var parallelNote = slots > 1
                ? $" Up to {slots} jobs run in parallel on separate background WebViews."
                : " Jobs drain sequentially (set concurrent jobs above for parallel).";
            UtilityWorkerStatusLine.Text =
                "Ephemeral worker active — each job uses a short-lived Project chat (create → send → capture → delete)." +
                parallelNote +
                " Entities file revision always uses Project source publish under sources/cgw-utility-io/… regardless of lane policy.";
            UtilityWorkerStatusLine.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            return;
        }

        if (UtilityWorkerCapabilityGate.IsGreen(_bundle))
        {
            UtilityWorkerStatusLine.Text =
                "Pinned utility worker ready — Worker first / Worker only policies route to the pinned thread. " +
                "Enable ephemeral chats above to avoid long-lived worker threads. File revision jobs always use ephemeral source-pointer I/O.";
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
            $"Utility worker not ready ({detail}). Worker-first policies fall back to the play thread until verified; Worker only blocks manual runs. " +
            "File revision needs a linked Project. Open Threads hub → Utility worker, or enable ephemeral chats.";
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
        settings.UseEphemeralUtilityWorkerChat = UseEphemeralUtilityWorkerChatCheck.IsChecked == true;
        if (MaxParallelUtilityWorkerJobsCombo.SelectedItem is MaxParallelUtilityWorkerJobsComboItem parallelItem)
            settings.MaxParallelUtilityWorkerJobs = settings.UseEphemeralUtilityWorkerChat
                ? parallelItem.SlotCount
                : 1;
        else
            settings.MaxParallelUtilityWorkerJobs = settings.UseEphemeralUtilityWorkerChat ? 3 : 1;

        settings.ForceUtilityWorkerDomAttach = settings.UseEphemeralUtilityWorkerChat
            && ForceUtilityWorkerDomAttachCheck.IsChecked == true;

        var localEnabled = LocalUtilityInferenceCheck.IsChecked == true;
        settings.LocalUtilityInference.Enabled = localEnabled;
        settings.LocalUtilityInference.DualRun = localEnabled
            && LocalUtilityInferenceDualRunCheck.IsChecked == true;
        var baseUrl = LocalInferenceBaseUrlBox.Text?.Trim() ?? "";
        var model = LocalInferenceModelBox.Text?.Trim() ?? "";
        var envDefaults = ChatGPTWrapper.Core.LocalInference.LocalInferenceOptions.FromEnvironment();
        settings.LocalUtilityInference.BaseUrl = string.Equals(baseUrl, envDefaults.BaseUrl, StringComparison.OrdinalIgnoreCase)
            ? null
            : string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl;
        settings.LocalUtilityInference.Model = string.Equals(model, envDefaults.Model, StringComparison.OrdinalIgnoreCase)
            ? null
            : string.IsNullOrWhiteSpace(model) ? null : model;
    }

    private void SaveUtilityDeliverySettings() =>
        SaveUtilityDeliverySettingsTo(_bundle.Metadata.Settings);

    private void BindAutomationPanel()
    {
        var s = _bundle.Metadata.Settings;
        AutomationCheck.IsChecked = s.AdventureAutomationEnabled;
        AutoExtractEntitiesCheck.IsChecked = s.AutoExtractEntities;
        AutoProposeMemoriesCheck.IsChecked = s.AutoProposeMemories;
        AutoUpdateSummaryCheck.IsChecked = s.AutoUpdateSummary;
        SummaryIntervalBox.Text = s.SummaryUpdateIntervalTurns.ToString();
        AutoContinuityCheckCheck.IsChecked = s.AutoContinuityCheck;
        AutoUpdateStateCheck.IsChecked = s.AutoUpdateState;
        AutoProposeEntityStateCheck.IsChecked = s.AutoProposeEntityState;
        AutoProposeCanonEvolutionCheck.IsChecked = s.AutoProposeCanonEvolution;
        AutoSyncInstructionsCheck.IsChecked = s.AutoSyncProjectInstructions;
        BindAutomationContextGrid();

        var hasProject = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        AutoExtractEntitiesCheck.IsEnabled = hasProject;
        AutoProposeMemoriesCheck.IsEnabled = hasProject;
        AutoUpdateSummaryCheck.IsEnabled = hasProject;
        AutoContinuityCheckCheck.IsEnabled = hasProject;
        AutoUpdateStateCheck.IsEnabled = hasProject;
        AutoProposeEntityStateCheck.IsEnabled = hasProject;
        AutoProposeCanonEvolutionCheck.IsEnabled = hasProject;
        AutoSyncInstructionsCheck.IsEnabled = hasProject;
        AutoExtractEntitiesHint.Text = hasProject
            ? "Post-turn proposals route to Reference → review queue by layer after each accepted turn."
            : "Link a Project to enable post-turn utility jobs.";

        NarrativeLayerHint.Text = GenerationJobGuideService.DescribeUtilityLayer("Narrative");
        SessionLayerHint.Text = GenerationJobGuideService.DescribeUtilityLayer("Session");
        CanonProfileLayerHint.Text = GenerationJobGuideService.DescribeUtilityLayer("Canon profile");
        PlayStateLayerHint.Text = GenerationJobGuideService.DescribeUtilityLayer("Play state");
        CanonEvolutionLayerHint.Text = GenerationJobGuideService.DescribeUtilityLayer("Canon evolution");

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
        settings.AutoUpdateState = AutoUpdateStateCheck.IsChecked == true;
        settings.AutoProposeEntityState = AutoProposeEntityStateCheck.IsChecked == true;
        settings.AutoProposeCanonEvolution = AutoProposeCanonEvolutionCheck.IsChecked == true;
        settings.AutoSyncProjectInstructions = AutoSyncInstructionsCheck.IsChecked == true;
    }

    /// <summary>Writes the selected utility job's in-progress edits into the working bundle.</summary>
    private void FlushCurrentAiActionEdits()
    {
        if (string.IsNullOrWhiteSpace(_selectedAiActionJobId))
            return;

        GenerationJobGuideService.SetInstructionOverride(_bundle, _selectedAiActionJobId, AiActionInstructionBox.Text);
        SaveJobOverrideSettingsTo(_bundle.Metadata.Settings);
        SaveStoryContextSettingsTo(_bundle);
    }

    private void BindMaxParallelUtilityWorkerJobsCombo()
    {
        if (MaxParallelUtilityWorkerJobsCombo.Items.Count > 0)
            return;

        MaxParallelUtilityWorkerJobsCombo.ItemsSource = new[]
        {
            new MaxParallelUtilityWorkerJobsComboItem(1, "1 — sequential (legacy)"),
            new MaxParallelUtilityWorkerJobsComboItem(2, "2 — parallel jobs"),
            new MaxParallelUtilityWorkerJobsComboItem(3, "3 — parallel jobs (recommended)"),
            new MaxParallelUtilityWorkerJobsComboItem(4, "4 — parallel jobs (max)"),
        };
        MaxParallelUtilityWorkerJobsCombo.DisplayMemberPath = nameof(MaxParallelUtilityWorkerJobsComboItem.DisplayName);
    }

    private void ApplyMaxParallelUtilityWorkerJobsUi(bool ephemeralEnabled)
    {
        if (MaxParallelUtilityWorkerJobsPanel is null || MaxParallelUtilityWorkerJobsCombo is null)
            return;

        MaxParallelUtilityWorkerJobsPanel.IsEnabled = ephemeralEnabled;
        if (!ephemeralEnabled)
            return;

        if (MaxParallelUtilityWorkerJobsCombo.SelectedItem is MaxParallelUtilityWorkerJobsComboItem { SlotCount: 1 }
            && UtilityWorkerParallelPolicy.NormalizeForUi(
                _bundle.Metadata.Settings.MaxParallelUtilityWorkerJobs,
                ephemeralEnabled: false) <= 1)
        {
            MaxParallelUtilityWorkerJobsCombo.SelectedItem = MaxParallelUtilityWorkerJobsCombo.Items
                .Cast<MaxParallelUtilityWorkerJobsComboItem>()
                .First(i => i.SlotCount == UtilityWorkerParallelPolicy.RecommendedParallelSlots);
        }
    }

    private sealed class MaxParallelUtilityWorkerJobsComboItem(int slotCount, string displayName)
    {
        public int SlotCount { get; } = slotCount;

        public string DisplayName { get; } = displayName;
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
