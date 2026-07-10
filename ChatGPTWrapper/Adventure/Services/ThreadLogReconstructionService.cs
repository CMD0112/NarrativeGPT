using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ThreadLogReconstructionResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public int ThreadsReconstructed { get; init; }

    public int IngestEventsWritten { get; init; }

    public int FlightRecordsLinked { get; init; }
}

/// <summary>
/// Backfills <c>events.jsonl</c> and synthetic <c>raw/</c> from legacy rolling logs and snapshots.
/// </summary>
internal static class ThreadLogReconstructionService
{
    public const string SyntheticSourceRollingReconstruction = "rolling-reconstruction";

    public static ThreadLogReconstructionResult ReconstructAdventure(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var threadsReconstructed = 0;
        var ingestEventsWritten = 0;
        var flightRecordsLinked = 0;

        foreach (var entry in bundle.Metadata.ThreadRegistry)
        {
            if (string.IsNullOrWhiteSpace(entry.ConversationId))
                continue;

            if (!ThreadConversationLogStore.Exists(bundle.Metadata.Id, entry.Id))
                continue;

            var threadResult = ReconstructThread(bundle, entry);
            if (!threadResult.Success)
                continue;

            if (threadResult.IngestEventsWritten > 0)
                threadsReconstructed++;

            ingestEventsWritten += threadResult.IngestEventsWritten;
            flightRecordsLinked += threadResult.FlightRecordsLinked;
        }

        return new ThreadLogReconstructionResult
        {
            Success = true,
            ThreadsReconstructed = threadsReconstructed,
            IngestEventsWritten = ingestEventsWritten,
            FlightRecordsLinked = flightRecordsLinked,
        };
    }

    public static ThreadLogReconstructionResult ReconstructThread(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);

        var adventureId = bundle.Metadata.Id;
        var threadEntryId = threadEntry.Id;
        if (!ThreadConversationLogStore.Exists(adventureId, threadEntryId))
        {
            return new ThreadLogReconstructionResult
            {
                Success = false,
                Error = "thread_log_missing",
            };
        }

        var existingEvents = ThreadConversationLogStore.LoadAllIngestEvents(adventureId, threadEntryId);
        var ingestEventsWritten = 0;

        if (!existingEvents.Any(e => e.Synthetic && e.SyntheticSource == SyntheticSourceRollingReconstruction))
        {
            var active = ThreadConversationLogService.GetActiveBranch(adventureId, threadEntryId);
            if (active.Count > 0)
            {
                var branch = active.Select(LogEntryToBranchMessage).ToList();
                ThreadIngestService.RecordBranchProjectionIngest(
                    bundle,
                    threadEntry,
                    branch,
                    ThreadConversationLogCaptureSource.Migration,
                    ThreadConversationLogSnapshotTrigger.Migration,
                    synthetic: true,
                    syntheticSource: SyntheticSourceRollingReconstruction);
                ingestEventsWritten++;
            }
        }

        foreach (var relativePath in ThreadConversationLogStore.ListSnapshotRelativePaths(adventureId, threadEntryId))
        {
            var snapshot = ThreadConversationLogStore.LoadBranchSnapshot(adventureId, threadEntryId, relativePath);
            if (snapshot?.Messages is not { Count: > 0 })
                continue;

            var alreadyBacked = existingEvents.Any(evt =>
                evt.Correlation?.TurnId == snapshot.Correlation?.TurnId
                && snapshot.Correlation?.TurnId is not null
                || string.Equals(evt.SyntheticSource, $"snapshot-reconstruction:{relativePath.Replace('\\', '/')}",
                    StringComparison.Ordinal));

            if (alreadyBacked)
                continue;

            var branch = snapshot.Messages.Select(msg => new ConversationBranchMessage
            {
                NodeId = msg.NodeId,
                MessageId = msg.MessageId,
                BranchIndex = msg.BranchIndex,
                Role = msg.Role,
                RawText = msg.RawText,
                DisplayText = msg.DisplayText,
                IsUtility = msg.IsUtility,
                IsInjectedContext = msg.IsInjectedContext,
            }).ToList();

            ThreadIngestService.RecordBranchProjectionIngest(
                bundle,
                threadEntry,
                branch,
                snapshot.CaptureSource,
                snapshot.CaptureTrigger,
                snapshot.Correlation,
                synthetic: true,
                syntheticSource: $"snapshot-reconstruction:{relativePath.Replace('\\', '/')}");

            ingestEventsWritten++;
        }

