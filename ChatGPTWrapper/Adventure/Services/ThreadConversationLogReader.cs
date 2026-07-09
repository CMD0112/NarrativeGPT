using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadConversationLogReader
{
    public static AdventureThreadEntry? GetActiveEntry(AdventureBundle bundle, AdventureThreadKind kind)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        return AdventureThreadRegistryService.GetActiveEntry(bundle, kind);
    }

    public static bool HasLog(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        if (!ThreadConversationLogStore.Exists(bundle.Metadata.Id, entry.Id))
            return false;

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            bundle.Metadata.Id,
            entry.Id,
            entry.Kind,
            entry.ConversationId);

        return string.Equals(manifest.ConversationId, entry.ConversationId, StringComparison.OrdinalIgnoreCase)
               && manifest.EntryCount > 0;
    }

    public static bool HasActivePlayLog(AdventureBundle bundle)
    {
        var entry = GetActiveEntry(bundle, AdventureThreadKind.Play);
        return entry is not null && HasLog(bundle, entry);
    }

    public static ThreadBranchSnapshot? GetLatestSnapshot(
        Guid adventureId,
        Guid threadEntryId,
        string? captureTrigger = null)
    {
        if (!ThreadConversationLogStore.Exists(adventureId, threadEntryId))
            return null;

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            AdventureThreadKind.Play,
            conversationId: "");

        if (!string.IsNullOrWhiteSpace(manifest.LatestSnapshotPath)
            && (captureTrigger is null
                || string.Equals(manifest.LastSnapshotTrigger, captureTrigger, StringComparison.Ordinal)))
        {
            var fromManifest = ThreadConversationLogStore.LoadBranchSnapshot(
                adventureId,
                threadEntryId,
                manifest.LatestSnapshotPath);
            if (fromManifest is not null)
                return fromManifest;
        }

        var paths = ThreadConversationLogStore.ListSnapshotRelativePaths(adventureId, threadEntryId);
        for (var i = paths.Count - 1; i >= 0; i--)
        {
            var snapshot = ThreadConversationLogStore.LoadBranchSnapshot(adventureId, threadEntryId, paths[i]);
            if (snapshot is null)
                continue;

            if (captureTrigger is not null
                && !string.Equals(snapshot.CaptureTrigger, captureTrigger, StringComparison.Ordinal))
                continue;

            return snapshot;
        }

        return null;
    }

    public static ThreadBranchSnapshot? GetLatestSendSnapshot(Guid adventureId, Guid threadEntryId)
    {
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            AdventureThreadKind.Play,
            conversationId: "");

        if (!string.IsNullOrWhiteSpace(manifest.LatestSendSnapshotPath))
        {
            var snapshot = ThreadConversationLogStore.LoadBranchSnapshot(
                adventureId,
                threadEntryId,
                manifest.LatestSendSnapshotPath);
            if (snapshot is not null)
                return snapshot;
        }

        return GetLatestSnapshot(adventureId, threadEntryId, ThreadConversationLogSnapshotTrigger.Send);
    }

    public static ThreadBranchSnapshot? GetSnapshotByTurnId(
        Guid adventureId,
        Guid threadEntryId,
        Guid turnId)
    {
        foreach (var relativePath in ThreadConversationLogStore.ListSnapshotRelativePaths(adventureId, threadEntryId))
        {
            var snapshot = ThreadConversationLogStore.LoadBranchSnapshot(adventureId, threadEntryId, relativePath);
            if (snapshot?.Correlation?.TurnId == turnId)
                return snapshot;
        }

        return null;
    }

    public static IReadOnlyList<TranscriptTurnPair> GetTranscriptPairs(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        var projectionPairs = ThreadProjectionService.GetTranscriptPairs(bundle.Metadata.Id, entry.Id);
        if (projectionPairs.Count > 0)
            return projectionPairs;

        var snapshot = GetLatestSnapshot(bundle.Metadata.Id, entry.Id);
        if (snapshot?.TranscriptPairs is { Count: > 0 } pairs)
        {
            return pairs
                .Select(pair => new TranscriptTurnPair
                {
                    PlayerText = pair.PlayerText,
                    NarratorText = pair.NarratorText,
                })
                .ToList();
        }

        return ThreadConversationLogService.ToTranscriptPairs(bundle.Metadata.Id, entry.Id);
    }

    public static IReadOnlyList<ThreadConversationLogEntry> GetActiveBranchOrLatestSnapshot(
        AdventureBundle bundle,
        AdventureThreadEntry entry)
    {
        var snapshot = GetLatestSnapshot(bundle.Metadata.Id, entry.Id);
        if (snapshot?.Messages is { Count: > 0 } messages)
        {
            return messages.Select(msg => new ThreadConversationLogEntry
            {
                NodeId = msg.NodeId,
                MessageId = msg.MessageId,
                BranchIndex = msg.BranchIndex,
                Role = msg.Role,
                RawText = msg.RawText,
                DisplayText = msg.DisplayText,
                IsUtility = msg.IsUtility,
                IsInjectedContext = msg.IsInjectedContext,
                Status = ThreadConversationLogEntryStatus.Active,
                EntryType = ThreadConversationLogEntryType.Message,
            }).ToList();
        }

        return ThreadConversationLogService.GetActiveBranch(bundle.Metadata.Id, entry.Id);
    }

    public static IReadOnlyList<TurnRecord> ToSyntheticTurnRecords(
        AdventureBundle bundle,
        AdventureThreadEntry entry)
    {
        var pairs = GetTranscriptPairs(bundle, entry);
        var conversationId = entry.ConversationId;
        var turns = new List<TurnRecord>();

        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            turns.Add(new TurnRecord
            {
                Id = CreateStableTurnId(bundle.Metadata.Id, entry.Id, i),
                Index = i,
                PlayerText = pair.PlayerText ?? "",
                NarratorText = pair.NarratorText ?? "",
                Status = TurnStatus.Accepted,
                ConversationId = conversationId,
                At = DateTimeOffset.UtcNow,
            });
        }

        return turns;
    }

    public static IReadOnlyDictionary<string, int> BuildOrdinalMap(AdventureBundle bundle, AdventureThreadKind kind)
    {
        var entry = GetActiveEntry(bundle, kind);
        if (entry is null || !HasLog(bundle, entry))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        return ThreadConversationLogService.BuildOrdinalMap(bundle.Metadata.Id, entry.Id);
    }

    public static IReadOnlyDictionary<int, LogTurnLink> BuildLogTurnLinkMap(AdventureBundle bundle)
    {
        var entry = GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (entry is null || !HasLog(bundle, entry))
            return new Dictionary<int, LogTurnLink>();

        return ThreadConversationLogService.BuildLogTurnLinkMap(bundle.Metadata.Id, entry.Id);
    }

    public static IReadOnlyList<RevisionHideEntry> BuildRevisionHideEntries(AdventureBundle bundle) =>
        ThreadMetadataService.BuildRevisionHideEntries(bundle);

    private static Guid CreateStableTurnId(Guid adventureId, Guid threadEntryId, int turnIndex)
    {
        var bytes = new byte[16];
        adventureId.TryWriteBytes(bytes.AsSpan(0, 8));
        threadEntryId.TryWriteBytes(bytes.AsSpan(8, 8));
        var hash = System.Security.Cryptography.SHA256.HashData(
            [.. bytes, .. BitConverter.GetBytes(turnIndex)]);
        return new Guid(hash.AsSpan(0, 16));
    }
}
