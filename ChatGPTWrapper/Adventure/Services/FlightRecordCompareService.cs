using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record FlightSectionDiffRowViewModel(
    string Id,
    string DisplayName,
    string StatusBadge,
    bool Included,
    int CharEstimate,
    string? Note,
    string? ChangeBadge);

public sealed record FlightPointerDiffRowViewModel(
    string Bucket,
    string Title,
    string SourceLabel,
    int Score,
    string ModeLabel,
    bool IsNew);

public sealed record FlightRecordCompareResult(
    string BaselineLabel,
    string SummaryLine,
    IReadOnlyList<string> DeltaMessages,
    IReadOnlyList<FlightSectionDiffRowViewModel> SectionRows,
    IReadOnlyList<FlightPointerDiffRowViewModel> BaselinePointers,
    IReadOnlyList<FlightPointerDiffRowViewModel> ThisTurnPointers,
    bool HasBaseline);

public static class FlightRecordCompareService
{
    public static PromptHistoryEntry? FindPreviousEntry(
        IReadOnlyList<PromptHistoryEntry> entries,
        PromptHistoryEntry current) =>
        entries
            .Where(e => e.Id != current.Id && e.At < current.At)
            .OrderByDescending(e => e.At)
            .FirstOrDefault();

    public static FlightRecordCompareResult Compare(
        PromptHistoryEntry current,
        PromptHistoryEntry? baseline,
        string? baselineLabel = null)
    {
        if (baseline is null)
        {
            return new FlightRecordCompareResult(
                "",
                "No prior send to compare.",
                [],
                ToSectionRowsWithoutDiff(current.Injection),
                ToPointerRowsWithoutDiff(current.Injection, baselinePointers: true),
                ToPointerRowsWithoutDiff(current.Injection, baselinePointers: false),
                HasBaseline: false);
        }

        var label = baselineLabel ?? FormatEntryLabel(baseline);
        var sectionRows = BuildSectionDiffRows(current.Injection, baseline.Injection);
        var baselinePointers = BuildPointerDiffRows(current.Injection, baseline.Injection, baselinePointers: true);
        var thisTurnPointers = BuildPointerDiffRows(current.Injection, baseline.Injection, baselinePointers: false);
        var deltaMessages = BuildDeltaMessages(current, baseline, sectionRows);
        var summary = deltaMessages.Count == 0
            ? $"No manifest changes vs {label}."
            : string.Join(" · ", deltaMessages.Take(4))
              + (deltaMessages.Count > 4 ? $" · +{deltaMessages.Count - 4} more" : "");

        return new FlightRecordCompareResult(
            label,
            summary,
            deltaMessages,
            sectionRows,
            baselinePointers,
            thisTurnPointers,
            HasBaseline: true);
    }

    public static string FormatPacketDiff(
        PromptHistoryEntry current,
        PromptHistoryEntry baseline,
        string? baselineLabel = null,
        string? currentLabel = null)
    {
        var diff = TextDiffService.ComputeLineDiff(
            baseline.PacketText ?? "",
            current.PacketText ?? "");
        return TextDiffService.FormatUnifiedDiff(
            diff,
            baselineLabel ?? FormatEntryLabel(baseline),
            currentLabel ?? FormatEntryLabel(current));
    }

    public static string FormatEntryLabel(PromptHistoryEntry entry)
    {
        var player = string.IsNullOrWhiteSpace(entry.PlayerLine)
            ? "(no player line)"
            : Truncate(entry.PlayerLine, 32);
        return $"{entry.At:g} · {player}";
    }

    private static List<FlightSectionDiffRowViewModel> BuildSectionDiffRows(
        FlightInjectionSnapshot? current,
        FlightInjectionSnapshot? baseline)
    {
        if (current?.Sections is not { Count: > 0 } sections)
            return [];

        var prevMap = (baseline?.Sections ?? [])
            .ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var prevTrimmed = (baseline?.Trimmed ?? [])
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var curTrimmed = (current.Trimmed ?? [])
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sections.Select(section =>
        {
            string? changeBadge = null;
            if (!prevMap.TryGetValue(section.Id, out var prev))
            {
                if (section.Included)
                    changeBadge = "Added";
            }
            else if (prev.Included && !section.Included)
            {
                changeBadge = "Removed";
            }
            else if (!prev.Included && section.Included)
            {
                changeBadge = "Added";
            }

            if (curTrimmed.Contains(section.Id) && !prevTrimmed.Contains(section.Id))
                changeBadge = "Trimmed";

            var status = section.Included
                ? $"{section.Kind} · {section.CharEstimate:N0} ch"
                : "Omitted";
            if (!string.IsNullOrWhiteSpace(section.Note))
                status += $" ({section.Note})";

            return new FlightSectionDiffRowViewModel(
                section.Id,
                InjectionSectionManifestBuilder.GetDisplayName(section.Id),
                status,
                section.Included,
                section.CharEstimate,
                section.Note,
                changeBadge);
        }).ToList();
    }

