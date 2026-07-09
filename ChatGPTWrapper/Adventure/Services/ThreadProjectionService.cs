using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

public enum ThreadProjectionSource
{
    None,
    RawIngest,
    ProjectionIngest,
    Rolling,
    Snapshot,
}

public sealed class ThreadProjectionResult
{
    public ThreadProjectionSource Source { get; init; }

    public Guid? IngestEventId { get; init; }

    public string? RawPath { get; init; }

    public string? ProjectionPath { get; init; }

    public string? SnapshotPath { get; init; }

    public IReadOnlyList<ConversationBranchMessage> Messages { get; init; } = [];

    public IReadOnlyList<TranscriptTurnPair> TurnPairs { get; init; } = [];
}

internal static class ThreadProjectionService
{
    public static ThreadProjectionResult Resolve(Guid adventureId, Guid threadEntryId)
    {
        if (!ThreadConversationLogStore.Exists(adventureId, threadEntryId))
            return new ThreadProjectionResult();

        var fromIngest = TryResolveFromLatestIngest(adventureId, threadEntryId);
        if (fromIngest.Messages.Count > 0)
            return fromIngest;

        var fromRolling = TryResolveFromRolling(adventureId, threadEntryId);
        if (fromRolling.Messages.Count > 0)
            return fromRolling;

        return TryResolveFromSnapshot(adventureId, threadEntryId);
    }

    public static IReadOnlyList<TranscriptTurnPair> GetTranscriptPairs(
        Guid adventureId,
        Guid threadEntryId,
        bool excludeUtility = true,
        bool excludeInjectedContext = true)
    {
        var projection = Resolve(adventureId, threadEntryId);
        if (projection.TurnPairs.Count > 0)
            return projection.TurnPairs;

        return ThreadConversationLogService.ToTranscriptPairs(
            adventureId,
            threadEntryId,
            excludeUtility,
            excludeInjectedContext);
    }

    private static ThreadProjectionResult TryResolveFromLatestIngest(Guid adventureId, Guid threadEntryId)
    {
        var events = ThreadConversationLogStore.LoadAllIngestEvents(adventureId, threadEntryId);
        for (var i = events.Count - 1; i >= 0; i--)
        {
            var evt = events[i];
            if (!string.IsNullOrWhiteSpace(evt.RawPath))
            {
                var raw = ThreadConversationLogStore.ReadRelativeFile(adventureId, threadEntryId, evt.RawPath);
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (TryParseSynthetic(raw, out var syntheticMessages))
                {
                    return BuildResult(
                        ThreadProjectionSource.RawIngest,
                        evt.EventId,
                        evt.RawPath,
                        evt.ProjectionPath,
                        snapshotPath: null,
                        syntheticMessages);
                }

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var branch = ConversationBranchExtractor.ExtractActiveBranch(doc.RootElement);
                    if (branch.Count > 0)
                    {
                        return BuildResult(
                            ThreadProjectionSource.RawIngest,
                            evt.EventId,
                            evt.RawPath,
                            evt.ProjectionPath,
                            snapshotPath: null,
                            branch);
                    }
                }
                catch (JsonException)
                {
                    // Fall through to next ingest event.
                }
            }

            if (!string.IsNullOrWhiteSpace(evt.ProjectionPath))
            {
                var projection = ThreadConversationLogStore.ReadRelativeFile(
                    adventureId,
                    threadEntryId,
                    evt.ProjectionPath);
                if (string.IsNullOrWhiteSpace(projection))
                    continue;

                if (TryParseSynthetic(projection, out var branch) && branch.Count > 0)
                {
                    return BuildResult(
                        ThreadProjectionSource.ProjectionIngest,
                        evt.EventId,
                        evt.RawPath,
                        evt.ProjectionPath,
                        snapshotPath: null,
                        branch);
                }
            }
        }

        return new ThreadProjectionResult();
    }

    private static ThreadProjectionResult TryResolveFromRolling(Guid adventureId, Guid threadEntryId)
    {
        var active = ThreadConversationLogService.GetActiveBranch(adventureId, threadEntryId);
        if (active.Count == 0)
            return new ThreadProjectionResult();

        var branch = active.Select(entry => new ConversationBranchMessage
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
        }).ToList();
        return BuildResult(
            ThreadProjectionSource.Rolling,
            ingestEventId: null,
            rawPath: null,
            projectionPath: null,
            snapshotPath: null,
            branch);
    }

    private static ThreadProjectionResult TryResolveFromSnapshot(Guid adventureId, Guid threadEntryId)
    {
        var snapshot = ThreadConversationLogReader.GetLatestSnapshot(adventureId, threadEntryId);
        if (snapshot?.Messages is not { Count: > 0 } messages)
            return new ThreadProjectionResult();

        var branch = messages.Select(msg => new ConversationBranchMessage
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

        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            AdventureThreadKind.Play,
            conversationId: "");

        return BuildResult(
            ThreadProjectionSource.Snapshot,
            ingestEventId: null,
            rawPath: null,
            projectionPath: null,
            snapshotPath: manifest.LatestSnapshotPath,
            branch);
    }

    private static bool TryParseSynthetic(string json, out List<ConversationBranchMessage> branch)
    {
        branch = [];
        try
        {
            var doc = JsonSerializer.Deserialize<SyntheticConversationDocument>(json, AdventureJson.Options);
            if (doc?.Branch is not { Count: > 0 })
                return false;

            branch = doc.Branch.Select(msg => new ConversationBranchMessage
            {
                NodeId = msg.NodeId,
                MessageId = msg.MessageId,
                ParentNodeId = msg.ParentNodeId,
                BranchIndex = msg.BranchIndex,
                Role = msg.Role,
                RawText = msg.RawText,
                DisplayText = msg.DisplayText,
                IsUtility = msg.IsUtility,
                IsInjectedContext = msg.IsInjectedContext,
            }).ToList();

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ThreadProjectionResult BuildResult(
        ThreadProjectionSource source,
        Guid? ingestEventId,
        string? rawPath,
        string? projectionPath,
        string? snapshotPath,
        IReadOnlyList<ConversationBranchMessage> branch) =>
        new()
        {
            Source = source,
            IngestEventId = ingestEventId,
            RawPath = rawPath,
            ProjectionPath = projectionPath,
            SnapshotPath = snapshotPath,
            Messages = branch,
            TurnPairs = ThreadConversationLogService.ToTranscriptPairsFromBranch(branch),
        };
}
