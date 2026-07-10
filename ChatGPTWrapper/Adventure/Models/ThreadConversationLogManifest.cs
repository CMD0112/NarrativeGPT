namespace ChatGPTWrapper.Adventure.Models;

public sealed class ThreadConversationLogManifest
{
    public int SchemaVersion { get; set; } = 1;

    public Guid ThreadEntryId { get; set; }

    public Guid AdventureId { get; set; }

    public AdventureThreadKind Kind { get; set; }

    public string ConversationId { get; set; } = "";

    public int NextOrdinal { get; set; }

    public int EntryCount { get; set; }

    public string? ActiveBranchTailNodeId { get; set; }

    public int ActiveBranchLength { get; set; }

    public DateTimeOffset? LastRollingSyncAt { get; set; }

    public DateTimeOffset? LastDumpAt { get; set; }

    public int DumpCount { get; set; }

    public int SnapshotCount { get; set; }

    public DateTimeOffset? LastSnapshotAt { get; set; }

    public string? LastSnapshotTrigger { get; set; }

    public string? LatestSnapshotPath { get; set; }

    public string? LatestSendSnapshotPath { get; set; }

    public int IngestEventCount { get; set; }

    public DateTimeOffset? LastIngestAt { get; set; }

    public string? LastIngestTrigger { get; set; }

    public Guid? LatestIngestEventId { get; set; }

    public string? LatestRawPath { get; set; }

    public string? LatestProjectionPath { get; set; }
}
