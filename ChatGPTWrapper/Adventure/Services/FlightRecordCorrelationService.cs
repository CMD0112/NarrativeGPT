using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record FlightUtilityRunRowViewModel(
    string JobLabel,
    string LaneLabel,
    string ManifestSummary,
    string StateLabel,
    Guid RunId);

public sealed record FlightTraceEventRowViewModel(
    string Event,
    string Message,
    string? Outcome);

public static class FlightRecordCorrelationService
{
    public static Guid? FindFlightRecordIdForUtilityRun(AdventureBundle bundle, Guid utilityRunId) =>
        bundle.PromptHistory.Entries
            .LastOrDefault(e => e.UtilityJobIds.Contains(utilityRunId))
            ?.Id;

    public static LogTurnLink? ResolveLogTurnLink(AdventureBundle bundle, PromptHistoryEntry entry)
    {
        if (entry.TurnId is not Guid turnId)
            return null;

        return ThreadConversationLogReader.BuildLogTurnLinkMap(bundle)
            .Values
            .FirstOrDefault(link => link.TurnId == turnId);
    }

    public static string FormatTraceRunIdShort(string? playSendTraceRunId)
    {
        if (string.IsNullOrWhiteSpace(playSendTraceRunId)
            || !Guid.TryParse(playSendTraceRunId, out var runId))
        {
            return "";
        }

        return runId.ToString("N")[..8];
    }

    public static string? ResolveTraceSummaryPath(string? playSendTraceRunId)
    {
        var shortId = FormatTraceRunIdShort(playSendTraceRunId);
        return string.IsNullOrWhiteSpace(shortId) ? null : PlaySendTrace.GetRunSummaryPath(shortId);
    }

    public static IReadOnlyList<FlightUtilityRunRowViewModel> BuildUtilityRows(
        AdventureBundle bundle,
        PromptHistoryEntry entry)
    {
        if (entry.UtilityRuns.Count == 0 && entry.UtilityJobIds.Count == 0)
            return [];

        var snapshots = entry.UtilityRuns.Count > 0
            ? entry.UtilityRuns
            : entry.UtilityJobIds.Select(id => new FlightUtilityRunSnapshot { RunId = id }).ToList();

        return snapshots.Select(snapshot => ToUtilityRow(bundle, snapshot)).ToList();
    }

    public static IReadOnlyList<FlightTraceEventRowViewModel> LoadTraceExcerpt(
        string? playSendTraceRunId,
        int maxEvents = 10)
    {
        var path = ResolveTraceSummaryPath(playSendTraceRunId);
        if (path is null || !File.Exists(path))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("timeline", out var timeline)
                || timeline.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return timeline.EnumerateArray()
                .TakeLast(maxEvents)
                .Select(element => new FlightTraceEventRowViewModel(
                    element.TryGetProperty("event", out var evt) ? evt.GetString() ?? "" : "",
                    element.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                    element.TryGetProperty("outcome", out var outcome) ? outcome.GetString() : null))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static FlightUtilityRunRowViewModel ToUtilityRow(
        AdventureBundle bundle,
        FlightUtilityRunSnapshot snapshot)
    {
        var persisted = UtilityJobResultStore.LoadRun(bundle.Metadata.Id, snapshot.RunId);
        var manifest = persisted?.ContextManifest ?? snapshot.ContextManifest;
        var jobId = !string.IsNullOrWhiteSpace(snapshot.JobId)
            ? snapshot.JobId
            : persisted?.JobId ?? "";
        var lane = !string.IsNullOrWhiteSpace(persisted?.Lane)
            ? persisted.Lane
            : snapshot.Channel;
        var state = persisted?.State.ToString() ?? "bundled";

        return new FlightUtilityRunRowViewModel(
            string.IsNullOrWhiteSpace(jobId)
                ? "Utility job"
                : GenerationJobGuideService.GetDisplayLabel(jobId),
            FormatLaneLabel(lane, snapshot.Channel),
            manifest?.FormatSummary() ?? "(manifest pending)",
            state,
            snapshot.RunId);
    }

    private static string FormatLaneLabel(string? persistedLane, string? snapshotChannel)
    {
        if (!string.IsNullOrWhiteSpace(persistedLane))
        {
            return persistedLane switch
            {
                "worker" => "worker solo",
                "play-injection" => "play bundled",
                "local-llm" => "local LLM",
                "play-legacy-inline" => "play inline",
                _ => persistedLane,
            };
        }

        return snapshotChannel switch
        {
            nameof(UtilityExecutionChannel.WorkerBackground) => "worker solo",
            nameof(UtilityExecutionChannel.AutoBackground) => "play bundled",
            nameof(UtilityExecutionChannel.ManualBackground) => "play utility-only",
            _ => string.IsNullOrWhiteSpace(snapshotChannel) ? "unknown" : snapshotChannel,
        };
    }
}
