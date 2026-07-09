using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog
{
    private static readonly IReadOnlyList<LookbackAnchorChoice> LookbackAnchorChoices =
        Enum.GetValues<UtilityLookbackAnchor>()
            .Select(anchor => new LookbackAnchorChoice(anchor, UtilityStoryContextDefaults.FormatTranscriptScope(anchor)))
            .ToList();

    private bool _suppressAutomationContextEvents;

    private void BindAutomationContextGrid()
    {
        _suppressAutomationContextEvents = true;
        try
        {
            AutomationContextGrid.ItemsSource = UtilityStoryContextDefaults.AutomationJobs
                .Select(spec => CreateAutomationContextRow(spec))
                .ToList();
        }
        finally
        {
            _suppressAutomationContextEvents = false;
        }
    }

    private void RefreshAutomationContextRow(string jobId)
    {
        if (AutomationContextGrid.ItemsSource is not List<AutomationContextRowViewModel> rows)
            return;

        var spec = UtilityStoryContextDefaults.AutomationJobs
            .FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return;

        var index = rows.FindIndex(r => string.Equals(r.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        rows[index] = CreateAutomationContextRow(spec);
        AutomationContextGrid.Items.Refresh();
    }

    private AutomationContextRowViewModel CreateAutomationContextRow(UtilityStoryContextDefaults.AutomationContextJobSpec spec)
    {
        var jobDefaults = UtilityStoryContextDefaults.GetJobProfileDefaults(spec.JobId);
        var effective = UtilityStoryContextDefaults.GetEffective(_bundle, spec.JobId);
        return new AutomationContextRowViewModel
        {
            JobId = spec.JobId,
            Layer = UtilityStoryContextDefaults.GetAutomationLayer(spec.JobId),
            Label = spec.Label,
            TurnPairs = effective.MaxTurnPairs,
            DefaultTurnPairs = jobDefaults.MaxTurnPairs,
            Scope = effective.LookbackAnchor,
            DefaultScope = jobDefaults.LookbackAnchor,
            DefaultScopeLabel = UtilityStoryContextDefaults.FormatTranscriptScope(jobDefaults.LookbackAnchor),
            ScopeChoices = LookbackAnchorChoices,
            HasOverride = UtilityStoryContextDefaults.UsesJobOverride(_bundle, spec.JobId),
        };
    }

    private void SaveAutomationContextFromGrid(AdventureBundle target)
    {
        if (AutomationContextGrid.ItemsSource is not IEnumerable<AutomationContextRowViewModel> rows)
            return;

        foreach (var row in rows)
            ApplyAutomationContextRow(target, row);
    }

    private void ApplyAutomationContextRow(AdventureBundle target, AutomationContextRowViewModel row)
    {
        var jobDefaults = UtilityStoryContextDefaults.GetJobProfileDefaults(row.JobId);
        var turnPairs = Math.Max(0, row.TurnPairs);
        var scope = row.Scope;

        if (turnPairs == jobDefaults.MaxTurnPairs && scope == jobDefaults.LookbackAnchor)
        {
            UtilityStoryContextDefaults.ClearJobOverride(target, row.JobId);
            row.HasOverride = false;
            return;
        }

        var settings = UtilityStoryContextDefaults.GetEditableBase(target, row.JobId);
        settings.MaxTurnPairs = turnPairs;
        settings.LookbackAnchor = scope;
        UtilityStoryContextSettingsService.SetJobOverride(target, row.JobId, settings);
        row.HasOverride = true;
    }

    private void ResetAutomationContextRow(string jobId)
    {
        UtilityStoryContextDefaults.ClearJobOverride(_bundle, jobId);
        if (AutomationContextGrid.ItemsSource is not IEnumerable<AutomationContextRowViewModel> rows)
            return;

        var row = rows.FirstOrDefault(r => string.Equals(r.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;

        var effective = UtilityStoryContextDefaults.GetEffective(_bundle, jobId);
        row.TurnPairs = effective.MaxTurnPairs;
        row.Scope = effective.LookbackAnchor;
        row.HasOverride = false;
        AutomationContextGrid.Items.Refresh();

        if (string.Equals(_selectedAiActionJobId, jobId, StringComparison.OrdinalIgnoreCase))
            BindStoryContextPanel(jobId);

        MarkPlaySettingsDirty();
    }

    private void ResetAllAutomationContext_Click(object sender, RoutedEventArgs e)
    {
        foreach (var spec in UtilityStoryContextDefaults.AutomationJobs)
            UtilityStoryContextDefaults.ClearJobOverride(_bundle, spec.JobId);

        BindAutomationContextGrid();
        if (_selectedAiActionJobId is { } jobId)
            BindStoryContextPanel(jobId);

        MarkPlaySettingsDirty();
    }

    private void ResetAutomationContextRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string jobId })
            return;

        ResetAutomationContextRow(jobId);
    }

    private void AutomationContextGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_suppressAutomationContextEvents)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (AutomationContextGrid.ItemsSource is not IEnumerable<AutomationContextRowViewModel> rows)
                return;

            if (_selectedAiActionJobId is { } jobId
                && rows.Any(r => string.Equals(r.JobId, jobId, StringComparison.OrdinalIgnoreCase)))
            {
                BindStoryContextPanel(jobId);
            }

            MarkPlaySettingsDirty();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AutomationContextLookback_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAutomationContextEvents || sender is not ComboBox combo)
            return;

        if (combo.DataContext is not AutomationContextRowViewModel row)
            return;

        if (combo.SelectedValue is UtilityLookbackAnchor anchor)
            row.Scope = anchor;

        if (string.Equals(_selectedAiActionJobId, row.JobId, StringComparison.OrdinalIgnoreCase))
            BindStoryContextPanel(row.JobId);

        MarkPlaySettingsDirty();
    }

    private sealed class LookbackAnchorChoice(UtilityLookbackAnchor anchor, string label)
    {
        public UtilityLookbackAnchor Anchor { get; } = anchor;

        public string Label { get; } = label;
    }

    private sealed class AutomationContextRowViewModel
    {
        public required string JobId { get; init; }

        public required string Layer { get; init; }

        public required string Label { get; init; }

        public int TurnPairs { get; set; }

        public int DefaultTurnPairs { get; init; }

        public UtilityLookbackAnchor Scope { get; set; }

        public UtilityLookbackAnchor DefaultScope { get; init; }

        public required string DefaultScopeLabel { get; init; }

        public required IReadOnlyList<LookbackAnchorChoice> ScopeChoices { get; init; }

        public bool HasOverride { get; set; }
    }
}
