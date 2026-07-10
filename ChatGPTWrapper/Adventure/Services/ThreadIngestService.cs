using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ThreadIngestService
{
    public static ThreadIngestResult RecordApiIngest(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        JsonElement conversationJson,
        string captureSource,
        string captureTrigger,
        ThreadSnapshotCorrelation? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);

        var adventureId = bundle.Metadata.Id;
        var threadEntryId = threadEntry.Id;
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            threadEntry.Kind,
            threadEntry.ConversationId);

        var conversationJsonText = conversationJson.GetRawText();
        var rawPath = ThreadConversationLogStore.WriteRawConversation(
            adventureId,
            threadEntryId,
            conversationJsonText,
            captureTrigger);

        var branch = ConversationBranchExtractor.ExtractActiveBranch(conversationJson);
        return AppendIngestEvent(
            bundle,
            threadEntry,
            manifest,
            captureSource,
            captureTrigger,
            correlation,
            rawPath,
            projectionPath: null,
            synthetic: false,
            syntheticSource: null,
            branch);
    }

    public static ThreadIngestResult RecordBranchProjectionIngest(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        IReadOnlyList<ConversationBranchMessage> branch,
        string captureSource,
        string captureTrigger,
        ThreadSnapshotCorrelation? correlation = null,
        bool synthetic = false,
        string? syntheticSource = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(threadEntry);

        var adventureId = bundle.Metadata.Id;
        var threadEntryId = threadEntry.Id;
        var manifest = ThreadConversationLogStore.LoadOrCreateManifest(
            adventureId,
            threadEntryId,
            threadEntry.Kind,
            threadEntry.ConversationId);

        var document = BuildSyntheticDocument(bundle, threadEntry, branch, synthetic, syntheticSource);
        var documentJson = JsonSerializer.Serialize(document, AdventureJson.Options);

        string? rawPath = null;
        string? projectionPath = null;
        if (synthetic)
        {
            rawPath = ThreadConversationLogStore.WriteRawConversation(
                adventureId,
                threadEntryId,
                documentJson,
                captureTrigger);
        }
        else
        {
            projectionPath = ThreadConversationLogStore.WriteSyntheticProjection(
                adventureId,
                threadEntryId,
                documentJson,
                captureTrigger);
        }

        return AppendIngestEvent(
            bundle,
            threadEntry,
            manifest,
            captureSource,
            captureTrigger,
            correlation,
            rawPath,
            projectionPath,
            synthetic,
            syntheticSource,
            branch);
    }

    public static string ResolveIngestTrigger(
        string captureSource,
        ThreadSnapshotCaptureRequest? snapshotRequest)
    {
        if (!string.IsNullOrWhiteSpace(snapshotRequest?.CaptureTrigger))
            return snapshotRequest.CaptureTrigger;

        return captureSource switch
        {
            ThreadConversationLogCaptureSource.Send => ThreadConversationLogSnapshotTrigger.Send,
            ThreadConversationLogCaptureSource.Invalidation => ThreadConversationLogSnapshotTrigger.Invalidation,
            ThreadConversationLogCaptureSource.Migration => ThreadConversationLogSnapshotTrigger.Migration,
            ThreadConversationLogCaptureSource.ManualDump => ThreadConversationLogSnapshotTrigger.Manual,
            _ => "sync",
        };
    }

    private static ThreadIngestResult AppendIngestEvent(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        ThreadConversationLogManifest manifest,
        string captureSource,
        string captureTrigger,
        ThreadSnapshotCorrelation? correlation,
        string? rawPath,
        string? projectionPath,
        bool synthetic,
        string? syntheticSource,
        IReadOnlyList<ConversationBranchMessage> branch)
    {
        var adventureId = bundle.Metadata.Id;
        var threadEntryId = threadEntry.Id;
        var contentForHash = rawPath is not null
            ? ThreadConversationLogStore.ReadRelativeFile(adventureId, threadEntryId, rawPath) ?? ""
            : ThreadConversationLogStore.ReadRelativeFile(adventureId, threadEntryId, projectionPath ?? "") ?? "";

        var ingestEvent = new ThreadIngestEvent
        {
            CaptureTrigger = captureTrigger,
            CaptureSource = captureSource,
            AdventureId = adventureId,
            ThreadEntryId = threadEntryId,
            ThreadKind = threadEntry.Kind,
            ConversationId = threadEntry.ConversationId ?? "",
            RawPath = rawPath,
            ProjectionPath = projectionPath,
            Synthetic = synthetic,
            SyntheticSource = syntheticSource,
            BranchTailNodeId = branch.Count > 0 ? branch[^1].NodeId : null,
            BranchMessageCount = branch.Count,
            RollingOrdinalHighWater = Math.Max(0, manifest.NextOrdinal - 1),
            Correlation = correlation,
            ContentHash = string.IsNullOrWhiteSpace(contentForHash)
                ? null
                : ThreadConversationLogStore.ComputeContentHash(contentForHash),
        };

        ThreadConversationLogStore.AppendIngestEvent(adventureId, threadEntryId, ingestEvent);
        UpdateManifestAfterIngest(manifest, ingestEvent);
        ThreadConversationLogStore.SaveManifest(manifest);
        ThreadConversationLogStore.ApplyIngestRetention(adventureId, threadEntryId, captureTrigger);

        PlaySendTrace.Event(
            "thread_ingest_recorded",
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "Thread ingest event recorded",
            data: new
            {
                trigger = captureTrigger,
                source = captureSource,
                eventId = ingestEvent.EventId,
                rawPath,
                projectionPath,
                synthetic,
                messageCount = branch.Count,
                turnId = correlation?.TurnId,
                flightRecordId = correlation?.FlightRecordId,
            });

        return new ThreadIngestResult
        {
            EventId = ingestEvent.EventId,
            RawPath = rawPath,
            ProjectionPath = projectionPath,
        };
    }

    private static void UpdateManifestAfterIngest(
        ThreadConversationLogManifest manifest,
        ThreadIngestEvent ingestEvent)
    {
        manifest.IngestEventCount++;
        manifest.LastIngestAt = ingestEvent.CapturedAt;
        manifest.LastIngestTrigger = ingestEvent.CaptureTrigger;
        manifest.LatestIngestEventId = ingestEvent.EventId;

        if (!string.IsNullOrWhiteSpace(ingestEvent.RawPath))
            manifest.LatestRawPath = ingestEvent.RawPath;

        if (!string.IsNullOrWhiteSpace(ingestEvent.ProjectionPath))
            manifest.LatestProjectionPath = ingestEvent.ProjectionPath;
    }

    private static SyntheticConversationDocument BuildSyntheticDocument(
        AdventureBundle bundle,
        AdventureThreadEntry threadEntry,
        IReadOnlyList<ConversationBranchMessage> branch,
        bool synthetic,
        string? syntheticSource) =>
        new()
        {
            Synthetic = synthetic,
            Source = syntheticSource ?? (synthetic ? "rolling-reconstruction" : "branch-projection"),
            CapturedAt = DateTimeOffset.UtcNow,
            AdventureId = bundle.Metadata.Id,
            ThreadEntryId = threadEntry.Id,
            ConversationId = threadEntry.ConversationId ?? "",
            Branch = branch.Select(msg => new SyntheticBranchMessage
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
            }).ToList(),
        };
}

internal sealed class SyntheticConversationDocument
{
    public int SchemaVersion { get; set; } = 1;

    public bool Synthetic { get; set; }

    public string Source { get; set; } = "";

    public DateTimeOffset CapturedAt { get; set; }

    public Guid AdventureId { get; set; }

    public Guid ThreadEntryId { get; set; }

    public string ConversationId { get; set; } = "";

    public List<SyntheticBranchMessage> Branch { get; set; } = [];
}

internal sealed class SyntheticBranchMessage
{
    public string NodeId { get; set; } = "";

    public string? MessageId { get; set; }

    public string? ParentNodeId { get; set; }

    public int BranchIndex { get; set; }

    public string Role { get; set; } = "";

    public string RawText { get; set; } = "";

    public string? DisplayText { get; set; }

    public bool IsUtility { get; set; }

    public bool IsInjectedContext { get; set; }
}