        var flightRecordsLinked = LinkFlightRecords(bundle, threadEntry);

        return new ThreadLogReconstructionResult
        {
            Success = true,
            IngestEventsWritten = ingestEventsWritten,
            FlightRecordsLinked = flightRecordsLinked,
        };
    }

    public static int LinkFlightRecords(AdventureBundle bundle, AdventureThreadEntry threadEntry)
    {
        var adventureId = bundle.Metadata.Id;
        var linked = 0;
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntry.Id,
            threadEntry.Kind,
            threadEntry.ConversationId);

        foreach (var entry in bundle.PromptHistory.Entries)
        {
            if (entry.ThreadEntryId == threadEntry.Id && entry.ThreadIngestEventId is not null)
                continue;

            ThreadBranchSnapshot? snapshot = null;
            if (entry.TurnId is { } turnId)
                snapshot = ThreadConversationLogReader.GetSnapshotByTurnId(adventureId, threadEntry.Id, turnId);

            if (snapshot is null && entry.Kind == FlightRecordKind.PlaySend)
                snapshot = ThreadConversationLogReader.GetLatestSendSnapshot(adventureId, threadEntry.Id);

            if (snapshot is not null)
            {
                var ingest = FindIngestForSnapshot(adventureId, threadEntry.Id, snapshot);
                var snapshotPath = FindSnapshotPath(adventureId, threadEntry.Id, snapshot);
                entry.ThreadEntryId = threadEntry.Id;
                entry.ThreadSnapshotPath = snapshotPath;
                entry.ThreadIngestEventId = ingest?.EventId;
                entry.ThreadRawPath = ingest?.RawPath ?? manifest.LatestRawPath;
                entry.ThreadProjectionPath = ingest?.ProjectionPath ?? manifest.LatestProjectionPath;
                linked++;
                continue;
            }

            if (manifest.LatestIngestEventId is { } latestIngestId)
            {
                entry.ThreadEntryId = threadEntry.Id;
                entry.ThreadIngestEventId = latestIngestId;
                entry.ThreadRawPath = manifest.LatestRawPath;
                entry.ThreadProjectionPath = manifest.LatestProjectionPath;
                linked++;
            }
        }

        if (linked > 0)
            PromptHistoryMigration.EnsureCurrentSchema(bundle.PromptHistory);

        return linked;
    }

    private static ThreadIngestEvent? FindIngestForSnapshot(
        Guid adventureId,
        Guid threadEntryId,
        ThreadBranchSnapshot snapshot)
    {
        foreach (var evt in ThreadConversationLogStore.LoadAllIngestEvents(adventureId, threadEntryId))
        {
            if (evt.Correlation?.TurnId == snapshot.Correlation?.TurnId && snapshot.Correlation?.TurnId is not null)
                return evt;

            if (string.Equals(evt.CaptureTrigger, snapshot.CaptureTrigger, StringComparison.Ordinal)
                && evt.BranchMessageCount == snapshot.BranchMessageCount)
                return evt;
        }

        return ThreadConversationLogStore.LoadAllIngestEvents(adventureId, threadEntryId).LastOrDefault();
    }

    private static string? FindSnapshotPath(
        Guid adventureId,
        Guid threadEntryId,
        ThreadBranchSnapshot snapshot)
    {
        foreach (var relativePath in ThreadConversationLogStore.ListSnapshotRelativePaths(adventureId, threadEntryId))
        {
            var loaded = ThreadConversationLogStore.LoadBranchSnapshot(adventureId, threadEntryId, relativePath);
            if (loaded is null)
                continue;

            if (loaded.Correlation?.TurnId == snapshot.Correlation?.TurnId && snapshot.Correlation?.TurnId is not null)
                return relativePath;

            if (loaded.CapturedAt == snapshot.CapturedAt
                && loaded.BranchMessageCount == snapshot.BranchMessageCount)
                return relativePath;
        }

        return null;
    }

    private static ConversationBranchMessage LogEntryToBranchMessage(ThreadConversationLogEntry entry) =>
        new()
        {
            NodeId = entry.NodeId,
            MessageId = entry.MessageId,
            ParentNodeId = entry.ParentNodeId,
            BranchIndex = entry.BranchIndex,
            Role = entry.Role,
            RawText = entry.RawText,
            DisplayText = entry.DisplayText,
            IsUtility = entry.IsUtility,
            IsInjectedContext = entry.IsInjectedContext,
        };
}
