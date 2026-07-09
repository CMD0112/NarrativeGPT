using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.WinUI.Views.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsUtilityJobsTab
{
    private static readonly IReadOnlyList<LookbackAnchorChoice> LookbackAnchorChoices =
        Enum.GetValues<UtilityLookbackAnchor>()
            .Select(anchor => new LookbackAnchorChoice(anchor, UtilityStoryContextDefaults.FormatTranscriptScope(anchor)))
            .ToList();

    private bool _suppressStoryContextEvents;

    private void InitializeStoryContextCombos()
    {
        if (StoryContextSourceCombo.Items.Count > 0)
            return;

        StoryContextSourceCombo.ItemsSource = Enum.GetValues<UtilityStorySource>();
        StoryContextFormatCombo.ItemsSource = UtilityStoryContextSettingsNormalizer.LayoutFormats;
        StoryContextTrimCombo.ItemsSource = Enum.GetValues<UtilityTrimStrategy>();
        StoryContextAnchorCombo.ItemsSource = LookbackAnchorChoices;
        StoryContextAnchorCombo.DisplayMemberPath = nameof(LookbackAnchorChoice.Label);
    }

    private void UpdateStoryContextHints(string jobId, UtilityStoryContextSettings baseSettings)
    {
        if (_ctx is null)
            return;

        var jobDefaults = UtilityStoryContextDefaults.GetJobProfileDefaults(jobId);
        var effective = UtilityStoryContextSettingsService.Resolve(_ctx.Bundle, jobId);
        var hasOverride = StoryContextPerJobOverrideCheck.IsChecked == true;
        var layer = GenerationJobGuideService.GetCatalogCategory(jobId);

        StoryContextJobDefaultHint.Text =
            $"{layer} · built-in default for {GenerationJobGuideService.GetDisplayLabel(jobId)}: " +
            UtilityStoryContextDefaults.FormatContextWindowSummary(jobDefaults, jobId) + ".";
        StoryContextEffectiveHint.Text =
            $"Effective after profile: {UtilityStoryContextDefaults.FormatContextWindowSummary(effective, jobId)}.";
        StoryContextMaxTurnsDefaultHint.Text = $"Default: {jobDefaults.MaxTurnPairs}";
        StoryContextLookbackDefaultHint.Text =
            $"Default: {UtilityStoryContextDefaults.DescribeTranscriptScopeForLayer(layer, jobDefaults.LookbackAnchor)}";
        StoryContextMaxCharsDefaultHint.Text =
            $"Default: {UtilityStoryContextDefaults.FormatContextCharBudget(jobDefaults.MaxContextChars)}";
        StoryContextLayerGuidanceHint.Text =
            UtilityStoryContextDefaults.DescribePacketIncludesForLayer(layer);
        StoryContextPreviewMeta.Text = hasOverride
            ? "Per-action context override active."
            : "Edits apply adventure-wide unless you enable per-action customization.";
    }

    private void BindStoryContextPanel(string jobId, UtilityStoryContextSettings? settingsOverride = null)
    {
        if (_ctx is null)
            return;

        _suppressStoryContextEvents = true;
        try
        {
            var hasOverride = settingsOverride is null && UtilityStoryContextDefaults.UsesJobOverride(_ctx.Bundle, jobId);
            StoryContextPerJobOverrideCheck.IsChecked = settingsOverride is null && hasOverride;
            var settings = settingsOverride
                ?? (hasOverride
                    ? UtilityStoryContextDefaults.GetEditableBase(_ctx.Bundle, jobId)
                    : _ctx.Bundle.Metadata.Settings.UtilityStoryContext.Clone());
            StoryContextSourceCombo.SelectedItem = settings.Source;
            StoryContextMaxTurnsBox.Text = settings.MaxTurnPairs.ToString();
            StoryContextSkipNewestBox.Text = settings.SkipNewestTurnPairs.ToString();
            StoryContextMinTurnsBox.Text = settings.MinTurnPairs.ToString();
            StoryContextMaxTranscriptCharsBox.Text = settings.MaxTranscriptChars.ToString();
            StoryContextAnchorCombo.SelectedItem = LookbackAnchorChoices
                .FirstOrDefault(c => c.Anchor == settings.LookbackAnchor);
            StoryContextAnchorTurnIndexBox.Text = settings.AnchorTurnIndex.ToString();
            StoryContextMaxCharsBox.Text = settings.MaxContextChars.ToString();
            StoryContextFormatCombo.SelectedItem = settings.Format;
            StoryContextTrimCombo.SelectedItem = settings.Trim;
            StoryContextIncludePlayerCheck.IsChecked = settings.IncludePlayerMessages;
            StoryContextIncludeNarratorCheck.IsChecked = settings.IncludeNarratorMessages;
            StoryContextIncludePendingLocalCheck.IsChecked = settings.IncludePendingLocalTurns;
            StoryContextExcludeIncompleteCheck.IsChecked = settings.ExcludeIncompleteTrailingPair;
            StoryContextStripEmptyCheck.IsChecked = settings.StripEmptyTurnPairs;
            StoryContextMaxCharsPerPairBox.Text = settings.MaxCharsPerTurnPair.ToString();
            StoryContextOmitRedundantCheck.IsChecked = settings.OmitRedundantJobTurnSlices;
            StoryContextIncludeSummaryCheck.IsChecked = settings.IncludeRollingSummary;
            StoryContextIncludeStateCheck.IsChecked = settings.IncludeState;
            StoryContextIncludeMemoryCheck.IsChecked = settings.IncludePinnedMemory;
            StoryContextIncludeEntitiesCheck.IsChecked = settings.IncludeEntityIndex;
            StoryContextIncludeScenarioCheck.IsChecked = settings.IncludeScenarioExcerpt;
            StoryContextDirectionBox.Text = settings.DirectionPreamble ?? "";
            UpdateStoryContextHints(jobId, settings);
        }
        finally
        {
            _suppressStoryContextEvents = false;
        }
    }

    private UtilityStoryContextSettings ReadStoryContextFromForm()
    {
        var settings = new UtilityStoryContextSettings();
        if (StoryContextSourceCombo.SelectedItem is UtilityStorySource source)
            settings.Source = source;
        if (int.TryParse(StoryContextMaxTurnsBox.Text, out var turns))
            settings.MaxTurnPairs = Math.Max(0, turns);
        if (int.TryParse(StoryContextSkipNewestBox.Text, out var skipNewest))
            settings.SkipNewestTurnPairs = Math.Max(0, skipNewest);
        if (int.TryParse(StoryContextMinTurnsBox.Text, out var minTurns))
            settings.MinTurnPairs = Math.Max(0, minTurns);
        if (int.TryParse(StoryContextMaxTranscriptCharsBox.Text, out var maxTranscript))
            settings.MaxTranscriptChars = Math.Max(0, maxTranscript);
        if (StoryContextAnchorCombo.SelectedItem is LookbackAnchorChoice anchorChoice)
            settings.LookbackAnchor = anchorChoice.Anchor;
        if (int.TryParse(StoryContextAnchorTurnIndexBox.Text, out var anchorIndex))
            settings.AnchorTurnIndex = Math.Max(0, anchorIndex);
        if (int.TryParse(StoryContextMaxCharsBox.Text, out var chars))
            settings.MaxContextChars = Math.Max(500, chars);
        if (StoryContextFormatCombo.SelectedItem is UtilityTranscriptFormat format)
            settings.Format = format;
        if (StoryContextTrimCombo.SelectedItem is UtilityTrimStrategy trim)
            settings.Trim = trim;
        settings.IncludePlayerMessages = StoryContextIncludePlayerCheck.IsChecked == true;
        settings.IncludeNarratorMessages = StoryContextIncludeNarratorCheck.IsChecked == true;
        settings.IncludePendingLocalTurns = StoryContextIncludePendingLocalCheck.IsChecked == true;
        settings.ExcludeIncompleteTrailingPair = StoryContextExcludeIncompleteCheck.IsChecked == true;
        settings.StripEmptyTurnPairs = StoryContextStripEmptyCheck.IsChecked == true;
        if (int.TryParse(StoryContextMaxCharsPerPairBox.Text, out var maxPerPair))
            settings.MaxCharsPerTurnPair = Math.Max(0, maxPerPair);
        settings.OmitRedundantJobTurnSlices = StoryContextOmitRedundantCheck.IsChecked == true;
        settings.IncludeRollingSummary = StoryContextIncludeSummaryCheck.IsChecked == true;
        settings.IncludeState = StoryContextIncludeStateCheck.IsChecked == true;
        settings.IncludePinnedMemory = StoryContextIncludeMemoryCheck.IsChecked == true;
        settings.IncludeEntityIndex = StoryContextIncludeEntitiesCheck.IsChecked == true;
        settings.IncludeScenarioExcerpt = StoryContextIncludeScenarioCheck.IsChecked == true;
        settings.DirectionPreamble = string.IsNullOrWhiteSpace(StoryContextDirectionBox.Text)
            ? null
            : StoryContextDirectionBox.Text.Trim();
        return settings;
    }

    private void SaveStoryContextSettingsTo(AdventureBundle target)
    {
        var settings = ReadStoryContextFromForm();
        string? affectedJobId = null;
        if (StoryContextPerJobOverrideCheck.IsChecked == true && _selectedJobId is { } jobId)
        {
            UtilityStoryContextSettingsService.SetJobOverride(target, jobId, settings);
            affectedJobId = jobId;
        }
        else
        {
            target.Metadata.Settings.UtilityStoryContext = settings;
            if (_selectedJobId is { } clearJobId)
            {
                UtilityStoryContextSettingsService.SetJobOverride(target, clearJobId, null);
                affectedJobId = clearJobId;
            }
        }

        if (ReferenceEquals(target, _ctx?.Bundle) && affectedJobId is not null)
            RefreshAutomationContextRow(affectedJobId);
    }

    private void StoryContextSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressStoryContextEvents || _ctx is null)
            return;

        if (_selectedJobId is { } jobId)
            UpdateStoryContextHints(jobId, ReadStoryContextFromForm());

        OnChanged(sender, e);
    }

    private void ResetStoryContext_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null || _selectedJobId is not { } jobId)
            return;

        _suppressStoryContextEvents = true;
        if (StoryContextPerJobOverrideCheck.IsChecked == true)
        {
            UtilityStoryContextDefaults.ClearJobOverride(_ctx.Bundle, jobId);
            StoryContextPerJobOverrideCheck.IsChecked = false;
            BindStoryContextPanel(jobId);
        }
        else
        {
            UtilityStoryContextDefaults.ResetAdventureBaseline(_ctx.Bundle.Metadata);
            BindStoryContextPanel(jobId);
        }

        BindAutomationContextGrid();
        _suppressStoryContextEvents = false;
        OnChanged(sender, e);
    }

    private async void PreviewStoryContextLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null || _selectedJobId is not { } jobId)
            return;

        SaveStoryContextSettingsTo(_ctx.Bundle);
        var preview = UtilityJobContextPreviewService.BuildLocal(_ctx.Bundle, jobId);
        StoryContextPreviewMeta.Text = preview.Manifest is not null
            ? $"Preview (local): {preview.Manifest.FormatSummary()}"
            : $"Preview (local): {preview.FormatStatusHint()}";

        await WinUiDialogService.ShowWorkbenchAsync(
            App.CurrentMainWindow,
            "Story context preview (local)",
            new RecapPage(preview.FormatPreviewBody()),
            layoutKey: "StoryContextPreviewLocal",
            designWidth: 720,
            designHeight: 560,
            configure: w => WinUiDialogService.AddCloseFooter(w));
    }

    private async void PreviewStoryContextLive_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null || _selectedJobId is not { } jobId)
            return;

        SaveStoryContextSettingsTo(_ctx.Bundle);
        if (_ctx.Host?.PreviewLiveStoryContextAsync is not { } previewLive)
        {
            StoryContextPreviewMeta.Text = "Live preview unavailable — pin play tab and open from play cockpit.";
            return;
        }

        try
        {
            var preview = await previewLive(jobId);
            StoryContextPreviewMeta.Text = preview.Manifest is not null
                ? $"Preview (live): {preview.Manifest.FormatSummary()}"
                : $"Preview (live): {preview.FormatStatusHint()}";
            await WinUiDialogService.ShowWorkbenchAsync(
                App.CurrentMainWindow,
                "Story context preview (live)",
                new RecapPage(preview.FormatPreviewBody()),
                layoutKey: "StoryContextPreviewLive",
                designWidth: 720,
                designHeight: 560,
                configure: w => WinUiDialogService.AddCloseFooter(w));
        }
        catch (Exception ex)
        {
            StoryContextPreviewMeta.Text = $"Live preview failed: {ex.Message}";
        }
    }
}
