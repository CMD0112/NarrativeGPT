using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsUtilityJobsTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;
    private string? _selectedJobId;
    private bool _suppressBind;

    public PlaySettingsUtilityJobsTab()
    {
        InitializeComponent();
        InitializeJobOverrideCombos();
    }

    public event EventHandler? SettingsChanged;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        _suppressBind = true;
        try
        {
            UtilityStoryContextSettingsService.EnsureDefaults(context.Bundle.Metadata);
            AdventureStore.SyncUtilityWorkerCapabilitiesFromDisk(context.Bundle);

            InitializeStoryContextCombos();
            BindUtilityDelivery(context.Bundle);
            BindAutomationContextGrid();

            var rows = GenerationJobGuideService.EditablePlayUtilityJobIds
                .Select(jobId => new AiActionRowViewModel(jobId))
                .OrderBy(r => GenerationJobGuideService.GetLayerSortOrder(r.Category))
                .ThenBy(r => r.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
            JobsList.ItemsSource = rows;
            if (rows.Count > 0 && JobsList.SelectedItem is null)
                JobsList.SelectedIndex = 0;
            else if (_selectedJobId is { } jobId)
                ReselectJob(jobId);
        }
        finally
        {
            _suppressBind = false;
        }
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        FlushCurrentJobEdits(context);
        SaveUtilityDeliverySettings(context.Bundle.Metadata.Settings);
        SaveAutomationContextFromGrid(context.Bundle);
    }

    private void FlushCurrentJobEdits(PlaySettingsWorkbenchContext context)
    {
        if (string.IsNullOrWhiteSpace(_selectedJobId))
            return;

        GenerationJobGuideService.SetInstructionOverride(context.Bundle, _selectedJobId, InstructionBox.Text);
        SaveJobOverrideSettings(context.Bundle.Metadata.Settings);
        SaveStoryContextSettingsTo(context.Bundle);
    }

    private void BindUtilityDelivery(AdventureBundle bundle)
    {
        var s = bundle.Metadata.Settings;
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
                new UtilityInjectionModeComboItem(PlayUtilityInjectionMode.LegacyInlineSend, "Separate send"),
                new UtilityInjectionModeComboItem(PlayUtilityInjectionMode.InjectionFirst, "Bundled send"),
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
                new UtilityExecutionPolicyComboItem(UtilityExecutionPolicy.PlayInjectionPreferred, "Play thread first"),
                new UtilityExecutionPolicyComboItem(UtilityExecutionPolicy.WorkerPreferred, "Utility worker first"),
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
        UseEphemeralWorkerCheck.IsChecked = s.UseEphemeralUtilityWorkerChat;
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
        UpdateUtilityWorkerStatus(bundle);
    }

    private void SaveUtilityDeliverySettings(AdventureSettings settings)
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
        settings.UseEphemeralUtilityWorkerChat = UseEphemeralWorkerCheck.IsChecked == true;
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

    private void UpdateUtilityWorkerStatus(AdventureBundle bundle)
    {
        if (UtilityEphemeralWorkerPolicy.IsEnabled(bundle))
        {
            var slots = UtilityWorkerParallelPolicy.ResolveMaxSlots(bundle);
            var parallelNote = slots > 1
                ? $" Up to {slots} jobs run in parallel."
                : "";
            UtilityWorkerStatusLine.Text =
                "Ephemeral worker active — each job uses a short-lived Project chat." + parallelNote;
            UtilityWorkerStatusLine.Foreground = (Brush)Application.Current.Resources["TextMutedBrush"];
            return;
        }

        if (UtilityWorkerCapabilityGate.IsGreen(bundle))
        {
            UtilityWorkerStatusLine.Text = "Pinned utility worker ready.";
            UtilityWorkerStatusLine.Foreground = (Brush)Application.Current.Resources["TextMutedBrush"];
            return;
        }

        UtilityWorkerStatusLine.Text =
            "Utility worker not ready — open Threads hub → Utility worker, or enable ephemeral chats.";
        UtilityWorkerStatusLine.Foreground = (Brush)Application.Current.Resources["WarningBrush"];
    }

    private void BindMaxParallelUtilityWorkerJobsCombo()
    {
        if (MaxParallelUtilityWorkerJobsCombo.Items.Count > 0)
            return;

        MaxParallelUtilityWorkerJobsCombo.ItemsSource = new[]
        {
            new MaxParallelUtilityWorkerJobsComboItem(1, "1 — sequential"),
            new MaxParallelUtilityWorkerJobsComboItem(2, "2 — parallel jobs"),
            new MaxParallelUtilityWorkerJobsComboItem(3, "3 — parallel jobs (recommended)"),
            new MaxParallelUtilityWorkerJobsComboItem(4, "4 — parallel jobs (max)"),
        };
        MaxParallelUtilityWorkerJobsCombo.DisplayMemberPath = nameof(MaxParallelUtilityWorkerJobsComboItem.DisplayName);
    }

    private void ApplyMaxParallelUtilityWorkerJobsUi(bool ephemeralEnabled)
    {
        MaxParallelUtilityWorkerJobsPanel.Visibility = ephemeralEnabled ? Visibility.Visible : Visibility.Collapsed;
        MaxParallelUtilityWorkerJobsCombo.IsEnabled = ephemeralEnabled;
    }

    private void InitializeJobOverrideCombos()
    {
        JobOverrideResponseLengthCombo.ItemsSource = new[] { "normal", "short", "long" };
        JobOverrideResponseDetailCombo.ItemsSource = new[] { "standard", "brief", "deep" };
    }

    private void BindJobOverridePanel(string jobId)
    {
        if (_ctx is null)
            return;

        var utilityId = GenerationJobHandlers.GetUtilityJobId(jobId);
        if (!_ctx.Bundle.Metadata.Settings.UtilityJobOverrides.TryGetValue(utilityId, out var overrides))
            overrides = new UtilityJobOverrideSettings();

        JobOverrideResponseLengthCombo.SelectedItem = overrides.ResponseLength;
        JobOverrideResponseDetailCombo.SelectedItem = overrides.ResponseDetail;
    }

    private void SaveJobOverrideSettings(AdventureSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_selectedJobId))
            return;

        var utilityId = GenerationJobHandlers.GetUtilityJobId(_selectedJobId);
        var overrides = new UtilityJobOverrideSettings
        {
            ResponseLength = JobOverrideResponseLengthCombo.SelectedItem as string ?? "normal",
            ResponseDetail = JobOverrideResponseDetailCombo.SelectedItem as string ?? "standard",
        };

        if (string.Equals(overrides.ResponseLength, "normal", StringComparison.OrdinalIgnoreCase)
            && string.Equals(overrides.ResponseDetail, "standard", StringComparison.OrdinalIgnoreCase))
        {
            settings.UtilityJobOverrides.Remove(utilityId);
        }
        else
        {
            settings.UtilityJobOverrides[utilityId] = overrides;
        }
    }

    private void UpdateAiActionStatus(string jobId)
    {
        if (_ctx is null)
            return;

        var resolved = GenerationJobGuideService.ResolveInstructionBody(_ctx.Bundle, jobId);
        if (!string.Equals(InstructionBox.Text, resolved, StringComparison.Ordinal))
        {
            AiActionStatusBlock.Text = "Unsaved edits — included when you Save play settings";
            return;
        }

        AiActionStatusBlock.Text = GenerationJobGuideService.IsUsingDefaultInstruction(_ctx.Bundle, jobId)
            ? "Using built-in default"
            : "Customized — applies on the next inline job run";
    }

    private void JobsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ctx is null)
            return;

        if (e.RemovedItems.Count > 0 && _selectedJobId is not null)
            FlushCurrentJobEdits(_ctx);

        if (JobsList.SelectedItem is not AiActionRowViewModel row)
            return;

        _selectedJobId = row.JobId;
        JobTitleBlock.Text = row.DisplayLabel;
        AiActionLayerBadge.Text = row.Category;
        JobLayerHintBlock.Text = GenerationJobGuideService.DescribeUtilityLayer(row.Category);
        InstructionBox.Text = GenerationJobGuideService.ResolveInstructionBody(_ctx.Bundle, row.JobId);
        UpdateAiActionStatus(row.JobId);
        BindJobOverridePanel(row.JobId);
        BindStoryContextPanel(row.JobId);
    }

    private void ReselectJob(string jobId)
    {
        if (JobsList.ItemsSource is not IEnumerable<AiActionRowViewModel> rows)
            return;

        var row = rows.FirstOrDefault(r => string.Equals(r.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
            JobsList.SelectedItem = row;
    }

    private void InstructionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ctx is null || string.IsNullOrWhiteSpace(_selectedJobId))
            return;

        UpdateAiActionStatus(_selectedJobId);
        OnChanged(sender, e);
    }

    private void ResetGuide_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedJobId))
            return;

        InstructionBox.Text = GenerationJobGuideService.BuildDefaultInstructionBody(_selectedJobId);
        UpdateAiActionStatus(_selectedJobId);
        OnChanged(sender, e);
    }

    private async void RunJob_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.RunUtilityJobWithAttachmentsAsync is not { } run || string.IsNullOrWhiteSpace(_selectedJobId))
            return;

        Flush(_ctx);
        await run(_selectedJobId);
    }

    private void OpenReview_Click(object sender, RoutedEventArgs e) =>
        _ctx?.Host?.OpenProposalReviewHub?.Invoke(null);

    private void OpenThreadsHub_Click(object sender, RoutedEventArgs e) =>
        _ctx?.Host?.OpenThreadsHub?.Invoke();

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressBind || _ctx is null)
            return;

        if (sender is CheckBox useEphemeralWorkerCheck && ReferenceEquals(useEphemeralWorkerCheck, UseEphemeralWorkerCheck))
        {
            ApplyMaxParallelUtilityWorkerJobsUi(useEphemeralWorkerCheck.IsChecked == true);
            ForceUtilityWorkerDomAttachCheck.IsEnabled = useEphemeralWorkerCheck.IsChecked == true;
            UpdateUtilityWorkerStatus(_ctx.Bundle);
        }

        if (sender is CheckBox localUtilityInferenceCheck && ReferenceEquals(localUtilityInferenceCheck, LocalUtilityInferenceCheck))
            LocalUtilityInferenceDualRunCheck.IsEnabled = localUtilityInferenceCheck.IsChecked == true;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.NotifySettingsChanged();
        _ctx.RaiseTransportSettingsCommitted();
    }

    private sealed class AiActionRowViewModel(string jobId)
    {
        public string JobId { get; } = jobId;

        public string DisplayLabel { get; } = GenerationJobGuideService.GetDisplayLabel(jobId);

        public string Category { get; } = GenerationJobGuideService.GetCatalogCategory(jobId);

        public string Description { get; } = GenerationJobGuideService.GetCatalogDescription(jobId);

        public override string ToString() => DisplayLabel;
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

    private sealed class MaxParallelUtilityWorkerJobsComboItem(int slotCount, string displayName)
    {
        public int SlotCount { get; } = slotCount;

        public string DisplayName { get; } = displayName;
    }
}
