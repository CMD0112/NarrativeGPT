using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.Views;

public partial class FlightRecorderPanel : UserControl
{
    private AdventureBundle? _bundle;
    private IReadOnlyDictionary<Guid, int>? _turnOrdinals;
    private PromptHistoryEntry? _selectedEntry;
    private IReadOnlyList<FlightRecordTimelineItem> _timelineRows = [];
    private bool _suppressCompareSelection;

    public FlightRecorderPanel()
    {
        InitializeComponent();
    }

    public void Bind(AdventureBundle bundle)
    {
        _bundle = bundle;
        _turnOrdinals = BuildTurnOrdinalMap(bundle);
        _timelineRows = bundle.PromptHistory.Entries
            .OrderByDescending(e => e.At)
            .Select(e => new FlightRecordTimelineItem(
                e,
                FlightRecordDetailFormatter.FormatTimelineLabel(
                    e,
                    ResolveTurnOrdinal(e))))
            .ToList();

        TimelineList.ItemsSource = _timelineRows;
        DetailPanel.Visibility = _timelineRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_timelineRows.Count > 0)
            TimelineList.SelectedIndex = 0;
        else
            ClearDetail();
    }

    private void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineList.SelectedItem is not FlightRecordTimelineItem row)
        {
            ClearDetail();
            return;
        }

        _selectedEntry = row.Entry;
        DetailPanel.Visibility = Visibility.Visible;
        DetailHeaderText.Text = FlightRecordDetailFormatter.FormatDetailHeader(
            row.Entry,
            ResolveTurnOrdinal(row.Entry));

        var logTurnLink = _bundle is null
            ? null
            : FlightRecordCorrelationService.ResolveLogTurnLink(_bundle, row.Entry);
        LogTurnLinkText.Text = FlightRecordDetailFormatter.FormatLogTurnLink(logTurnLink);
        LogTurnLinkText.Visibility = string.IsNullOrWhiteSpace(LogTurnLinkText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ApplyDetailLayout(row.Entry);

        var traceShort = FlightRecordCorrelationService.FormatTraceRunIdShort(row.Entry.PlaySendTraceRunId);
        TraceRunIdText.Text = string.IsNullOrWhiteSpace(traceShort)
            ? "(no send trace run id)"
            : traceShort;
        TraceEventList.ItemsSource = FlightRecordCorrelationService.LoadTraceExcerpt(row.Entry.PlaySendTraceRunId);

        var utilityRows = _bundle is null
            ? []
            : FlightRecordCorrelationService.BuildUtilityRows(_bundle, row.Entry);
        UtilityRunList.ItemsSource = utilityRows;
        UtilityRunsEmptyText.Visibility = utilityRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        UtilityRunList.Visibility = utilityRows.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        BindCompareBaselineOptions(row.Entry);
        ApplyCompare(row.Entry, GetSelectedBaselineEntry(row.Entry));
        PacketTextBox.Text = row.Entry.PacketText ?? "";
    }

    private void ApplyDetailLayout(PromptHistoryEntry entry)
    {
        var isWorkerSend = entry.Kind == FlightRecordKind.WorkerUtilitySend;
        var playVisibility = isWorkerSend ? Visibility.Collapsed : Visibility.Visible;
        PlaySendTracePanel.Visibility = playVisibility;
        BundledUtilityPanel.Visibility = playVisibility;
        ComparePanel.Visibility = playVisibility;
        InjectionPanel.Visibility = playVisibility;
        PointersPanel.Visibility = playVisibility;

        if (!isWorkerSend)
        {
            WorkerAttachmentDetailText.Visibility = Visibility.Collapsed;
            WorkerAttachmentDetailText.Text = "";
            return;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.AttachmentDeliveryLane))
            parts.Add($"Delivery lane: {entry.AttachmentDeliveryLane}");
        if (entry.AttachmentFiles is { Count: > 0 })
            parts.Add($"Files: {string.Join(", ", entry.AttachmentFiles)}");
        if (entry.Delivery is { } delivery)
        {
            parts.Add($"Outcome: {delivery.Outcome}");
            if (!string.IsNullOrWhiteSpace(delivery.FailureCode))
                parts.Add($"Error: {delivery.FailureCode}");
        }

        WorkerAttachmentDetailText.Text = parts.Count == 0
            ? "Worker utility job with reference attachments."
            : string.Join(" · ", parts);
        WorkerAttachmentDetailText.Visibility = Visibility.Visible;
    }

    private void CompareBaselineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCompareSelection || _selectedEntry is null)
            return;

        ApplyCompare(_selectedEntry, GetSelectedBaselineEntry(_selectedEntry));
    }

    private void BindCompareBaselineOptions(PromptHistoryEntry current)
    {
        if (_bundle is null)
        {
            CompareBaselineCombo.ItemsSource = null;
            return;
        }

        var options = _bundle.PromptHistory.Entries
            .Where(e => e.Id != current.Id)
            .OrderByDescending(e => e.At)
            .Select(e => new CompareBaselineOption(
                e,
                FlightRecordCompareService.FormatEntryLabel(e),
                isPrevious: e.Id == FlightRecordCompareService.FindPreviousEntry(_bundle.PromptHistory.Entries, current)?.Id))
            .ToList();

        _suppressCompareSelection = true;
        CompareBaselineCombo.ItemsSource = options;
        var selected = options.FirstOrDefault(o => o.IsPrevious) ?? options.FirstOrDefault();
        CompareBaselineCombo.SelectedItem = selected;
        CompareBaselineCombo.IsEnabled = options.Count > 0;
        _suppressCompareSelection = false;
    }

    private PromptHistoryEntry? GetSelectedBaselineEntry(PromptHistoryEntry current)
    {
        if (CompareBaselineCombo.SelectedItem is CompareBaselineOption option)
            return option.Entry;

        return _bundle is null
            ? null
            : FlightRecordCompareService.FindPreviousEntry(_bundle.PromptHistory.Entries, current);
    }

    private void ApplyCompare(PromptHistoryEntry current, PromptHistoryEntry? baseline)
    {
        var compare = FlightRecordCompareService.Compare(
            current,
            baseline,
            baseline is null ? null : FlightRecordCompareService.FormatEntryLabel(baseline));

        CompareSummaryText.Text = compare.SummaryLine;
        SectionList.ItemsSource = compare.SectionRows;
        BaselinePointerList.ItemsSource = compare.BaselinePointers;
        ThisTurnPointerList.ItemsSource = compare.ThisTurnPointers;
    }

    private void ViewFull_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null || string.IsNullOrWhiteSpace(_selectedEntry.PacketText))
            return;

        var owner = Window.GetWindow(this);
        var useStructured = _bundle?.Metadata.Settings.UseContextTags == true;
        var meta = FlightRecordDetailFormatter.FormatDetailHeader(
            _selectedEntry,
            ResolveTurnOrdinal(_selectedEntry));
        new ContextViewerDialog(_selectedEntry.PacketText, meta, useStructuredPreview: useStructured)
        {
            Owner = owner,
        }.ShowDialog();
    }

    private void ComparePacket_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null || _bundle is null)
            return;

        var baseline = GetSelectedBaselineEntry(_selectedEntry);
        if (baseline is null)
            return;

        var diff = FlightRecordCompareService.FormatPacketDiff(
            _selectedEntry,
            baseline,
            FlightRecordCompareService.FormatEntryLabel(baseline),
            FlightRecordCompareService.FormatEntryLabel(_selectedEntry));

        new FlightPacketCompareDialog(
            diff,
            $"Packet diff · {FlightRecordCompareService.FormatEntryLabel(baseline)} → {FlightRecordCompareService.FormatEntryLabel(_selectedEntry)}")
        {
            Owner = Window.GetWindow(this),
        }.ShowDialog();
    }

    private void CopyPacket_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null || string.IsNullOrWhiteSpace(_selectedEntry.PacketText))
            return;

        try
        {
            Clipboard.SetText(_selectedEntry.PacketText);
        }
        catch
        {
            /* ignore */
        }
    }

    private void CopyTraceRunId_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry?.PlaySendTraceRunId is not { } runId)
            return;

        try
        {
            Clipboard.SetText(runId);
        }
        catch
        {
            /* ignore */
        }
    }

    private void CopyTracePath_Click(object sender, RoutedEventArgs e)
    {
        var path = FlightRecordCorrelationService.ResolveTraceSummaryPath(_selectedEntry?.PlaySendTraceRunId);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            Clipboard.SetText(path);
        }
        catch
        {
            /* ignore */
        }
    }

    private void ClearDetail()
    {
        _selectedEntry = null;
        DetailHeaderText.Text = "";
        LogTurnLinkText.Text = "";
        WorkerAttachmentDetailText.Text = "";
        WorkerAttachmentDetailText.Visibility = Visibility.Collapsed;
        PlaySendTracePanel.Visibility = Visibility.Visible;
        BundledUtilityPanel.Visibility = Visibility.Visible;
        ComparePanel.Visibility = Visibility.Visible;
        InjectionPanel.Visibility = Visibility.Visible;
        PointersPanel.Visibility = Visibility.Visible;
        TraceRunIdText.Text = "";
        TraceEventList.ItemsSource = null;
        UtilityRunList.ItemsSource = null;
        UtilityRunsEmptyText.Visibility = Visibility.Visible;
        UtilityRunList.Visibility = Visibility.Collapsed;
        CompareBaselineCombo.ItemsSource = null;
        CompareSummaryText.Text = "";
        SectionList.ItemsSource = null;
        BaselinePointerList.ItemsSource = null;
        ThisTurnPointerList.ItemsSource = null;
        PacketTextBox.Text = "";
    }

    private int? ResolveTurnOrdinal(PromptHistoryEntry entry)
    {
        if (entry.TurnId is not Guid turnId || _turnOrdinals is null)
            return null;

        return _turnOrdinals.TryGetValue(turnId, out var ordinal) ? ordinal : null;
    }

    private static IReadOnlyDictionary<Guid, int> BuildTurnOrdinalMap(AdventureBundle bundle)
    {
        var map = new Dictionary<Guid, int>();
        var index = 0;
        foreach (var turn in bundle.Log.Turns)
        {
            index++;
            map[turn.Id] = index;
        }

        return map;
    }

    private sealed class FlightRecordTimelineItem
    {
        public FlightRecordTimelineItem(PromptHistoryEntry entry, string displayLabel)
        {
            Entry = entry;
            DisplayLabel = displayLabel;
        }

        public PromptHistoryEntry Entry { get; }

        public string DisplayLabel { get; }
    }

    private sealed class CompareBaselineOption
    {
        public CompareBaselineOption(PromptHistoryEntry entry, string label, bool isPrevious)
        {
            Entry = entry;
            Label = isPrevious ? $"{label} (previous)" : label;
            IsPrevious = isPrevious;
        }

        public PromptHistoryEntry Entry { get; }

        public string Label { get; }

        public bool IsPrevious { get; }
    }
}
