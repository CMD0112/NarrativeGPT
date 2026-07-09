namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Append-only ingest fact in <c>thread-logs/{id}/events.jsonl</c>.</summary>
public sealed class ThreadIngestEvent
{
    public int SchemaVersion { get; set; } = 1;

    public Guid EventId { get; set; } = Guid.NewGuid();

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string CaptureTrigger { get; set; } = "";

    public string CaptureSource { get; set; } = ThreadConversationLogCaptureSource.Api;

    public Guid AdventureId { get; set; }

    public Guid ThreadEntryId { get; set; }

    public AdventureThreadKind ThreadKind { get; set; }

    public string ConversationId { get; set; } = "";

    /// <summary>Relative path under thread log dir, e.g. <c>raw/...-conversation.json</c>.</summary>
    public string? RawPath { get; set; }

    /// <summary>When raw is unavailable (DOM-only), points at branch projection file.</summary>
    public string? ProjectionPath { get; set; }

    public bool Synthetic { get; set; }

    public string? SyntheticSource { get; set; }

    public string? BranchTailNodeId { get; set; }

    public int BranchMessageCount { get; set; }

    public int RollingOrdinalHighWater { get; set; }

    public ThreadSnapshotCorrelation? Correlation { get; set; }

    public string? ContentHash { get; set; }
}

public sealed class ThreadIngestResult
{
    public Guid EventId { get; init; }

    public string? RawPath { get; init; }

    public string? ProjectionPath { get; init; }

    public string? SnapshotPath { get; init; }
}
