using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsHistoryTab : UserControl, IPlaySettingsTabPanel
{
    private AdventureBundle? _bundle;
    private IReadOnlyDictionary<Guid, int>? _turnOrdinals;

    public PlaySettingsHistoryTab()
    {
        InitializeComponent();
    }

#pragma warning disable CS0067
    public event EventHandler? SettingsChanged;
#pragma warning restore CS0067

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _bundle = context.Bundle;
        _turnOrdinals = BuildTurnOrdinalMap(context.Bundle);
        var rows = context.Bundle.PromptHistory.Entries
            .OrderByDescending(e => e.At)
            .Select(e => new FlightRecordTimelineItem(
                e,
                FlightRecordDetailFormatter.FormatTimelineLabel(e, ResolveTurnOrdinal(e))))
            .ToList();

        TimelineList.ItemsSource = rows;
        DetailPanel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count > 0)
            TimelineList.SelectedIndex = 0;
        else
            ClearDetail();
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
    }

    private void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineList.SelectedItem is not FlightRecordTimelineItem row || _bundle is null)
        {
            ClearDetail();
            return;
        }

        DetailPanel.Visibility = Visibility.Visible;
        DetailHeaderText.Text = FlightRecordDetailFormatter.FormatDetailHeader(
            row.Entry,
            ResolveTurnOrdinal(row.Entry));

        var logTurnLink = FlightRecordCorrelationService.ResolveLogTurnLink(_bundle, row.Entry);
        LogTurnLinkText.Text = FlightRecordDetailFormatter.FormatLogTurnLink(logTurnLink);
        LogTurnLinkText.Visibility = string.IsNullOrWhiteSpace(LogTurnLinkText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ManifestSummaryText.Text = row.Entry.Injection is { } injection
            ? $"{injection.Profile} · {injection.MergedCharCount} chars · {injection.Sections.Count} sections"
            : "(no manifest)";
        var excerpt = row.Entry.PacketText ?? "";
        PacketExcerptBox.Text = excerpt.Length > 4000 ? excerpt[..4000] + "…" : excerpt;
    }

    private void ClearDetail()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailHeaderText.Text = "";
        LogTurnLinkText.Text = "";
        ManifestSummaryText.Text = "";
        PacketExcerptBox.Text = "";
    }

    private int? ResolveTurnOrdinal(PromptHistoryEntry entry) =>
        entry.TurnId is { } turnId
        && _turnOrdinals is not null
        && _turnOrdinals.TryGetValue(turnId, out var ordinal)
            ? ordinal
            : null;

    private static IReadOnlyDictionary<Guid, int> BuildTurnOrdinalMap(AdventureBundle bundle)
    {
        var map = new Dictionary<Guid, int>();
        var ordinal = 0;
        foreach (var turn in bundle.Log.Turns.Where(t => t.Status == TurnStatus.Accepted))
            map[turn.Id] = ++ordinal;
        return map;
    }

    private sealed record FlightRecordTimelineItem(PromptHistoryEntry Entry, string Label)
    {
        public override string ToString() => Label;
    }
}