    private static List<FlightPointerDiffRowViewModel> BuildPointerDiffRows(
        FlightInjectionSnapshot? current,
        FlightInjectionSnapshot? baseline,
        bool baselinePointers)
    {
        if (current is null)
            return [];

        var pointers = baselinePointers ? current.BaselinePointers : current.ThisTurnPointers;
        var bucket = baselinePointers ? "Always retrieve" : "This turn";
        var prevIds = (baselinePointers
                ? baseline?.BaselinePointers
                : baseline?.ThisTurnPointers)
            ?.Select(p => p.MachineId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        return pointers.Select(p => new FlightPointerDiffRowViewModel(
            bucket,
            string.IsNullOrWhiteSpace(p.Title) ? p.MachineId : p.Title,
            FlightRecordDetailFormatter.FormatPointerSource(p.Source),
            p.Score,
            p.Mode,
            IsNew: !baselinePointers && !prevIds.Contains(p.MachineId))).ToList();
    }

    private static List<string> BuildDeltaMessages(
        PromptHistoryEntry current,
        PromptHistoryEntry baseline,
        IReadOnlyList<FlightSectionDiffRowViewModel> sectionRows)
    {
        var messages = sectionRows
            .Where(s => !string.IsNullOrWhiteSpace(s.ChangeBadge))
            .Select(s => $"{s.DisplayName}: {s.ChangeBadge}")
            .ToList();

        var prevTrimmed = (baseline.Injection?.Trimmed ?? [])
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var trimmed in current.Injection?.Trimmed ?? [])
        {
            if (!prevTrimmed.Contains(trimmed.Id))
                messages.Add($"Trimmed: {trimmed.Id} ({trimmed.Reason})");
        }

        if (current.Injection?.WasTrimmed == true
            && baseline.Injection?.WasTrimmed != true
            && (current.Injection.Trimmed?.Count ?? 0) == 0)
        {
            messages.Add("Packet tail truncated (MaxPacketChars)");
        }

        var newPointerCount = BuildPointerDiffRows(current.Injection, baseline.Injection, baselinePointers: false)
            .Count(p => p.IsNew);
        if (newPointerCount > 0)
            messages.Add($"{newPointerCount} new THIS TURN pointer(s)");

        return messages;
    }

    private static IReadOnlyList<FlightSectionDiffRowViewModel> ToSectionRowsWithoutDiff(
        FlightInjectionSnapshot? injection)
    {
        if (injection?.Sections is not { Count: > 0 } sections)
            return [];

        return sections.Select(s =>
        {
            var status = s.Included
                ? $"{s.Kind} · {s.CharEstimate:N0} ch"
                : "Omitted";
            if (!string.IsNullOrWhiteSpace(s.Note))
                status += $" ({s.Note})";

            return new FlightSectionDiffRowViewModel(
                s.Id,
                InjectionSectionManifestBuilder.GetDisplayName(s.Id),
                status,
                s.Included,
                s.CharEstimate,
                s.Note,
                ChangeBadge: null);
        }).ToList();
    }

    private static IReadOnlyList<FlightPointerDiffRowViewModel> ToPointerRowsWithoutDiff(
        FlightInjectionSnapshot? injection,
        bool baselinePointers)
    {
        if (injection is null)
            return [];

        var pointers = baselinePointers ? injection.BaselinePointers : injection.ThisTurnPointers;
        var bucket = baselinePointers ? "Always retrieve" : "This turn";
        return pointers.Select(p => new FlightPointerDiffRowViewModel(
            bucket,
            string.IsNullOrWhiteSpace(p.Title) ? p.MachineId : p.Title,
            FlightRecordDetailFormatter.FormatPointerSource(p.Source),
            p.Score,
            p.Mode,
            IsNew: false)).ToList();
    }

    private static string Truncate(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
