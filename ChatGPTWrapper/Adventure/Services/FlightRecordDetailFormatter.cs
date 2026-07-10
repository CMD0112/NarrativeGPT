using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record FlightPointerRowViewModel(
    string Bucket,
    string Title,
    string SourceLabel,
    int Score,
    string ModeLabel);

public static class FlightRecordDetailFormatter
{
    public static string FormatTimelineLabel(PromptHistoryEntry entry, int? turnOrdinal = null)
    {
        if (entry.Kind == FlightRecordKind.WorkerUtilitySend)
        {
            var job = string.IsNullOrWhiteSpace(entry.WorkerJobId) ? "utility job" : entry.WorkerJobId;
            var attach = string.IsNullOrWhiteSpace(entry.AttachmentDeliveryLane)
                ? ""
                : $" · {entry.AttachmentDeliveryLane}";
            return $"{entry.At:g} · worker {job}{attach}";
        }

        var turnPrefix = turnOrdinal is > 0 ? $"Turn {turnOrdinal} · " : "";
        var player = string.IsNullOrWhiteSpace(entry.PlayerLine)
            ? "(no player line)"
            : Truncate(entry.PlayerLine, 48);
        var mode = entry.Injection?.Profile;
        var modeSuffix = string.IsNullOrWhiteSpace(mode) ? "" : $" · {mode}";
        var verified = entry.Delivery?.Verified == true ? " ✓" : "";
        return $"{turnPrefix}{entry.At:g} · {player}{modeSuffix}{verified}";
    }

    public static string FormatDetailHeader(PromptHistoryEntry entry, int? turnOrdinal = null)
    {
        var parts = new List<string>();
        if (entry.Kind == FlightRecordKind.WorkerUtilitySend)
        {
            parts.Add(entry.At.ToString("g"));
            if (!string.IsNullOrWhiteSpace(entry.WorkerJobId))
                parts.Add($"worker {entry.WorkerJobId}");
            if (!string.IsNullOrWhiteSpace(entry.AttachmentDeliveryLane))
                parts.Add(entry.AttachmentDeliveryLane);
            if (entry.AttachmentFiles is { Count: > 0 })
                parts.Add(string.Join(", ", entry.AttachmentFiles));
        }
        else
        {
            if (turnOrdinal is > 0)
                parts.Add($"Turn {turnOrdinal}");
            parts.Add(entry.At.ToString("g"));
            if (!string.IsNullOrWhiteSpace(entry.Injection?.Profile))
                parts.Add(entry.Injection.Profile);
        }

        if (entry.Delivery is { } delivery)
        {
            parts.Add(delivery.Verified ? $"Verified {delivery.Channel}" : $"Unverified {delivery.Channel}");
            if (!string.IsNullOrWhiteSpace(delivery.FailureCode))
                parts.Add(delivery.FailureCode);
        }

        parts.Add($"{entry.PacketText.Length:N0} chars");
        if (!string.IsNullOrWhiteSpace(entry.PacketHash))
            parts.Add($"hash {entry.PacketHash[..Math.Min(8, entry.PacketHash.Length)]}");

        return string.Join(" · ", parts);
    }

    public static string FormatLogTurnLink(LogTurnLink? link) =>
        link is null
            ? ""
            : $"Play pair {link.DisplayTurnNumber} · log index {link.TurnIndex}";

    public static IReadOnlyList<InjectionSectionViewModel> ToSectionRows(FlightInjectionSnapshot? injection)
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

            return new InjectionSectionViewModel(
                s.Id,
                InjectionSectionManifestBuilder.GetDisplayName(s.Id),
                status,
                s.Included,
                s.CharEstimate,
                s.Note,
                null);
        }).ToList();
    }

    public static IReadOnlyList<FlightPointerRowViewModel> ToPointerRows(
        FlightInjectionSnapshot? injection,
        bool baseline)
    {
        if (injection is null)
            return [];

        var pointers = baseline ? injection.BaselinePointers : injection.ThisTurnPointers;
        var bucket = baseline ? "Always retrieve" : "This turn";
        return pointers.Select(p => new FlightPointerRowViewModel(
            bucket,
            string.IsNullOrWhiteSpace(p.Title) ? p.MachineId : p.Title,
            FormatPointerSource(p.Source),
            p.Score,
            p.Mode)).ToList();
    }

    public static string FormatPointerSource(string? source) =>
        source switch
        {
            "Baseline" => "Always retrieve",
            "Pin" => "Pinned",
            "State" => "Location",
            "NameMatch" => "Name match",
            "Trigger" => "Trigger",
            "Attachment" => "Attachment",
            "Cluster" => "Cluster",
            "SemanticMatch" => "Semantic",
            _ => source ?? "Unknown",
        };

    private static string Truncate(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
