using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsUtilityJobsTab
{
    private bool _suppressAutomationContextEvents;

    private void BindAutomationContextGrid()
    {
        _suppressAutomationContextEvents = true;
        try
        {
            var rows = UtilityStoryContextDefaults.AutomationJobs
                .Select(CreateAutomationContextRow)
                .ToList();
            AutomationContextHost.Children.Clear();
            foreach (var row in rows)
                AutomationContextHost.Children.Add(BuildAutomationContextRow(row));
        }
        finally
        {
            _suppressAutomationContextEvents = false;
        }
    }

    private UIElement BuildAutomationContextRow(AutomationContextRowViewModel row)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var layer = new TextBlock { Text = row.Layer, Style = GetStyle("ShellSectionHintStyle"), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = row.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var turns = new TextBox { Text = row.TurnPairsText, Width = 52 };
        turns.LostFocus += (_, _) =>
        {
            row.TurnPairsText = turns.Text;
            OnAutomationRowChanged(row);
        };
        var scope = new ComboBox
        {
            ItemsSource = row.ScopeChoices,
            DisplayMemberPath = nameof(LookbackAnchorChoice.Label),
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        scope.SelectedItem = row.ScopeChoice;
        scope.SelectionChanged += (_, _) =>
        {
            if (scope.SelectedItem is LookbackAnchorChoice choice)
                row.ScopeChoice = choice;
            OnAutomationRowChanged(row);
        };
        var reset = new Button { Content = "Reset", Tag = row.JobId, Style = GetStyle("ShellGhostButtonStyle") };
        reset.Click += ResetAutomationContextRow_Click;

        Grid.SetColumn(layer, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(turns, 2);
        Grid.SetColumn(scope, 3);
        Grid.SetColumn(reset, 4);
        grid.Children.Add(layer);
        grid.Children.Add(label);
        grid.Children.Add(turns);
        grid.Children.Add(scope);
        grid.Children.Add(reset);
        return grid;
    }

    private void OnAutomationRowChanged(AutomationContextRowViewModel row)
    {
        if (_suppressAutomationContextEvents)
            return;

        if (_selectedJobId is { } jobId && string.Equals(jobId, row.JobId, StringComparison.OrdinalIgnoreCase))
            BindStoryContextPanel(jobId);

        OnChanged(this, new RoutedEventArgs());
    }

    private static Style? GetStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Style style ? style : null;

    private void RefreshAutomationContextRow(string jobId)
    {
        var spec = UtilityStoryContextDefaults.AutomationJobs
            .FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return;

        BindAutomationContextGrid();
    }

    private AutomationContextRowViewModel CreateAutomationContextRow(
        UtilityStoryContextDefaults.AutomationContextJobSpec spec)
    {
        var jobDefaults = UtilityStoryContextDefaults.GetJobProfileDefaults(spec.JobId);
        var effective = UtilityStoryContextDefaults.GetEffective(_ctx!.Bundle, spec.JobId);
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
            HasOverride = UtilityStoryContextDefaults.UsesJobOverride(_ctx.Bundle, spec.JobId),
        };
    }

    private void SaveAutomationContextFromGrid(AdventureBundle target)
    {
        foreach (var child in AutomationContextHost.Children)
        {
            if (child is not Grid grid)
                continue;

            var reset = grid.Children.OfType<Button>().FirstOrDefault();
            if (reset?.Tag is not string jobId)
                continue;

            var turnsBox = grid.Children.OfType<TextBox>().FirstOrDefault();
            var scopeBox = grid.Children.OfType<ComboBox>().FirstOrDefault();
            if (turnsBox is null || scopeBox?.SelectedItem is not LookbackAnchorChoice choice)
                continue;

            var row = CreateAutomationContextRow(
                UtilityStoryContextDefaults.AutomationJobs.First(j => j.JobId == jobId));
            if (int.TryParse(turnsBox.Text, out var turns))
                row.TurnPairs = turns;
            row.Scope = choice.Anchor;
            ApplyAutomationContextRow(target, row);
        }
    }

    private static void ApplyAutomationContextRow(AdventureBundle target, AutomationContextRowViewModel row)
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
        if (_ctx is null)
            return;

        UtilityStoryContextDefaults.ClearJobOverride(_ctx.Bundle, jobId);
        BindAutomationContextGrid();

        if (string.Equals(_selectedJobId, jobId, StringComparison.OrdinalIgnoreCase))
            BindStoryContextPanel(jobId);

        OnChanged(this, new RoutedEventArgs());
    }

    private void ResetAllAutomationContext_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        foreach (var spec in UtilityStoryContextDefaults.AutomationJobs)
            UtilityStoryContextDefaults.ClearJobOverride(_ctx.Bundle, spec.JobId);

        BindAutomationContextGrid();
        if (_selectedJobId is { } jobId)
            BindStoryContextPanel(jobId);

        OnChanged(sender, e);
    }

    private void ResetAutomationContextRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string jobId })
            return;

        ResetAutomationContextRow(jobId);
    }

    private void AutomationContextTurnPairs_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressAutomationContextEvents || sender is not TextBox box)
            return;

        if (box.DataContext is not AutomationContextRowViewModel row)
            return;

        if (int.TryParse(box.Text, out var turns))
            row.TurnPairs = Math.Max(0, turns);

        if (_selectedJobId is { } jobId && string.Equals(jobId, row.JobId, StringComparison.OrdinalIgnoreCase))
            BindStoryContextPanel(jobId);

        OnChanged(sender, e);
    }

    private void AutomationContextScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAutomationContextEvents || sender is not ComboBox combo)
            return;

        if (combo.DataContext is not AutomationContextRowViewModel row)
            return;

        if (combo.SelectedItem is LookbackAnchorChoice choice)
            row.Scope = choice.Anchor;

        if (_selectedJobId is { } jobId && string.Equals(jobId, row.JobId, StringComparison.OrdinalIgnoreCase))
            BindStoryContextPanel(jobId);

        OnChanged(sender, e);
    }

    private sealed class LookbackAnchorChoice(UtilityLookbackAnchor anchor, string label)
    {
        public UtilityLookbackAnchor Anchor { get; } = anchor;

        public string Label { get; } = label;

        public override string ToString() => Label;
    }

    private sealed class AutomationContextRowViewModel
    {
        public required string JobId { get; init; }

        public required string Layer { get; init; }

        public required string Label { get; init; }

        public int TurnPairs { get; set; }

        public string TurnPairsText
        {
            get => TurnPairs.ToString();
            set
            {
                if (int.TryParse(value, out var turns))
                    TurnPairs = Math.Max(0, turns);
            }
        }

        public int DefaultTurnPairs { get; init; }

        public UtilityLookbackAnchor Scope { get; set; }

        public LookbackAnchorChoice? ScopeChoice
        {
            get => ScopeChoices.FirstOrDefault(c => c.Anchor == Scope);
            set
            {
                if (value is not null)
                    Scope = value.Anchor;
            }
        }

        public UtilityLookbackAnchor DefaultScope { get; init; }

        public required string DefaultScopeLabel { get; init; }

        public required IReadOnlyList<LookbackAnchorChoice> ScopeChoices { get; init; }

        public bool HasOverride { get; set; }
    }
}
